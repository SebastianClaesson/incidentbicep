# incident.io Bicep Extension — Claude Instructions

## Purpose

A Bicep extension that wraps the incident.io REST API, so incident.io configuration (severities, roles, statuses, custom fields, catalog, alert attributes) can be declared in Bicep and deployed with `bicep local-deploy`.

## Background

- The author has PowerShell and Bicep expertise and no C# background, so explain C# decisions clearly.
- Sibling project and structural template: https://github.com/NetboxTools/netbox-bicep-extension
- No incident.io test account is assumed. Use `tools/mock-server/mock_incidentio.py` locally, and a non-production incident.io workspace when one is available.

## incident.io references

- API docs: https://api-docs.incident.io/
- OpenAPI spec: https://api.incident.io/v1/openapiV3.json (the source of truth for payload shapes; check create *and* update payloads)
- Terraform provider (useful prior art): https://github.com/incident-io/terraform-provider-incident

## Bicep extension references

- Extensibility SDK: https://github.com/Azure/bicep/tree/main/src/Bicep.Local.Extension
- Quickstart: https://github.com/Azure/bicep/blob/main/docs/experimental/local-deploy-dotnet-quickstart.md
- Sample extensions: https://github.com/anthony-c-martin/bicep-ext-github, https://github.com/anthony-c-martin/bicep-ext-keyvault, https://github.com/maikvandergaag/bicep-ext-http

---

## Code rules

### General

- Double-check every code decision and explain the reasoning.
- No repetitive code: use `IncidentIoResourceHandlerBase` / `NamedResourceHandler`.
- SDK 0.41.2 supports `int?`, `bool?`, arrays, `Dictionary<string, T>`, nested classes and enums. Use real types; don't stringify numbers.
- Never add `[JsonPropertyName]` to models. The SDK (camelCase) and the API (snake_case) share them.
- Enum member names are the Bicep literals and must equal the API values. Add `[JsonConverter(typeof(JsonStringEnumConverter))]` on the property.
- Output-only properties must have `ObjectTypePropertyFlags.ReadOnly`, so they are never sent to the API.
- Fields missing from the API's *update* payload go in `CreateOnlyFields`.

### Security and compliance

This solution may be deployed in sectors with high regulatory requirements:

- **Never commit API keys.** `.env`, `*.token`, `apikey.txt` and `secrets.json` are git-ignored.
- HTTPS only (loopback HTTP for tests). Keep redirects disabled.
- Track risks and every dependency change in `vulnerabilities.md`.
- **Minimize dependencies.** Use `HttpClient` and `System.Text.Json`. The only production dependency is `Azure.Bicep.Local.Extension`.
- Packages may come from an internal mirror, so don't assume nuget.org access.
- Never retry non-idempotent requests (POST) after network failures.

### Documentation

- Update `README.md` (resource table), `wiki/<Resource>.md`, and `ARCHITECTURE.md` for design decisions.
- Keep the samples working.

### Testing: mandatory before committing

1. `dotnet test bicep-ext-incidentio.sln`
2. `./scripts/publish.ps1 -Runtimes win-x64` (it also clears `~/.bicep/local`)
3. Start the mock: `python tools/mock-server/mock_incidentio.py 8787`
4. With `$env:INCIDENT_IO_BASE_URL='http://127.0.0.1:8787'` and `$env:INCIDENT_IO_API_KEY='test-key'`, deploy each sample **twice**: `bicep local-deploy ./samples/<name>/main.bicepparam`. The second run must update rather than create (check `GET /_state`).
5. When a test workspace is available, repeat step 4 against it.

**Don't commit if any step fails.** Use `http://127.0.0.1`, not `localhost`. On Windows, `localhost` resolves to IPv6 first and adds a ~2 s delay per call.

## Adding new resource types

1. Add the model in `src/Models.cs`: an identifiers class plus a `[ResourceType]` class.
2. Add a handler in `src/Handlers/`: `NamedResourceHandler<T>` for name-keyed resources, otherwise `IncidentIoResourceHandlerBase<T, TId>`.
3. Register it in `src/Program.cs`, add it to `TypeDefinitionTests`, the mock server, a sample, the README table and the wiki.
