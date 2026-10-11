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

Every resource exposes a read-only `id` output. See the [wiki](wiki/Home.md) for full property references.

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
  Models.cs                          # Resource types (C# classes → Bicep types)
  IncidentIoClient.cs                # HTTP: auth, HTTPS check, retries, pagination, errors
  Handlers/
    IncidentIoResourceHandlerBase.cs # Find → create/update → outputs; 'existing' support
    SeverityHandler.cs ...           # ~8 lines each: API path + JSON keys
    CatalogTypeHandler.cs            # + schema management
    CatalogEntryHandler.cs           # + attribute name → ID resolution
tests/                               # xUnit: schema generation, serialization, client, catalog logic
samples/                             # Deployable examples
tools/mock-server/                   # Local incident.io mock for end-to-end tests
wiki/                                # Per-resource reference
```

## Adding a resource type

1. Add the model in `src/Models.cs`: a class with `[ResourceType("Name")]` that inherits `NameIdentifiers` (or its own identifiers class).
2. Add a handler in `src/Handlers/`. For name-keyed resources, inherit `NamedResourceHandler<T>` and set `ApiPath`, `ItemKey` and `CollectionKey`. Use `CreateOnlyFields` for fields the update payload doesn't accept.
3. Register it in `src/Program.cs` with `.WithResourceHandler<YourHandler>()`.
4. Add the endpoint to the mock server, a sample, and a wiki page.

**Candidates for later:** schedules, escalation paths, alert sources and routes, workflows, and incident templates. Their API payloads are deeply nested, so each needs careful modelling.

## Further reading

- [ARCHITECTURE.md](ARCHITECTURE.md): design decisions
- [vulnerabilities.md](vulnerabilities.md): security considerations and dependencies
- Inspired by [netbox-bicep-extension](https://github.com/NetboxTools/netbox-bicep-extension)
