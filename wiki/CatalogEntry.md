# CatalogEntry

An entry in a catalog type.

## API endpoint

`/v3/catalog_entries`

## Found by

`catalogTypeId` + `externalId` when set (recommended), otherwise `catalogTypeId` + `name`. Changing the value used for lookup makes the extension create a new resource.

## Properties

| Property | Type | Required | Description |
|---|---|---|---|
| `aliases` | string[] | No | Alternative identifiers for referencing this entry. |
| `rank` | int | No | Ordering rank when the catalog type is ranked. |
| `attributeValues` | object (map of CatalogEntryAttributeValue) | No | Attribute values keyed by attribute NAME (resolved automatically) or attribute ID. Attributes not listed are cleared. |
| `catalogTypeId` | string | Yes | ID of the catalog type (e.g. serviceType.id). |
| `name` | string | Yes | Entry name. Used to find the entry when externalId is not set. |
| `externalId` | string | No | Stable external ID, unique within the type. Recommended: when set, it is used to find the entry, so the name can change freely. |

### CatalogEntryAttributeValue

| Property | Type | Required | Description |
|---|---|---|---|
| `value` | string | No | Literal value for a non-array attribute. For references to other catalog entries use the entry ID or external ID. |
| `arrayValue` | string[] | No | Literal values for an array attribute. |

## Output properties

| Property | Type | Required | Description |
|---|---|---|---|
| `id` | string | No | incident.io ID (read-only, returned after create/update). |

## Notes

- `attributeValues` keys can be attribute **names** (resolved from the type's schema) or attribute IDs.
- Use `value` for single attributes and `arrayValue` for array attributes. To reference another catalog entry, use its ID or external ID.
- Attributes you don't list in `attributeValues` are cleared. Leave out `attributeValues` entirely to leave all values untouched.

## Bicep example

```bicep
resource paymentsApi 'CatalogEntry' = {
  catalogTypeId: serviceType.id
  externalId: 'payments-api'
  name: 'Payments API'
  aliases: ['payments']
  attributeValues: {
    Tier: { value: '1' }
    Owner: { value: 'payments' }   // external ID of a Team entry
    Runbooks: { arrayValue: ['https://runbooks.example.com/payments'] }
  }
}
```
