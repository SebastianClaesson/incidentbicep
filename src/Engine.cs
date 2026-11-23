using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.Json.Serialization;
using Bicep.Local.Extension.Types.Attributes;

namespace Bicep.Extension.IncidentIo;

// ──────────────────────────────────────────────
// incident.io "engine" building blocks, shared by alert sources, maintenance windows and escalation paths.
// Shapes and shorthands mirror the Terraform provider (valueLiteral, valueReference, values, expressionRef).
// ──────────────────────────────────────────────

/// <summary>A single literal or reference value.</summary>
public class EngineValue
{
    [TypeProperty("A fixed value. A catalog entry ID is a literal, not a reference.")]
    public string? Literal { get; set; }

    [TypeProperty("A reference into the scope, such as 'payload.team' or 'alert.title'.")]
    public string? Reference { get; set; }
}

/// <summary>
/// A parameter binding. Set exactly one of: value, arrayValue, valueLiteral, valueReference, values, expressionRef.
/// The shorthands are expanded before the request is sent.
/// </summary>
public class EngineBinding
{
    [TypeProperty("The literal or reference value.")]
    public EngineValue? Value { get; set; }

    [TypeProperty("A list of literal or reference values.")]
    public EngineValue[]? ArrayValue { get; set; }

    [TypeProperty("Shorthand for value: { literal: ... }.")]
    public string? ValueLiteral { get; set; }

    [TypeProperty("Shorthand for value: { reference: ... }.")]
    public string? ValueReference { get; set; }

    [TypeProperty("Shorthand for an arrayValue of literals.")]
    public string[]? Values { get; set; }

    [TypeProperty("The name of an expression on this resource whose result becomes the value. Shorthand for value: { reference: 'expressions[\"name\"]' }.")]
    public string? ExpressionRef { get; set; }
}

/// <summary>A condition: '{subject} {operation} {paramBindings}'.</summary>
public class EngineCondition
{
    [TypeProperty("The reference this condition tests, e.g. 'alert.title'.", Azure.Bicep.Types.Concrete.ObjectTypePropertyFlags.Required)]
    public required string Subject { get; set; }

    [TypeProperty("The operation, e.g. 'is_set', 'one_of', 'contains'. Available operations depend on the subject's type.", Azure.Bicep.Types.Concrete.ObjectTypePropertyFlags.Required)]
    public required string Operation { get; set; }

    [TypeProperty("Parameters for the operation. Omit (or empty) for operations without parameters.")]
    public EngineBinding[]? ParamBindings { get; set; } = [];
}

/// <summary>A group of conditions that must all be satisfied.</summary>
public class EngineConditionGroup
{
    [TypeProperty("All of these conditions must be satisfied for the group to match.", Azure.Bicep.Types.Concrete.ObjectTypePropertyFlags.Required)]
    public required EngineCondition[] Conditions { get; set; }
}

/// <summary>
/// Converts the Bicep-facing <see cref="EngineBinding"/> (with Terraform-style shorthands) to and from the API shape
/// { "value": {...} } / { "array_value": [...] }. Registered only on the API serializer, never on the SDK's gRPC serializer.
/// </summary>
public sealed class EngineBindingApiConverter : JsonConverter<EngineBinding>
{
    public override EngineBinding? Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
    {
        using var doc = JsonDocument.ParseValue(ref reader);
        var root = doc.RootElement;
        return new EngineBinding
        {
            Value = root.TryGetProperty("value", out var v) && v.ValueKind == JsonValueKind.Object ? ReadValue(v) : null,
            ArrayValue = root.TryGetProperty("array_value", out var av) && av.ValueKind == JsonValueKind.Array
                ? av.EnumerateArray().Select(ReadValue).ToArray()
                : null
        };
    }

    private static EngineValue ReadValue(JsonElement e) => new()
    {
        Literal = e.TryGetProperty("literal", out var l) && l.ValueKind == JsonValueKind.String ? l.GetString() : null,
        Reference = e.TryGetProperty("reference", out var r) && r.ValueKind == JsonValueKind.String ? r.GetString() : null
    };

    public override void Write(Utf8JsonWriter writer, EngineBinding binding, JsonSerializerOptions options)
        => ToApi(binding).WriteTo(writer);

    /// <summary>Expands shorthands; throws when more than one form is set.</summary>
    public static JsonObject ToApi(EngineBinding binding)
    {
        var forms = new List<string>();
        if (binding.Value is not null) forms.Add("value");
        if (binding.ArrayValue is not null) forms.Add("arrayValue");
        if (binding.ValueLiteral is not null) forms.Add("valueLiteral");
        if (binding.ValueReference is not null) forms.Add("valueReference");
        if (binding.Values is not null) forms.Add("values");
        if (binding.ExpressionRef is not null) forms.Add("expressionRef");
        if (forms.Count > 1)
            throw new IncidentIoException("InvalidBinding",
                $"A binding sets {string.Join(" and ", forms)}. Set exactly one of value, arrayValue, valueLiteral, valueReference, values or expressionRef.");

        if (binding.Value is { } value) return new JsonObject { ["value"] = Value(value.Literal, value.Reference) };
        if (binding.ValueLiteral is { } literal) return new JsonObject { ["value"] = Value(literal, null) };
        if (binding.ValueReference is { } reference) return new JsonObject { ["value"] = Value(null, reference) };
        if (binding.ExpressionRef is { } name) return new JsonObject { ["value"] = Value(null, ExpressionReference(name)) };
        if (binding.ArrayValue is { } array)
            return new JsonObject { ["array_value"] = new JsonArray(array.Select(a => (JsonNode)Value(a.Literal, a.Reference)).ToArray()) };
        if (binding.Values is { } values)
            return new JsonObject { ["array_value"] = new JsonArray(values.Select(v => (JsonNode)Value(v, null)).ToArray()) };
        return new JsonObject();
    }

    /// <summary>Same format as the Terraform provider: expressions["name"].</summary>
    public static string ExpressionReference(string name) => $"expressions[{JsonSerializer.Serialize(name)}]";

    private static JsonObject Value(string? literal, string? reference)
    {
        if (literal is not null && reference is not null)
            throw new IncidentIoException("InvalidBinding", "A value sets both literal and reference; set one.");
        var node = new JsonObject();
        if (literal is not null) node["literal"] = literal;
        if (reference is not null) node["reference"] = reference;
        return node;
    }
}

/// <summary>
/// Semantic JSON comparison used to skip no-op updates.
/// </summary>
public static class JsonCompare
{
    /// <summary>
    /// True when every property in <paramref name="desired"/> has an equal value in <paramref name="actual"/>.
    /// Objects compare as subsets (the API may add fields such as names or timestamps); arrays compare
    /// element-wise in order; numbers numerically; timestamps by instant ("…Z" equals "….000Z" equals "+00:00").
    /// </summary>
    public static bool IsSubset(JsonNode? desired, JsonElement actual)
    {
        switch (desired)
        {
            case null:
                return actual.ValueKind is JsonValueKind.Null or JsonValueKind.Undefined;
            case JsonObject obj:
                if (actual.ValueKind != JsonValueKind.Object) return false;
                foreach (var (key, value) in obj)
                {
                    if (!actual.TryGetProperty(key, out var actualValue))
                    {
                        // An empty desired value matches a field the API leaves out.
                        if (value is null || value is JsonArray { Count: 0 } || value is JsonObject { Count: 0 }) continue;
                        return false;
                    }
                    if (!IsSubset(value, actualValue)) return false;
                }
                return true;
            case JsonArray array:
                if (actual.ValueKind != JsonValueKind.Array || actual.GetArrayLength() != array.Count) return false;
                var i = 0;
                foreach (var element in actual.EnumerateArray())
                    if (!IsSubset(array[i++], element)) return false;
                return true;
            case JsonValue value:
                return ValueEquals(value, actual);
            default:
                return false;
        }
    }

    private static bool ValueEquals(JsonValue desired, JsonElement actual)
    {
        var kind = desired.GetValueKind();
        if (kind == JsonValueKind.String && actual.ValueKind == JsonValueKind.String)
        {
            var a = desired.GetValue<string>();
            var b = actual.GetString();
            if (a == b) return true;
            return a.Contains('T') && DateTimeOffset.TryParse(a, out var da) && DateTimeOffset.TryParse(b, out var db) && da == db;
        }
        if (kind == JsonValueKind.Number && actual.ValueKind == JsonValueKind.Number)
            return decimal.Parse(desired.ToJsonString(), System.Globalization.CultureInfo.InvariantCulture) == actual.GetDecimal();
        if (kind is JsonValueKind.True or JsonValueKind.False)
            return actual.ValueKind == kind;
        return false;
    }
}
