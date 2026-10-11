# CustomFieldOption

An option of a select custom field.

## API endpoint

`/v1/custom_field_options`

## Found by

`customFieldId` + `value` (case-insensitive). Changing the value used for lookup makes the extension create a new resource.

## Properties

| Property | Type | Required | Description |
|---|---|---|---|
| `sortKey` | int | No | Sort key used to order options. |
| `customFieldId` | string | Yes | ID of the custom field this option belongs to (e.g. myField.id). |
| `value` | string | Yes | Option value shown to users. Unique within the custom field. |

## Output properties

| Property | Type | Required | Description |
|---|---|---|---|
| `id` | string | No | incident.io ID (read-only, returned after create/update). |

## Notes

- If `sortKey` is left out on update, the current sort key is kept.

## Bicep example

```bicep
resource areaOptions 'CustomFieldOption' = [
  for (area, i) in ['API', 'Web app', 'Mobile app']: {
    customFieldId: affectedArea.id
    value: area
    sortKey: (i + 1) * 10
  }
]
```
