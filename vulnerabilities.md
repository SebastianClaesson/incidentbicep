# Known Vulnerabilities and Security Considerations

## Active Concerns

### 1. API key handling (Medium risk)
**What:** The incident.io API key is passed as extension configuration. It's typed as a sensitive string (`isSecure: true`), so Bicep treats it like a `@secure()` value. It's still read from a parameter or environment variable and held in memory by the extension process.

**Mitigation:**
- HTTPS is enforced for the API base URL. Plain HTTP is accepted only for loopback addresses (mock or testing).
- Redirects are disabled, so the `Authorization` header can't be forwarded to another host.
- The key is never included in error messages (covered by a unit test).
- `.gitignore` excludes `.env`, `*.token`, `apikey.txt` and `secrets.json`.

**What you should do:** Use a dedicated API key with only the permissions the deployment needs. Store it in a secret store (Key Vault, pipeline secret) and read it via `readEnvironmentVariable()` in `.bicepparam`. Never commit it. Rotate it if it's exposed.

### 2. Broad API key permissions (Medium risk, informational)
**What:** Managing settings needs a key that can change organisation-wide incident configuration. Anyone holding the key can change severities, statuses and the catalog.

**What you should do:** Scope keys to the minimum permissions and restrict who can run deployments (for example, protected pipeline environments).

### 3. Destructive schema replacement (Medium risk, operational)
**What:** Setting `attributes` on a `CatalogType` replaces the whole schema. Attributes you don't list are removed, together with their values on every entry. Similarly, `attributeValues` on a `CatalogEntry` clears attributes you don't list.

**Mitigation:** Existing attributes are matched by name, so their IDs and values are kept. Leaving out `attributes` or `attributeValues` leaves them unmanaged. Both behaviors are documented in the README and the wiki.

### 4. Error bodies in deployment output (Low risk)
**What:** API error responses (truncated to 2000 characters) are included in deployment errors to aid troubleshooting. incident.io error bodies describe validation failures and don't echo credentials. They can still contain configuration values you sent, such as names and descriptions.

### 5. Experimental Bicep features (Low risk, informational)
**What:** `localDeploy` and the extensibility SDK are experimental. The protocol and SDK may change. Pin the SDK version and re-test after Bicep upgrades.

### 6. Mock server (None in production)
**What:** `tools/mock-server` binds to `127.0.0.1` only, accepts a hard-coded test key, and has no persistence. It's for local testing only. Never expose it.

## Resolved / Mitigated

| Concern (from netbox-bicep-extension) | Resolution here |
|---|---|
| Token sent over plain HTTP if the URL isn't HTTPS | HTTPS enforced; HTTP allowed for loopback only |
| SDK lacked nullable value types, so numbers and booleans were sent as strings | SDK 0.41.2 supports `int?` and `bool?`, so values are sent with correct JSON types |

## Third-Party Dependencies

All NuGet packages used by this project. Environments with regulatory requirements may need to mirror them through an internal proxy (edit `nuget.config`).

| Package | Version | Publisher | Purpose | Risk |
|---|---|---|---|---|
| `Azure.Bicep.Local.Extension` | 0.41.2 | Microsoft (Azure) | Bicep extensibility SDK: extension host, type system, handler framework. Pulls in gRPC and ASP.NET Core (Microsoft). | Low. Official Microsoft package; experimental, so breaking changes are possible |
| `xunit` | 2.9.2 | .NET Foundation | Unit tests (test project only, not shipped) | None. Dev dependency only |
| `xunit.runner.visualstudio` | 2.8.2 | .NET Foundation | Test runner (test project only) | None. Dev dependency only |
| `Microsoft.NET.Test.Sdk` | 17.12.0 | Microsoft | Test infrastructure (test project only) | None. Dev dependency only |

**Changes from the NetBox extension:**
- Not using `Bicep.LocalDeploy`. It's a third-party community package (author: Gijsreyn) for documentation-generation attributes, not a Microsoft package, and it isn't required for `targetScope = 'local'` or `bicep local-deploy`. The NetBox extension references it without using it, and its `vulnerabilities.md` wrongly lists it as Microsoft (Azure).
- `Azure.Bicep.Local.Extension` 0.37.4 → 0.41.2, which requires .NET 10 (LTS). Risk: low. Same publisher; it fixes type-generation bugs (see ARCHITECTURE.md).

**Runtime:** binaries are published self-contained, so they bundle the .NET 10 runtime. Rebuild and republish when .NET security patches ship.

### Dependency update policy

- Pin exact versions in `.csproj` files.
- Document every version change here with a risk assessment.
- Run unit tests and the samples (against the mock and a test workspace) after every update.

## Notes

- This solution may be used in regulated environments. Document every security concern and dependency change in this file.
