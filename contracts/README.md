# /contracts — single source of truth for the API surface

Two artifacts live here:

- [`openapi.yaml`](./openapi.yaml) — the authoritative OpenAPI 3 spec.
- [`CHANGELOG.md`](./CHANGELOG.md) — every change to the spec is a versioned, dated entry.

**Coordination rule** (proposal §20.3): any PR that modifies `openapi.yaml` OR
`src/VrBook.Contracts/**` MUST be labelled `contracts-change` and MUST bump `CHANGELOG.md`.
CI fails otherwise.

## Lint

```powershell
npx @stoplight/spectral-cli lint openapi.yaml
```

## Diff against main (used in CI)

```powershell
oasdiff breaking origin/main:contracts/openapi.yaml ./openapi.yaml --fail-on ERR
oasdiff changelog origin/main:contracts/openapi.yaml ./openapi.yaml --format markdown
```

## Frontend code generation

```powershell
cd ../web
npm run gen:api    # writes to src/lib/api/generated/ — gitignored
```
