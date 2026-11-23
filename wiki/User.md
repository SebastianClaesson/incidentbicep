# User

Look up a user by email to get their ID (Terraform: `data "incident_user"`). Read-only: use `existing`.

## API endpoint

`/v2/users`

## Found by

`email` (case-insensitive). Changing the value used for lookup makes the extension create a new resource.

## Properties

| Property | Type | Required | Description |
|---|---|---|---|
| `email` | string | Yes | The user's email address. |

## Output properties

| Property | Type | Required | Description |
|---|---|---|---|
| `name` | string | No | Full name. |
| `slackUserId` | string | No | Slack user ID. |
| `role` | string | No | Base role (e.g. 'owner', 'administrator', 'responder', 'viewer'). |
| `id` | string | No | incident.io user ID. |

## Notes

- Users can't be created or changed by Bicep. Declaring one without `existing` fails with `ReadOnlyResource`.

## Bicep example

```bicep
resource alice 'User' existing = {
  email: 'alice@example.com'
}

output aliceId string = alice.id
```
