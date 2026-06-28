/**
 * Slice OPS.M.8 Step 10 — pins the request shape of every platform-admin
 * API call. Same intent as `tenant.test.ts`: catch a misnamed endpoint
 * or missing target-tenant-id segment at PR time.
 */
import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest';
import {
  listPlatformTenants,
  getPlatformTenant,
  suspendTenant,
  reactivateTenant,
  setPlatformFee,
} from './platform';

const fetchMock = vi.fn();
const originalFetch = globalThis.fetch;

beforeEach(() => {
  (process.env as Record<string, string>).NEXT_PUBLIC_API_BASE_URL =
    'http://localhost:5000';
  fetchMock.mockReset();
  fetchMock.mockResolvedValue(
    new Response(JSON.stringify({}), {
      status: 200,
      headers: { 'Content-Type': 'application/json' },
    }),
  );
  globalThis.fetch = fetchMock as unknown as typeof fetch;
});

afterEach(() => {
  globalThis.fetch = originalFetch;
});

const lastUrl = () => new URL(fetchMock.mock.calls[0]![0] as string);
const lastInit = () => fetchMock.mock.calls[0]![1] as RequestInit;

describe('platform API client', () => {
  it('listPlatformTenants calls GET with paged query string', async () => {
    await listPlatformTenants({ page: 2, pageSize: 25, status: 'Active', search: 'acme' });
    const u = lastUrl();
    expect(u.pathname).toBe('/api/v1/admin/platform/tenants');
    expect(u.searchParams.get('page')).toBe('2');
    expect(u.searchParams.get('pageSize')).toBe('25');
    expect(u.searchParams.get('status')).toBe('Active');
    expect(u.searchParams.get('search')).toBe('acme');
    expect(lastInit().method).toBe('GET');
  });

  it('getPlatformTenant calls GET on the detail route', async () => {
    await getPlatformTenant('tnt-1');
    expect(lastUrl().pathname).toBe('/api/v1/admin/platform/tenants/tnt-1');
    expect(lastInit().method).toBe('GET');
  });

  it('suspendTenant POSTs with reason body', async () => {
    await suspendTenant('tnt-1', 'fraud');
    expect(lastUrl().pathname).toBe('/api/v1/admin/platform/tenants/tnt-1/suspend');
    expect(lastInit().method).toBe('POST');
    expect(lastInit().body).toContain('"reason"');
    expect(lastInit().body).toContain('fraud');
  });

  it('reactivateTenant POSTs the route', async () => {
    await reactivateTenant('tnt-1');
    expect(lastUrl().pathname).toBe('/api/v1/admin/platform/tenants/tnt-1/reactivate');
    expect(lastInit().method).toBe('POST');
  });

  it('setPlatformFee PUTs the bps body', async () => {
    await setPlatformFee('tnt-1', 2000);
    expect(lastUrl().pathname).toBe('/api/v1/admin/platform/tenants/tnt-1/platform-fee');
    expect(lastInit().method).toBe('PUT');
    expect(lastInit().body).toContain('"bps":2000');
  });
});
