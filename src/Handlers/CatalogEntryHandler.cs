using System.Text.Json;
using System.Text.Json.Nodes;

namespace Bicep.Extension.IncidentIo.Handlers;

/// <summary>
/// Catalog entries are found by external ID when set (recommended), otherwise by name within the catalog type.
/// Attribute values may be keyed by attribute name; names are resolved to IDs from the type's schema.
/// </summary>
public class CatalogEntryHandler : IncidentIoResourceHandlerBase<CatalogEntry, CatalogEntryIdentifiers>
{
    protected override string ApiPath => "v3/catalog_entries";
    protected override string ItemKey => "catalog_entry";
    protected override string CollectionKey => "catalog_entries";
    protected override int? PageSize => 250;

    // 'catalog_type_id' is not in the update payload; an entry cannot move between types.
    protected override IReadOnlyCollection<string> CreateOnlyFields => ["catalog_type_id"];

    protected override string? GetListQuery(CatalogEntryIdentifiers identifiers)
    {
        var query = $"catalog_type_id={Uri.EscapeDataString(identifiers.CatalogTypeId)}";
        // 'identifier' filters server side by ID / external ID / aliases. It only matches names when the type
        // has useNameAsIdentifier, so without an external ID we list the whole type and match the name locally.
        return string.IsNullOrEmpty(identifiers.ExternalId)
            ? query
            : $"{query}&identifier={Uri.EscapeDataString(identifiers.ExternalId)}";
    }

    protected override bool IsMatch(JsonElement item, CatalogEntryIdentifiers identifiers)
        => string.IsNullOrEmpty(identifiers.ExternalId)
            ? StringEquals(item, "name", identifiers.Name)
            : item.TryGetProperty("external_id", out var externalId) && externalId.GetString() == identifiers.ExternalId;

    protected override CatalogEntryIdentifiers GetIdentifiers(CatalogEntry properties) => new()
    {
        CatalogTypeId = properties.CatalogTypeId,
        Name = properties.Name,
        ExternalId = properties.ExternalId
    };

    protected override async Task<JsonObject> BuildPayload(IncidentIoClient client, CatalogEntry properties, JsonElement? existing, CancellationToken cancellationToken)
    {
        var payload = await base.BuildPayload(client, properties, existing, cancellationToken);

        if (properties.AttributeValues is null)
        {
            // attribute_values is required by the API. Send it empty, and on update restrict the write to
            // zero attributes, so omitting 'attributeValues' in Bicep leaves existing values untouched.
            payload["attribute_values"] = new JsonObject();
            if (existing is not null)
                payload["update_attributes"] = new JsonArray();
            return payload;
        }

        var schema = await CatalogSchema.GetAsync(client, properties.CatalogTypeId, cancellationToken);
        payload["attribute_values"] = BuildAttributeValues(properties.AttributeValues, schema);
        return payload;
    }

    /// <summary>
    /// Converts { "Owner": { value: "x" } } into the API shape { "&lt;attributeId&gt;": { "value": { "literal": "x" } } }.
    /// </summary>
    internal static JsonObject BuildAttributeValues(IDictionary<string, CatalogEntryAttributeValue> values, CatalogSchema schema)
    {
        var knownIds = schema.Attributes.Select(a => a.Id).ToHashSet(StringComparer.Ordinal);
        var result = new JsonObject();

        foreach (var (key, binding) in values)
        {
            var attributeId = knownIds.Contains(key) ? key
                : schema.IdsByName.TryGetValue(key, out var id) ? id
                : throw new IncidentIoException("UnknownAttribute",
                    $"Catalog attribute '{key}' does not exist on this catalog type. Known attributes: " +
                    (schema.Attributes.Count == 0 ? "(none)" : string.Join(", ", schema.Attributes.Select(a => a.Name))) + ".");

            if (binding.Value is not null && binding.ArrayValue is not null)
                throw new IncidentIoException("InvalidAttributeValue", $"Catalog attribute '{key}': set either 'value' or 'arrayValue', not both.");

            if (result.ContainsKey(attributeId))
                throw new IncidentIoException("InvalidAttributeValue", $"Catalog attribute '{key}' is set more than once (by name and by ID).");

            var node = new JsonObject();
            if (binding.Value is not null)
                node["value"] = Literal(binding.Value);
            if (binding.ArrayValue is not null)
                node["array_value"] = new JsonArray(binding.ArrayValue.Select(v => (JsonNode)Literal(v)).ToArray());

            result[attributeId] = node;
        }

        return result;
    }

    private static JsonObject Literal(string value) => new() { ["literal"] = value };

    protected override async Task<CatalogEntry> FromApi(IncidentIoClient client, JsonElement item, CancellationToken cancellationToken)
    {
        // The API returns attribute values with extra metadata (labels, entry references), so map them by hand.
        var schema = await CatalogSchema.GetAsync(client, item.GetProperty("catalog_type_id").GetString()!, cancellationToken);
        var namesById = schema.Attributes.ToDictionary(a => a.Id, a => a.Name);

        var entry = new CatalogEntry
        {
            CatalogTypeId = item.GetProperty("catalog_type_id").GetString()!,
            Name = item.GetProperty("name").GetString()!,
            ExternalId = item.TryGetProperty("external_id", out var ext) ? ext.GetString() : null,
            Rank = item.TryGetProperty("rank", out var rank) && rank.ValueKind == JsonValueKind.Number ? rank.GetInt32() : null,
            Aliases = item.TryGetProperty("aliases", out var aliases) && aliases.ValueKind == JsonValueKind.Array
                ? aliases.EnumerateArray().Select(a => a.GetString()!).ToArray()
                : null,
            AttributeValues = []
        };

        if (item.TryGetProperty("attribute_values", out var values) && values.ValueKind == JsonValueKind.Object)
        {
            foreach (var attribute in values.EnumerateObject())
            {
                var key = namesById.GetValueOrDefault(attribute.Name, attribute.Name);
                entry.AttributeValues[key] = new CatalogEntryAttributeValue
                {
                    Value = attribute.Value.TryGetProperty("value", out var v) && v.ValueKind == JsonValueKind.Object
                        ? ReadLiteral(v) : null,
                    ArrayValue = attribute.Value.TryGetProperty("array_value", out var av) && av.ValueKind == JsonValueKind.Array
                        ? av.EnumerateArray().Select(ReadLiteral).OfType<string>().ToArray() : null
                };
            }
        }

        PopulateOutputs(entry, item);
        return entry;
    }

    private static string? ReadLiteral(JsonElement binding)
        => binding.TryGetProperty("literal", out var literal) ? literal.GetString() : null;
}
