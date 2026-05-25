# 5. Azure SignalR Service in Serverless Mode

- Status: Accepted
- Date: 2026-01-15
- Deciders: Solutions Architecture
- Tags: real-time, signalr, container-apps, messaging

## Context and Problem Statement

The §10 messaging system needs real-time delivery of `MessageReceived`, `MessagesRead`, `BookingStatusChanged`, and `ConflictDetected` events to connected web clients (guests and owners). The §15.1 SKU table provisions "SignalR Service Standard, 1 unit, Serverless mode" and §10.1 is explicit: "Recommendation: SignalR Service in Serverless mode. Container Apps scale to zero; we don't want a persistent hub holding connections. In Serverless mode, the SignalR Service holds connections and our API calls it to broadcast — we never need to maintain client connections on our app instances."

The platform's API hosting is Azure Container Apps with KEDA autoscaling and the Phase 1 dev environment configured to scale to zero (§15.2). A self-hosted SignalR hub on Container Apps would fight that model in two ways: (a) the hub holds long-lived WebSocket connections, which prevent KEDA from scaling the API replicas to zero; (b) scaling out the API to multiple replicas would require a backplane (Redis or Service Bus) to keep groups in sync across replicas, which is operational complexity we want to avoid in Phase 1.

The §3.2 container diagram shows the negotiate-once pattern: the browser calls the API's `/realtime/negotiate` endpoint, gets a SignalR Service URL and access token, then connects directly to the SignalR Service via WebSocket. The API itself never sees the WebSocket. To broadcast, the API calls the SignalR Service REST API (`POST /api/v1/hubs/{hub}/groups/{group}/messages`).

## Decision Drivers

- Container Apps scale-to-zero compatibility (§15.2 dev `min replicas = 0`).
- No backplane: we do not want to operate a Redis or Service Bus backplane just for SignalR group state.
- Multi-replica scale-out without sticky sessions: a guest's WebSocket can land on any SignalR Service node, and an API replica can broadcast without knowing which node holds the connection.
- The API is stateless and request/response — adding WebSocket lifecycle management is a class of complexity we don't have a budget for in Phase 1.
- Cost: at Phase 1 traffic, one Standard unit of SignalR Service is well inside the §15.2 prod budget of ~$1,200/month light.
- §21 R12 risk: "SignalR Service free-tier cost surprise on launch" — using Standard tier with explicit unit cap from W7.

## Considered Options

- **Azure SignalR Service in Serverless mode** — Managed service holds connections; API calls REST API to broadcast; no hub class on our side, just an `IHubContext` or direct REST client.
- **Azure SignalR Service in Default (Classic) mode** — Managed service holds connections; the *server* component of the SignalR connection still runs in our API and the service is a load-balancing proxy.
- **Self-hosted SignalR Hub on Container Apps + Redis Backplane** — Our API hosts the hub; Redis publishes group messages across replicas.
- **Self-hosted SignalR Hub on Container Apps + Azure Service Bus Backplane** — Same, but backplane is Service Bus.
- **Server-Sent Events (SSE) only** — Drop bidirectional real-time and use SSE one-way push.
- **No real-time, poll only** — Frontend polls `/threads/{id}/messages?cursor=` every N seconds.

## Decision Outcome

Chosen option: **"Azure SignalR Service in Serverless mode"**, because it is the only option that simultaneously (a) lets Container Apps scale API replicas to zero between requests, (b) needs no backplane, (c) needs no sticky sessions, (d) gives us bidirectional real-time without our process owning a connection lifecycle, and (e) is supported by the Microsoft-supplied `Microsoft.Azure.SignalR.Management` SDK (§23.2 pins 1.25.x).

### Positive Consequences

- API replicas do not hold WebSocket connections — KEDA can scale to zero on the API and the messaging real-time path stays alive.
- Broadcast is a REST call to the SignalR Service from any API replica or background worker — fan-out is the service's problem.
- Group membership (e.g., `thread:{threadId}`) is managed via REST calls; no in-process state to keep consistent across replicas.
- The negotiate endpoint (§10.2 step 1: "Client calls `GET /realtime/negotiate` → returns SignalR connection URL + access token (JWT-scoped to `userId`)") is a pure request/response API endpoint — no hub plumbing.
- The Notification Worker (§3.2, §10.2 step 4) can broadcast in exactly the same way as the API — no special hub access pattern for workers.
- Free tier exists for development (`Free_F1`), Standard tier scales linearly with units — §21 R12 ("free-tier cost surprise") is handled by explicit unit cap configured in Bicep.

### Negative Consequences / Trade-offs

- We pay for the SignalR Service unit even when idle — the trade for not needing a backplane.
- Some advanced SignalR features (server-to-client streaming, complex hub method binding) are constrained in Serverless mode. Phase 1 needs only `broadcast to group` and `broadcast to user` — both fully supported.
- The client connects to a Microsoft endpoint, not to `*.vrbook.example.com` — minor brand leak (mitigated, in practice users don't see hub URLs).
- The REST broadcast path is asynchronous from the API's perspective — the API gets `202 Accepted` and trusts the service to deliver. Mitigation: persistence to PostgreSQL is the source of truth (§10.3); SignalR is the delivery channel, and missed messages are reconciled via the cursor-based `GET /threads/{id}/messages?cursor=` on reconnect.

## Pros and Cons of the Options

### Azure SignalR Service in Serverless mode

- Good, because Container Apps scale-to-zero is fully compatible — the API is stateless.
- Good, because no backplane operations.
- Good, because the §3.2 architecture works as drawn — API calls service via REST, browser connects to service via WebSocket.
- Good, because the Notification Worker can broadcast just like the API — symmetric access pattern.
- Bad, because we pay for the service unit at idle.
- Bad, because feature surface is intentionally smaller than full SignalR — not a problem at Phase 1 message-broadcast scope.

### Azure SignalR Service in Default mode

- Good, because uses standard ASP.NET Core SignalR programming model — fewer concepts to learn.
- Good, because gives full SignalR feature surface.
- Bad, because the hub's *server* component still runs in our API — API holds connection lifecycle state, which blocks scale-to-zero on the API.
- Bad, because the model is designed for a fixed pool of always-on hub servers — fights Container Apps scale rules.

### Self-hosted SignalR Hub + Redis Backplane

- Good, because no managed-service dependency for the real-time path.
- Good, because Redis is already provisioned (§15.1) so the backplane is "free" infrastructure.
- Bad, because the hub holds connections in API replicas — blocks scale-to-zero.
- Bad, because backplane operations are not free — Redis pub/sub topology, message routing, and replica coordination become our problem.
- Bad, because scaling-out requires careful connection-affinity tuning (Front Door sticky cookies or sticky-session pattern), adding network-config complexity.

### Self-hosted SignalR Hub + Service Bus Backplane

- Good, because Service Bus is already provisioned.
- Bad, because Service Bus latency (typically tens of milliseconds plus delivery semantics designed for durability, not real-time) is the wrong tool for chat-style broadcast.
- Bad, because every broadcast is metered Service Bus traffic.
- Bad, because still blocks scale-to-zero on the API.

### Server-Sent Events (SSE) only

- Good, because no WebSocket layer at all — one HTTP response per client, write events as they arrive.
- Bad, because SSE is one-way — the client cannot send messages over the same channel; we'd still need POST endpoints for sends. Not a deal-breaker, but adds a second model.
- Bad, because long-lived SSE connections still block scale-to-zero on the API; we would need a managed proxy, which essentially is SignalR Service.
- Bad, because SSE has weaker reconnection semantics in browsers than WebSocket+SignalR; we would re-implement what SignalR already does.

### Poll only

- Good, because trivially compatible with scale-to-zero and stateless API.
- Bad, because §10 expects "real-time messaging" — polling at the cadence needed for a chat-like experience hammers the API and the database for no reason.
- Bad, because read receipts and presence-style features (last-seen-at, online/offline detection per §10.2 step 4) need a push channel to be cost-effective.

## Implementation Notes

- The negotiate endpoint (`/realtime/negotiate`) is an authenticated API endpoint protected by the platform's standard B2C JWT auth.
- It calls `ServiceManager.CreateHubContextAsync("messages")` then `GenerateClientAccessTokenAsync(userId, [...claims], ttl: 1h)` and returns `{ url, accessToken }`.
- Group membership is added by the API on join: `await hubContext.UserGroups.AddToGroupAsync(userId, $"thread:{threadId}")`. The API enforces that the user is a participant in the thread before adding.
- Broadcast on message send: `await hubContext.Clients.Group($"thread:{threadId}").SendAsync("MessageReceived", payload)`.
- A separate hub `notifications` carries `BookingStatusChanged` and `ConflictDetected` events — one connection per user via group `user:{userId}`.
- Connection-string secret stored in Key Vault as `signalr-cs`, referenced from the Container Apps env as `SignalR__ConnectionString` (§23.3).
- Standard tier, 1 unit, hard-capped in Bicep parameters — §21 R12 mitigation.

## Persistence is Postgres, Not SignalR

Per §10.3: "All messages are persisted in PostgreSQL. SignalR is a delivery channel, not a store. Offline clients fetch via `GET /threads/{id}/messages?cursor=` on reconnect." This separation gives us three useful properties:

1. **Replayability.** A client that missed a broadcast (network blip, page reload, browser tab in background) re-syncs via the REST cursor — no broker-managed undelivered-message queue.
2. **Auditability.** Every message is a row in `messaging.messages` with an immutable created_at, available to Admin moderation (§10.5) and audit logs (§14.5).
3. **No backplane state.** SignalR Service holds no message history. If the SignalR Service unit is recycled, no message is lost — the truth is in Postgres.

The broadcast call is fire-and-forget from the API's perspective. The §17.2 metric `signalr.message.delivered` counts SignalR REST 202 responses, not actual client receipts (which are unobservable from the server side). End-to-end delivery confidence comes from the cursor-based read on the next client poll/reconnect.

## Trade-Off vs. Self-Hosted: The Specific Number

A self-hosted SignalR hub on a single Container App replica with 100 concurrent WebSocket connections holds those connections open for the duration of each session — typically minutes. KEDA cannot scale this replica down even when no HTTP traffic is arriving, because the WebSocket connections register as active traffic. The min-replicas=0 behaviour configured in §15.2 dev — and even the min-replicas=1 in prod with scale-to-N — becomes effectively a fixed-floor pool sized to peak WebSocket count, not request rate.

Serverless mode breaks this coupling. The API's HTTP traffic shape (bursty, request/response) drives API scaling. The SignalR Service's connection count (steady, long-lived) drives SignalR Service unit count. The two scale independently.

## Capacity and Cost Sizing

Per the SignalR Service pricing model (one Standard unit handles ~1,000 concurrent connections and ~1,000,000 messages/day), one unit covers Phase 1 by a comfortable margin:

- Peak concurrent connections: ≤ 200 (estimated, one owner with ≤ 10 properties; concurrent guest sessions during high-season checkout flow).
- Daily message volume: ≤ 50,000 (messages + status broadcasts + read-receipts).

§21 R12 flags the risk of an unbounded SignalR Service unit count appearing on the bill. The mitigation is a hard cap in Bicep: `properties.unitCount: 1` with the autoscale block deliberately not configured. Crossing the cap requires a PR to `/infra` — visible, reviewed, intentional.

## Links

- [Proposal §10 Messaging System Design](../../BookingApp_Proposal.md)
- [Proposal §10.1 Topology](../../BookingApp_Proposal.md)
- [Proposal §15.1 Per-Resource Details](../../BookingApp_Proposal.md)
- [Proposal §21 R12 SignalR cost risk](../../BookingApp_Proposal.md)
- [Proposal §23.2 Library/Framework versions (Microsoft.Azure.SignalR.Management 1.25.x)](../../BookingApp_Proposal.md)
- [Azure SignalR Service Serverless docs](https://learn.microsoft.com/azure/azure-signalr/concept-service-mode)
