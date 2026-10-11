using System.Text.Json;
using System.Text.Json.Nodes;

namespace Bicep.Extension.IncidentIo.Handlers;

public class CustomFieldOptionHandler : IncidentIoResourceHandlerBase<CustomFieldOption, CustomFieldOptionIdentifiers>
{
    protected override string ApiPath => "v1/custom_field_options";
    protected override string ItemKey => "custom_field_option";
    protected override string CollectionKey => "custom_field_options";
    protected override int? PageSize => 250;

    // The list endpoint requires the parent field; options are unique by value within it.
    protected override string? GetListQuery(CustomFieldOptionIdentifiers identifiers)
        => $"custom_field_id={Uri.EscapeDataString(identifiers.CustomFieldId)}";

    protected override bool IsMatch(JsonElement item, CustomFieldOptionIdentifiers identifiers)
        => StringEquals(item, "value", identifiers.Value);

    protected override IReadOnlyCollection<string> CreateOnlyFields => ["custom_field_id"];

    protected override async Task<JsonObject> BuildPayload(IncidentIoClient client, CustomFieldOption properties, JsonElement? existing, CancellationToken cancellationToken)
    {
        var payload = await base.BuildPayload(client, properties, existing, cancellationToken);

        // sort_key is optional on create but required on update: keep the current one when not specified.
        if (properties.SortKey is null && existing?.TryGetProperty("sort_key", out var current) == true)
            payload["sort_key"] = current.GetInt64();

        return payload;
    }

    protected override CustomFieldOptionIdentifiers GetIdentifiers(CustomFieldOption properties) => new()
    {
        CustomFieldId = properties.CustomFieldId,
        Value = properties.Value
    };
}
