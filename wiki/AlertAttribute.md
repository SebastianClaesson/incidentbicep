# AlertAttribute

An attribute extracted from incoming alerts, used for routing and display.

## API endpoint

`/v2/alert_attributes`

## Found by

`name` (case-insensitive). Changing the value used for lookup makes the extension create a new resource.

## Properties

| Property | Type | Required | Description |
|---|---|---|---|
| `type` | string | Yes | Engine type: e.g. 'String', 'Number', 'Bool', or 'CatalogEntry["<catalog type ID>"]'. |
| `array` | bool | Yes | Whether the attribute holds a list of values. |
| `required` | bool | No | Whether the attribute is required on alerts. |
| `emoji` | string | No | Emoji shown next to the attribute in chat messages, without colons (e.g. 'rotating_light'). |
| `name` | string | Yes | Unique name. Used to find the existing resource, so renaming creates a new resource. |

## Output properties

| Property | Type | Required | Description |
|---|---|---|---|
| `id` | string | No | incident.io ID (read-only, returned after create/update). |

## Bicep example

```bicep
resource alertService 'AlertAttribute' = {
  name: 'Service'
  type: 'CatalogEntry["${serviceType.id}"]'
  array: false
  emoji: 'gear'
}
```
