# bicep-ext-incidentio

A Bicep extension for managing [incident.io](https://incident.io) configuration as code.

Declare severities, incident roles, statuses, custom fields, catalog types and entries, and alert attributes in Bicep, then deploy them with `bicep local-deploy`. The extension talks to the [incident.io public API](https://api-docs.incident.io/).

> **Experimental:** Bicep local-deploy and the extensibility SDK are experimental features of Bicep.

## Prerequisites

- [.NET 10 SDK](https://dotnet.microsoft.com/download/dotnet/10.0) (only to build the extension)
- [Bicep CLI](https://learn.microsoft.com/azure/azure-resource-manager/bicep/install) v0.41.2 or later
- An incident.io API key (**Settings > API keys**) with the permissions for the resources you manage

## Supported resource types

| Resource | Bicep type | incident.io API | Found by | Notes |
|---|---|---|---|---|
| Severity | `Severity` | `/v1/severities` | `name` | |
| Incident role | `IncidentRole` | `/v2/incident_roles` | `name` | |
| Incident status | `IncidentStatus` | `/v1/incident_statuses` | `name` | `category` is immutable |
| Custom field | `CustomField` | `/v2/custom_fields` | `name` | `fieldType`, `catalogTypeId` are immutable |
| Custom field option | `CustomFieldOption` | `/v1/custom_field_options` | `customFieldId` + `value` | |
| Catalog type | `CatalogType` | `/v3/catalog_types` | `name` | Optional `attributes` manages the schema |
| Catalog entry | `CatalogEntry` | `/v3/catalog_entries` | `catalogTypeId` + `externalId` (or `name`) | Attribute values by name or ID |
| Alert attribute | `AlertAttribute` | `/v2/alert_attributes` | `name` | |
| Alert source | `AlertSource` | `/v3/alert_sources` ¹ | `name` | `sourceType` is immutable; `secretToken` is a sensitive output |
| Maintenance window | `MaintenanceWindow` | `/v1/maintenance_windows` | `name` (non-archived) | |
| Schedule | `Schedule` | `/v3/schedules` ¹ | `name` | `timezone` is immutable |
| Schedule rotation | `ScheduleRotation` | `/v3/schedules/{id}/rotations` ¹ | `scheduleId` + `name` | Supports `rollout`; unchanged rotations are never rewritten |
| Escalation path | `EscalationPath` | `/v2/escalation_paths` | `name` | Terraform v7 `sequences` shape |
| User (lookup only) | `User` | `/v2/users` | `email` | `existing` only |

¹ These are the endpoints the official Terraform provider uses. They aren't in incident.io's public API docs; see [vulnerabilities.md](vulnerabilities.md).

Every resource exposes a read-only `id` output. See the [wiki](wiki/Home.md) for full property references.

### Compared with the Terraform provider

Property names follow the [incident-io/incident](https://registry.terraform.io/providers/incident-io/incident/latest) Terraform provider (v7) in camelCase. A Terraform configuration translates nearly one-to-one: `time_to_ack_seconds` becomes `timeToAckSeconds`, `value_literal` becomes `valueLiteral`, and so on. Terraform data sources map to `existing` resources.

| Terraform | Bicep | Notes |
|---|---|---|
| `incident_schedule` + `incident_schedule_rotation` | `Schedule` + `ScheduleRotation` | Same split and `rollout` semantics |
| `incident_escalation_path` | `EscalationPath` | Same `start` and `sequences` shape and validation rules |
| `incident_maintenance_window` | `MaintenanceWindow` | `force_destroy` not applicable (nothing is deleted) |
| `incident_alert_source` | `AlertSource` | `expressions` uses the API shape, because `named_expression` sugar isn't supported yet |
| `data "incident_user"` | `User` existing | Looked up by email |
| `unlock_in_dashboard` | — | Not supported: Bicep-managed resources stay editable in the dashboard |

Not yet covered: `alert_source_attribute`, `alert_route`, `escalation_path_template`, `schedule_replica`, `schedule_sync_*`, workflows, policies and the remaining resources (see [Adding a resource type](#adding-a-resource-type)).

All resource types support `existing`, so you can reference things created in the UI, such as the built-in severities:

```bicep
resource critical 'Severity' existing = {
  name: 'Critical'
}
```

## Quick start

### 1. Build and package the extension

```powershell
./scripts/publish.ps1                     # win-x64, linux-x64, osx-arm64
./scripts/publish.ps1 -Runtimes win-x64   # faster, current platform only
```

This writes the extension to `./extension-publish/bicep-ext-incidentio`, which `samples/bicepconfig.json` points at. To share it with your team, publish to a registry instead:

```powershell
./scripts/publish.ps1 -OutputPath 'br:myregistry.azurecr.io/bicep/extensions/incidentio:0.1.0'
```

### 2. Set your API key

```powershell
$env:INCIDENT_IO_API_KEY = '<your API key>'
```

### 3. Deploy

```powershell
bicep local-deploy ./samples/basic/main.bicepparam
```

## Usage example

```bicep
targetScope = 'local'

@secure()
param apiKey string

extension incidentio with {
  apiKey: apiKey
}

resource sevMajor 'Severity' = {
  name: 'Major'
  description: 'Significant customer impact. Respond immediately.'
  rank: 2
}

resource serviceType 'CatalogType' = {
  name: 'Service'
  typeName: 'Custom["Service"]'
  description: 'Production services, managed in Bicep.'
  icon: 'server'
  color: 'blue'
  attributes: [
    { name: 'Tier', type: 'String' }
    { name: 'Runbooks', type: 'String', array: true }
  ]
}

resource paymentsApi 'CatalogEntry' = {
  catalogTypeId: serviceType.id
  externalId: 'payments-api'
  name: 'Payments API'
  attributeValues: {
    Tier: { value: '1' }                                        // by attribute name
    Runbooks: { arrayValue: ['https://runbooks.example.com/payments'] }
  }
}
```

More samples:

| Sample | Shows |
|---|---|
| [`samples/basic`](samples/basic/main.bicep) | Severities, role, status, a select field with options (loop), and `existing` |
| [`samples/catalog`](samples/catalog/main.bicep) | Two catalog types referencing each other, entries from a parameter array, and a catalog-backed custom field |
| [`samples/oncall`](samples/oncall/main.bicep) | User lookups, a schedule with weekly and office-hours rotations, and escalation paths with a branch, loop and handoff |
| [`samples/alerts`](samples/alerts/main.bicep) | HTTP and heartbeat alert sources with expressions and filters, and a maintenance window |

## Configuration

| Property | Required | Description |
|---|---|---|
| `apiKey` | Yes | incident.io API key. Marked sensitive; pass it from a `@secure()` parameter. |
| `baseUrl` | No | Defaults to `https://api.incident.io`. Must be HTTPS. Plain HTTP is accepted only for `localhost` and `127.0.0.1` (testing). |

## How it works

incident.io generates opaque IDs, so the extension finds existing resources by their natural key:

1. It lists the collection and matches on the identifier (usually `name`).
2. If it finds a match, it updates the resource with `PUT` (a full replace). Otherwise it creates one with `POST`.
3. It returns the server `id` so other resources can reference it.

Deployments are **idempotent**: running the same file twice creates the resources and then updates them, without duplicates.

**Behaviors to know:**

- **Renaming a resource creates a new one.** The name is how the extension finds it. Catalog entries are the exception when they have an `externalId`: they're found by it, so their name can change freely.
- **Nothing is deleted.** Removing a resource from your Bicep file leaves it in incident.io. This is the safer default, and it matches the official sample extensions.
- **Immutable fields fail loudly.** Changing a field the API can't update (such as `IncidentStatus.category` or `CustomField.fieldType`) fails the deployment with a clear message. The extension never ignores the change silently.
- **The catalog schema is replaced as a whole.** When `attributes` is set on a `CatalogType`, attributes you don't list are removed. Existing attributes are matched by name, so their IDs and stored values survive. Leave out `attributes` to keep the schema unmanaged, for example when an external sync owns it.
- **Catalog entry attribute values** can be keyed by attribute name or ID. Attributes you don't list are cleared. Leave out `attributeValues` entirely to leave the values untouched.
- **Rotations skip no-op writes.** A rotation write with `rollout` schedules a new version even when nothing changed. So `ScheduleRotation` compares the desired rotation with the live one and only writes when something differs.
- **Retries.** Rate-limited responses (`429`) and gateway errors are retried with backoff, honouring `Retry-After`. Transient network errors are retried for reads and updates. Creates (`POST`) are never retried after a network failure, because a retry could create a duplicate.

## What-if

`Preview()` is implemented, but the Bicep CLI has no `--what-if` for local-deploy yet. `bicep build` still validates property names, required fields, types and allowed values against the extension's schema.

## Testing without an incident.io account

`tools/mock-server/mock_incidentio.py` is a small in-memory mock of the endpoints this extension uses. It mimics the response envelopes, pagination (with tiny pages so paging gets exercised), catalog schema versions, and rejection of create-only fields on update. It needs only the Python standard library.

```powershell
python tools/mock-server/mock_incidentio.py 8787    # separate terminal
$env:INCIDENT_IO_BASE_URL = 'http://127.0.0.1:8787'
$env:INCIDENT_IO_API_KEY  = 'test-key'
bicep local-deploy ./samples/catalog/main.bicepparam
Invoke-RestMethod http://127.0.0.1:8787/_state       # inspect what was written
```

The mock checks the extension's behavior, not incident.io's validation rules. Always test against a real (non-production) incident.io workspace before relying on a change.

## Project structure

```
src/
  Program.cs                         # Registers the extension and handlers
  Models.cs                          # Incident config and catalog types (C# classes → Bicep types)
  ModelsOnCall.cs                    # User, Schedule, ScheduleRotation, EscalationPath
  ModelsAlerting.cs                  # AlertSource (+ expressions), MaintenanceWindow
  Engine.cs                          # Shared bindings/conditions + Terraform shorthands; no-op comparison
  IncidentIoClient.cs                # HTTP: auth, HTTPS check, retries, pagination, errors
  Handlers/
    IncidentIoResourceHandlerBase.cs # Find → create/update → outputs; 'existing' support
    SeverityHandler.cs ...           # ~8 lines each: API path + JSON keys
    CatalogTypeHandler.cs            # + schema management
    CatalogEntryHandler.cs           # + attribute name → ID resolution
    ScheduleRotationHandler.cs       # + nested path, rollout, skip unchanged writes
    EscalationPathSequences.cs       # sequences ⇄ API tree, Terraform's validation rules
tests/                               # xUnit: schema generation, serialization, client, catalog, engine, escalation paths
samples/                             # Deployable examples
tools/mock-server/                   # Local incident.io mock for end-to-end tests
wiki/                                # Per-resource reference
```

## Adding a resource type

1. Add the model in `src/Models.cs`: a class with `[ResourceType("Name")]` that inherits `NameIdentifiers` (or its own identifiers class).
2. Add a handler in `src/Handlers/`. For name-keyed resources, inherit `NamedResourceHandler<T>` and set `ApiPath`, `ItemKey` and `CollectionKey`. Use `CreateOnlyFields` for fields the update payload doesn't accept.
3. Register it in `src/Program.cs` with `.WithResourceHandler<YourHandler>()`.
4. Add the endpoint to the mock server, a sample, and a wiki page.

**Candidates for later:** alert source attributes, alert routes, escalation path templates, schedule replicas and sync rules, workflows, incident forms and templates, and policies. The Terraform provider's schema (`internal/apischema/public-schema-v3-including-secret-endpoints.json` in [incident-io/terraform-provider-incident](https://github.com/incident-io/terraform-provider-incident)) is the best reference for payload shapes.

## Further reading

- [ARCHITECTURE.md](ARCHITECTURE.md): design decisions
- [vulnerabilities.md](vulnerabilities.md): security considerations and dependencies
- Inspired by [netbox-bicep-extension](https://github.com/NetboxTools/netbox-bicep-extension)
