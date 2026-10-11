# IncidentRole

A role that can be assigned during an incident.

## API endpoint

`/v2/incident_roles`

## Found by

`name` (case-insensitive). Changing the value used for lookup makes the extension create a new resource.

## Properties

| Property | Type | Required | Description |
|---|---|---|---|
| `description` | string | Yes | Describes the purpose of the role. |
| `instructions` | string | Yes | Instructions shown to whoever is nominated for the role. |
| `shortform` | string | Yes | Short name used in Slack (e.g. 'comms'). |
| `name` | string | Yes | Unique name. Used to find the existing resource, so renaming creates a new resource. |

## Output properties

| Property | Type | Required | Description |
|---|---|---|---|
| `id` | string | No | incident.io ID (read-only, returned after create/update). |

## Bicep example

```bicep
resource commsLead 'IncidentRole' = {
  name: 'Communications Lead'
  shortform: 'comms'
  description: 'Owns internal and external communication during the incident.'
  instructions: 'Post an update to the status page and #incidents at least every 30 minutes.'
}
```
