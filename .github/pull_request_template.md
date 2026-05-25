# Pull Request

## Summary

<!-- 1-3 sentences. What changed and why. Link to issue / ADR if relevant. -->

## Type of change

- [ ] Feature
- [ ] Bug fix
- [ ] Refactor / cleanup (no behavior change)
- [ ] Performance
- [ ] Documentation
- [ ] Infrastructure / CI
- [ ] Test only

## Contracts impact

- [ ] No public-contract change
- [ ] OpenAPI / `contracts/openapi.yaml` modified -> **`contracts-change` label MUST be applied**
- [ ] `VrBook.Contracts` (DTOs / message envelopes) modified -> **`contracts-change` label MUST be applied**
- [ ] Breaking change to a published contract -> describe migration path below

<!--
  If `contracts-change` applies, the OpenAPI diff job in ci.yml will produce a
  changelog comment. Breaking-rule violations FAIL the build.
-->

## Database migrations

- [ ] No migration
- [ ] New EF Core migration committed under `src/VrBook.Migrator/Migrations`
- [ ] Migration is additive and backward-compatible with the currently-deployed app (expand-then-contract per Proposal §16.3)
- [ ] Backfill / contract step scheduled for a follow-up PR (if needed)

## Architectural impact

- [ ] No architectural change
- [ ] **ADR added** under `docs/adr/NNNN-title.md` (required if introducing a new external dependency, new bounded context, or changing a cross-cutting concern)

## CHANGELOG

- [ ] N/A — internal / refactor / docs
- [ ] `CHANGELOG.md` bumped under the **Unreleased** section
- [ ] Version bump planned in this PR (note new version: `vX.Y.Z`)

## Test plan

<!-- Bullet list of what you tested. Be specific. -->

- [ ] Unit tests added / updated
- [ ] Integration tests cover the new path
- [ ] Manual smoke (describe):

## Security & privacy

- [ ] No new secrets committed; new secrets are referenced via Key Vault
- [ ] No new PII processed (or DPIA updated if it is)
- [ ] AuthZ / role checks reviewed for new endpoints

## Reviewer checklist

- [ ] Code owners notified for `contracts/`, `infra/`, `.github/`, or `VrBook.Contracts/` paths
- [ ] CI green (build, tests, OpenAPI diff, Spectral, Trivy HIGH+)
- [ ] Deployment risk assessed; rollback path is straightforward
