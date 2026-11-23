using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;

namespace Bicep.Extension.IncidentIo.Handlers;

/// <summary>
/// Converts between the Terraform-style flat map of named sequences and the API's tree of nodes.
///
/// The API stores an escalation path as a tree: an if_else node holds its then/else paths inline.
/// Bicep (like Terraform v7) writes it as named sequences that reference each other, so there is no
/// nesting limit. Validation rules and messages follow the Terraform provider so errors read the same.
/// </summary>
public static partial class EscalationPathSequences
{
    /// <summary>Separates the sequence key from the index in derived node ids ("main/0"). Not allowed in user ids.</summary>
    public const string NodeIdSeparator = "/";
    public const string RootSequenceKey = "main";

    private const string PrioritySubject = "escalation.priority";
    private const string WorkingHoursSubjectFormat = "escalation.working_hours[\"{0}\"]";

    [GeneratedRegex(@"^[a-zA-Z][a-zA-Z0-9_-]*$")]
    private static partial Regex SequenceKeyPattern();

    [GeneratedRegex(@"^escalation\.working_hours\[""([^""]+)""\]$")]
    private static partial Regex WorkingHoursSubjectPattern();

    public static string DerivedNodeId(string sequenceKey, int index) => $"{sequenceKey}{NodeIdSeparator}{index}";

    // ──────────────── Bicep → API ────────────────

    /// <summary>Validates the sequences and builds the API 'path' array. Throws with every problem found.</summary>
    public static JsonArray Unflatten(string? start, IDictionary<string, EscalationPathSequence>? sequences, IEnumerable<string> workingHoursIds)
    {
        var errors = Validate(start, sequences, workingHoursIds.ToHashSet(StringComparer.Ordinal));
        if (errors.Count > 0)
            throw new IncidentIoException("InvalidEscalationPath", "Invalid escalation path: " + string.Join(" ", errors));

        return BuildSequence(start!, sequences!);
    }

    private static JsonArray BuildSequence(string key, IDictionary<string, EscalationPathSequence> sequences)
    {
        var result = new JsonArray();
        var nodes = sequences[key].Nodes;
        for (var index = 0; index < nodes.Length; index++)
        {
            var node = nodes[index];
            var id = string.IsNullOrEmpty(node.Id) ? DerivedNodeId(key, index) : node.Id;
            var payload = new JsonObject { ["id"] = id };

            if (node.Branch is { } branch)
            {
                payload["type"] = "if_else";
                payload["if_else"] = new JsonObject
                {
                    ["conditions"] = BranchConditions(branch.If),
                    ["then_path"] = BuildSequence(branch.Then, sequences),
                    ["else_path"] = string.IsNullOrEmpty(branch.Else) ? new JsonArray() : BuildSequence(branch.Else, sequences)
                };
            }
            else if (node.Loop is { } loop)
            {
                payload["type"] = "repeat";
                payload["repeat"] = new JsonObject { ["repeat_times"] = loop.Times, ["to_node"] = loop.BackTo };
            }
            else if (node.Level is { } level)
            {
                payload["type"] = "level";
                var levelJson = Serialize(level);
                levelJson["ack_mode"] ??= "first"; // Terraform v7 default
                payload["level"] = levelJson;
            }
            else if (node.NotifyChannel is { } notify)
            {
                payload["type"] = "notify_channel";
                payload["notify_channel"] = Serialize(notify);
            }
            else if (node.Delay is { } delay)
            {
                payload["type"] = "delay";
                payload["delay"] = Serialize(delay);
            }
            else if (node.EscalationPath is { } handoff)
            {
                payload["type"] = "escalation_path";
                payload["escalation_path"] = new JsonObject { ["escalation_path_id"] = handoff.EscalationPathId };
            }

            result.Add(payload);
        }
        return result;
    }

    private static JsonArray BranchConditions(EscalationPathBranchIf condition)
    {
        if (!string.IsNullOrEmpty(condition.WorkingHoursActive))
        {
            return
            [
                new JsonObject
                {
                    ["subject"] = string.Format(WorkingHoursSubjectFormat, condition.WorkingHoursActive),
                    ["operation"] = "is_active",
                    ["param_bindings"] = new JsonArray()
                }
            ];
        }

        var literals = new JsonArray(condition.PriorityOneOf!.Select(p => (JsonNode)new JsonObject { ["literal"] = p }).ToArray());
        return
        [
            new JsonObject
            {
                ["subject"] = PrioritySubject,
                ["operation"] = "one_of",
                ["param_bindings"] = new JsonArray(new JsonObject { ["array_value"] = literals })
            }
        ];
    }

    private static JsonObject Serialize<T>(T value)
        => JsonSerializer.SerializeToNode(value, IncidentIoResourceHandlerBase<EscalationPath, NameIdentifiers>.ApiJsonOptions)!.AsObject();

    // ──────────────── Validation (mirrors the Terraform provider) ────────────────

    internal static List<string> Validate(string? start, IDictionary<string, EscalationPathSequence>? sequences, ISet<string> workingHoursIds)
    {
        var errors = new List<string>();
        if (string.IsNullOrEmpty(start))
            errors.Add("'start' is required unless kind is 'templated'.");
        if (sequences is null || sequences.Count == 0)
            errors.Add("'sequences' is required unless kind is 'templated'.");
        if (errors.Count > 0)
            return errors;

        var ids = new Dictionary<string, int>(StringComparer.Ordinal);
        var branchedTo = new Dictionary<string, int>(StringComparer.Ordinal);

        foreach (var (key, sequence) in sequences!)
        {
            if (!SequenceKeyPattern().IsMatch(key))
                errors.Add($"Invalid sequence name: \"{key}\" must start with a letter and contain only letters, numbers, underscores and hyphens.");

            var nodes = sequence.Nodes ?? [];
            if (nodes.Length == 0)
                errors.Add($"Empty sequence: sequence \"{key}\" has no nodes. Remove it, or give it something to do.");

            for (var index = 0; index < nodes.Length; index++)
            {
                var node = nodes[index];
                if (!string.IsNullOrEmpty(node.Id) && node.Id.Contains(NodeIdSeparator))
                    errors.Add($"Invalid node id: \"{node.Id}\" must not contain \"{NodeIdSeparator}\", which is reserved for derived ids.");

                var id = string.IsNullOrEmpty(node.Id) ? DerivedNodeId(key, index) : node.Id;
                ids[id] = ids.GetValueOrDefault(id) + 1;

                var blocks = BlockNames(node);
                if (blocks.Count == 0)
                    errors.Add($"Empty node: node {index} of sequence \"{key}\" sets none of level, notifyChannel, delay, escalationPath, branch or loop.");
                else if (blocks.Count > 1)
                    errors.Add($"Node does more than one thing: node {index} of sequence \"{key}\" sets {string.Join(" and ", blocks)}. Split it into one node each.");

                var rest = nodes.Skip(index + 1).ToArray();
                if (node.Branch is { } branch)
                {
                    if (rest.Length > 0)
                        errors.Add($"Nodes after a branch: sequence \"{key}\" continues after a branch node. A branch must be the last node in its sequence.");

                    var hasHours = !string.IsNullOrEmpty(branch.If.WorkingHoursActive);
                    var hasPriority = branch.If.PriorityOneOf is { Length: > 0 };
                    if (hasHours == hasPriority)
                        errors.Add($"Branch in sequence \"{key}\" must set exactly one of if.workingHoursActive or if.priorityOneOf.");
                    if (hasHours && !workingHoursIds.Contains(branch.If.WorkingHoursActive!))
                        errors.Add($"Branch in sequence \"{key}\": this escalation path declares no workingHours with the id \"{branch.If.WorkingHoursActive}\".");

                    foreach (var target in new[] { branch.Then, branch.Else }.Where(t => !string.IsNullOrEmpty(t)))
                    {
                        if (!sequences.ContainsKey(target!))
                            errors.Add($"Unknown sequence: there is no sequence named \"{target}\".");
                        else
                            branchedTo[target!] = branchedTo.GetValueOrDefault(target!) + 1;
                    }
                }

                if (node.Loop is not null && !(rest.Length == 0 || (rest.Length == 1 && rest[0].EscalationPath is not null && BlockNames(rest[0]).Count == 1)))
                    errors.Add($"Nodes after a loop: sequence \"{key}\" continues after a loop node. A loop must be the last node in its sequence, or be followed by a single escalationPath node.");
            }
        }

        foreach (var (id, count) in ids.Where(kv => kv.Value > 1))
            errors.Add($"Duplicate node id: more than one node is called \"{id}\".");

        if (!sequences.ContainsKey(start!))
        {
            errors.Add($"Unknown sequence: start names \"{start}\", which isn't one of the sequences.");
            return errors;
        }

        if (branchedTo.ContainsKey(start!))
            errors.Add($"Start sequence is branched to: \"{start}\" is where the path begins, so nothing may branch to it. Use a loop node to go back to the start.");
        foreach (var (key, count) in branchedTo.Where(kv => kv.Value > 1))
            errors.Add($"Sequence branched to more than once: {count} branches name \"{key}\". Give the others their own copy.");

        // Walk the tree from start: detects cycles and unreachable sequences, and checks each loop target
        // is the path's first node or a branch the loop sits underneath.
        var firstNodeId = sequences[start!].Nodes is { Length: > 0 } startNodes
            ? (string.IsNullOrEmpty(startNodes[0].Id) ? DerivedNodeId(start!, 0) : startNodes[0].Id!)
            : null;
        var reached = new HashSet<string>(StringComparer.Ordinal);
        Walk(start!, [], new HashSet<string>(StringComparer.Ordinal));

        foreach (var key in sequences.Keys.Where(k => !reached.Contains(k)))
            errors.Add($"Unreachable sequence: nothing branches to \"{key}\" and it isn't the start, so it would never run.");

        return errors;

        void Walk(string key, List<string> ancestorBranches, HashSet<string> visiting)
        {
            if (!sequences.TryGetValue(key, out var sequence))
                return;
            if (!visiting.Add(key))
            {
                errors.Add($"Escalation path loops back on itself: \"{key}\" is reachable from itself through a branch. Use a loop node to repeat.");
                return;
            }
            reached.Add(key);

            var nodes = sequence.Nodes ?? [];
            for (var index = 0; index < nodes.Length; index++)
            {
                var node = nodes[index];
                var id = string.IsNullOrEmpty(node.Id) ? DerivedNodeId(key, index) : node.Id;

                if (node.Loop is { } loop)
                {
                    if (!ids.ContainsKey(loop.BackTo))
                        errors.Add($"Unknown node: there is no node with id \"{loop.BackTo}\". Give the node you want to loop back to an explicit id.");
                    else if (loop.BackTo != firstNodeId && !ancestorBranches.Contains(loop.BackTo))
                        errors.Add($"Cannot loop back to this node: a loop may only go back to the escalation path's first node, or to a branch it sits underneath. \"{loop.BackTo}\" is neither.");
                }

                if (node.Branch is { } branch)
                {
                    var below = new List<string>(ancestorBranches) { id };
                    Walk(branch.Then, below, visiting);
                    if (!string.IsNullOrEmpty(branch.Else))
                        Walk(branch.Else, below, visiting);
                }
            }
            visiting.Remove(key);
        }
    }

    private static List<string> BlockNames(EscalationPathNode node)
    {
        var names = new List<string>();
        if (node.Level is not null) names.Add("level");
        if (node.NotifyChannel is not null) names.Add("notifyChannel");
        if (node.Delay is not null) names.Add("delay");
        if (node.EscalationPath is not null) names.Add("escalationPath");
        if (node.Branch is not null) names.Add("branch");
        if (node.Loop is not null) names.Add("loop");
        return names;
    }

    // ──────────────── API → Bicep (for 'existing') ────────────────

    /// <summary>
    /// Turns the API tree back into named sequences. Sequence names are recovered from derived node ids
    /// ("services/0" → "services"); otherwise the start is "main" and branch children are "&lt;key&gt;_then"/"&lt;key&gt;_else".
    /// </summary>
    public static (string Start, Dictionary<string, EscalationPathSequence> Sequences) Flatten(JsonElement path)
    {
        var sequences = new Dictionary<string, EscalationPathSequence>(StringComparer.Ordinal);
        var start = FlattenSequence(path, RootSequenceKey, sequences);
        return (start, sequences);
    }

    private static string FlattenSequence(JsonElement nodes, string fallbackKey, Dictionary<string, EscalationPathSequence> sequences)
    {
        var items = nodes.ValueKind == JsonValueKind.Array ? nodes.EnumerateArray().ToList() : [];
        var key = KeyFromDerivedId(items.FirstOrDefault()) ?? fallbackKey;
        while (sequences.ContainsKey(key)) key += "_";

        var result = new List<EscalationPathNode>();
        sequences[key] = new EscalationPathSequence { Nodes = [] }; // reserve the name before recursing

        for (var index = 0; index < items.Count; index++)
        {
            var item = items[index];
            var id = item.GetProperty("id").GetString();
            var node = new EscalationPathNode { Id = id == DerivedNodeId(key, index) ? null : id };
            var type = item.GetProperty("type").GetString();

            switch (type)
            {
                case "if_else":
                    var ifElse = item.GetProperty("if_else");
                    var then = FlattenSequence(ifElse.GetProperty("then_path"), $"{key}_then", sequences);
                    var elsePath = ifElse.TryGetProperty("else_path", out var e) && e.GetArrayLength() > 0
                        ? FlattenSequence(e, $"{key}_else", sequences)
                        : null;
                    node.Branch = new EscalationPathBranch { If = ReadBranchIf(ifElse), Then = then, Else = elsePath };
                    break;
                case "repeat":
                    var repeat = item.GetProperty("repeat");
                    node.Loop = new EscalationPathLoop { BackTo = repeat.GetProperty("to_node").GetString()!, Times = repeat.GetProperty("repeat_times").GetInt32() };
                    break;
                case "level":
                    node.Level = Deserialize<EscalationPathLevel>(item.GetProperty("level"));
                    break;
                case "notify_channel":
                    node.NotifyChannel = Deserialize<EscalationPathNotifyChannel>(item.GetProperty("notify_channel"));
                    break;
                case "delay":
                    node.Delay = Deserialize<EscalationPathDelay>(item.GetProperty("delay"));
                    break;
                case "escalation_path":
                    node.EscalationPath = new EscalationPathHandoff { EscalationPathId = item.GetProperty("escalation_path").GetProperty("escalation_path_id").GetString()! };
                    break;
                default:
                    continue; // node types the Bicep shape can't express (e.g. voicemail) are skipped on read
            }
            result.Add(node);
        }

        sequences[key] = new EscalationPathSequence { Nodes = [.. result] };
        return key;
    }

    private static string? KeyFromDerivedId(JsonElement node)
    {
        if (node.ValueKind != JsonValueKind.Object || !node.TryGetProperty("id", out var idProp)) return null;
        var id = idProp.GetString();
        var cut = id?.IndexOf(NodeIdSeparator, StringComparison.Ordinal) ?? -1;
        return cut > 0 ? id![..cut] : null;
    }

    private static EscalationPathBranchIf ReadBranchIf(JsonElement ifElse)
    {
        var result = new EscalationPathBranchIf();
        if (!ifElse.TryGetProperty("conditions", out var conditions) || conditions.GetArrayLength() == 0)
            return result;

        var condition = conditions[0];
        var subject = condition.GetProperty("subject").GetString() ?? "";
        if (WorkingHoursSubjectPattern().Match(subject) is { Success: true } match)
            result.WorkingHoursActive = match.Groups[1].Value;
        else if (subject == PrioritySubject && condition.TryGetProperty("param_bindings", out var bindings) && bindings.GetArrayLength() > 0
                 && bindings[0].TryGetProperty("array_value", out var values))
            result.PriorityOneOf = values.EnumerateArray().Select(v => v.GetProperty("literal").GetString()!).ToArray();
        return result;
    }

    private static T Deserialize<T>(JsonElement element)
        => element.Deserialize<T>(IncidentIoResourceHandlerBase<EscalationPath, NameIdentifiers>.ApiJsonOptions)!;
}
