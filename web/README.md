# VrBook — Web (Next.js 14)

Frontend for the Direct-Booking Vacation Rental Platform. See the architectural
single-source-of-truth in [`../BookingApp_Proposal.md`](../BookingApp_Proposal.md).

## Prerequisites

- Node.js **>= 20**
- npm **>= 10**
- The .NET API running locally (or a deployed `NEXT_PUBLIC_API_BASE_URL`)

## Quick start

```bash
npm ci
cp .env.local.example .env.local
# fill the NEXT_PUBLIC_* values
npm run dev
```

The dev server runs on http://localhost:3000.

## Scripts

| Script               | What it does                                                            |
| -------------------- | ----------------------------------------------------------------------- |
| `npm run dev`        | Next.js dev server with HMR                                             |
| `npm run build`      | Production build (standalone output for Docker)                         |
| `npm run start`      | Start the production server                                             |
| `npm run lint`       | ESLint (`next/core-web-vitals` + `@typescript-eslint/recommended`)      |
| `npm run typecheck`  | `tsc --noEmit` strict                                                   |
| `npm run test`       | Vitest unit tests (jsdom + @testing-library/react)                      |
| `npm run test:e2e`   | Playwright E2E (smoke + e2e projects)                                   |
| `npm run gen:api`    | Generate typed API client from `../contracts/openapi.yaml`              |

## Environment variables

All client-visible vars are `NEXT_PUBLIC_*` and listed in `.env.local.example`.
The full env-var inventory across API + workers + web lives in proposal §23.3.

| Key                                  | Purpose                                                 |
| ------------------------------------ | ------------------------------------------------------- |
| `NEXT_PUBLIC_API_BASE_URL`           | Versioned API root, e.g. `https://api…/api/v1`         |
| `NEXT_PUBLIC_STRIPE_PUBLISHABLE_KEY` | Stripe.js publishable key                               |
| `NEXT_PUBLIC_B2C_AUTHORITY`          | AD B2C SignUpSignIn policy URL                          |
| `NEXT_PUBLIC_B2C_CLIENT_ID`          | App registration client id                              |
| `NEXT_PUBLIC_MAPBOX_TOKEN`           | Map tiles for property location previews                |
| `NEXT_PUBLIC_SIGNALR_NEGOTIATE_URL`  | SignalR Serverless negotiate endpoint (proposal §10)    |

## Project layout

```
src/
  app/        # App-Router routes (RSC by default, "use client" where needed)
  components/ # Reusable UI (shadcn/ui components land in components/ui)
  lib/        # API client, auth (MSAL), pure utils
  hooks/      # Reusable React hooks
  types/      # Ambient + env types
```

Routes are scaffolded for every URL referenced in proposal §12 (admin nav) and
the user-facing flows. Admin and account pages render a placeholder labeled with
the owning agent and proposal section; UI implementation is delegated to
agents F1 (frontend foundations) and O1 (owner/admin module).

## Generated API client

`npm run gen:api` writes to `src/lib/api/generated/` from
`../contracts/openapi.yaml` using `openapi-typescript-codegen`. The directory
is gitignored; only the `.gitkeep` is committed so the path is reserved.

## Testing

- **Unit** — Vitest + Testing Library. Files: `src/**/*.test.tsx`.
- **E2E** — Playwright Chromium. Two projects: `smoke` (`*.smoke.spec.ts`) and
  `e2e` (everything else under `tests/e2e/`).

## Docker

Multi-stage build: `node:20-alpine` builder → `distroless/nodejs20` runtime,
exposes port 3000, container-platform probe hits `/api/health`.

```bash
docker build -t vrbook-web:dev .
docker run -p 3000:3000 --env-file .env.local vrbook-web:dev
```
