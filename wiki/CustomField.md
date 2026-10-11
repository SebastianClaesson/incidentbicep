# CustomField

A custom field captured on incidents.

## API endpoint

`/v2/custom_fields`

## Found by

`name` (case-insensitive). Changing the value used for lookup makes the extension create a new resource.

## Properties

| Property | Type | Required | Description |
|---|---|---|---|
| `description` | string | Yes | Description of the custom field. |
| `fieldType` | 'single_select' \| 'multi_select' \| 'text' \| 'link' \| 'numeric' | Yes | Field type. Cannot be changed after creation. |
| `catalogTypeId` | string | No | For catalog-backed select fields: the catalog type ID that provides the options. Cannot be changed after creation. |
| `groupByCatalogAttributeId` | string | No | For catalog fields: the attribute ID used to group catalog entries. |
| `helptextCatalogAttributeId` | string | No | For catalog fields: the attribute ID that provides help text for options. |
| `name` | string | Yes | Unique name. Used to find the existing resource, so renaming creates a new resource. |

## Output properties

| Property | Type | Required | Description |
|---|---|---|---|
| `id` | string | No | incident.io ID (read-only, returned after create/update). |

## Notes

- `fieldType` and `catalogTypeId` can't be changed after creation.
- For `single_select`/`multi_select` fields, add options with [CustomFieldOption](CustomFieldOption.md) or back the field with a catalog type via `catalogTypeId`.

## Bicep example

```bicep
resource affectedArea 'CustomField' = {
  name: 'Affected Area'
  description: 'Which part of the product is affected?'
  fieldType: 'single_select'
}

resource affectedServices 'CustomField' = {
  name: 'Affected Services'
  description: 'Which services are affected?'
  fieldType: 'multi_select'
  catalogTypeId: serviceType.id
}
```
