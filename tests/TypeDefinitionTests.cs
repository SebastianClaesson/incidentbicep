using System.Text.Json;
using Bicep.Local.Extension.Builder.Models;
using Bicep.Local.Extension.Types;
using Bicep.Local.Extension.Types.Models;

namespace Bicep.Extension.IncidentIo.Tests;

/// <summary>
/// Generates the Bicep type schema exactly as the extension host does at startup.
/// A model property the SDK cannot represent fails here instead of at deploy time.
/// </summary>
public class TypeDefinitionTests
{
    private static JsonDocument GenerateTypes(out JsonDocument index)
    {
        var provider = new TypeProvider([typeof(Configuration).Assembly], new ConfigurationTypeContainer(typeof(Configuration)));
        var builder = new TypeDefinitionBuilder(new ExtensionInfo("incidentio", "0.0.0", true), provider);
        var definition = builder.GenerateTypeDefinition();

        index = JsonDocument.Parse(definition.IndexFileContent);
        return JsonDocument.Parse(definition.TypeFileContents["types.json"]);
    }

    [Fact]
    public void All_Resource_Types_Are_Registered()
    {
        using var types = GenerateTypes(out var index);
        var resources = index.RootElement.GetProperty("resources").EnumerateObject().Select(p => p.Name).ToHashSet();

        string[] expected =
        [
            "Severity", "IncidentRole", "IncidentStatus", "CustomField", "CustomFieldOption",
            "CatalogType", "CatalogEntry", "AlertAttribute"
        ];
        foreach (var name in expected)
            Assert.Contains(resources, r => r.StartsWith(name, StringComparison.Ordinal));
        Assert.Equal(expected.Length, resources.Count);
    }

    [Fact]
    public void Every_Property_Of_Every_Model_Is_In_The_Schema()
    {
        // Guards against SDK regressions that silently drop properties (0.39.x skipped repeated array types).
        using var types = GenerateTypes(out _);
        var objects = types.RootElement.EnumerateArray()
            .Where(t => t.TryGetProperty("$type", out var kind) && kind.GetString() == "ObjectType")
            .ToDictionary(t => t.GetProperty("name").GetString()!, t => t.GetProperty("properties"));

        var modelTypes = typeof(Configuration).Assembly.GetTypes()
            .Where(t => t.Namespace == "Bicep.Extension.IncidentIo" && t.IsClass && t.IsPublic && objects.ContainsKey(t.Name));

        foreach (var model in modelTypes)
        {
            foreach (var property in model.GetProperties())
            {
                var camel = char.ToLowerInvariant(property.Name[0]) + property.Name[1..];
                Assert.True(objects[model.Name].TryGetProperty(camel, out _), $"{model.Name}.{camel} missing from Bicep schema");
            }
        }
    }

    [Fact]
    public void ApiKey_Is_Marked_Sensitive()
    {
        using var types = GenerateTypes(out _);
        var config = types.RootElement.EnumerateArray()
            .First(t => t.TryGetProperty("name", out var n) && n.GetString() == nameof(Configuration));
        var typeRef = config.GetProperty("properties").GetProperty("apiKey").GetProperty("type").GetProperty("$ref").GetString()!;
        var stringType = types.RootElement[int.Parse(typeRef.TrimStart('#', '/'))];

        Assert.True(stringType.TryGetProperty("sensitive", out var sensitive) && sensitive.GetBoolean());
    }
}
