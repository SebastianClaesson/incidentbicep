using System.Text.Json;
using System.Text.Json.Nodes;

namespace Bicep.Extension.IncidentIo.Handlers;

public class MaintenanceWindowHandler : NamedResourceHandler<MaintenanceWindow>
{
    protected override string ApiPath => "v1/maintenance_windows";
    protected override string ItemKey => "maintenance_window";
    protected override string CollectionKey => "maintenance_windows";
    protected override int? PageSize => 50;

    // Archived windows are kept in the list; never match (and so never resurrect) one.
    protected override bool IsMatch(JsonElement item, NameIdentifiers identifiers)
        => base.IsMatch(item, identifiers)
           && (!item.TryGetProperty("archived_at", out var archived) || archived.ValueKind == JsonValueKind.Null);

    protected override async Task<JsonObject> BuildPayload(IncidentIoClient client, MaintenanceWindow properties, JsonElement? existing, CancellationToken cancellationToken)
    {
        var payload = await base.BuildPayload(client, properties, existing, cancellationToken);
        payload.Remove("lead_id");
        payload["lead"] = new JsonObject { ["id"] = properties.LeadId };
        payload["show_in_sidebar"] ??= false; // required by the API
        return payload;
    }

    protected override Task<MaintenanceWindow> FromApi(IncidentIoClient client, JsonElement item, CancellationToken cancellationToken)
    {
        var node = JsonNode.Parse(item.GetRawText())!.AsObject();
        node["lead_id"] = item.TryGetProperty("lead", out var lead) && lead.TryGetProperty("id", out var id) ? id.GetString() : "";
        var window = node.Deserialize<MaintenanceWindow>(ApiJsonOptions)!;
        PopulateOutputs(window, item);
        return Task.FromResult(window);
    }
}
