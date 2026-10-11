using System.Reflection;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.Json.Serialization;
using Azure.Bicep.Types.Concrete;
using Bicep.Local.Extension.Host.Handlers;
using Bicep.Local.Extension.Types.Attributes;

namespace Bicep.Extension.IncidentIo.Handlers;

/// <summary>
/// Base handler for all incident.io resource types.
///
/// incident.io assigns opaque IDs, so idempotency works like this:
///   1. List the collection and find an item matching the resource's identifiers (name, value, external ID...).
///   2. Found → PUT /{path}/{id} (full replace). Not found → POST /{path}.
///   3. Copy the server ID (and any other outputs) back onto the resource.
///
/// A derived handler declares its API path and JSON keys, and how an API item matches its identifiers.
/// Most handlers are ~10 lines.
/// </summary>
public abstract class IncidentIoResourceHandlerBase<TProperties, TIdentifiers>
    : TypedResourceHandler<TProperties, TIdentifiers, Configuration>
    where TProperties : class, TIdentifiers, IIncidentIoResource
    where TIdentifiers : class
{
    /// <summary>
    /// Serializer for the incident.io API: C# PascalCase → snake_case.
    /// Do NOT put [JsonPropertyName] on models — the Bicep SDK serializes the same classes (camelCase) over gRPC.
    /// Dictionary keys (attribute IDs, annotation keys) are deliberately left untouched.
    /// </summary>
    internal static readonly JsonSerializerOptions ApiJsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
        PropertyNameCaseInsensitive = true
    };

    /// <summary>API collection path without leading slash, e.g. "v1/severities".</summary>
    protected abstract string ApiPath { get; }

    /// <summary>JSON key for a single item in create/update/show responses, e.g. "severity".</summary>
    protected abstract string ItemKey { get; }

    /// <summary>JSON key for the item array in list responses, e.g. "severities".</summary>
    protected abstract string CollectionKey { get; }

    /// <summary>Page size for paginated list endpoints; null when the endpoint returns everything at once.</summary>
    protected virtual int? PageSize => null;

    /// <summary>Extra list query (e.g. "custom_field_id=..."), required by some endpoints.</summary>
    protected virtual string? GetListQuery(TIdentifiers identifiers) => null;

    /// <summary>Returns true when an API item is the resource described by these identifiers.</summary>
    protected abstract bool IsMatch(JsonElement item, TIdentifiers identifiers);

    /// <summary>
    /// snake_case fields accepted on create but not on update. They are removed from PUT payloads and,
    /// if the desired value differs from the existing one, the deployment fails instead of silently ignoring it.
    /// </summary>
    protected virtual IReadOnlyCollection<string> CreateOnlyFields => [];

    // ──────────────── Bicep operations ────────────────

    protected override Task<ResourceResponse> Preview(ResourceRequest request, CancellationToken cancellationToken)
    {
        // Returns the desired state without calling the API (same as the official sample extensions).
        // Bicep uses this during validation; local-deploy has no what-if yet.
        return Task.FromResult(GetResponse(request));
    }

    protected override async Task<ResourceResponse> CreateOrUpdate(ResourceRequest request, CancellationToken cancellationToken)
    {
        return await WithErrorMapping(async () =>
        {
            using var client = new IncidentIoClient(request.Config);
            var properties = request.Properties;

            var existing = await FindExisting(client, properties, cancellationToken);
            var payload = await BuildPayload(client, properties, existing, cancellationToken);

            JsonElement response;
            if (existing is { } current)
            {
                EnsureCreateOnlyFieldsUnchanged(payload, current);
                foreach (var field in CreateOnlyFields)
                    payload.Remove(field);

                var id = current.GetProperty("id").GetString()!;
                response = await client.PutAsync($"{ApiPath}/{Uri.EscapeDataString(id)}", payload, cancellationToken);
            }
            else
            {
                response = await client.PostAsync(ApiPath, payload, cancellationToken);
            }

            var item = response.GetProperty(ItemKey);
            await AfterWrite(client, properties, item, cancellationToken);
            return GetResponse(request);
        });
    }

    /// <summary>
    /// Supports 'existing' resources, e.g. looking up a built-in severity by name to get its ID.
    /// </summary>
    protected override async Task<ResourceResponse> Get(ReferenceRequest request, CancellationToken cancellationToken)
    {
        return await WithErrorMapping(async () =>
        {
            using var client = new IncidentIoClient(request.Config);
            var item = await FindExisting(client, request.Identifiers, cancellationToken)
                ?? throw new IncidentIoException("NotFound", $"{typeof(TProperties).Name} {DescribeIdentifiers(request.Identifiers)} was not found in incident.io.");

            var properties = await FromApi(client, item, cancellationToken);
            return GetResponse(request, properties);
        });
    }

    // ──────────────── Extension points ────────────────

    /// <summary>
    /// Builds the JSON body for POST/PUT. Default: serialize the model to snake_case and drop read-only (output) properties.
    /// Override to reshape fields the API expects differently. <paramref name="existing"/> is the current API item (null on create).
    /// </summary>
    protected virtual Task<JsonObject> BuildPayload(IncidentIoClient client, TProperties properties, JsonElement? existing, CancellationToken cancellationToken)
        => Task.FromResult(SerializeWritable(properties));

    /// <summary>
    /// Runs after a successful create/update. Default: copy outputs (ID) from the API item onto the resource.
    /// Override for follow-up calls (e.g. updating a catalog type's schema).
    /// </summary>
    protected virtual Task AfterWrite(IncidentIoClient client, TProperties properties, JsonElement item, CancellationToken cancellationToken)
    {
        PopulateOutputs(properties, item);
        return Task.CompletedTask;
    }

    /// <summary>Copies output properties from an API item onto the resource.</summary>
    protected virtual void PopulateOutputs(TProperties properties, JsonElement item)
    {
        if (item.TryGetProperty("id", out var id))
            properties.Id = id.GetString();
    }

    /// <summary>Maps an API item to the resource model (used for 'existing' resources).</summary>
    protected virtual Task<TProperties> FromApi(IncidentIoClient client, JsonElement item, CancellationToken cancellationToken)
        => Task.FromResult(item.Deserialize<TProperties>(ApiJsonOptions)
            ?? throw new IncidentIoException("UnexpectedResponse", $"Could not read {typeof(TProperties).Name} from incident.io response."));

    // ──────────────── Shared helpers ────────────────

    protected async Task<JsonElement?> FindExisting(IncidentIoClient client, TIdentifiers identifiers, CancellationToken cancellationToken)
    {
        var items = await client.ListAsync(ApiPath, CollectionKey, GetListQuery(identifiers), PageSize, cancellationToken);
        var matches = items.Where(i => IsMatch(i, identifiers)).ToList();

        return matches.Count switch
        {
            0 => null,
            1 => matches[0],
            _ => throw new IncidentIoException("AmbiguousMatch",
                $"Found {matches.Count} {typeof(TProperties).Name} resources matching {DescribeIdentifiers(identifiers)}. Make the identifier unique in incident.io first.")
        };
    }

    /// <summary>
    /// Serializes a model to snake_case JSON, excluding properties marked ReadOnly in [TypeProperty].
    /// Read-only properties are outputs (id, attributeIds...) and must never be sent to the API.
    /// </summary>
    protected internal static JsonObject SerializeWritable(TProperties properties)
    {
        var node = JsonSerializer.SerializeToNode(properties, typeof(TProperties), ApiJsonOptions)?.AsObject()
            ?? throw new InvalidOperationException("Serialization produced no JSON object.");

        foreach (var name in ReadOnlyApiNames)
            node.Remove(name);

        return node;
    }

    private static readonly string[] ReadOnlyApiNames = typeof(TProperties)
        .GetProperties(BindingFlags.Public | BindingFlags.Instance)
        .Where(p => p.GetCustomAttribute<TypePropertyAttribute>()?.Flags.HasFlag(ObjectTypePropertyFlags.ReadOnly) == true)
        .Select(p => JsonNamingPolicy.SnakeCaseLower.ConvertName(p.Name))
        .ToArray();

    /// <summary>Case-insensitive comparison of a string property on an API item.</summary>
    protected static bool StringEquals(JsonElement item, string property, string? expected)
        => expected is not null
           && item.TryGetProperty(property, out var value)
           && value.ValueKind == JsonValueKind.String
           && string.Equals(value.GetString(), expected, StringComparison.OrdinalIgnoreCase);

    private void EnsureCreateOnlyFieldsUnchanged(JsonObject payload, JsonElement existing)
    {
        foreach (var field in CreateOnlyFields)
        {
            if (payload[field] is not { } desired || !existing.TryGetProperty(field, out var actual))
                continue;

            var desiredText = desired.GetValueKind() == JsonValueKind.String ? desired.GetValue<string>() : desired.ToJsonString();
            var actualText = actual.ValueKind == JsonValueKind.String ? actual.GetString() : actual.GetRawText();
            if (!string.Equals(desiredText, actualText, StringComparison.Ordinal))
                throw new IncidentIoException("ImmutableProperty",
                    $"{typeof(TProperties).Name} '{field}' cannot be changed after creation (existing: '{actualText}', desired: '{desiredText}'). " +
                    "Delete it in incident.io or use a new name.");
        }
    }

    private static string DescribeIdentifiers(TIdentifiers identifiers)
        => JsonSerializer.Serialize(identifiers, typeof(TIdentifiers), ApiJsonOptions);

    /// <summary>Converts client errors into the SDK's error type so Bicep shows a clean message.</summary>
    private static async Task<T> WithErrorMapping<T>(Func<Task<T>> action)
    {
        try
        {
            return await action();
        }
        catch (IncidentIoException ex)
        {
            throw new ResourceErrorException(ex.Code, ex.Message);
        }
        catch (HttpRequestException ex)
        {
            var detail = ex.InnerException is { } inner ? $"{ex.Message} ({inner.Message})" : ex.Message;
            throw new ResourceErrorException("NetworkError", $"Could not reach incident.io: {detail}");
        }
    }
}

/// <summary>
/// Base handler for resources identified by a unique name (Severity, IncidentRole, IncidentStatus, ...).
/// Inheritors only declare the API path and JSON keys.
/// </summary>
public abstract class NamedResourceHandler<TProperties> : IncidentIoResourceHandlerBase<TProperties, NameIdentifiers>
    where TProperties : NameIdentifiers
{
    protected override bool IsMatch(JsonElement item, NameIdentifiers identifiers)
        => StringEquals(item, "name", identifiers.Name);

    protected override NameIdentifiers GetIdentifiers(TProperties properties) => new()
    {
        Name = properties.Name
    };
}
