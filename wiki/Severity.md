# Severity

An incident severity, such as Minor, Major or Critical.

## API endpoint

`/v1/severities`

## Found by

`name` (case-insensitive). Changing the value used for lookup makes the extension create a new resource.

## Properties

| Property | Type | Required | Description |
|---|---|---|---|
| `description` | string | Yes | Description of the severity. |
| `rank` | int | No | Sort rank. Lower numbers are less severe. |
| `name` | string | Yes | Unique name. Used to find the existing resource, so renaming creates a new resource. |

## Output properties

| Property | Type | Required | Description |
|---|---|---|---|
| `id` | string | No | incident.io ID (read-only, returned after create/update). |

## Bicep example

```bicep
resource sevMajor 'Severity' = {
  name: 'Major'
  description: 'Significant customer impact. Respond immediately.'
  rank: 2
}

// Built-in severities can be referenced without managing them:
resource critical 'Severity' existing = {
  name: 'Critical'
}
```
