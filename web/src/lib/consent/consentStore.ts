/**
 * VRB-311 — cookie-consent record. Stored as a first-party cookie (readable by
 * the server layout to decide banner visibility without a hydration flash, and
 * the conventional consent artefact). Two categories per PRD §9: `necessary`
 * (always on) and `analytics`. Bumping {@link CONSENT_VERSION} re-prompts every
 * visitor (e.g. when the analytics vendor or policy changes).
 */
export const CONSENT_COOKIE = 'vrb_consent';
export const CONSENT_VERSION = 1;
export const CONSENT_MAX_AGE_DAYS = 180;

export interface ConsentState {
  readonly necessary: true;
  readonly analytics: boolean;
  readonly ts: number;
  readonly version: number;
}

/** Parse a raw cookie value into consent state; null = no valid consent (re-prompt). */
export const parseConsent = (raw: string | undefined | null): ConsentState | null => {
  if (!raw) return null;
  try {
    const decoded = JSON.parse(decodeURIComponent(raw)) as Record<string, unknown>;
    if (typeof decoded !== 'object' || decoded === null) return null;
    // A different version means our categories/policy changed — re-prompt.
    if (decoded.version !== CONSENT_VERSION) return null;
    return {
      necessary: true,
      analytics: decoded.analytics === true,
      ts: typeof decoded.ts === 'number' ? decoded.ts : 0,
      version: CONSENT_VERSION,
    };
  } catch {
    return null;
  }
};

/** Serialize a consent choice to a cookie value (URL-encoded JSON). */
export const serializeConsent = (analytics: boolean, now: number): string =>
  encodeURIComponent(
    JSON.stringify({ necessary: true, analytics, ts: now, version: CONSENT_VERSION }),
  );

/** Client-only: read the current consent from `document.cookie`. */
export const readConsentCookie = (): ConsentState | null => {
  if (typeof document === 'undefined') return null;
  const entry = document.cookie
    .split('; ')
    .find((c) => c.startsWith(`${CONSENT_COOKIE}=`));
  return parseConsent(entry?.slice(CONSENT_COOKIE.length + 1));
};

/** Client-only: persist a consent choice. */
export const writeConsentCookie = (analytics: boolean, now: number = Date.now()): void => {
  if (typeof document === 'undefined') return;
  const maxAge = CONSENT_MAX_AGE_DAYS * 24 * 60 * 60;
  document.cookie =
    `${CONSENT_COOKIE}=${serializeConsent(analytics, now)}; path=/; max-age=${maxAge}; SameSite=Lax`;
};
