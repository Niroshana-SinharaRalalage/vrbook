import { render, screen, waitFor } from '@testing-library/react';
import userEvent from '@testing-library/user-event';
import { axe, toHaveNoViolations } from 'jest-axe';
import { beforeEach, describe, expect, it, vi } from 'vitest';

import { GlobalTiersForm } from './GlobalTiersPanel';
import type { GlobalCancellationTiersDto } from '@/lib/api/settings';

const putTiers = vi.fn();
vi.mock('@/lib/api/settings', () => ({
  putGlobalCancellationTiers: (...a: unknown[]) => putTiers(...a),
  getSettingsChanges: () => Promise.resolve([]),
}));
vi.mock('@/hooks/useAuthedQuery', () => ({
  useAuthedQuery: () => ({ data: [], isLoading: false, isError: false, needsSignIn: false }),
}));

expect.extend(toHaveNoViolations);

const initial: GlobalCancellationTiersDto = {
  firstTierDays: 7,
  secondTierDays: 2,
  middleTierRefundPct: 50,
  finalCutoffHours: 48,
  upgradePricePct: 8,
  version: 1,
  lastChangedBy: null,
  lastChangedAt: null,
};

const field = (label: RegExp) => screen.getByRole('spinbutton', { name: label });

describe('<GlobalTiersForm /> (VRB-216 web)', () => {
  beforeEach(() => putTiers.mockReset());

  it('renders the five tier fields with current values', () => {
    render(<GlobalTiersForm initial={initial} />);
    expect(field(/full-refund cutoff/i)).toHaveValue(7);
    expect(field(/partial-refund cutoff/i)).toHaveValue(2);
    expect(field(/partial refund/i)).toHaveValue(50);
    expect(field(/no-refund cutoff/i)).toHaveValue(48);
    expect(field(/refundable-upgrade price/i)).toHaveValue(8);
  });

  it('blocks save when the tiers are not monotonic (mirrors the server rule)', async () => {
    const user = userEvent.setup();
    render(<GlobalTiersForm initial={initial} />);
    // make firstTierDays (1) <= secondTierDays (2)
    await user.clear(field(/full-refund cutoff/i));
    await user.type(field(/full-refund cutoff/i), '1');
    await user.click(screen.getByRole('button', { name: /save changes/i }));
    expect(putTiers).not.toHaveBeenCalled();
    expect(field(/full-refund cutoff/i)).toHaveAccessibleDescription(/greater than the partial-refund cutoff/i);
  });

  it('saves a valid edit', async () => {
    putTiers.mockResolvedValue({ ...initial, middleTierRefundPct: 60 });
    const user = userEvent.setup();
    render(<GlobalTiersForm initial={initial} />);
    await user.clear(field(/partial refund/i));
    await user.type(field(/partial refund/i), '60');
    await user.click(screen.getByRole('button', { name: /save changes/i }));
    await waitFor(() =>
      expect(putTiers).toHaveBeenCalledWith(expect.objectContaining({ middleTierRefundPct: 60 })),
    );
    await waitFor(() => expect(screen.getByRole('status')).toHaveTextContent(/saved/i));
  });

  // Server-side 422 → field-error mapping is exercised at the hook level in
  // useSettingsForm.test.ts; the panel's field-error wiring is proven by the
  // client-validation test above (both use the same DS Field `error` path).

  it('has no axe violations', async () => {
    const { container } = render(<GlobalTiersForm initial={initial} />);
    expect(await axe(container)).toHaveNoViolations();
  });
});
