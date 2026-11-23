using System.Text.Json;
using System.Text.Json.Nodes;

namespace Bicep.Extension.IncidentIo.Handlers;

/// <summary>
/// Rotations live under their schedule (/v3/schedules/{scheduleId}/rotations) and are found by name.
///
/// A write with a 'rollout' schedules a new version of the rotation even when nothing changed, so
/// unchanged rotations are never written (SkipUnchangedUpdates).
/// </summary>
public class ScheduleRotationHandler : IncidentIoResourceHandlerBase<ScheduleRotation, ScheduleRotationIdentifiers>
{
    protected override string ApiPath => "v3/schedules";
    protected override string ItemKey => "rotation";
    protected override string CollectionKey => "rotations";
    protected override string? RequestWrapperKey => "rotation";
    protected override int? PageSize => 250;
    protected override bool SkipUnchangedUpdates => true;

    protected override string GetCollectionPath(ScheduleRotationIdentifiers identifiers)
        => $"v3/schedules/{Uri.EscapeDataString(identifiers.ScheduleId)}/rotations";

    protected override bool IsMatch(JsonElement item, ScheduleRotationIdentifiers identifiers)
        => StringEquals(item, "name", identifiers.Name);

    protected override ScheduleRotationIdentifiers GetIdentifiers(ScheduleRotation properties) => new()
    {
        ScheduleId = properties.ScheduleId,
        Name = properties.Name
    };

    protected override async Task<JsonObject> BuildPayload(IncidentIoClient client, ScheduleRotation properties, JsonElement? existing, CancellationToken cancellationToken)
    {
        if (properties.Users.Length == 0)
            throw new IncidentIoException("InvalidRotation",
                "A rotation needs at least one entry in 'users'. For a rotation that schedules shifts without putting anyone on call, use the special ID 'NOBODY'.");
        if (properties.Handovers.Length == 0)
            throw new IncidentIoException("InvalidRotation", "A rotation needs at least one entry in 'handovers'.");

        var payload = await base.BuildPayload(client, properties, existing, cancellationToken);
        payload.Remove("schedule_id"); // part of the URL
        payload.Remove("rollout");     // a top-level request field, see WrapRequest
        payload["users"] = new JsonArray(properties.Users.Select(id => (JsonNode)new JsonObject { ["id"] = id }).ToArray());

        // A rollout keeps the interval start the rotation already runs, so a new one would be silently discarded.
        if (existing is { } current && properties.Rollout is not null
            && current.TryGetProperty("first_interval_starts_at", out var currentStart)
            && !SameInstant(currentStart.GetString(), properties.FirstIntervalStartsAt))
        {
            throw new IncidentIoException("InvalidRotation",
                "'firstIntervalStartsAt' can't change in the same deployment as a 'rollout'. Deploy the new firstIntervalStartsAt " +
                "with rollout unset first, then make the line-up change with rollout set.");
        }

        return payload;
    }

    protected override JsonObject WrapRequest(JsonObject payload, ScheduleRotation properties, bool isUpdate)
    {
        var body = base.WrapRequest(payload, properties, isUpdate);
        if (isUpdate && properties.Rollout is { } rollout)
            body["rollout"] = rollout.ToString();
        return body;
    }

    protected override bool IsUpToDate(JsonObject payload, JsonElement existing)
    {
        // Working intervals are a set: compare them order-insensitively, everything else as usual.
        var rest = payload.DeepClone().AsObject();
        var desiredIntervals = rest["working_intervals"] as JsonArray;
        rest.Remove("working_intervals");
        if (!JsonCompare.IsSubset(rest, existing))
            return false;

        var actualIntervals = existing.TryGetProperty("working_intervals", out var w) && w.ValueKind == JsonValueKind.Array
            ? w.EnumerateArray().Select(i => IntervalKey(i.GetProperty("weekday").GetString(), i.GetProperty("start_time").GetString(), i.GetProperty("end_time").GetString())).Order().ToList()
            : [];
        var desired = (desiredIntervals ?? []).Select(i => IntervalKey(i!["weekday"]!.GetValue<string>(), i["start_time"]!.GetValue<string>(), i["end_time"]!.GetValue<string>())).Order().ToList();
        return desired.SequenceEqual(actualIntervals);
    }

    private static string IntervalKey(string? day, string? start, string? end) => $"{day}|{start}|{end}";

    private static bool SameInstant(string? a, string b)
        => a == b || (DateTimeOffset.TryParse(a, out var da) && DateTimeOffset.TryParse(b, out var db) && da == db);

    protected override void PopulateOutputs(ScheduleRotation properties, JsonElement item)
    {
        base.PopulateOutputs(properties, item);
        if (item.TryGetProperty("effective_from", out var effectiveFrom) && effectiveFrom.ValueKind == JsonValueKind.String)
            properties.EffectiveFrom = effectiveFrom.GetString();
    }

    protected override Task<ScheduleRotation> FromApi(IncidentIoClient client, JsonElement item, CancellationToken cancellationToken)
    {
        // The API returns users as objects; the Bicep shape is a list of IDs.
        var node = JsonNode.Parse(item.GetRawText())!.AsObject();
        node["users"] = new JsonArray(item.GetProperty("users").EnumerateArray().Select(u => (JsonNode)u.GetProperty("id").GetString()!).ToArray());
        var rotation = node.Deserialize<ScheduleRotation>(ApiJsonOptions)!;
        PopulateOutputs(rotation, item);
        return Task.FromResult(rotation);
    }
}
