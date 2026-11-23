using System.Text.Json;
using System.Text.Json.Nodes;
using Bicep.Extension.IncidentIo.Handlers;

namespace Bicep.Extension.IncidentIo.Tests;

public class EngineTests
{
    private static string Api(EngineBinding binding) => EngineBindingApiConverter.ToApi(binding).ToJsonString();

    [Fact]
    public void Shorthands_Expand_Like_Terraform()
    {
        Assert.Equal("""{"value":{"literal":"x"}}""", Api(new() { ValueLiteral = "x" }));
        Assert.Equal("""{"value":{"reference":"payload.team"}}""", Api(new() { ValueReference = "payload.team" }));
        Assert.Equal("""{"array_value":[{"literal":"a"},{"literal":"b"}]}""", Api(new() { Values = ["a", "b"] }));
        Assert.Equal("expressions[\"team\"]", EngineBindingApiConverter.ToApi(new() { ExpressionRef = "team" })["value"]!["reference"]!.GetValue<string>());
        Assert.Equal("""{"array_value":[{"reference":"r"}]}""", Api(new() { ArrayValue = [new() { Reference = "r" }] }));
    }

    [Fact]
    public void Expression_Reference_Matches_Terraform_Format()
    {
        Assert.Equal("expressions[\"team\"]", EngineBindingApiConverter.ExpressionReference("team"));
    }

    [Fact]
    public void More_Than_One_Form_Is_Rejected()
    {
        var ex = Assert.Throws<IncidentIoException>(() => Api(new() { ValueLiteral = "x", Values = ["y"] }));
        Assert.Contains("valueLiteral and values", ex.Message);
    }

    [Fact]
    public void Bindings_Nested_In_Models_Use_The_Api_Shape()
    {
        var group = new EngineConditionGroup
        {
            Conditions = [new() { Subject = "alert.title", Operation = "contains", ParamBindings = [new() { ValueLiteral = "db" }] }]
        };

        var json = JsonSerializer.Serialize(group, IncidentIoResourceHandlerBase<AlertSource, NameIdentifiers>.ApiJsonOptions);

        Assert.Equal("""{"conditions":[{"subject":"alert.title","operation":"contains","param_bindings":[{"value":{"literal":"db"}}]}]}""", json);
    }

    [Fact]
    public void Condition_ParamBindings_Default_To_Empty_List()
    {
        // The API requires param_bindings even for operations without parameters (e.g. is_set).
        var json = JsonSerializer.Serialize(new EngineCondition { Subject = "alert.title", Operation = "is_set" },
            IncidentIoResourceHandlerBase<AlertSource, NameIdentifiers>.ApiJsonOptions);

        Assert.Contains("\"param_bindings\":[]", json);
    }

    [Fact]
    public void Bindings_Read_Back_From_Api()
    {
        var binding = JsonSerializer.Deserialize<EngineBinding>("""{"array_value":[{"literal":"a"},{"reference":"b"}]}""",
            IncidentIoResourceHandlerBase<AlertSource, NameIdentifiers>.ApiJsonOptions)!;

        Assert.Equal("a", binding.ArrayValue![0].Literal);
        Assert.Equal("b", binding.ArrayValue![1].Reference);
    }
}

public class JsonCompareTests
{
    private static JsonElement E(string json) => JsonDocument.Parse(json).RootElement;

    [Theory]
    [InlineData("""{"a":1}""", """{"a":1,"extra":true}""", true)]          // API adds fields
    [InlineData("""{"a":1}""", """{"a":2}""", false)]
    [InlineData("""{"a":1.0}""", """{"a":1}""", true)]                     // numerically equal
    [InlineData("""{"t":"2026-01-05T08:00:00Z"}""", """{"t":"2026-01-05T08:00:00.000Z"}""", true)]
    [InlineData("""{"t":"2026-01-05T08:00:00Z"}""", """{"t":"2026-01-05T10:00:00+02:00"}""", true)]
    [InlineData("""{"t":"2026-01-05T08:00:00Z"}""", """{"t":"2026-01-05T08:00:01Z"}""", false)]
    [InlineData("""{"l":[1,2]}""", """{"l":[2,1]}""", false)]              // order matters by default
    [InlineData("""{"l":[]}""", """{}""", true)]                           // empty matches absent
    [InlineData("""{"users":[{"id":"u1"}]}""", """{"users":[{"id":"u1","name":"Alice"}]}""", true)]
    [InlineData("""{"b":true}""", """{"b":false}""", false)]
    public void IsSubset(string desired, string actual, bool expected)
    {
        Assert.Equal(expected, JsonCompare.IsSubset(JsonNode.Parse(desired), E(actual)));
    }
}
