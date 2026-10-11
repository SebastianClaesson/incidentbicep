using System.Text.Json;
using Bicep.Extension.IncidentIo.Handlers;

namespace Bicep.Extension.IncidentIo.Tests;

/// <summary>
/// Verifies models become the snake_case JSON incident.io expects, and that outputs are never sent.
/// </summary>
public class SerializationTests
{
    [Fact]
    public void Properties_Become_SnakeCase()
    {
        var field = new CustomField
        {
            Name = "Affected Area",
            Description = "d",
            FieldType = CustomFieldType.single_select,
            CatalogTypeId = "01CT",
            GroupByCatalogAttributeId = "01ATTR"
        };

        var json = IncidentIoResourceHandlerBase<CustomField, NameIdentifiers>.SerializeWritable(field);

        Assert.Equal("single_select", json["field_type"]!.GetValue<string>());
        Assert.Equal("01CT", json["catalog_type_id"]!.GetValue<string>());
        Assert.Equal("01ATTR", json["group_by_catalog_attribute_id"]!.GetValue<string>());
        Assert.Null(json["FieldType"]);
        Assert.Null(json["fieldType"]);
    }

    [Fact]
    public void ReadOnly_Outputs_Are_Not_Sent()
    {
        var type = new CatalogType
        {
            Name = "Service",
            Description = "d",
            Id = "01SHOULDNOTBESENT",
            AttributeIds = new() { ["Tier"] = "01X" }
        };

        var json = IncidentIoResourceHandlerBase<CatalogType, NameIdentifiers>.SerializeWritable(type);

        Assert.False(json.ContainsKey("id"));
        Assert.False(json.ContainsKey("attribute_ids"));
        Assert.Equal("Service", json["name"]!.GetValue<string>());
    }

    [Fact]
    public void Nulls_Are_Omitted()
    {
        var severity = new Severity { Name = "Minor", Description = "d" };

        var json = IncidentIoResourceHandlerBase<Severity, NameIdentifiers>.SerializeWritable(severity);

        Assert.False(json.ContainsKey("rank"));
    }

    [Fact]
    public void Dictionary_Keys_Are_Not_Renamed()
    {
        var type = new CatalogType
        {
            Name = "Service",
            Description = "d",
            Annotations = new() { ["ManagedBy"] = "bicep" }
        };

        var json = IncidentIoResourceHandlerBase<CatalogType, NameIdentifiers>.SerializeWritable(type);

        Assert.Equal("bicep", json["annotations"]!["ManagedBy"]!.GetValue<string>());
    }

    [Fact]
    public void Enums_Use_Exact_Member_Names_Over_Grpc_Too()
    {
        // The SDK serializes with camelCase + JsonStringEnumConverter(camelCase); the property-level
        // converter must win so Bicep sees 'multi_select', not 'multi_select' re-cased or a number.
        var sdkOptions = new JsonSerializerOptions
        {
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
            Converters = { new System.Text.Json.Serialization.JsonStringEnumConverter(JsonNamingPolicy.CamelCase) }
        };
        var field = new CustomField { Name = "n", Description = "d", FieldType = CustomFieldType.multi_select };

        var json = JsonSerializer.Serialize(field, sdkOptions);
        var roundTrip = JsonSerializer.Deserialize<CustomField>(json, sdkOptions)!;

        Assert.Contains("\"fieldType\":\"multi_select\"", json);
        Assert.Equal(CustomFieldType.multi_select, roundTrip.FieldType);
    }
}
