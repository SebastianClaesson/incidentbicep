using System.Text.Json;
using System.Text.Json.Nodes;

namespace Bicep.Extension.IncidentIo.Handlers;

/// <summary>
/// Catalog types are created/updated via /v3/catalog_types. When 'attributes' is set, the schema is then
/// replaced via /v3/catalog_types/{id}/actions/update_schema, matching existing attributes by name so their
/// IDs — and the data stored against them on entries — survive redeployments.
/// </summary>
public class CatalogTypeHandler : NamedResourceHandler<CatalogType>
{
    protected override string ApiPath => "v3/catalog_types";
    protected override string ItemKey => "catalog_type";
    protected override string CollectionKey => "catalog_types";

    protected override IReadOnlyCollection<string> CreateOnlyFields => ["type_name"];

    protected override async Task<JsonObject> BuildPayload(IncidentIoClient client, CatalogType properties, JsonElement? existing, CancellationToken cancellationToken)
    {
        var payload = await base.BuildPayload(client, properties, existing, cancellationToken);
        payload.Remove("attributes"); // sent separately through update_schema
        return payload;
    }

    protected override async Task AfterWrite(IncidentIoClient client, CatalogType properties, JsonElement item, CancellationToken cancellationToken)
    {
        PopulateOutputs(properties, item);
        if (properties.Attributes is null)
            return;

        var schema = await CatalogSchema.GetAsync(client, properties.Id!, cancellationToken);
        var body = new JsonObject
        {
            ["version"] = schema.Version,
            ["attributes"] = BuildSchemaAttributes(properties.Attributes, schema)
        };

        var response = await client.PostAsync(
            $"v3/catalog_types/{Uri.EscapeDataString(properties.Id!)}/actions/update_schema", body, cancellationToken);
        PopulateOutputs(properties, response.GetProperty("catalog_type"));
    }

    internal static JsonArray BuildSchemaAttributes(IEnumerable<CatalogTypeAttribute> desired, CatalogSchema current)
    {
        var duplicate = desired.GroupBy(a => a.Name, StringComparer.OrdinalIgnoreCase).FirstOrDefault(g => g.Count() > 1);
        if (duplicate is not null)
            throw new IncidentIoException("InvalidAttributes", $"Catalog type attribute '{duplicate.Key}' is defined more than once.");

        var result = new JsonArray();
        foreach (var attribute in desired)
        {
            var node = JsonSerializer.SerializeToNode(attribute, ApiJsonOptions)!.AsObject();
            node["array"] ??= false;
            if (current.IdsByName.TryGetValue(attribute.Name, out var id))
                node["id"] = id;
            result.Add(node);
        }
        return result;
    }

    protected override void PopulateOutputs(CatalogType properties, JsonElement item)
    {
        base.PopulateOutputs(properties, item);
        if (item.TryGetProperty("type_name", out var typeName))
            properties.TypeName = typeName.GetString();
        properties.AttributeIds = CatalogSchema.FromCatalogType(item).IdsByName
            .ToDictionary(kv => kv.Key, kv => kv.Value);
    }

    protected override async Task<CatalogType> FromApi(IncidentIoClient client, JsonElement item, CancellationToken cancellationToken)
    {
        // 'schema' is not part of the model's shape, so read the scalar fields first, then map the attributes.
        var catalogType = await base.FromApi(client, item, cancellationToken);
        PopulateOutputs(catalogType, item);
        catalogType.Attributes = CatalogSchema.FromCatalogType(item).Attributes
            .Select(a => new CatalogTypeAttribute { Name = a.Name, Type = a.Type, Array = a.Array })
            .ToArray();
        return catalogType;
    }
}

/// <summary>
/// The attribute schema of a catalog type, as returned by the API.
/// Shared by CatalogType (schema updates) and CatalogEntry (attribute name → ID resolution).
/// </summary>
public sealed record CatalogSchema(long Version, IReadOnlyList<CatalogSchema.Attribute> Attributes)
{
    public sealed record Attribute(string Id, string Name, string Type, bool Array);

    public IReadOnlyDictionary<string, string> IdsByName { get; } =
        Attributes.GroupBy(a => a.Name, StringComparer.OrdinalIgnoreCase)
            .ToDictionary(g => g.Key, g => g.First().Id, StringComparer.OrdinalIgnoreCase);

    public static async Task<CatalogSchema> GetAsync(IncidentIoClient client, string catalogTypeId, CancellationToken cancellationToken)
    {
        var response = await client.GetAsync($"v3/catalog_types/{Uri.EscapeDataString(catalogTypeId)}", cancellationToken);
        return FromCatalogType(response.GetProperty("catalog_type"));
    }

    public static CatalogSchema FromCatalogType(JsonElement catalogType)
    {
        if (!catalogType.TryGetProperty("schema", out var schema) || schema.ValueKind != JsonValueKind.Object)
            return new CatalogSchema(0, []);

        var version = schema.TryGetProperty("version", out var v) && v.ValueKind == JsonValueKind.Number ? v.GetInt64() : 0;
        var attributes = schema.TryGetProperty("attributes", out var attrs) && attrs.ValueKind == JsonValueKind.Array
            ? attrs.EnumerateArray().Select(a => new Attribute(
                a.GetProperty("id").GetString()!,
                a.GetProperty("name").GetString()!,
                a.TryGetProperty("type", out var t) ? t.GetString() ?? "" : "",
                a.TryGetProperty("array", out var arr) && arr.ValueKind == JsonValueKind.True)).ToList()
            : [];

        return new CatalogSchema(version, attributes);
    }
}
