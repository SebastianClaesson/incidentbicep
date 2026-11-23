# MaintenanceWindow

A maintenance window that holds back matching alerts.

## API endpoint

`/v1/maintenance_windows`

## Found by

`name` (case-insensitive), ignoring archived windows. Changing the value used for lookup makes the extension create a new resource.

## Properties

| Property | Type | Required | Description |
|---|---|---|---|
| `startAt` | string | Yes | Start time (ISO 8601). |
| `endAt` | string | Yes | End time (ISO 8601). |
| `leadId` | string | Yes | User ID of the window's lead. |
| `alertConditionGroups` | EngineConditionGroup[] | Yes | Which alerts the window applies to: all conditions in at least one group must match. |
| `escalationTargets` | MaintenanceWindowEscalationTarget[] | No | Escalate matching alerts to these targets. |
| `incidentId` | string | No | Attach matching alerts to this incident. |
| `notificationMessage` | string | No | Message included in notifications about the window. |
| `notifyChannels` | MaintenanceWindowNotifyChannel[] | No | Channels notified when the window starts and ends. |
| `notifyStartMinutesBefore` | int | No | Minutes before the start to notify. |
| `notifyEndMinutesBefore` | int | No | Minutes before the end to notify. |
| `rerouteOnEnd` | bool | No | Re-route firing alerts through alert routing when the window ends. |
| `resolveOnEnd` | bool | No | Resolve firing alerts that matched the window when it ends. |
| `showInSidebar` | bool | No | Show the window in the dashboard sidebar while active. Defaults to false. |
| `owningTeamIds` | string[] | No | IDs of teams that own this window. |
| `name` | string | Yes | Unique name. Used to find the existing resource, so renaming creates a new resource. |

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

### MaintenanceWindowEscalationTarget

| Property | Type | Required | Description |
|---|---|---|---|
| `escalationPaths` | EngineBinding | No | Escalation paths to route matching alerts to. |
| `users` | EngineBinding | No | Users to notify directly. |

### MaintenanceWindowNotifyChannel

| Property | Type | Required | Description |
|---|---|---|---|
| `channelId` | string | Yes | Channel ID in the chat provider (e.g. a Slack channel ID). |
| `channelType` | string | Yes | Channel type (e.g. 'public', 'private'). |
| `channelName` | string | No | Human readable channel name. |

## Output properties

| Property | Type | Required | Description |
|---|---|---|---|
| `id` | string | No | incident.io ID (read-only, returned after create/update). |

## Notes

- Window names should be unique among non-archived windows. More than one match fails with `AmbiguousMatch`.
- `showInSidebar` defaults to `false`.

## Bicep example

```bicep
resource dbUpgrade 'MaintenanceWindow' = {
  name: 'Database upgrade 2026-11'
  startAt: '2026-11-14T22:00:00Z'
  endAt: '2026-11-15T02:00:00Z'
  leadId: alice.id
  alertConditionGroups: [
    {
      conditions: [
        { subject: 'alert.source', operation: 'one_of', paramBindings: [{ values: [httpSource.id] }] }
      ]
    }
  ]
  resolveOnEnd: true
}
```
