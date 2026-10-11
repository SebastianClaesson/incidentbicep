# bicep-ext-incidentio wiki

Reference for every resource type. The property tables are generated from the extension schema by `tools/generate_wiki.py`.

| Resource | Description |
|---|---|
| [Severity](Severity.md) | An incident severity, such as Minor, Major or Critical. |
| [IncidentRole](IncidentRole.md) | A role that can be assigned during an incident. |
| [IncidentStatus](IncidentStatus.md) | A custom incident status in the Active (`live`), Post-incident (`learning`) or `closed` category. |
| [CustomField](CustomField.md) | A custom field captured on incidents. |
| [CustomFieldOption](CustomFieldOption.md) | An option of a select custom field. |
| [CatalogType](CatalogType.md) | A catalog type (for example Service, Team or Customer), optionally including its attribute schema. |
| [CatalogEntry](CatalogEntry.md) | An entry in a catalog type. |
| [AlertAttribute](AlertAttribute.md) | An attribute extracted from incoming alerts, used for routing and display. |

## Extension configuration

```bicep
extension incidentio with {
  apiKey: apiKey      // @secure() parameter
  baseUrl: baseUrl    // optional, defaults to https://api.incident.io
}
```
