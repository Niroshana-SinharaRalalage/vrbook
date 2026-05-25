# Updating CODEOWNERS

`@architects` in `.github/CODEOWNERS` is a **placeholder**. CODEOWNERS only
takes effect if the principal on the right-hand side is:

1. A real GitHub **user** (`@octocat`), OR
2. A GitHub **team** that:
   - Exists inside the repository's organization, AND
   - Has been granted at least **write** access to the repository.

Until one of those is true, the entry is silently ignored — PRs touching
`/src/VrBook.Contracts/`, `/contracts/`, `/infra/`, or `/.github/` will not
require the intended reviewer.

## When the architecture team is formed

1. Create the team in the GitHub org, e.g. `vrbook-architects`.
2. Grant it **write** access to this repository.
3. Replace `@architects` everywhere in `.github/CODEOWNERS` with the new
   slug, e.g. `@your-org/vrbook-architects`.
4. In repo **Settings -> Branches -> Branch protection rule** for `main`
   (and ideally `develop`), enable **Require review from Code Owners**.

## Adding more granular ownership later

Examples for when the team grows:

```
/src/VrBook.Modules.Payment/   @your-org/payments-squad
/src/VrBook.Modules.Sync/      @your-org/integrations-squad
/web/                          @your-org/frontend-squad
```

CODEOWNERS uses gitignore-style globs (last match wins), so put more
specific paths **below** broader ones.
