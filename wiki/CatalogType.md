# CatalogType

A catalog type (for example Service, Team or Customer), optionally including its attribute schema.

## API endpoint

`/v3/catalog_types (schema: /v3/catalog_types/{id}/actions/update_schema)`

## Found by

`name` (case-insensitive). Changing the value used for lookup makes the extension create a new resource.

## Properties

| Property | Type | Required | Description |
|---|---|---|---|
| `description` | string | Yes | Human readable description. |
| `typeName` | string | No | Type name used when referencing this type from attributes, e.g. 'Custom["Service"]'. Generated from name if omitted. Cannot be changed after creation. |
| `color` | 'yellow' \| 'green' \| 'blue' \| 'violet' \| 'pink' \| 'cyan' \| 'orange' | No | Display color in the dashboard. |
| `icon` | string | No | Display icon (e.g. 'server', 'users', 'bolt', 'database', 'globe', 'escalation-path'). |
| `categories` | string[] | No | Categories this type belongs to (e.g. 'service', 'team', 'customer', 'product', 'issue-tracker'). |
| `ranked` | bool | No | Whether entries of this type are ranked. |
| `useNameAsIdentifier` | bool | No | Allow entries to be referenced by name as well as by external ID. |
| `sourceRepoUrl` | string | No | URL of the repository that manages this type (shown in the dashboard). Tip: point it at the repo holding this Bicep file. |
| `owningTeamIds` | string[] | No | IDs of the teams that own this catalog type. |
| `annotations` | object (map of string) | No | Free-form annotations (key/value metadata). |
| `attributes` | CatalogTypeAttribute[] | No | Attribute schema. When set, the full schema is replaced: attributes not listed are removed. Omit to leave the schema unmanaged. |
| `name` | string | Yes | Unique name. Used to find the existing resource, so renaming creates a new resource. |

### CatalogTypeAttribute

| Property | Type | Required | Description |
|---|---|---|---|
| `name` | string | Yes | Unique attribute name. Existing attributes are matched by name so their IDs (and data) are kept. |
| `type` | string | Yes | Attribute type: 'String', 'Text', 'Number', 'Bool', 'IncidentSeverity', 'User', or another catalog type name such as 'Custom["Team"]' (use otherType.typeName). |
| `array` | bool | No | Whether the attribute holds a list of values. Defaults to false. |
| `mode` | 'api' \| 'dashboard' \| 'external' \| 'backlink' | No | Who manages the attribute's values: 'api' (default when managed by Bicep), 'dashboard', 'external' or 'backlink'. |
| `backlinkAttribute` | string | No | For mode 'backlink': the ID of the attribute on the other type to follow back. |

## Output properties

| Property | Type | Required | Description |
|---|---|---|---|
| `attributeIds` | object (map of string) | No | Map of attribute name to attribute ID (read-only). Useful for custom fields that need attribute IDs. |
| `id` | string | No | incident.io ID (read-only, returned after create/update). |

## Notes

- `typeName` can't be changed after creation. Reference it from other types' attributes (`type: teamType.typeName`).
- **`attributes` replaces the whole schema:** attributes you don't list are removed, together with their values on entries. Existing attributes are matched by name, so their IDs and values survive.
- Leave out `attributes` to leave the schema unmanaged (for example, when an external sync owns it).
- `attributeIds` (output) maps each attribute name to its ID.

## Bicep example

```bicep
resource serviceType 'CatalogType' = {
  name: 'Service'
  typeName: 'Custom["Service"]'
  description: 'Production services, managed in Bicep.'
  color: 'blue'
  icon: 'server'
  categories: ['service']
  annotations: {
    'managed-by': 'bicep'
  }
  attributes: [
    { name: 'Tier', type: 'String' }
    { name: 'Owner', type: teamType.typeName }
    { name: 'Runbooks', type: 'String', array: true }
  ]
}
```
