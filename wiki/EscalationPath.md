# EscalationPath

An escalation path, written as a flat map of named node sequences (the Terraform v7 shape).

## API endpoint

`/v2/escalation_paths`

## Found by

`name` (case-insensitive). Changing the value used for lookup makes the extension create a new resource.

## Properties

| Property | Type | Required | Description |
|---|---|---|---|
| `kind` | 'standalone' \| 'templated' | No | 'standalone' (default) or 'templated'. Cannot be changed after creation. |
| `start` | string | No | Key of the sequence the path begins with. Required unless kind is 'templated'. |
| `sequences` | object (map of EscalationPathSequence) | No | Named sequences of nodes. Branches reference other sequences by key. Required unless kind is 'templated'. |
| `workingHours` | EscalationPathWorkingHours[] | No | Working hours referenced by branches and time windows. |
| `repeatConfig` | EscalationPathRepeatConfig | No | Repeat an acknowledged escalation while the alert is unresolved. |
| `teamIds` | string[] | No | IDs of the teams that own this path. |
| `templateId` | string | No | Templated paths: the escalation path template to build from. |
| `paramBindings` | object (map of EngineBinding) | No | Templated paths: a value for each template param, keyed by param name. |
| `name` | string | Yes | Unique name. Used to find the existing resource, so renaming creates a new resource. |

### EscalationPathSequence

| Property | Type | Required | Description |
|---|---|---|---|
| `nodes` | EscalationPathNode[] | Yes | Nodes in the order they run. |

### EscalationPathNode

| Property | Type | Required | Description |
|---|---|---|---|
| `id` | string | No | Node id, unique in the path, so a loop can name it. Omit unless something loops back here; a stable id is derived from the position. |
| `level` | EscalationPathLevel | No | Page users or schedules. |
| `notifyChannel` | EscalationPathNotifyChannel | No | Notify channels. |
| `delay` | EscalationPathDelay | No | Wait before the next node. |
| `escalationPath` | EscalationPathHandoff | No | Hand over to another escalation path. |
| `branch` | EscalationPathBranch | No | Continue down one of two sequences. Must be the last node in its sequence. |
| `loop` | EscalationPathLoop | No | Go back to an earlier node. Must be last in its sequence, or followed by a single escalationPath node. |

### EscalationPathLevel

| Property | Type | Required | Description |
|---|---|---|---|
| `targets` | EscalationPathTarget[] | Yes | Users or schedules to page. |
| `ackMode` | 'all' \| 'first' | No | 'first' (default): the first acknowledgement cancels everyone else on the level. 'all': everyone must acknowledge. |
| `retryConfig` | EscalationPathRetryConfig | No | Re-page this level before moving on. |
| `roundRobinConfig` | EscalationPathRoundRobinConfig | No | Round-robin between targets. |
| `timeToAckSeconds` | int | No | Seconds to wait for an acknowledgement before moving on. |
| `timeToAckIntervalCondition` | 'active' \| 'inactive' | No | If time to ack is relative to working hours: move on while they are 'active' or 'inactive'. |
| `timeToAckWeekdayIntervalConfigId` | string | No | If time to ack is relative to working hours: the workingHours id. |

### EscalationPathTarget

| Property | Type | Required | Description |
|---|---|---|---|
| `id` | string | Yes | ID of the user, schedule or channel. |
| `type` | 'schedule' \| 'user' \| 'slack_channel' \| 'msteams_channel' | Yes | Target type. |
| `urgency` | 'high' \| 'low' | Yes | Urgency of the page. |
| `scheduleMode` | string | No | Schedule targets only: 'currently_on_call', 'all_users', 'all_users_for_rota', 'currently_on_call_for_rota', 'next_on_call_for_rota' or 'next_on_call'. |
| `selectedRotaId` | string | No | Schedule targets only: the rotation the *_for_rota schedule modes apply to. |

### EscalationPathRetryConfig

| Property | Type | Required | Description |
|---|---|---|---|
| `attempts` | int | Yes | Total pages for this level, counting the first (2-10). |
| `intervalSeconds` | int | Yes | Seconds between attempts (a whole number of minutes). |

### EscalationPathRoundRobinConfig

| Property | Type | Required | Description |
|---|---|---|---|
| `enabled` | bool | Yes | Whether round robin is enabled. |
| `rotateAfterSeconds` | int | No | Seconds before rotating to the next target. Unset sticks with one target per level. |

### EscalationPathNotifyChannel

| Property | Type | Required | Description |
|---|---|---|---|
| `targets` | EscalationPathTarget[] | Yes | Slack or Microsoft Teams channels to notify. |
| `timeToAckSeconds` | int | No | Seconds to wait for an acknowledgement before moving on. |
| `timeToAckIntervalCondition` | 'active' \| 'inactive' | No | If time to ack is relative to working hours: move on while they are 'active' or 'inactive'. |
| `timeToAckWeekdayIntervalConfigId` | string | No | If time to ack is relative to working hours: the workingHours id. |

### EscalationPathDelay

| Property | Type | Required | Description |
|---|---|---|---|
| `delaySeconds` | int | No | Seconds to wait before the next node. |
| `delayIntervalCondition` | 'active' \| 'inactive' | No | If relative to working hours: advance while they are 'active' or 'inactive'. |
| `delayWeekdayIntervalConfigId` | string | No | If relative to working hours: the workingHours id. |

### EscalationPathHandoff

| Property | Type | Required | Description |
|---|---|---|---|
| `escalationPathId` | string | Yes | ID of the escalation path to hand over to. |

### EscalationPathBranch

| Property | Type | Required | Description |
|---|---|---|---|
| `if` | EscalationPathBranchIf | Yes | What the branch tests. Set exactly one of workingHoursActive or priorityOneOf. |
| `then` | string | Yes | Sequence to continue down when the condition is met. |
| `else` | string | No | Sequence to continue down otherwise. Omit to end the path. |

### EscalationPathBranchIf

| Property | Type | Required | Description |
|---|---|---|---|
| `workingHoursActive` | string | No | The id of one of this path's workingHours; met while those hours are active. |
| `priorityOneOf` | string[] | No | Alert priority IDs; met when the escalation came in at one of them. |

### EscalationPathLoop

| Property | Type | Required | Description |
|---|---|---|---|
| `backTo` | string | Yes | The id of the node to repeat from: the path's first node, or a branch this loop sits under. |
| `times` | int | Yes | How many times to repeat. |

### EscalationPathWorkingHours

| Property | Type | Required | Description |
|---|---|---|---|
| `id` | string | Yes | Identifier referenced by branches and time windows (e.g. 'UK'). |
| `name` | string | Yes | Display name. |
| `timezone` | string | Yes | IANA timezone for the intervals. |
| `weekdayIntervals` | WeekdayInterval[] | Yes | The working intervals. |

### WeekdayInterval

| Property | Type | Required | Description |
|---|---|---|---|
| `weekday` | 'monday' \| 'tuesday' \| 'wednesday' \| 'thursday' \| 'friday' \| 'saturday' \| 'sunday' | Yes | Day of the week. |
| `startTime` | string | Yes | Time of day the window opens, as HH:MM. |
| `endTime` | string | Yes | Time of day the window closes, as HH:MM. |

### EscalationPathRepeatConfig

| Property | Type | Required | Description |
|---|---|---|---|
| `delayRepeatOnActivity` | bool | Yes | When true, incident activity resets the repeat timer. |
| `repeatAfterSeconds` | int | Yes | Seconds to wait before repeating an acknowledged, unresolved escalation. |

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

## Output properties

| Property | Type | Required | Description |
|---|---|---|---|
| `id` | string | No | incident.io ID (read-only, returned after create/update). |

## Notes

- Each sequence runs its nodes in order. A `branch`, which must be the last node, names the sequences to continue down. Without one, the path ends after the last node.
- A `loop` can only go back to the path's first node or to a branch it sits under. It must be last in its sequence, or be followed by a single `escalationPath` handoff.
- Node ids are optional. An id is derived from the node's position (`<sequence>/<index>`), so it stays stable. Set `id` only on nodes that a loop goes back to.
- `ackMode` defaults to `first`, as in Terraform v7: the first person to acknowledge cancels everyone else's page on that level. Set `ackMode: 'all'` to keep the old behaviour.
- Validation follows the Terraform provider. It rejects unknown or unreachable sequences, cycles, a sequence branched to twice, and nodes that set zero or several blocks, and it reports every problem at once.
- When read with `existing`, sequence names are recovered from derived node ids. If that isn't possible, the start is called `main` and branch targets `<sequence>_then` and `<sequence>_else`.

## Bicep example

```bicep
resource platform 'EscalationPath' = {
  name: 'Platform'
  start: 'main'
  sequences: {
    main: {
      nodes: [
        {
          id: 'start'
          branch: {
            if: { workingHoursActive: 'office' }
            then: 'in_hours'
            else: 'out_of_hours'
          }
        }
      ]
    }
    in_hours: {
      nodes: [
        { level: { targets: [{ type: 'schedule', id: schedule.id, urgency: 'high', scheduleMode: 'currently_on_call' }], timeToAckSeconds: 300 } }
        { delay: { delaySeconds: 120 } }
        { loop: { backTo: 'start', times: 2 } }
      ]
    }
    out_of_hours: {
      nodes: [
        { level: { targets: [{ type: 'user', id: alice.id, urgency: 'low' }], timeToAckSeconds: 900 } }
        { escalationPath: { escalationPathId: fallback.id } }
      ]
    }
  }
  workingHours: [
    {
      id: 'office'
      name: 'Office hours'
      timezone: 'Europe/Stockholm'
      weekdayIntervals: [for day in ['monday', 'tuesday', 'wednesday', 'thursday', 'friday']: { weekday: day, startTime: '08:00', endTime: '18:00' }]
    }
  ]
}
```
