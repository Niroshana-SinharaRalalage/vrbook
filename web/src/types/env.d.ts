// Typed access to NEXT_PUBLIC_* env vars (proposal §23.3).
// Keep in sync with .env.local.example.

declare namespace NodeJS {
  interface ProcessEnv {
    readonly NEXT_PUBLIC_API_BASE_URL: string;
    readonly NEXT_PUBLIC_STRIPE_PUBLISHABLE_KEY: string;
    readonly NEXT_PUBLIC_B2C_AUTHORITY: string;
    readonly NEXT_PUBLIC_B2C_CLIENT_ID: string;
    readonly NEXT_PUBLIC_MAPBOX_TOKEN: string;
    readonly NEXT_PUBLIC_SIGNALR_NEGOTIATE_URL: string;
  }
}
