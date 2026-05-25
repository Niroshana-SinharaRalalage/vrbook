# 6. Next.js 14 (App Router) for the Web Layer

- Status: Accepted
- Date: 2026-01-15
- Deciders: Solutions Architecture
- Tags: frontend, ssr, seo, nextjs

## Context and Problem Statement

The Phase 1 product is a direct-booking site. The §2.1 business case is unambiguous about *why* it exists: "AirBnB remains the customer-acquisition channel; this platform is the retention and margin-recovery channel." The primary acquisition path for direct bookings is *not* paid search — it is repeat guests, referrals, QR codes in properties, and post-stay flyers. But there is a secondary acquisition channel that *is* paid search and organic search: someone who has heard of the property by name, or someone looking for a specific city or amenity, and the §13 SEO-friendly listing pages are how that conversion completes.

The §1 Executive Summary is explicit about the frontend choice and the *reason* for it: "The frontend is **Next.js 14 (App Router) with TypeScript**, chosen over plain React+Vite because public listing pages depend on organic search traffic for the direct-booking funnel — server-rendered metadata and crawlable pages are revenue-critical, not optional. The admin dashboard runs as a separate route segment with client-side rendering."

The §23.2 versions table pins Next.js 14.x, React 18.x, TypeScript 5.x, TanStack Query 5.x, Tailwind 3.4.x, and shadcn/ui. The web app is deployed (§16.4) as "a Container App as well (Node 20 runtime image). Static assets are served via Front Door with caching. ISR/revalidation hits the API directly."

## Decision Drivers

- **SEO is revenue-critical**, not optional (§1). Listing pages must be server-rendered with correct `<title>`, `<meta description>`, OpenGraph, JSON-LD, and crawlable links.
- **ISR (Incremental Static Regeneration)** for listing pages — property data changes slowly; the public listing page should be cacheable for minutes, regenerated on `PropertyUpdated`/`ReviewApproved` events, and served from edge cache otherwise.
- **One TypeScript codebase** spanning marketing pages, public listings, booking funnel, guest area, and owner/admin dashboard — sharing the OpenAPI-generated client (ADR-0002) and the design system.
- **React Server Components** for data-heavy listing pages — let the server fetch from the API without round-tripping data to the client just to render it.
- **Phase 2 React Native** (§22.1) shares TypeScript types and the API client via a workspace package — fits naturally next to a Next.js app in a monorepo.
- **Container Apps deploy target** (§16.4) — Next.js runs as a Node 20 server, not as a static export. Front Door fronts it for TLS, WAF, and edge caching.
- **Authenticated admin dashboard** does not need SEO — client-side rendering is fine for `/owner/*` and `/admin/*` route segments.

## Considered Options

- **Next.js 14 (App Router)** — React framework with file-based routing, RSC, ISR, image optimization, and SSR built in.
- **Next.js 13 (Pages Router)** — Older but more stable Next.js model; getStaticProps / getServerSideProps.
- **Remix (React Router 7)** — Server-first React framework, loader/action model, no RSC.
- **Plain React + Vite (SPA)** — Single-page app with client-side rendering only.
- **Astro** — Island architecture, static-first with hydration islands.
- **SvelteKit** — Strong SSR story, smaller bundle, smaller .NET-shop talent pool.

## Decision Outcome

Chosen option: **"Next.js 14 (App Router)"**, because it is the only mature React framework that delivers (a) server-rendered listing pages with first-class SEO controls, (b) ISR for the slow-changing property data that dominates page weight, (c) React Server Components for data-fetching without an over-the-wire JSON hop, (d) one codebase covering both the SSR public pages and the CSR admin dashboard via route-segment configuration, and (e) a deployment model that fits Container Apps + Front Door.

### Positive Consequences

- Listing pages (`/listings/[slug]`) are rendered server-side with full HTML and metadata. Search engines index them as static documents.
- ISR with `revalidate: 300` (5 min) at the route segment level keeps listing pages near-instant served from Front Door's edge cache, with background regeneration on a sliding window.
- On-demand revalidation: the API publishes `PropertyUpdated` or `ReviewApproved` and a small Next.js route handler calls `revalidatePath(/listings/${slug})` — content freshness without polling.
- React Server Components render the property detail page on the server with a direct fetch from the API. The client receives serialized React tree, not raw JSON to re-render — smaller payload, faster TTI.
- The admin route segment (`/owner/*`, `/admin/*`) is marked `dynamic = 'force-dynamic'` with `'use client'` boundaries — pure CSR with TanStack Query for data caching, no SEO concern.
- TypeScript end-to-end with the OpenAPI-generated client in `/contracts/clients/typescript` — same types in API DTOs and React components.
- Phase 2 React Native (§22.1) shares the same TypeScript API client as a workspace package — the natural shape for a monorepo (ADR-0002).
- Next.js Image (`next/image`) handles `srcset`, lazy-loading, and modern format negotiation for the property photo galleries — material UX win for listing pages.

### Negative Consequences / Trade-offs

- App Router is younger than the Pages Router — some patterns (caching, fetch deduplication) still evolve between minor Next.js versions. Pinning to 14.x in §23.2 locks the surface.
- RSC adds conceptual overhead — engineers must understand the server/client boundary. Trade-off accepted because the SEO and performance benefit on the public surface is decisive.
- The Next.js Node server must be operated as a Container App — slightly heavier deploy than a static-file frontend, but the SSR/ISR functions require it.
- Front Door cache invalidation is a separate concern from Next.js's `revalidatePath` — content propagates to the edge with a small lag. Acceptable for the property-listing use case.
- Lock-in to Vercel-stewarded Next.js conventions. Reversible at substantial cost (any React framework migration would be); mitigated by keeping the API as the single source of truth for all business logic — the frontend is a presentation layer over a well-defined OpenAPI surface.

## Pros and Cons of the Options

### Next.js 14 (App Router)

- Good, because SSR + ISR + RSC out of the box.
- Good, because route-segment configuration lets the same app serve SSR public pages and CSR admin pages without two builds.
- Good, because `next/image`, `next/font`, and metadata API are first-class — handles property photo galleries and SEO metadata as built-in primitives.
- Good, because the largest React-shop talent pool — recruiting is easy.
- Good, because `revalidatePath` and `revalidateTag` give us on-demand cache busting tied to domain events.
- Bad, because App Router is younger and patterns still evolve.
- Bad, because RSC adds a server/client boundary engineers must internalise.

### Next.js 13 (Pages Router)

- Good, because more battle-tested; copious community knowledge.
- Good, because `getStaticProps` + ISR is well understood.
- Bad, because no RSC — every data point goes JSON-over-the-wire even when it doesn't need to.
- Bad, because Vercel is steering investment into the App Router — Pages Router is feature-frozen.
- Bad, because metadata API is less ergonomic than App Router's.

### Remix

- Good, because clean loader/action model.
- Good, because progressive enhancement is a first-class concern.
- Bad, because no ISR analog — Remix's caching story relies on HTTP cache headers + a CDN. Workable, but more bespoke than `revalidate: N` on a route segment.
- Bad, because no RSC.
- Bad, because smaller ecosystem of integrations (image optimization, font loading) than Next.js.

### Plain React + Vite (SPA)

- Good, because simplest possible build.
- Good, because deploys as static files behind a CDN.
- Bad, because SEO is **not optional for our public surface** (§1) — and an SPA serves a blank `<div id="root">` to the crawler. Even with prerender services or Google's JS-rendering, this is a material handicap that we explicitly cannot accept per the proposal's framing.
- Bad, because no ISR concept — cache invalidation tied to deploys, not domain events.
- Bad, because would force a second framework for the public listing pages (e.g., Astro for marketing + React SPA for app) — exactly the two-codebase outcome we are avoiding.

### Astro

- Good, because excellent for static, content-heavy sites with islands of interactivity.
- Good, because smallest JS payload of the lot.
- Bad, because the booking funnel and the admin dashboard are *highly interactive* — large parts of the app are not Astro's sweet spot.
- Bad, because we would end up writing the interactive parts in React anyway — Astro becomes overhead, not value.
- Bad, because no first-class React Server Components story.

### SvelteKit

- Good, because excellent SSR, small bundles, great DX.
- Bad, because the recruitable React talent pool is much larger than the Svelte one — for a fast-moving team this matters.
- Bad, because shadcn/ui and the React component ecosystem we want to lean on (TanStack Query, Stripe Elements, Mapbox React, etc.) are React-shaped.
- Bad, because Phase 2 React Native (§22.1) cannot share components with a Svelte web app — kills the type-sharing win.

## Route Architecture

```
/web/app
  /(marketing)              # public, SSR + ISR, SEO-critical
    /page.tsx               # landing
    /about/page.tsx
    /how-it-works/page.tsx
  /(public)                 # public, SSR + ISR, SEO-critical
    /listings
      /page.tsx             # search results
      /[slug]/page.tsx      # property detail page — ISR revalidate: 300
  /(booking)                # dynamic SSR, no caching
    /book/[propertyId]/page.tsx
    /checkout/page.tsx
  /(guest)                  # authenticated, CSR — 'use client'
    /bookings/page.tsx
    /messages/page.tsx
    /profile/page.tsx
  /(owner)                  # authenticated, CSR — 'use client', dynamic = 'force-dynamic'
    /properties/page.tsx
    /bookings/page.tsx
    /messages/page.tsx
    /payouts/page.tsx
  /(admin)                  # admin role, CSR
    /properties/page.tsx
    /users/page.tsx
    /reports/page.tsx
  /api
    /revalidate/route.ts    # webhook target: POST { path } → revalidatePath(path)
```

- Marketing and public segments default to `revalidate: 300` and use the metadata API for `<title>`, `<meta>`, OpenGraph, and JSON-LD `LodgingBusiness` schema.
- Booking segment is `dynamic = 'force-dynamic'` because pricing quotes are time-sensitive and personalised.
- Authenticated segments use a layout-level auth check that redirects unauthenticated users to the B2C sign-in policy.

## Links

- [Proposal §1 Executive Summary (frontend choice rationale)](../../BookingApp_Proposal.md)
- [Proposal §2.1 Business Case](../../BookingApp_Proposal.md)
- [Proposal §16.4 Frontend Deploy](../../BookingApp_Proposal.md)
- [Proposal §22.1 Native Mobile (React Native + workspace types)](../../BookingApp_Proposal.md)
- [Proposal §23.2 Library/Framework versions (Next.js 14.x)](../../BookingApp_Proposal.md)
- [Next.js App Router docs](https://nextjs.org/docs/app)
- [React Server Components RFC](https://react.dev/reference/rsc/server-components)
