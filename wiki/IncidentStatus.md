# IncidentStatus

A custom incident status in the Active (`live`), Post-incident (`learning`) or `closed` category.

## API endpoint

`/v1/incident_statuses`

## Found by

`name` (case-insensitive). Changing the value used for lookup makes the extension create a new resource.

## Properties

| Property | Type | Required | Description |
|---|---|---|---|
| `category` | 'live' \| 'learning' \| 'closed' | Yes | Category: 'live' (shown as Active), 'learning' (shown as Post-incident) or 'closed'. Cannot be changed after creation. |
| `description` | string | Yes | Rich text description of the status. |
| `rank` | int | No | Position within its category, lowest first. Must be unique within the category. |
| `name` | string | Yes | Unique name. Used to find the existing resource, so renaming creates a new resource. |

## Output properties

| Property | Type | Required | Description |
|---|---|---|---|
| `id` | string | No | incident.io ID (read-only, returned after create/update). |

## Notes

- `category` can't be changed after creation. Changing it fails the deployment with `ImmutableProperty`.
- Statuses in other categories (triage, declined, merged, paused) are managed by incident.io.

## Bicep example

```bicep
resource monitoring 'IncidentStatus' = {
  name: 'Monitoring'
  category: 'live'
  description: 'A fix is deployed and we are watching for recurrence.'
}
```
