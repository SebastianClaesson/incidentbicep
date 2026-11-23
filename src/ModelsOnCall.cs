using System.Text.Json.Serialization;
using Azure.Bicep.Types.Concrete;
using Bicep.Local.Extension.Types.Attributes;

namespace Bicep.Extension.IncidentIo;

// ──────────────────────────────────────────────
// On-call: users (lookup only), schedules, rotations, escalation paths.
// Property names follow the Terraform provider (v7) in camelCase.
// ──────────────────────────────────────────────

#pragma warning disable IDE1006 // Enum member names are the API/Bicep values
public enum Weekday { monday, tuesday, wednesday, thursday, friday, saturday, sunday }

public enum HandoverIntervalType { hourly, daily, weekly }

public enum RotationRollout { immediate, after_current_shift, after_full_rotation }

public enum RotationSchedulingMode { fair, sequential }

public enum EscalationPathKind { standalone, templated }

public enum EscalationAckMode { all, first }

public enum EscalationIntervalCondition { active, inactive }

public enum EscalationTargetType { schedule, user, slack_channel, msteams_channel }

public enum EscalationUrgency { high, low }
#pragma warning restore IDE1006

/// <summary>
/// A user, looked up by email. Read-only: declare it with 'existing'.
/// API: /v2/users
/// </summary>
public class UserIdentifiers : IIncidentIoResource
{
    [TypeProperty("The user's email address.", ObjectTypePropertyFlags.Required | ObjectTypePropertyFlags.Identifier)]
    public required string Email { get; set; }

    [TypeProperty("incident.io user ID.", ObjectTypePropertyFlags.ReadOnly)]
    public string? Id { get; set; }
}

[ResourceType("User")]
public class User : UserIdentifiers
{
    [TypeProperty("Full name.", ObjectTypePropertyFlags.ReadOnly)]
    public string? Name { get; set; }

    [TypeProperty("Slack user ID.", ObjectTypePropertyFlags.ReadOnly)]
    public string? SlackUserId { get; set; }

    [TypeProperty("Base role (e.g. 'owner', 'administrator', 'responder', 'viewer').", ObjectTypePropertyFlags.ReadOnly)]
    public string? Role { get; set; }
}

// ──────────────── Schedules ────────────────

public class ScheduleHolidaysPublicConfig
{
    [TypeProperty("ISO 3166-1 alpha-2 country codes whose public holidays are shown on the schedule.", ObjectTypePropertyFlags.Required)]
    public required string[] CountryCodes { get; set; }
}

/// <summary>
/// An on-call schedule. Rotations are separate ScheduleRotation resources, as in Terraform v7.
/// API: /v3/schedules
/// </summary>
[ResourceType("Schedule")]
public class Schedule : NameIdentifiers
{
    [TypeProperty("IANA timezone the schedule's rotations are anchored to, e.g. 'Europe/Stockholm'. Cannot be changed after creation.", ObjectTypePropertyFlags.Required)]
    public required string Timezone { get; set; }

    [TypeProperty("IDs of teams that own this schedule.")]
    public string[]? TeamIds { get; set; }

    [TypeProperty("Public holidays to show on this schedule. Omit to show none.")]
    public ScheduleHolidaysPublicConfig? HolidaysPublicConfig { get; set; }

    [TypeProperty("Free-form annotations (key/value metadata).")]
    public Dictionary<string, string>? Annotations { get; set; }
}

public class ScheduleHandover
{
    [TypeProperty("How many of the interval type to wait between handovers.", ObjectTypePropertyFlags.Required)]
    public required int Interval { get; set; }

    [TypeProperty("How often a handover occurs.", ObjectTypePropertyFlags.Required)]
    [JsonConverter(typeof(JsonStringEnumConverter))]
    public required HandoverIntervalType IntervalType { get; set; }
}

public class WeekdayInterval
{
    [TypeProperty("Day of the week.", ObjectTypePropertyFlags.Required)]
    [JsonConverter(typeof(JsonStringEnumConverter))]
    public required Weekday Weekday { get; set; }

    [TypeProperty("Time of day the window opens, as HH:MM.", ObjectTypePropertyFlags.Required)]
    public required string StartTime { get; set; }

    [TypeProperty("Time of day the window closes, as HH:MM.", ObjectTypePropertyFlags.Required)]
    public required string EndTime { get; set; }
}

/// <summary>
/// A rotation on a schedule. Each rotation is managed independently, so changing one never rewrites the others.
/// API: /v3/schedules/{scheduleId}/rotations
/// </summary>
public class ScheduleRotationIdentifiers : IIncidentIoResource
{
    [TypeProperty("ID of the schedule this rotation belongs to (e.g. schedule.id).", ObjectTypePropertyFlags.Required | ObjectTypePropertyFlags.Identifier)]
    public required string ScheduleId { get; set; }

    [TypeProperty("Name of the rotation, unique within the schedule.", ObjectTypePropertyFlags.Required | ObjectTypePropertyFlags.Identifier)]
    public required string Name { get; set; }

    [TypeProperty("Rotation ID (read-only).", ObjectTypePropertyFlags.ReadOnly)]
    public string? Id { get; set; }
}

[ResourceType("ScheduleRotation")]
public class ScheduleRotation : ScheduleRotationIdentifiers
{
    [TypeProperty("When the first handover interval is counted from (ISO 8601, e.g. '2026-01-05T09:00:00Z'). Fixes the time of day and day of week shifts change hands. Can't change in the same deployment as a rollout.", ObjectTypePropertyFlags.Required)]
    public required string FirstIntervalStartsAt { get; set; }

    [TypeProperty("The cadence shifts hand over on; several entries alternate.", ObjectTypePropertyFlags.Required)]
    public required ScheduleHandover[] Handovers { get; set; }

    [TypeProperty("User IDs in the order they take shifts (use User 'existing' to look them up). Use 'NOBODY' for an empty slot.", ObjectTypePropertyFlags.Required)]
    public required string[] Users { get; set; }

    [TypeProperty("How many people are on call at once. Defaults to 1.")]
    public int? ConcurrentShifts { get; set; }

    [TypeProperty("Position in the schedule's running order, lowest first.")]
    public int? Rank { get; set; }

    [TypeProperty("How a change is introduced: 'immediate', 'after_current_shift' or 'after_full_rotation'. Only used when the rotation changes.")]
    [JsonConverter(typeof(JsonStringEnumConverter))]
    public RotationRollout? Rollout { get; set; }

    [TypeProperty("How users are allocated across shifts of differing length: 'fair' or 'sequential'. Omit to let incident.io pick.")]
    [JsonConverter(typeof(JsonStringEnumConverter))]
    public RotationSchedulingMode? SchedulingMode { get; set; }

    [TypeProperty("Restrict on-call to these weekday intervals. Omit for around-the-clock.")]
    public WeekdayInterval[]? WorkingIntervals { get; set; }

    [TypeProperty("When the current shape of the rotation took over (read-only).", ObjectTypePropertyFlags.ReadOnly)]
    public string? EffectiveFrom { get; set; }
}

// ──────────────── Escalation paths ────────────────

public class EscalationPathTarget
{
    [TypeProperty("ID of the user, schedule or channel.", ObjectTypePropertyFlags.Required)]
    public required string Id { get; set; }

    [TypeProperty("Target type.", ObjectTypePropertyFlags.Required)]
    [JsonConverter(typeof(JsonStringEnumConverter))]
    public required EscalationTargetType Type { get; set; }

    [TypeProperty("Urgency of the page.", ObjectTypePropertyFlags.Required)]
    [JsonConverter(typeof(JsonStringEnumConverter))]
    public required EscalationUrgency Urgency { get; set; }

    [TypeProperty("Schedule targets only: 'currently_on_call', 'all_users', 'all_users_for_rota', 'currently_on_call_for_rota', 'next_on_call_for_rota' or 'next_on_call'.")]
    public string? ScheduleMode { get; set; }

    [TypeProperty("Schedule targets only: the rotation the *_for_rota schedule modes apply to.")]
    public string? SelectedRotaId { get; set; }
}

public class EscalationPathRetryConfig
{
    [TypeProperty("Total pages for this level, counting the first (2-10).", ObjectTypePropertyFlags.Required)]
    public required int Attempts { get; set; }

    [TypeProperty("Seconds between attempts (a whole number of minutes).", ObjectTypePropertyFlags.Required)]
    public required int IntervalSeconds { get; set; }
}

public class EscalationPathRoundRobinConfig
{
    [TypeProperty("Whether round robin is enabled.", ObjectTypePropertyFlags.Required)]
    public required bool Enabled { get; set; }

    [TypeProperty("Seconds before rotating to the next target. Unset sticks with one target per level.")]
    public int? RotateAfterSeconds { get; set; }
}

public class EscalationPathLevel
{
    [TypeProperty("Users or schedules to page.", ObjectTypePropertyFlags.Required)]
    public required EscalationPathTarget[] Targets { get; set; }

    [TypeProperty("'first' (default): the first acknowledgement cancels everyone else on the level. 'all': everyone must acknowledge.")]
    [JsonConverter(typeof(JsonStringEnumConverter))]
    public EscalationAckMode? AckMode { get; set; }

    [TypeProperty("Re-page this level before moving on.")]
    public EscalationPathRetryConfig? RetryConfig { get; set; }

    [TypeProperty("Round-robin between targets.")]
    public EscalationPathRoundRobinConfig? RoundRobinConfig { get; set; }

    [TypeProperty("Seconds to wait for an acknowledgement before moving on.")]
    public int? TimeToAckSeconds { get; set; }

    [TypeProperty("If time to ack is relative to working hours: move on while they are 'active' or 'inactive'.")]
    [JsonConverter(typeof(JsonStringEnumConverter))]
    public EscalationIntervalCondition? TimeToAckIntervalCondition { get; set; }

    [TypeProperty("If time to ack is relative to working hours: the workingHours id.")]
    public string? TimeToAckWeekdayIntervalConfigId { get; set; }
}

public class EscalationPathNotifyChannel
{
    [TypeProperty("Slack or Microsoft Teams channels to notify.", ObjectTypePropertyFlags.Required)]
    public required EscalationPathTarget[] Targets { get; set; }

    [TypeProperty("Seconds to wait for an acknowledgement before moving on.")]
    public int? TimeToAckSeconds { get; set; }

    [TypeProperty("If time to ack is relative to working hours: move on while they are 'active' or 'inactive'.")]
    [JsonConverter(typeof(JsonStringEnumConverter))]
    public EscalationIntervalCondition? TimeToAckIntervalCondition { get; set; }

    [TypeProperty("If time to ack is relative to working hours: the workingHours id.")]
    public string? TimeToAckWeekdayIntervalConfigId { get; set; }
}

public class EscalationPathDelay
{
    [TypeProperty("Seconds to wait before the next node.")]
    public int? DelaySeconds { get; set; }

    [TypeProperty("If relative to working hours: advance while they are 'active' or 'inactive'.")]
    [JsonConverter(typeof(JsonStringEnumConverter))]
    public EscalationIntervalCondition? DelayIntervalCondition { get; set; }

    [TypeProperty("If relative to working hours: the workingHours id.")]
    public string? DelayWeekdayIntervalConfigId { get; set; }
}

public class EscalationPathHandoff
{
    [TypeProperty("ID of the escalation path to hand over to.", ObjectTypePropertyFlags.Required)]
    public required string EscalationPathId { get; set; }
}

public class EscalationPathBranchIf
{
    [TypeProperty("The id of one of this path's workingHours; met while those hours are active.")]
    public string? WorkingHoursActive { get; set; }

    [TypeProperty("Alert priority IDs; met when the escalation came in at one of them.")]
    public string[]? PriorityOneOf { get; set; }
}

public class EscalationPathBranch
{
    [TypeProperty("What the branch tests. Set exactly one of workingHoursActive or priorityOneOf.", ObjectTypePropertyFlags.Required)]
    public required EscalationPathBranchIf If { get; set; }

    [TypeProperty("Sequence to continue down when the condition is met.", ObjectTypePropertyFlags.Required)]
    public required string Then { get; set; }

    [TypeProperty("Sequence to continue down otherwise. Omit to end the path.")]
    public string? Else { get; set; }
}

public class EscalationPathLoop
{
    [TypeProperty("The id of the node to repeat from: the path's first node, or a branch this loop sits under.", ObjectTypePropertyFlags.Required)]
    public required string BackTo { get; set; }

    [TypeProperty("How many times to repeat.", ObjectTypePropertyFlags.Required)]
    public required int Times { get; set; }
}

/// <summary>One step of a sequence. Set exactly one of level, notifyChannel, delay, escalationPath, branch or loop.</summary>
public class EscalationPathNode
{
    [TypeProperty("Node id, unique in the path, so a loop can name it. Omit unless something loops back here; a stable id is derived from the position.")]
    public string? Id { get; set; }

    [TypeProperty("Page users or schedules.")]
    public EscalationPathLevel? Level { get; set; }

    [TypeProperty("Notify channels.")]
    public EscalationPathNotifyChannel? NotifyChannel { get; set; }

    [TypeProperty("Wait before the next node.")]
    public EscalationPathDelay? Delay { get; set; }

    [TypeProperty("Hand over to another escalation path.")]
    public EscalationPathHandoff? EscalationPath { get; set; }

    [TypeProperty("Continue down one of two sequences. Must be the last node in its sequence.")]
    public EscalationPathBranch? Branch { get; set; }

    [TypeProperty("Go back to an earlier node. Must be last in its sequence, or followed by a single escalationPath node.")]
    public EscalationPathLoop? Loop { get; set; }
}

public class EscalationPathSequence
{
    [TypeProperty("Nodes in the order they run.", ObjectTypePropertyFlags.Required)]
    public required EscalationPathNode[] Nodes { get; set; }
}

public class EscalationPathRepeatConfig
{
    [TypeProperty("When true, incident activity resets the repeat timer.", ObjectTypePropertyFlags.Required)]
    public required bool DelayRepeatOnActivity { get; set; }

    [TypeProperty("Seconds to wait before repeating an acknowledged, unresolved escalation.", ObjectTypePropertyFlags.Required)]
    public required int RepeatAfterSeconds { get; set; }
}

public class EscalationPathWorkingHours
{
    [TypeProperty("Identifier referenced by branches and time windows (e.g. 'UK').", ObjectTypePropertyFlags.Required)]
    public required string Id { get; set; }

    [TypeProperty("Display name.", ObjectTypePropertyFlags.Required)]
    public required string Name { get; set; }

    [TypeProperty("IANA timezone for the intervals.", ObjectTypePropertyFlags.Required)]
    public required string Timezone { get; set; }

    [TypeProperty("The working intervals.", ObjectTypePropertyFlags.Required)]
    public required WeekdayInterval[] WeekdayIntervals { get; set; }
}

/// <summary>
/// An escalation path, written as a flat map of named node sequences (Terraform v7 shape).
/// API: /v2/escalation_paths
/// </summary>
[ResourceType("EscalationPath")]
public class EscalationPath : NameIdentifiers
{
    [TypeProperty("'standalone' (default) or 'templated'. Cannot be changed after creation.")]
    [JsonConverter(typeof(JsonStringEnumConverter))]
    public EscalationPathKind? Kind { get; set; }

    [TypeProperty("Key of the sequence the path begins with. Required unless kind is 'templated'.")]
    public string? Start { get; set; }

    [TypeProperty("Named sequences of nodes. Branches reference other sequences by key. Required unless kind is 'templated'.")]
    public Dictionary<string, EscalationPathSequence>? Sequences { get; set; }

    [TypeProperty("Working hours referenced by branches and time windows.")]
    public EscalationPathWorkingHours[]? WorkingHours { get; set; }

    [TypeProperty("Repeat an acknowledged escalation while the alert is unresolved.")]
    public EscalationPathRepeatConfig? RepeatConfig { get; set; }

    [TypeProperty("IDs of the teams that own this path.")]
    public string[]? TeamIds { get; set; }

    [TypeProperty("Templated paths: the escalation path template to build from.")]
    public string? TemplateId { get; set; }

    [TypeProperty("Templated paths: a value for each template param, keyed by param name.")]
    public Dictionary<string, EngineBinding>? ParamBindings { get; set; }
}
