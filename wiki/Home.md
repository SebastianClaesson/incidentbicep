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
| [User](User.md) | Look up a user by email to get their ID (Terraform: `data "incident_user"`). Read-only: use `existing`. |
| [Schedule](Schedule.md) | An on-call schedule. Its rotations are separate `ScheduleRotation` resources, as in Terraform v7. |
| [ScheduleRotation](ScheduleRotation.md) | A rotation on a schedule: who takes turns being on call, and the cadence they hand over on. |
| [EscalationPath](EscalationPath.md) | An escalation path, written as a flat map of named node sequences (the Terraform v7 shape). |
| [AlertSource](AlertSource.md) | An alert source: where alerts come from and how their title, description and other fields are derived. |
| [MaintenanceWindow](MaintenanceWindow.md) | A maintenance window that holds back matching alerts. |

## Extension configuration

```bicep
extension incidentio with {
  apiKey: apiKey      // @secure() parameter
  baseUrl: baseUrl    // optional, defaults to https://api.incident.io
}
```
