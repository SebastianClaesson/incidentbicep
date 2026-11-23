# Schedule

An on-call schedule. Its rotations are separate `ScheduleRotation` resources, as in Terraform v7.

## API endpoint

`/v3/schedules (the endpoint the Terraform provider uses; not in the public API docs)`

## Found by

`name` (case-insensitive). Changing the value used for lookup makes the extension create a new resource.

## Properties

| Property | Type | Required | Description |
|---|---|---|---|
| `timezone` | string | Yes | IANA timezone the schedule's rotations are anchored to, e.g. 'Europe/Stockholm'. Cannot be changed after creation. |
| `teamIds` | string[] | No | IDs of teams that own this schedule. |
| `holidaysPublicConfig` | ScheduleHolidaysPublicConfig | No | Public holidays to show on this schedule. Omit to show none. |
| `annotations` | object (map of string) | No | Free-form annotations (key/value metadata). |
| `name` | string | Yes | Unique name. Used to find the existing resource, so renaming creates a new resource. |

### ScheduleHolidaysPublicConfig

| Property | Type | Required | Description |
|---|---|---|---|
| `countryCodes` | string[] | Yes | ISO 3166-1 alpha-2 country codes whose public holidays are shown on the schedule. |

## Output properties

| Property | Type | Required | Description |
|---|---|---|---|
| `id` | string | No | incident.io ID (read-only, returned after create/update). |

## Notes

- `timezone` can't be changed after creation; rotations are anchored to it.

## Bicep example

```bicep
resource platform 'Schedule' = {
  name: 'Platform on-call'
  timezone: 'Europe/Stockholm'
  holidaysPublicConfig: {
    countryCodes: ['SE']
  }
}
```
