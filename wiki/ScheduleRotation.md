# ScheduleRotation

A rotation on a schedule: who takes turns being on call, and the cadence they hand over on.

## API endpoint

`/v3/schedules/{scheduleId}/rotations (the endpoint the Terraform provider uses; not in the public API docs)`

## Found by

`scheduleId` + `name` (case-insensitive). Changing the value used for lookup makes the extension create a new resource.

## Properties

| Property | Type | Required | Description |
|---|---|---|---|
| `firstIntervalStartsAt` | string | Yes | When the first handover interval is counted from (ISO 8601, e.g. '2026-01-05T09:00:00Z'). Fixes the time of day and day of week shifts change hands. Can't change in the same deployment as a rollout. |
| `handovers` | ScheduleHandover[] | Yes | The cadence shifts hand over on; several entries alternate. |
| `users` | string[] | Yes | User IDs in the order they take shifts (use User 'existing' to look them up). Use 'NOBODY' for an empty slot. |
| `concurrentShifts` | int | No | How many people are on call at once. Defaults to 1. |
| `rank` | int | No | Position in the schedule's running order, lowest first. |
| `rollout` | 'immediate' \| 'after_current_shift' \| 'after_full_rotation' | No | How a change is introduced: 'immediate', 'after_current_shift' or 'after_full_rotation'. Only used when the rotation changes. |
| `schedulingMode` | 'fair' \| 'sequential' | No | How users are allocated across shifts of differing length: 'fair' or 'sequential'. Omit to let incident.io pick. |
| `workingIntervals` | WeekdayInterval[] | No | Restrict on-call to these weekday intervals. Omit for around-the-clock. |
| `scheduleId` | string | Yes | ID of the schedule this rotation belongs to (e.g. schedule.id). |
| `name` | string | Yes | Name of the rotation, unique within the schedule. |

### ScheduleHandover

| Property | Type | Required | Description |
|---|---|---|---|
| `interval` | int | Yes | How many of the interval type to wait between handovers. |
| `intervalType` | 'hourly' \| 'daily' \| 'weekly' | Yes | How often a handover occurs. |

### WeekdayInterval

| Property | Type | Required | Description |
|---|---|---|---|
| `weekday` | 'monday' \| 'tuesday' \| 'wednesday' \| 'thursday' \| 'friday' \| 'saturday' \| 'sunday' | Yes | Day of the week. |
| `startTime` | string | Yes | Time of day the window opens, as HH:MM. |
| `endTime` | string | Yes | Time of day the window closes, as HH:MM. |

## Output properties

| Property | Type | Required | Description |
|---|---|---|---|
| `effectiveFrom` | string | No | When the current shape of the rotation took over (read-only). |
| `id` | string | No | Rotation ID (read-only). |

## Notes

- **Unchanged rotations are never written.** A write with `rollout` schedules a new version of the rotation even when nothing changed, so the extension compares the desired rotation with the live one first. Working intervals compare as a set, but user order matters.
- `rollout` only applies when the rotation changes: `immediate` replaces the line-up now, `after_current_shift` lets the current shift finish, and `after_full_rotation` waits for a full cycle.
- `firstIntervalStartsAt` can't change in the same deployment as a `rollout`. Change it first with `rollout` unset, then make the line-up change.
- `users` takes user IDs, which you can look up with `User` existing. Use `NOBODY` for a slot with nobody in it; an empty list is rejected.

## Bicep example

```bicep
resource primary 'ScheduleRotation' = {
  scheduleId: platform.id
  name: 'Primary'
  firstIntervalStartsAt: '2026-01-05T08:00:00Z'
  handovers: [
    { interval: 1, intervalType: 'weekly' }
  ]
  users: [alice.id, bob.id]
  rollout: 'after_current_shift'
}
```
