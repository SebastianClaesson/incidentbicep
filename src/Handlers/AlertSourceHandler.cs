using System.Text.Json;
using System.Text.Json.Nodes;

namespace Bicep.Extension.IncidentIo.Handlers;

public class AlertSourceHandler : NamedResourceHandler<AlertSource>
{
    protected override string ApiPath => "v3/alert_sources";
    protected override string ItemKey => "alert_source";
    protected override string CollectionKey => "alert_sources";
    protected override string? RequestWrapperKey => "alert_source";

    protected override IReadOnlyCollection<string> CreateOnlyFields => ["source_type"];

    protected override async Task<JsonObject> BuildPayload(IncidentIoClient client, AlertSource properties, JsonElement? existing, CancellationToken cancellationToken)
    {
        var payload = await base.BuildPayload(client, properties, existing, cancellationToken);

        if (existing is null)
            payload.Remove("disabled"); // only accepted on update
        payload["is_private"] ??= false; // required on update

        // The API requires a label on each expression; default it to the reference, as Terraform does.
        if (payload["expressions"] is JsonArray expressions)
        {
            foreach (var expression in expressions.OfType<JsonObject>())
                expression["label"] ??= expression["reference"]?.DeepClone();

            var duplicate = expressions.OfType<JsonObject>().GroupBy(e => e["reference"]?.GetValue<string>()).FirstOrDefault(g => g.Count() > 1);
            if (duplicate is not null)
                throw new IncidentIoException("InvalidExpression", $"More than one expression has the reference '{duplicate.Key}'.");
        }

        return payload;
    }

    protected override void PopulateOutputs(AlertSource properties, JsonElement item)
    {
        base.PopulateOutputs(properties, item);
        properties.AlertEventsUrl = ReadString(item, "alert_events_url");
        properties.SecretToken = ReadString(item, "secret_token");
        properties.Version = item.TryGetProperty("version", out var v) && v.ValueKind == JsonValueKind.Number ? v.GetInt32() : null;
        properties.HeartbeatPingUrl = item.TryGetProperty("heartbeat_options", out var h) && h.ValueKind == JsonValueKind.Object
            ? ReadString(h, "ping_url")
            : null;
    }

    protected override async Task<AlertSource> FromApi(IncidentIoClient client, JsonElement item, CancellationToken cancellationToken)
    {
        var source = await base.FromApi(client, item, cancellationToken);
        PopulateOutputs(source, item);
        return source;
    }

    private static string? ReadString(JsonElement item, string property)
        => item.TryGetProperty(property, out var value) && value.ValueKind == JsonValueKind.String ? value.GetString() : null;
}
