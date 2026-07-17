// Typed access to NEXT_PUBLIC_* env vars (proposal §23.3).
// Keep in sync with .env.local.example.

declare namespace NodeJS {
  interface ProcessEnv {
    readonly NEXT_PUBLIC_API_BASE_URL: string;
    readonly NEXT_PUBLIC_STRIPE_PUBLISHABLE_KEY: string;
    readonly NEXT_PUBLIC_ENTRA_AUTHORITY: string;
    readonly NEXT_PUBLIC_ENTRA_CLIENT_ID: string;
    readonly NEXT_PUBLIC_MAPBOX_TOKEN: string;
    readonly NEXT_PUBLIC_SIGNALR_NEGOTIATE_URL: string;
    // Public base URL for canonicals/metadata (VRB-109); optional, falls back.
    readonly NEXT_PUBLIC_SITE_URL?: string;
    // VRB-311 — App Insights connection string for consent-gated web analytics.
    // Optional: when absent, analytics is silently disabled. DEVOPS wires the
    // per-env value via the web Dockerfile build-arg + infra webEnvVars.
    readonly NEXT_PUBLIC_APPLICATIONINSIGHTS_CONNECTION_STRING?: string;
  }
}
