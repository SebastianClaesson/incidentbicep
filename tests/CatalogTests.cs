using System.Text.Json;
using Bicep.Extension.IncidentIo.Handlers;

namespace Bicep.Extension.IncidentIo.Tests;

public class CatalogTests
{
    private static readonly CatalogSchema Schema = new(4,
    [
        new("01TIER", "Tier", "String", false),
        new("01OWNER", "Owner", "Custom[\"Team\"]", false),
        new("01RUNBOOKS", "Runbooks", "String", true)
    ]);

    [Fact]
    public void Attribute_Names_Resolve_To_Ids()
    {
        var values = new Dictionary<string, CatalogEntryAttributeValue>
        {
            ["tier"] = new() { Value = "1" },                       // case-insensitive name
            ["01OWNER"] = new() { Value = "payments" },             // ID passes through
            ["Runbooks"] = new() { ArrayValue = ["https://a", "https://b"] }
        };

        var json = CatalogEntryHandler.BuildAttributeValues(values, Schema);

        Assert.Equal("1", json["01TIER"]!["value"]!["literal"]!.GetValue<string>());
        Assert.Equal("payments", json["01OWNER"]!["value"]!["literal"]!.GetValue<string>());
        Assert.Equal("https://b", json["01RUNBOOKS"]!["array_value"]![1]!["literal"]!.GetValue<string>());
    }

    [Fact]
    public void Unknown_Attribute_Lists_Valid_Names()
    {
        var values = new Dictionary<string, CatalogEntryAttributeValue> { ["Teir"] = new() { Value = "1" } };

        var ex = Assert.Throws<IncidentIoException>(() => CatalogEntryHandler.BuildAttributeValues(values, Schema));

        Assert.Equal("UnknownAttribute", ex.Code);
        Assert.Contains("Tier, Owner, Runbooks", ex.Message);
    }

    [Fact]
    public void Value_And_ArrayValue_Together_Is_Rejected()
    {
        var values = new Dictionary<string, CatalogEntryAttributeValue> { ["Tier"] = new() { Value = "1", ArrayValue = ["2"] } };

        Assert.Throws<IncidentIoException>(() => CatalogEntryHandler.BuildAttributeValues(values, Schema));
    }

    [Fact]
    public void Same_Attribute_By_Name_And_Id_Is_Rejected()
    {
        var values = new Dictionary<string, CatalogEntryAttributeValue>
        {
            ["Tier"] = new() { Value = "1" },
            ["01TIER"] = new() { Value = "2" }
        };

        Assert.Throws<IncidentIoException>(() => CatalogEntryHandler.BuildAttributeValues(values, Schema));
    }

    [Fact]
    public void Schema_Update_Keeps_Existing_Attribute_Ids()
    {
        CatalogTypeAttribute[] desired =
        [
            new() { Name = "Tier", Type = "String" },
            new() { Name = "Region", Type = "String", Mode = CatalogAttributeMode.api }
        ];

        var json = CatalogTypeHandler.BuildSchemaAttributes(desired, Schema);

        Assert.Equal("01TIER", json[0]!["id"]!.GetValue<string>());
        Assert.False(json[0]!["array"]!.GetValue<bool>()); // defaulted
        Assert.Null(json[1]!["id"]);                       // new attribute
        Assert.Equal("api", json[1]!["mode"]!.GetValue<string>());
    }

    [Fact]
    public void Duplicate_Attribute_Names_Are_Rejected()
    {
        CatalogTypeAttribute[] desired = [new() { Name = "Tier", Type = "String" }, new() { Name = "tier", Type = "Number" }];

        Assert.Throws<IncidentIoException>(() => CatalogTypeHandler.BuildSchemaAttributes(desired, Schema));
    }

    [Fact]
    public void Schema_Is_Read_From_Catalog_Type_Response()
    {
        using var doc = JsonDocument.Parse("""
            { "id": "01CT", "schema": { "version": 7, "attributes": [
                { "id": "01A", "name": "Tier", "type": "String", "array": false, "mode": "api" },
                { "id": "01B", "name": "Links", "type": "String", "array": true, "mode": "" } ] } }
            """);

        var schema = CatalogSchema.FromCatalogType(doc.RootElement);

        Assert.Equal(7, schema.Version);
        Assert.Equal("01B", schema.IdsByName["links"]);
        Assert.True(schema.Attributes[1].Array);
    }
}
