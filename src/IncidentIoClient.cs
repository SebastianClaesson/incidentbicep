using System.Net;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace Bicep.Extension.IncidentIo;

/// <summary>
/// Thin wrapper around HttpClient for the incident.io REST API.
/// Handles base URL validation, Bearer auth, retries on rate limiting, pagination and error mapping.
/// </summary>
public sealed class IncidentIoClient : IDisposable
{
    public const string DefaultBaseUrl = "https://api.incident.io";

    // One connection pool for the lifetime of the process. Creating a new HttpClientHandler per
    // request exhausts sockets under load; sharing the handler (disposeHandler: false) avoids that
    // while still allowing a per-request HttpClient with its own auth header.
    private static readonly SocketsHttpHandler SharedHandler = new()
    {
        PooledConnectionLifetime = TimeSpan.FromMinutes(5),
        AllowAutoRedirect = false // never forward the Bearer token to a redirect target
    };

    private const int MaxAttempts = 5;
    private static readonly TimeSpan MaxRetryDelay = TimeSpan.FromSeconds(30);

    private readonly HttpClient http;
    private readonly Func<TimeSpan, CancellationToken, Task> delay;

    public IncidentIoClient(Configuration config)
        : this(config, new HttpClient(SharedHandler, disposeHandler: false), Task.Delay)
    {
    }

    internal IncidentIoClient(Configuration config, HttpClient http, Func<TimeSpan, CancellationToken, Task> delay)
    {
        if (string.IsNullOrWhiteSpace(config.ApiKey))
            throw new IncidentIoException("InvalidConfiguration", "The incident.io 'apiKey' must not be empty.");

        this.http = http;
        this.delay = delay;
        http.BaseAddress = ValidateBaseUrl(config.BaseUrl);
        http.Timeout = TimeSpan.FromSeconds(100);
        http.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", config.ApiKey);
        http.DefaultRequestHeaders.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));
        http.DefaultRequestHeaders.UserAgent.Add(new ProductInfoHeaderValue("bicep-ext-incidentio", ThisAssembly.Version));
    }

    /// <summary>
    /// Only HTTPS is accepted, so the API key never crosses the network in clear text.
    /// Plain HTTP is allowed for loopback addresses only (local mock server / tests).
    /// </summary>
    internal static Uri ValidateBaseUrl(string? baseUrl)
    {
        var raw = string.IsNullOrWhiteSpace(baseUrl) ? DefaultBaseUrl : baseUrl.Trim();
        if (!Uri.TryCreate(raw.TrimEnd('/') + "/", UriKind.Absolute, out var uri))
            throw new IncidentIoException("InvalidConfiguration", $"'baseUrl' is not a valid absolute URL: '{raw}'.");

        var isHttps = uri.Scheme == Uri.UriSchemeHttps;
        var isLoopbackHttp = uri.Scheme == Uri.UriSchemeHttp && uri.IsLoopback;
        if (!isHttps && !isLoopbackHttp)
            throw new IncidentIoException("InvalidConfiguration", $"'baseUrl' must use HTTPS (got '{uri.Scheme}'). HTTP is only allowed for localhost.");

        if (!string.IsNullOrEmpty(uri.UserInfo))
            throw new IncidentIoException("InvalidConfiguration", "'baseUrl' must not contain credentials.");

        return uri;
    }

    public Task<JsonElement> GetAsync(string path, CancellationToken ct)
        => SendAsync(HttpMethod.Get, path, null, ct);

    public Task<JsonElement> PostAsync(string path, JsonNode body, CancellationToken ct)
        => SendAsync(HttpMethod.Post, path, body, ct);

    public Task<JsonElement> PutAsync(string path, JsonNode body, CancellationToken ct)
        => SendAsync(HttpMethod.Put, path, body, ct);

    /// <summary>
    /// Lists every item of a collection, following 'pagination_meta.after' cursors when the endpoint is paginated.
    /// </summary>
    /// <param name="path">Collection path, e.g. "v1/severities".</param>
    /// <param name="collectionKey">JSON property holding the items, e.g. "severities".</param>
    /// <param name="query">Extra query string (without '?'), or null.</param>
    /// <param name="pageSize">Page size for paginated endpoints, or null for endpoints that return everything at once.</param>
    public async Task<List<JsonElement>> ListAsync(string path, string collectionKey, string? query, int? pageSize, CancellationToken ct)
    {
        var items = new List<JsonElement>();
        string? after = null;

        // Hard cap prevents an infinite loop if the API ever returned a cursor that does not advance.
        for (var page = 0; page < 10_000; page++)
        {
            var parts = new List<string>();
            if (!string.IsNullOrEmpty(query)) parts.Add(query);
            if (pageSize is { } size) parts.Add($"page_size={size}");
            if (after is not null) parts.Add($"after={Uri.EscapeDataString(after)}");
            var url = parts.Count == 0 ? path : $"{path}?{string.Join('&', parts)}";

            var json = await GetAsync(url, ct);
            if (!json.TryGetProperty(collectionKey, out var collection) || collection.ValueKind != JsonValueKind.Array)
                throw new IncidentIoException("UnexpectedResponse", $"incident.io response for '{path}' did not contain '{collectionKey}'.");

            var count = 0;
            foreach (var item in collection.EnumerateArray())
            {
                items.Add(item.Clone());
                count++;
            }

            var next = pageSize is not null
                && json.TryGetProperty("pagination_meta", out var meta)
                && meta.TryGetProperty("after", out var afterProp)
                && afterProp.ValueKind == JsonValueKind.String
                    ? afterProp.GetString()
                    : null;

            if (count == 0 || string.IsNullOrEmpty(next) || next == after)
                return items;

            after = next;
        }

        throw new IncidentIoException("UnexpectedResponse", $"Pagination for '{path}' did not terminate.");
    }

    private async Task<JsonElement> SendAsync(HttpMethod method, string path, JsonNode? body, CancellationToken ct)
    {
        var payload = body?.ToJsonString();

        for (var attempt = 1; ; attempt++)
        {
            // HttpRequestMessage cannot be re-sent, so build a fresh one on each attempt.
            using var request = new HttpRequestMessage(method, path.TrimStart('/'));
            if (payload is not null)
                request.Content = new StringContent(payload, Encoding.UTF8, "application/json");

            HttpResponseMessage sent;
            try
            {
                sent = await http.SendAsync(request, ct);
            }
            catch (HttpRequestException) when (IsIdempotent(method) && attempt < MaxAttempts)
            {
                // Transient network failure (e.g. a pooled connection closed by the server).
                // Only GET/PUT are retried: retrying a POST could create a duplicate resource.
                await delay(TimeSpan.FromSeconds(Math.Pow(2, attempt - 1)), ct);
                continue;
            }

            using var response = sent;
            if (IsRetryable(response.StatusCode) && attempt < MaxAttempts)
            {
                await delay(GetRetryDelay(response, attempt), ct);
                continue;
            }

            var text = await response.Content.ReadAsStringAsync(ct);
            if (!response.IsSuccessStatusCode)
                throw IncidentIoException.FromResponse(method, path, response.StatusCode, text);

            if (string.IsNullOrWhiteSpace(text))
                return default;

            using var doc = JsonDocument.Parse(text);
            return doc.RootElement.Clone();
        }
    }

    private static bool IsIdempotent(HttpMethod method) => method == HttpMethod.Get || method == HttpMethod.Put;

    private static bool IsRetryable(HttpStatusCode status) => status is
        HttpStatusCode.TooManyRequests or
        HttpStatusCode.BadGateway or
        HttpStatusCode.ServiceUnavailable or
        HttpStatusCode.GatewayTimeout;

    internal static TimeSpan GetRetryDelay(HttpResponseMessage response, int attempt)
    {
        var retryAfter = response.Headers.RetryAfter;
        TimeSpan? requested = retryAfter?.Delta
            ?? (retryAfter?.Date is { } date ? date - DateTimeOffset.UtcNow : null);

        // Exponential backoff (1s, 2s, 4s, ...) when the server gives no hint.
        var wait = requested ?? TimeSpan.FromSeconds(Math.Pow(2, attempt - 1));
        if (wait < TimeSpan.Zero) wait = TimeSpan.Zero;
        return wait > MaxRetryDelay ? MaxRetryDelay : wait;
    }

    public void Dispose() => http.Dispose();
}

/// <summary>
/// Error raised for failed incident.io calls. Mapped to a Bicep deployment error by the handler base class.
/// </summary>
public class IncidentIoException(string code, string message, HttpStatusCode? status = null) : Exception(message)
{
    public string Code { get; } = code;
    public HttpStatusCode? StatusCode { get; } = status;

    internal static IncidentIoException FromResponse(HttpMethod method, string path, HttpStatusCode status, string body)
    {
        // incident.io error bodies look like: { "type": "validation_error", "status": 422, "errors": [{ "message": "..." }] }
        // They describe the request, never echo credentials, so including them is safe and very helpful.
        var detail = body.Length > 2000 ? body[..2000] + "…" : body;
        var hint = status switch
        {
            HttpStatusCode.Unauthorized => " Check that the API key is valid.",
            HttpStatusCode.Forbidden => " The API key is missing a required scope for this resource.",
            _ => ""
        };
        return new IncidentIoException(
            ((int)status).ToString(),
            $"incident.io {method} /{path.TrimStart('/')} returned {(int)status} {status}.{hint} {detail}".TrimEnd(),
            status);
    }
}

internal static class ThisAssembly
{
    public static readonly string Version =
        typeof(ThisAssembly).Assembly.GetName().Version?.ToString(3) ?? "0.0.0";
}
