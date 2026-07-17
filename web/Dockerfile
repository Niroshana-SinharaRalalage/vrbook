# syntax=docker/dockerfile:1.7

# ---------- Builder ----------
FROM node:20-alpine AS builder
WORKDIR /app

# Install OS deps required by some npm packages.
RUN apk add --no-cache libc6-compat

# Install deps with deterministic lockfile.
COPY package.json package-lock.json* ./
RUN npm ci --no-audit --no-fund

# Copy source.
COPY . .

# Build Next.js in standalone mode (next.config.mjs sets output: 'standalone').
# NEXT_PUBLIC_* vars are inlined into the BROWSER bundle at build time -
# the Container App runtime env never reaches hydrated client components.
# CI passes --build-arg NEXT_PUBLIC_API_BASE_URL=https://... per environment.
ARG NEXT_PUBLIC_API_BASE_URL=""
ARG NEXT_PUBLIC_ENTRA_AUTHORITY_ADMIN=""
ARG NEXT_PUBLIC_ENTRA_AUTHORITY_GUEST=""
ARG NEXT_PUBLIC_ENTRA_CLIENT_ID=""
# VRB-311 — App Insights (browser SDK). Per-env at build time so a staging
# property can never leak into a prod bundle. Empty default ⇒ analytics no-op,
# so the web ships safe-disabled until the value is wired.
ARG NEXT_PUBLIC_APPLICATIONINSIGHTS_CONNECTION_STRING=""
ENV NEXT_PUBLIC_API_BASE_URL=${NEXT_PUBLIC_API_BASE_URL}
ENV NEXT_PUBLIC_APPLICATIONINSIGHTS_CONNECTION_STRING=${NEXT_PUBLIC_APPLICATIONINSIGHTS_CONNECTION_STRING}
# Slice OPS.M.12.6/M.21.FIX — per-flow authorities inline into MSAL config.
# The legacy NEXT_PUBLIC_ENTRA_AUTHORITY was dropped in M.12.8; the M.12.6
# msalConfig reads _ADMIN + _GUEST directly. Missing this build-arg chain was
# the root cause of a 2026-07-07 login.live.com fallback bug — msalConfig
# saw both vars as undefined and fell through to microsoftonline.com/common,
# which routes to consumer identity for CIAM tenants.
ENV NEXT_PUBLIC_ENTRA_AUTHORITY_ADMIN=${NEXT_PUBLIC_ENTRA_AUTHORITY_ADMIN}
ENV NEXT_PUBLIC_ENTRA_AUTHORITY_GUEST=${NEXT_PUBLIC_ENTRA_AUTHORITY_GUEST}
ENV NEXT_PUBLIC_ENTRA_CLIENT_ID=${NEXT_PUBLIC_ENTRA_CLIENT_ID}
ENV NEXT_TELEMETRY_DISABLED=1
RUN npm run build

# ---------- Runtime ----------
# Distroless Node 20 — minimal attack surface, no shell.
FROM gcr.io/distroless/nodejs20-debian12 AS runtime
WORKDIR /app

ENV NODE_ENV=production
ENV PORT=3000
ENV HOSTNAME=0.0.0.0
ENV NEXT_TELEMETRY_DISABLED=1

# Copy the standalone server (Next.js trace-pruned node_modules) + static assets + public dir.
COPY --from=builder /app/.next/standalone ./
COPY --from=builder /app/.next/static ./.next/static
COPY --from=builder /app/public ./public

EXPOSE 3000

# Container Apps probes /api/health (see app/api/health/route.ts).
# Distroless has no shell or wget, so the platform probe (Kubernetes/Container Apps HTTP probe)
# is the real liveness check. The HEALTHCHECK directive below is documentation for non-K8s runners.
HEALTHCHECK --interval=30s --timeout=5s --start-period=20s --retries=3 \
  CMD ["/nodejs/bin/node", "-e", "fetch('http://127.0.0.1:3000/api/health').then(r=>process.exit(r.ok?0:1)).catch(()=>process.exit(1))"]

CMD ["server.js"]
