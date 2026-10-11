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

### Not implemented (deliberately)

- **Delete.** The SDK supports it, but `bicep local-deploy` doesn't remove resources that were taken out of a template. Deleting incident.io config (such as custom fields) can also destroy historical incident data.
- **What-if.** `Preview()` returns the desired state only, because the CLI exposes no what-if for local-deploy.
