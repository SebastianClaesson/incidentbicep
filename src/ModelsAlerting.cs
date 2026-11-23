using Azure.Bicep.Types.Concrete;
using Bicep.Local.Extension.Types.Attributes;

namespace Bicep.Extension.IncidentIo;

// ──────────────────────────────────────────────
// Alerting: maintenance windows and alert sources.
// ──────────────────────────────────────────────

public class MaintenanceWindowEscalationTarget
{
    [TypeProperty("Escalation paths to route matching alerts to.")]
    public EngineBinding? EscalationPaths { get; set; }

    [TypeProperty("Users to notify directly.")]
    public EngineBinding? Users { get; set; }
}

public class MaintenanceWindowNotifyChannel
{
    [TypeProperty("Channel ID in the chat provider (e.g. a Slack channel ID).", ObjectTypePropertyFlags.Required)]
    public required string ChannelId { get; set; }

    [TypeProperty("Channel type (e.g. 'public', 'private').", ObjectTypePropertyFlags.Required)]
    public required string ChannelType { get; set; }

    [TypeProperty("Human readable channel name.")]
    public string? ChannelName { get; set; }
}

/// <summary>
/// A maintenance window that holds back matching alerts.
/// API: /v1/maintenance_windows
/// </summary>
[ResourceType("MaintenanceWindow")]
public class MaintenanceWindow : NameIdentifiers
{
    [TypeProperty("Start time (ISO 8601).", ObjectTypePropertyFlags.Required)]
    public required string StartAt { get; set; }

    [TypeProperty("End time (ISO 8601).", ObjectTypePropertyFlags.Required)]
    public required string EndAt { get; set; }

    [TypeProperty("User ID of the window's lead.", ObjectTypePropertyFlags.Required)]
    public required string LeadId { get; set; }

    [TypeProperty("Which alerts the window applies to: all conditions in at least one group must match.", ObjectTypePropertyFlags.Required)]
    public required EngineConditionGroup[] AlertConditionGroups { get; set; }

    [TypeProperty("Escalate matching alerts to these targets.")]
    public MaintenanceWindowEscalationTarget[]? EscalationTargets { get; set; }

    [TypeProperty("Attach matching alerts to this incident.")]
    public string? IncidentId { get; set; }

    [TypeProperty("Message included in notifications about the window.")]
    public string? NotificationMessage { get; set; }

    [TypeProperty("Channels notified when the window starts and ends.")]
    public MaintenanceWindowNotifyChannel[]? NotifyChannels { get; set; }

    [TypeProperty("Minutes before the start to notify.")]
    public int? NotifyStartMinutesBefore { get; set; }

    [TypeProperty("Minutes before the end to notify.")]
    public int? NotifyEndMinutesBefore { get; set; }

    [TypeProperty("Re-route firing alerts through alert routing when the window ends.")]
    public bool? RerouteOnEnd { get; set; }

    [TypeProperty("Resolve firing alerts that matched the window when it ends.")]
    public bool? ResolveOnEnd { get; set; }

    [TypeProperty("Show the window in the dashboard sidebar while active. Defaults to false.")]
    public bool? ShowInSidebar { get; set; }

    [TypeProperty("IDs of teams that own this window.")]
    public string[]? OwningTeamIds { get; set; }
}

// ──────────────── Alert sources ────────────────

public class ExpressionReturns
{
    [TypeProperty("Expected return type, e.g. 'String', 'Number', 'CatalogEntry[\"<type id>\"]'.", ObjectTypePropertyFlags.Required)]
    public required string Type { get; set; }

    [TypeProperty("Whether the result is a list.", ObjectTypePropertyFlags.Required)]
    public required bool Array { get; set; }
}

public class ExpressionReferenceOpts
{
    [TypeProperty("The reference to navigate to / concatenate with.", ObjectTypePropertyFlags.Required)]
    public required string Reference { get; set; }
}

public class ExpressionFilterOpts
{
    [TypeProperty("Keep items matching any of these groups.", ObjectTypePropertyFlags.Required)]
    public required EngineConditionGroup[] ConditionGroups { get; set; }
}

public class ExpressionParseOpts
{
    [TypeProperty("JavaScript source evaluated to a result, e.g. '$.metadata.team'.", ObjectTypePropertyFlags.Required)]
    public required string Source { get; set; }

    [TypeProperty("What the result is cast to.", ObjectTypePropertyFlags.Required)]
    public required ExpressionReturns Returns { get; set; }
}

public class ExpressionCastOpts
{
    [TypeProperty("What the value is cast to.", ObjectTypePropertyFlags.Required)]
    public required ExpressionReturns Returns { get; set; }
}

public class ExpressionBranch
{
    [TypeProperty("The branch applies when any of these groups match.", ObjectTypePropertyFlags.Required)]
    public required EngineConditionGroup[] ConditionGroups { get; set; }

    [TypeProperty("The result when the branch applies.", ObjectTypePropertyFlags.Required)]
    public required EngineBinding Result { get; set; }
}

public class ExpressionBranchesOpts
{
    [TypeProperty("Branches tried in order.", ObjectTypePropertyFlags.Required)]
    public required ExpressionBranch[] Branches { get; set; }

    [TypeProperty("What the branches return.", ObjectTypePropertyFlags.Required)]
    public required ExpressionReturns Returns { get; set; }
}

/// <summary>One step of an expression pipeline. Set the options block matching operationType.</summary>
public class ExpressionOperation
{
    [TypeProperty("'navigate', 'filter', 'concatenate', 'count', 'min', 'max', 'sum', 'random', 'first', 'parse', 'branches' or 'cast'.", ObjectTypePropertyFlags.Required)]
    public required string OperationType { get; set; }

    [TypeProperty("For 'navigate'.")]
    public ExpressionReferenceOpts? Navigate { get; set; }

    [TypeProperty("For 'filter'.")]
    public ExpressionFilterOpts? Filter { get; set; }

    [TypeProperty("For 'concatenate'.")]
    public ExpressionReferenceOpts? Concatenate { get; set; }

    [TypeProperty("For 'parse'.")]
    public ExpressionParseOpts? Parse { get; set; }

    [TypeProperty("For 'cast'.")]
    public ExpressionCastOpts? Cast { get; set; }

    [TypeProperty("For 'branches'.")]
    public ExpressionBranchesOpts? Branches { get; set; }
}

public class ExpressionElseBranch
{
    [TypeProperty("The result when the pipeline produces nothing.", ObjectTypePropertyFlags.Required)]
    public required EngineBinding Result { get; set; }
}

/// <summary>An expression owned by the alert source, referenced from bindings with expressionRef: '&lt;reference&gt;'.</summary>
public class Expression
{
    [TypeProperty("Short name, unique on this resource; what expressionRef points at.", ObjectTypePropertyFlags.Required)]
    public required string Reference { get; set; }

    [TypeProperty("Label shown in the dashboard. Defaults to the reference.")]
    public string? Label { get; set; }

    [TypeProperty("Where the expression starts, e.g. 'payload' or 'alert'.", ObjectTypePropertyFlags.Required)]
    public required string RootReference { get; set; }

    [TypeProperty("Ordered pipeline; each operation feeds the next.", ObjectTypePropertyFlags.Required)]
    public required ExpressionOperation[] Operations { get; set; }

    [TypeProperty("Fallback result when the pipeline produces nothing.")]
    public ExpressionElseBranch? ElseBranch { get; set; }
}

public class AlertSourceEmailOptions
{
    [TypeProperty("PII to redact: 'credit_card_numbers', 'us_social_security_numbers', 'phone_numbers'.", ObjectTypePropertyFlags.Required)]
    public required string[] Redactions { get; set; }

    [TypeProperty("JavaScript expression that maps email fields to alert fields.")]
    public string? TransformExpression { get; set; }
}

public class AlertSourceHeartbeatOptions
{
    [TypeProperty("How often a ping is expected, in seconds.", ObjectTypePropertyFlags.Required)]
    public required int IntervalSeconds { get; set; }

    [TypeProperty("Consecutive missed pings before an alert fires.")]
    public int? FailureThreshold { get; set; }

    [TypeProperty("Seconds after a missed ping before the heartbeat counts as late.")]
    public int? GracePeriodSeconds { get; set; }
}

public class AlertSourceHttpCustomOptions
{
    [TypeProperty("JSON path to the deduplication key.", ObjectTypePropertyFlags.Required)]
    public required string DeduplicationKeyPath { get; set; }

    [TypeProperty("JavaScript expression returning an object with all alert fields.", ObjectTypePropertyFlags.Required)]
    public required string TransformExpression { get; set; }
}

public class AlertSourceProjectOptions
{
    [TypeProperty("Project IDs to watch.", ObjectTypePropertyFlags.Required)]
    public required string[] ProjectIds { get; set; }
}

public class AlertSourceCloudwatchOptions
{
    [TypeProperty("JSON path for the deduplication key. Defaults to AlarmName.", ObjectTypePropertyFlags.Required)]
    public required string DeduplicationKeyPath { get; set; }
}

public class AlertSourceRateLimitSharding
{
    [TypeProperty("JSON path to a value that splits the ingest rate limit into per-value buckets.", ObjectTypePropertyFlags.Required)]
    public required string RateLimitShardKeyPath { get; set; }
}

/// <summary>
/// An alert source: where alerts come from and how their fields are derived.
/// API: /v3/alert_sources (the endpoint used by the Terraform provider)
/// </summary>
[ResourceType("AlertSource")]
public class AlertSource : NameIdentifiers
{
    [TypeProperty("Source type, e.g. 'http', 'alertmanager', 'datadog', 'grafana', 'azure_monitor', 'heartbeat', 'email'. Cannot be changed after creation.", ObjectTypePropertyFlags.Required)]
    public required string SourceType { get; set; }

    [TypeProperty("Alert title binding, e.g. { valueLiteral: '{{ payload.title }}' } or { valueReference: 'payload.title' }.")]
    public EngineBinding? Title { get; set; }

    [TypeProperty("Alert description binding.")]
    public EngineBinding? Description { get; set; }

    [TypeProperty("Alert priority binding (alert priority ID).")]
    public EngineBinding? Priority { get; set; }

    [TypeProperty("Teams that can see private alerts from this source.")]
    public EngineBinding? VisibleToTeams { get; set; }

    [TypeProperty("Whether alerts from this source are private. Defaults to false.")]
    public bool? IsPrivate { get; set; }

    [TypeProperty("Expressions referenced by bindings via expressionRef.")]
    public Expression[]? Expressions { get; set; }

    [TypeProperty("Only ingest events matching any of these groups.")]
    public EngineConditionGroup[]? FilterConditionGroups { get; set; }

    [TypeProperty("Minutes before alerts auto-resolve.")]
    public int? AutoResolveTimeoutMinutes { get; set; }

    [TypeProperty("Whether alerts keep counting down to auto-resolve while attached to an incident. Defaults to true.")]
    public bool? AutoResolveIncidentAlerts { get; set; }

    [TypeProperty("Pause monitoring (heartbeat sources). Applied on update only.")]
    public bool? Disabled { get; set; }

    [TypeProperty("Attribute every alert to this team.")]
    public string? FixedTeamId { get; set; }

    [TypeProperty("IDs of teams that own this source.")]
    public string[]? OwningTeamIds { get; set; }

    [TypeProperty("Options for 'email' sources.")]
    public AlertSourceEmailOptions? EmailOptions { get; set; }

    [TypeProperty("Options for 'heartbeat' sources.")]
    public AlertSourceHeartbeatOptions? HeartbeatOptions { get; set; }

    [TypeProperty("Options for 'http_custom' sources.")]
    public AlertSourceHttpCustomOptions? HttpCustomOptions { get; set; }

    [TypeProperty("Options for 'jira' sources.")]
    public AlertSourceProjectOptions? JiraOptions { get; set; }

    [TypeProperty("Options for 'azure_devops' sources.")]
    public AlertSourceProjectOptions? AzureDevopsOptions { get; set; }

    [TypeProperty("Options for 'cloudwatch' sources.")]
    public AlertSourceCloudwatchOptions? CloudwatchOptions { get; set; }

    [TypeProperty("Split the ingest rate limit into buckets.")]
    public AlertSourceRateLimitSharding? RateLimitSharding { get; set; }

    [TypeProperty("Free-form annotations (key/value metadata).")]
    public Dictionary<string, string>? Annotations { get; set; }

    [TypeProperty("URL to send alert events to (read-only).", ObjectTypePropertyFlags.ReadOnly)]
    public string? AlertEventsUrl { get; set; }

    [TypeProperty("Token for sending alerts to this source (read-only, sensitive).", ObjectTypePropertyFlags.ReadOnly, isSecure: true)]
    public string? SecretToken { get; set; }

    [TypeProperty("Heartbeat sources: the URL to ping (read-only).", ObjectTypePropertyFlags.ReadOnly)]
    public string? HeartbeatPingUrl { get; set; }

    [TypeProperty("Version, incremented on every write (read-only).", ObjectTypePropertyFlags.ReadOnly)]
    public int? Version { get; set; }
}
