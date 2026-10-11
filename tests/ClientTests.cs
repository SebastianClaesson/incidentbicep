using System.Net;
using System.Text.Json.Nodes;

namespace Bicep.Extension.IncidentIo.Tests;

public class ClientTests
{
    /// <summary>Fake transport: replies from a queue and records requests.</summary>
    private sealed class FakeHandler(params Func<HttpRequestMessage, HttpResponseMessage>[] replies) : HttpMessageHandler
    {
        private int next;
        public List<HttpRequestMessage> Requests { get; } = [];

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            Requests.Add(request);
            return Task.FromResult(replies[Math.Min(next++, replies.Length - 1)](request));
        }
    }

    private static HttpResponseMessage Json(HttpStatusCode status, string body) =>
        new(status) { Content = new StringContent(body, System.Text.Encoding.UTF8, "application/json") };

    private static IncidentIoClient CreateClient(FakeHandler handler, string? baseUrl = null) =>
        new(new Configuration { ApiKey = "secret-key", BaseUrl = baseUrl }, new HttpClient(handler), (_, _) => Task.CompletedTask);

    [Theory]
    [InlineData(null, "https://api.incident.io/")]
    [InlineData("https://api.incident.io", "https://api.incident.io/")]
    [InlineData("https://proxy.example.com/incidentio/", "https://proxy.example.com/incidentio/")]
    [InlineData("http://localhost:8787", "http://localhost:8787/")]
    [InlineData("http://127.0.0.1:8787", "http://127.0.0.1:8787/")]
    public void Accepts_Https_And_Loopback_Http(string? input, string expected)
    {
        Assert.Equal(expected, IncidentIoClient.ValidateBaseUrl(input).ToString());
    }

    [Theory]
    [InlineData("http://api.incident.io")]
    [InlineData("ftp://api.incident.io")]
    [InlineData("https://user:pass@api.incident.io")]
    [InlineData("not a url")]
    public void Rejects_Insecure_Or_Invalid_Urls(string input)
    {
        var ex = Assert.Throws<IncidentIoException>(() => IncidentIoClient.ValidateBaseUrl(input));
        Assert.Equal("InvalidConfiguration", ex.Code);
    }

    [Fact]
    public async Task Sends_Bearer_Token()
    {
        var handler = new FakeHandler(_ => Json(HttpStatusCode.OK, """{"severities":[]}"""));
        using var client = CreateClient(handler);

        await client.ListAsync("v1/severities", "severities", null, null, CancellationToken.None);

        Assert.Equal("Bearer", handler.Requests[0].Headers.Authorization!.Scheme);
        Assert.Equal("secret-key", handler.Requests[0].Headers.Authorization!.Parameter);
    }

    [Fact]
    public async Task Follows_Pagination_Cursor()
    {
        var handler = new FakeHandler(
            _ => Json(HttpStatusCode.OK, """{"items":[{"id":"a"},{"id":"b"}],"pagination_meta":{"after":"b","page_size":2}}"""),
            _ => Json(HttpStatusCode.OK, """{"items":[{"id":"c"}],"pagination_meta":{"page_size":2}}"""));
        using var client = CreateClient(handler);

        var items = await client.ListAsync("v3/catalog_entries", "items", "catalog_type_id=01CT", 2, CancellationToken.None);

        Assert.Equal(["a", "b", "c"], items.Select(i => i.GetProperty("id").GetString()));
        Assert.Equal("/v3/catalog_entries?catalog_type_id=01CT&page_size=2&after=b", handler.Requests[1].RequestUri!.PathAndQuery);
    }

    [Fact]
    public async Task Retries_Rate_Limited_Requests()
    {
        var handler = new FakeHandler(
            _ => new HttpResponseMessage(HttpStatusCode.TooManyRequests),
            _ => Json(HttpStatusCode.OK, """{"severities":[{"id":"x"}]}"""));
        using var client = CreateClient(handler);

        var items = await client.ListAsync("v1/severities", "severities", null, null, CancellationToken.None);

        Assert.Single(items);
        Assert.Equal(2, handler.Requests.Count);
    }

    [Fact]
    public async Task Does_Not_Retry_Post_On_Network_Failure()
    {
        var handler = new FakeHandler(_ => throw new HttpRequestException("connection reset"));
        using var client = CreateClient(handler);

        await Assert.ThrowsAsync<HttpRequestException>(() => client.PostAsync("v1/severities", new JsonObject(), CancellationToken.None));
        Assert.Single(handler.Requests); // a retried POST could create a duplicate
    }

    [Fact]
    public async Task Retries_Get_On_Network_Failure()
    {
        var calls = 0;
        var handler = new FakeHandler(_ => ++calls == 1
            ? throw new HttpRequestException("connection reset")
            : Json(HttpStatusCode.OK, """{"severities":[]}"""));
        using var client = CreateClient(handler);

        await client.ListAsync("v1/severities", "severities", null, null, CancellationToken.None);

        Assert.Equal(2, calls);
    }

    [Fact]
    public async Task Error_Includes_Status_And_Body_But_Not_The_Key()
    {
        var handler = new FakeHandler(_ => Json(HttpStatusCode.UnprocessableEntity, """{"errors":[{"message":"name must be unique"}]}"""));
        using var client = CreateClient(handler);

        var ex = await Assert.ThrowsAsync<IncidentIoException>(() => client.PostAsync("v1/severities", new JsonObject(), CancellationToken.None));

        Assert.Equal("422", ex.Code);
        Assert.Contains("name must be unique", ex.Message);
        Assert.DoesNotContain("secret-key", ex.Message);
    }

    [Fact]
    public void Retry_After_Header_Is_Honoured_And_Capped()
    {
        var response = new HttpResponseMessage(HttpStatusCode.TooManyRequests);
        response.Headers.RetryAfter = new System.Net.Http.Headers.RetryConditionHeaderValue(TimeSpan.FromSeconds(3));
        Assert.Equal(TimeSpan.FromSeconds(3), IncidentIoClient.GetRetryDelay(response, 1));

        response.Headers.RetryAfter = new System.Net.Http.Headers.RetryConditionHeaderValue(TimeSpan.FromMinutes(10));
        Assert.Equal(TimeSpan.FromSeconds(30), IncidentIoClient.GetRetryDelay(response, 1));
    }
}
