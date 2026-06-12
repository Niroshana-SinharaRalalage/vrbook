import { apiFetch } from './client';

// Slice 2 — DevAuth persona switcher. Only active when the API is configured
// with DevAuth:AllowAnonymous=true (staging + local). Production Entra ignores
// the cookie entirely so leaving the switcher mounted there is harmless.
export type DevPersona = 'Owner' | 'Guest' | 'Admin';

export interface DevPersonaInfo {
  readonly value: DevPersona;
  readonly displayName: string;
  readonly email: string;
  readonly roles: readonly string[];
}

export interface DevPersonasState {
  readonly current: DevPersona;
  readonly options: readonly DevPersonaInfo[];
}

export const getDevPersonas = (): Promise<DevPersonasState> =>
  apiFetch<DevPersonasState>('/api/v1/dev-auth/personas', { anonymous: true });

export const switchDevPersona = (persona: DevPersona): Promise<{ persona: DevPersona; displayName: string }> =>
  apiFetch<{ persona: DevPersona; displayName: string }>(`/api/v1/dev-auth/switch?persona=${encodeURIComponent(persona)}`, {
    method: 'POST',
    anonymous: true,
  });
