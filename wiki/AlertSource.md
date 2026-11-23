# AlertSource

An alert source: where alerts come from and how their title, description and other fields are derived.

## API endpoint

`/v3/alert_sources (the endpoint the Terraform provider uses; not in the public API docs)`

## Found by

`name` (case-insensitive). Changing the value used for lookup makes the extension create a new resource.

## Properties

| Property | Type | Required | Description |
|---|---|---|---|
| `sourceType` | string | Yes | Source type, e.g. 'http', 'alertmanager', 'datadog', 'grafana', 'azure_monitor', 'heartbeat', 'email'. Cannot be changed after creation. |
| `title` | EngineBinding | No | Alert title binding, e.g. { valueLiteral: '{{ payload.title }}' } or { valueReference: 'payload.title' }. |
| `description` | EngineBinding | No | Alert description binding. |
| `priority` | EngineBinding | No | Alert priority binding (alert priority ID). |
| `visibleToTeams` | EngineBinding | No | Teams that can see private alerts from this source. |
| `isPrivate` | bool | No | Whether alerts from this source are private. Defaults to false. |
| `expressions` | Expression[] | No | Expressions referenced by bindings via expressionRef. |
| `filterConditionGroups` | EngineConditionGroup[] | No | Only ingest events matching any of these groups. |
| `autoResolveTimeoutMinutes` | int | No | Minutes before alerts auto-resolve. |
| `autoResolveIncidentAlerts` | bool | No | Whether alerts keep counting down to auto-resolve while attached to an incident. Defaults to true. |
| `disabled` | bool | No | Pause monitoring (heartbeat sources). Applied on update only. |
| `fixedTeamId` | string | No | Attribute every alert to this team. |
| `owningTeamIds` | string[] | No | IDs of teams that own this source. |
| `emailOptions` | AlertSourceEmailOptions | No | Options for 'email' sources. |
| `heartbeatOptions` | AlertSourceHeartbeatOptions | No | Options for 'heartbeat' sources. |
| `httpCustomOptions` | AlertSourceHttpCustomOptions | No | Options for 'http_custom' sources. |
| `jiraOptions` | AlertSourceProjectOptions | No | Options for 'jira' sources. |
| `azureDevopsOptions` | AlertSourceProjectOptions | No | Options for 'azure_devops' sources. |
| `cloudwatchOptions` | AlertSourceCloudwatchOptions | No | Options for 'cloudwatch' sources. |
| `rateLimitSharding` | AlertSourceRateLimitSharding | No | Split the ingest rate limit into buckets. |
| `annotations` | object (map of string) | No | Free-form annotations (key/value metadata). |
| `name` | string | Yes | Unique name. Used to find the existing resource, so renaming creates a new resource. |

### EngineBinding

| Property | Type | Required | Description |
|---|---|---|---|
| `value` | EngineValue | No | The literal or reference value. |
| `arrayValue` | EngineValue[] | No | A list of literal or reference values. |
| `valueLiteral` | string | No | Shorthand for value: { literal: ... }. |
| `valueReference` | string | No | Shorthand for value: { reference: ... }. |
| `values` | string[] | No | Shorthand for an arrayValue of literals. |
| `expressionRef` | string | No | The name of an expression on this resource whose result becomes the value. Shorthand for value: { reference: 'expressions["name"]' }. |

### EngineValue

| Property | Type | Required | Description |
|---|---|---|---|
| `literal` | string | No | A fixed value. A catalog entry ID is a literal, not a reference. |
| `reference` | string | No | A reference into the scope, such as 'payload.team' or 'alert.title'. |

### Expression

| Property | Type | Required | Description |
|---|---|---|---|
| `reference` | string | Yes | Short name, unique on this resource; what expressionRef points at. |
| `label` | string | No | Label shown in the dashboard. Defaults to the reference. |
| `rootReference` | string | Yes | Where the expression starts, e.g. 'payload' or 'alert'. |
| `operations` | ExpressionOperation[] | Yes | Ordered pipeline; each operation feeds the next. |
| `elseBranch` | ExpressionElseBranch | No | Fallback result when the pipeline produces nothing. |

### ExpressionOperation

| Property | Type | Required | Description |
|---|---|---|---|
| `operationType` | string | Yes | 'navigate', 'filter', 'concatenate', 'count', 'min', 'max', 'sum', 'random', 'first', 'parse', 'branches' or 'cast'. |
| `navigate` | ExpressionReferenceOpts | No | For 'navigate'. |
| `filter` | ExpressionFilterOpts | No | For 'filter'. |
| `concatenate` | ExpressionReferenceOpts | No | For 'concatenate'. |
| `parse` | ExpressionParseOpts | No | For 'parse'. |
| `cast` | ExpressionCastOpts | No | For 'cast'. |
| `branches` | ExpressionBranchesOpts | No | For 'branches'. |

### ExpressionReferenceOpts

| Property | Type | Required | Description |
|---|---|---|---|
| `reference` | string | Yes | The reference to navigate to / concatenate with. |

### ExpressionFilterOpts

| Property | Type | Required | Description |
|---|---|---|---|
| `conditionGroups` | EngineConditionGroup[] | Yes | Keep items matching any of these groups. |

### EngineConditionGroup

| Property | Type | Required | Description |
|---|---|---|---|
| `conditions` | EngineCondition[] | Yes | All of these conditions must be satisfied for the group to match. |

### EngineCondition

| Property | Type | Required | Description |
|---|---|---|---|
| `subject` | string | Yes | The reference this condition tests, e.g. 'alert.title'. |
| `operation` | string | Yes | The operation, e.g. 'is_set', 'one_of', 'contains'. Available operations depend on the subject's type. |
| `paramBindings` | EngineBinding[] | No | Parameters for the operation. Omit (or empty) for operations without parameters. |

### ExpressionParseOpts

| Property | Type | Required | Description |
|---|---|---|---|
| `source` | string | Yes | JavaScript source evaluated to a result, e.g. '$.metadata.team'. |
| `returns` | ExpressionReturns | Yes | What the result is cast to. |

### ExpressionReturns

| Property | Type | Required | Description |
|---|---|---|---|
| `type` | string | Yes | Expected return type, e.g. 'String', 'Number', 'CatalogEntry["<type id>"]'. |
| `array` | bool | Yes | Whether the result is a list. |

### ExpressionCastOpts

| Property | Type | Required | Description |
|---|---|---|---|
| `returns` | ExpressionReturns | Yes | What the value is cast to. |

### ExpressionBranchesOpts

| Property | Type | Required | Description |
|---|---|---|---|
| `branches` | ExpressionBranch[] | Yes | Branches tried in order. |
| `returns` | ExpressionReturns | Yes | What the branches return. |

### ExpressionBranch

| Property | Type | Required | Description |
|---|---|---|---|
| `conditionGroups` | EngineConditionGroup[] | Yes | The branch applies when any of these groups match. |
| `result` | EngineBinding | Yes | The result when the branch applies. |

### ExpressionElseBranch

| Property | Type | Required | Description |
|---|---|---|---|
| `result` | EngineBinding | Yes | The result when the pipeline produces nothing. |

### AlertSourceEmailOptions

| Property | Type | Required | Description |
|---|---|---|---|
| `redactions` | string[] | Yes | PII to redact: 'credit_card_numbers', 'us_social_security_numbers', 'phone_numbers'. |
| `transformExpression` | string | No | JavaScript expression that maps email fields to alert fields. |

### AlertSourceHeartbeatOptions

| Property | Type | Required | Description |
|---|---|---|---|
| `intervalSeconds` | int | Yes | How often a ping is expected, in seconds. |
| `failureThreshold` | int | No | Consecutive missed pings before an alert fires. |
| `gracePeriodSeconds` | int | No | Seconds after a missed ping before the heartbeat counts as late. |

### AlertSourceHttpCustomOptions

| Property | Type | Required | Description |
|---|---|---|---|
| `deduplicationKeyPath` | string | Yes | JSON path to the deduplication key. |
| `transformExpression` | string | Yes | JavaScript expression returning an object with all alert fields. |

### AlertSourceProjectOptions

| Property | Type | Required | Description |
|---|---|---|---|
| `projectIds` | string[] | Yes | Project IDs to watch. |

### AlertSourceCloudwatchOptions

| Property | Type | Required | Description |
|---|---|---|---|
| `deduplicationKeyPath` | string | Yes | JSON path for the deduplication key. Defaults to AlarmName. |

### AlertSourceRateLimitSharding

| Property | Type | Required | Description |
|---|---|---|---|
| `rateLimitShardKeyPath` | string | Yes | JSON path to a value that splits the ingest rate limit into per-value buckets. |

## Output properties

| Property | Type | Required | Description |
|---|---|---|---|
| `alertEventsUrl` | string | No | URL to send alert events to (read-only). |
| `secretToken` | string (secure) | No | Token for sending alerts to this source (read-only, sensitive). |
| `heartbeatPingUrl` | string | No | Heartbeat sources: the URL to ping (read-only). |
| `version` | int | No | Version, incremented on every write (read-only). |
| `id` | string | No | incident.io ID (read-only, returned after create/update). |

## Notes

- `sourceType` can't be changed after creation.
- Bindings accept the Terraform shorthands `valueLiteral`, `valueReference`, `values` and `expressionRef`, or the full `value` / `arrayValue`. Set exactly one.
- `expressions` uses the API shape (`reference`, `rootReference`, `operations`, `elseBranch`). Terraform's `named_expression` blocks, with their `fallback`/`if`/`else_if` shorthand, aren't supported yet.
- `secretToken` is a sensitive output. Pass it straight to where it's needed, such as a Key Vault secret, rather than printing it.
- Alert source attributes (Terraform's `incident_alert_source_attribute`) aren't supported yet.

## Bicep example

```bicep
resource httpSource 'AlertSource' = {
  name: 'Platform HTTP alerts'
  sourceType: 'http'
  title: { valueReference: 'payload.title' }
  description: { valueLiteral: '{{ payload.description }}' }
  filterConditionGroups: [
    {
      conditions: [
        { subject: 'payload.environment', operation: 'is_not', paramBindings: [{ valueLiteral: 'test' }] }
      ]
    }
  ]
}

output alertEventsUrl string = httpSource.alertEventsUrl
```
