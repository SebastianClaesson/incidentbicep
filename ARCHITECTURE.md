# Architecture

## Overview

```
bicep local-deploy ──gRPC──▶ bicep-ext-incidentio ──HTTPS (Bearer)──▶ api.incident.io
                              │
                              ├─ Models.cs          C# classes → Bicep type schema (via SDK reflection)
                              ├─ Handlers/*         one per resource type, all on a shared base
                              └─ IncidentIoClient   auth, URL validation, retries, pagination
```

Bicep starts the extension binary, asks it for its type schema (generated from `Models.cs`), and calls `CreateOrUpdate`, `Preview` or `Get` on the matching handler for each resource.

## Decisions

### .NET 10 and Bicep SDK 0.41.2 (not .NET 9 and 0.37.x like the NetBox extension)

`Azure.Bicep.Local.Extension` 0.40 and later target .NET 10 only. We moved up because the type generator in 0.41 fixes problems that shaped the NetBox extension:

| Capability | 0.37 – 0.39 | 0.41 |
|---|---|---|
| `int?`, `bool?` | ❌ (needed `string?` workarounds) | ✅ |
| `Dictionary<string, T>` | ❌ | ✅ |
| Arrays of objects | partial | ✅ |
| Several properties with the same array type | ❌ silently dropped after the first | ✅ |
| Fluent builder API | – | ✅ |

The incident.io catalog needs arrays, maps and nested objects, so moving up was the right call. .NET 10 is an LTS release. `TypeDefinitionTests` generate the schema in-process and assert that every model property is present, which guards against regressions like the dropped-array bug.

The NetBox extension also references `Bicep.LocalDeploy`. Despite the name, that's a **community** package (author: Gijsreyn, from [bicep-local-docgen](https://github.com/Gijsreyn/bicep-local-docgen)) that provides attributes for documentation generation. It plays no part in local deployment, and the NetBox code never uses it. This project doesn't reference it.

### Identity: natural keys, matched client side

incident.io IDs are server-generated ULIDs, and most list endpoints have no name filter. Each handler declares:

- `ApiPath`, `ItemKey` and `CollectionKey`, because the JSON envelopes differ per endpoint (`{"severity": …}` versus `{"severities": […]}`)
- `IsMatch(item, identifiers)`, which defaults to a case-insensitive `name` match
- `GetListQuery`/`PageSize`, for endpoints that require a parent filter or pagination

Matching more than one item raises `AmbiguousMatch` rather than guessing.

Catalog entries prefer `externalId` and use the API's `identifier` filter. They fall back to a name scan only when no external ID is set, because `identifier` matches names only when the type has `useNameAsIdentifier`.

### PUT is a full replace; create-only fields

incident.io updates use `PUT` with a payload that's a subset of the create payload. `CreateOnlyFields` lists the fields that must be removed on update. If the desired value differs from the existing one, the handler throws `ImmutableProperty`. The alternatives were to drop the change silently or to delete and recreate the resource, which would lose incident history.

### Serialization

- The **SDK** serializes models over gRPC with camelCase. **incident.io** expects snake_case. A separate `JsonSerializerOptions` (`SnakeCaseLower`) handles the API side, so models carry **no** `[JsonPropertyName]` attributes, which would break the SDK side.
- `[TypeProperty(..., ReadOnly)]` properties are outputs. `SerializeWritable` strips them by reflection, so outputs can never leak into requests.
- Dictionary keys (attribute IDs, annotation keys) are **not** renamed.
- **Enums:** the SDK turns enum member names into Bicep string literals, so members are named exactly as the API values (`single_select`). A property-level `[JsonConverter(typeof(JsonStringEnumConverter))]` makes both serializers use the names verbatim. Values containing `-` (catalog icons) can't be C# identifiers, so `icon` stays a string.

### Catalog type schema in the same resource

The schema is updated through a separate action endpoint that needs the current `version` (optimistic concurrency). Folding it into `CatalogType` lets users declare a type and its attributes in one block. Attributes are matched by name to existing IDs, so values stored on entries survive redeploys. Setting `attributes` to null leaves the schema alone, for types owned by an external sync.

### Attribute values by name

The API keys `attribute_values` by attribute ID, which isn't known until deploy time. The handler fetches the type's schema and resolves names, so Bicep can say `Tier: { value: '1' }`. Unknown names fail with the list of valid names.

### HTTP client

- A shared `SocketsHttpHandler` provides connection pooling. Each request gets its own `HttpClient` with its own auth header, so configurations never mix.
- Redirects are disabled, so the Bearer token is never forwarded to another host.
- Retries cover 429, 502, 503 and 504, with `Retry-After` honoured and capped at 30s, for up to 5 attempts. Network exceptions are retried for GET and PUT only, because a retried POST could create a duplicate.
- The base URL must be HTTPS. HTTP is allowed for loopback only.

### Terraform parity and the v3 endpoints

On-call and alerting resources mirror the [Terraform provider](https://github.com/incident-io/terraform-provider-incident) v7, with the same split, property names (in camelCase) and validation. Where Terraform uses endpoints that aren't in incident.io's public OpenAPI spec, we use the same ones:

| Resource | Endpoint | Why |
|---|---|---|
| `Schedule`, `ScheduleRotation` | `/v3/schedules`, `/v3/schedules/{id}/rotations` | The documented v2 API stores every rotation inline with its version history. A stateless full-replace `PUT` from Bicep could rewrite that history. v3 makes each rotation separately addressable and adds `rollout`, `concurrent_shifts` and `rank`. |
| `AlertSource` | `/v3/alert_sources` | Terraform's alert source shape; attributes are managed separately |

The Terraform provider's schema file (`public-schema-v3-including-secret-endpoints.json`, MIT licensed) is the reference for these payloads. The risk is tracked in `vulnerabilities.md`.

### Engine bindings and shorthands

Alert sources, maintenance windows and escalation paths share incident.io's engine building blocks: parameter bindings, conditions and condition groups. They're modelled once in `Engine.cs`. Bicep accepts Terraform's shorthands (`valueLiteral`, `valueReference`, `values`, `expressionRef`). `EngineBindingApiConverter` expands them into the API shape. It's registered **only** on the API serializer, so the SDK's gRPC serialization of the same classes is unaffected.

### Escalation paths: sequences ⇄ tree

The API stores an escalation path as a recursive tree, where an `if_else` node holds its `then_path` and `else_path` inline. Bicep uses Terraform v7's flat map of named `sequences`, in which branches reference other sequences by key. That removes the nesting limit and keeps every sequence at the same depth. `EscalationPathSequences`:

- **Unflattens** sequences into the tree, deriving stable node IDs (`<sequence>/<index>`) for nodes without an `id`.
- **Validates** with Terraform's rules and wording, and reports every problem at once.
- **Flattens** the tree back for `existing`, recovering sequence names from derived node IDs.

`branch.if` maps to engine conditions exactly as Terraform does: `escalation.working_hours["<id>"] is_active` and `escalation.priority one_of`.

### Skipping no-op writes

Most `PUT`s are harmless to repeat. A rotation `PUT` with `rollout` is not: it schedules a new version of the rotation. Handlers can opt into `SkipUnchangedUpdates`. The handler compares the payload with the live item (`JsonCompare.IsSubset`: objects as subsets, numbers numerically, timestamps by instant) and skips the write when nothing differs. `ScheduleRotation` also compares working intervals as a set.

### Not implemented (deliberately)

- **Dashboard locking (`unlock_in_dashboard`).** Terraform claims resources via `/v2/managed_resources` with an `incident.io/terraform/version` annotation, which labels them as managed by Terraform. We haven't found a documented way to claim a resource as managed by something else. Bicep-managed resources therefore stay editable in the dashboard, and the next deploy reverts manual edits.

- **Delete.** The SDK supports it, but `bicep local-deploy` doesn't remove resources that were taken out of a template. Deleting incident.io config (such as custom fields) can also destroy historical incident data.
- **What-if.** `Preview()` returns the desired state only, because the CLI exposes no what-if for local-deploy.
