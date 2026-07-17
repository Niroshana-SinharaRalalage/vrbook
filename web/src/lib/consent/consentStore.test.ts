import { describe, expect, it } from 'vitest';

import {
  CONSENT_VERSION,
  parseConsent,
  serializeConsent,
} from './consentStore';

describe('consentStore', () => {
  it('parses a valid serialized consent', () => {
    const raw = serializeConsent(true, 1234);
    const parsed = parseConsent(raw);
    expect(parsed).toEqual({ necessary: true, analytics: true, ts: 1234, version: CONSENT_VERSION });
  });

  it('round-trips analytics=false', () => {
    expect(parseConsent(serializeConsent(false, 1))?.analytics).toBe(false);
  });

  it('returns null for a missing / empty cookie', () => {
    expect(parseConsent(undefined)).toBeNull();
    expect(parseConsent('')).toBeNull();
  });

  it('returns null for malformed JSON (re-prompt rather than trust)', () => {
    expect(parseConsent('not-json')).toBeNull();
  });

  it('returns null when the version differs (policy changed → re-prompt)', () => {
    const stale = encodeURIComponent(JSON.stringify({ necessary: true, analytics: true, ts: 1, version: 999 }));
    expect(parseConsent(stale)).toBeNull();
  });

  it('coerces a truthy-but-not-true analytics field to false (fail safe)', () => {
    const raw = encodeURIComponent(JSON.stringify({ analytics: 'yes', ts: 1, version: CONSENT_VERSION }));
    expect(parseConsent(raw)?.analytics).toBe(false);
  });
});
