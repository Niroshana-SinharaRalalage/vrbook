# Pact contract drift — triage runbook

Slice OPS.1.7. Referenced by the failure message in `cd-staging-web.yml`'s `Pact consumer + drift gate` step and by `cd-staging-api.yml`'s `Pact provider verification` step (blocking from OPS.1.7 onward).

## Two failure modes

### 1. Consumer drift (`cd-staging-web` step fails)

**Symptom**:

```
::error::Pact drift — the SPA consumer expectations changed but
contracts/pacts/vrbook-web-vrbook-api.json was not committed.
```

**Root cause**: A dev edited `web/tests/pacts/consumer.pact.test.ts` (added / changed / removed an interaction) but didn't commit the regenerated pact file.

**Fix**:

```bash
cd web
npm run test:pact
cd ..
git add contracts/pacts/vrbook-web-vrbook-api.json
git commit --amend --no-edit
git push --force-with-lease
```

If the failure is on a PR branch, `git commit --amend` + `git push --force-with-lease` overwrites the PR head. For merge-commits, add a fresh commit with just the pact file update.

**Never edit `contracts/pacts/vrbook-web-vrbook-api.json` by hand.** It's a generated artefact. The consumer test is the source of truth. Hand-edits will get overwritten on the next `npm run test:pact` run + will silently drift from the SPA's actual runtime shape.

### 2. Provider drift (`cd-staging-api` step fails, blocking mode)

**Symptom**:

```
[XX] Pact verification failed. 1 of 12 interactions failed:
  Interaction "a guest lists the first page of public properties" (verification)
    Response body: expected key "items" but got "results"
```

**Root cause**: An API-side change reshaped a response (renamed a field, changed a type, dropped a key) without a matching update to the consumer test.

**Fix — usually consumer-driven per plan §5-Q4**:

1. Read the failure to identify WHICH interaction failed and WHAT changed.
2. If the API change is intentional and correct (the SPA also needs to move), update the consumer test in the same PR:
   ```bash
   # edit web/tests/pacts/consumer.pact.test.ts to expect the new shape
   cd web
   npm run test:pact
   cd ..
   # edit web/src/... to consume the new shape
   git add contracts/pacts/ web/tests/pacts/ web/src/
   git commit -m "..."
   ```
3. If the API change was accidental (a typo, a refactor that renamed a field that shouldn't have moved), REVERT the API change. The pact is the SPA's contract.
4. Push again. Provider verification should pass.

**Fix — provider-driven only for genuine additions**: if the API added a NEW field (backward-compatible), the pact matcher's `like(...)` accepts extras. No action needed. If the drift-gate still fires, the additions probably touched the SPA's read path.

## Common gotchas

- **Non-determinism**: if `npm run test:pact` produces different bytes on two consecutive runs (rare but catastrophic), file an issue against the Pact team + freeze CI until we resolve. The plan pins deterministic-output as an invariant (`web/tests/pacts/deterministic.pact.test.ts` catches this locally).
- **Line-ending mangling**: `.gitattributes` marks `*.json` as `text eol=lf`. Windows devs shouldn't see CRLF drift in the pact file. If you do, run `git config core.autocrlf false` in your local clone.
- **`Worker exited unexpectedly` on `npm run test:pact`**: known PactV3 mock-server issue when a single provider const is reused across multiple `executeTest` calls (root cause: Pact's Rust core cleanup between runs). OPS.1.5 will resolve by giving each interaction its own PactV3 instance OR migrating to the newer PactV4 API. Until fixed, add new interactions ONE at a time + verify locally before pushing.

## Escalation

If you can't resolve within 30 minutes:

- Ping the SPA + backend leads together (any code change that reshapes the pact should have been paired).
- Attach the failing CI job link + the local `npm run test:pact` output.
- If the failure is a genuinely BREAKING change (a required field disappeared from a Response), the FE PR must land first, then the API PR can land.

## References

- Plan: [`docs/OPS_1_PACT_PLAN.md`](../OPS_1_PACT_PLAN.md) §5-Q4 (drift merge policy).
- ADR-0018: [`../adr/0018-pact-scope-and-flow-6-carve-out.md`](../adr/0018-pact-scope-and-flow-6-carve-out.md) — what's IN scope for pact.
- Pact file: [`../../contracts/pacts/README.md`](../../contracts/pacts/README.md).
- Consumer test: `web/tests/pacts/consumer.pact.test.ts`.
- Provider verifier: `tests/VrBook.Api.PactTests/`.
