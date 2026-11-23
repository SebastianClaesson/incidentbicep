using System.Text.Json;
using System.Text.Json.Nodes;

namespace Bicep.Extension.IncidentIo.Handlers;

public class EscalationPathHandler : NamedResourceHandler<EscalationPath>
{
    protected override string ApiPath => "v2/escalation_paths";
    protected override string ItemKey => "escalation_path";
    protected override string CollectionKey => "escalation_paths";
    protected override int? PageSize => 25;

    // "A path is one kind for life."
    protected override IReadOnlyCollection<string> CreateOnlyFields => ["kind"];

    protected override Task<JsonObject> BuildPayload(IncidentIoClient client, EscalationPath properties, JsonElement? existing, CancellationToken cancellationToken)
    {
        var payload = new JsonObject { ["name"] = properties.Name };
        void Set(string key, object? value)
        {
            if (value is not null)
                payload[key] = JsonSerializer.SerializeToNode(value, value.GetType(), ApiJsonOptions);
        }

        Set("kind", properties.Kind?.ToString());
        Set("team_ids", properties.TeamIds);
        Set("repeat_config", properties.RepeatConfig);
        Set("working_hours", properties.WorkingHours);
        Set("template_id", properties.TemplateId);
        Set("param_bindings", properties.ParamBindings);

        if (properties.Kind == EscalationPathKind.templated)
        {
            if (string.IsNullOrEmpty(properties.TemplateId))
                throw new IncidentIoException("InvalidEscalationPath", "A templated escalation path needs 'templateId'.");
            if (properties.Sequences is not null || properties.Start is not null)
                throw new IncidentIoException("InvalidEscalationPath", "A templated escalation path takes its nodes from the template; set 'paramBindings' instead of 'start' and 'sequences'.");
        }
        else
        {
            payload["path"] = EscalationPathSequences.Unflatten(
                properties.Start, properties.Sequences, properties.WorkingHours?.Select(w => w.Id) ?? []);
        }

        return Task.FromResult(payload);
    }

    protected override Task<EscalationPath> FromApi(IncidentIoClient client, JsonElement item, CancellationToken cancellationToken)
    {
        // 'path' is a tree in the API and named sequences in Bicep, so it is mapped separately.
        var scalar = JsonNode.Parse(item.GetRawText())!.AsObject();
        scalar.Remove("path");
        var result = scalar.Deserialize<EscalationPath>(ApiJsonOptions)!;

        if (item.TryGetProperty("path", out var path) && path.ValueKind == JsonValueKind.Array && path.GetArrayLength() > 0)
            (result.Start, result.Sequences) = EscalationPathSequences.Flatten(path);

        PopulateOutputs(result, item);
        return Task.FromResult(result);
    }
}
