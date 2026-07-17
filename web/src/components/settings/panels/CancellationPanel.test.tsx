import { render, screen, waitFor } from '@testing-library/react';
import userEvent from '@testing-library/user-event';
import { beforeEach, describe, expect, it, vi } from 'vitest';

import { CancellationForm } from './CancellationPanel';
import type { PropertyCancellationSettingsDto } from '@/lib/api/settings';

const putPropertyCancellation = vi.fn();
vi.mock('@/lib/api/settings', () => ({
  putPropertyCancellation: (...a: unknown[]) => putPropertyCancellation(...a),
  getSettingsChanges: () => Promise.resolve([]),
}));
// RecentChangesPanel calls useAuthedQuery — stub it (no MSAL in unit tests).
vi.mock('@/hooks/useAuthedQuery', () => ({
  useAuthedQuery: () => ({ data: [], isLoading: false, isError: false, needsSignIn: false }),
}));

const initial: PropertyCancellationSettingsDto = {
  propertyId: 'p1',
  model: 'Tiered',
  resolvedTiers: {
    firstTierDays: 7,
    secondTierDays: 2,
    middleTierRefundPct: 50,
    finalCutoffHours: 48,
    upgradePricePct: 8,
    version: 1,
    lastChangedBy: null,
    lastChangedAt: null,
  },
  lastChangedBy: null,
  lastChangedAt: null,
};

describe('<CancellationForm /> — the VRB-210 worked example', () => {
  beforeEach(() => putPropertyCancellation.mockReset());

  it('shows the platform tiers read-only and the upgrade %', () => {
    render(<CancellationForm propertyId="p1" initial={initial} />);
    expect(screen.getByText(/platform tiers/i)).toBeInTheDocument();
    expect(screen.getByRole('option', { name: /refundable upgrade \(\+8% of subtotal\)/i })).toBeInTheDocument();
  });

  it('edits the model, saves, and calls the API', async () => {
    putPropertyCancellation.mockResolvedValue({ ...initial, model: 'RefundableUpgrade' });
    const user = userEvent.setup();
    render(<CancellationForm propertyId="p1" initial={initial} />);
    await user.selectOptions(screen.getByLabelText(/policy model/i), 'RefundableUpgrade');
    await user.click(screen.getByRole('button', { name: /save changes/i }));
    expect(putPropertyCancellation).toHaveBeenCalledWith('p1', { model: 'RefundableUpgrade' });
    await waitFor(() => expect(screen.getByRole('status')).toHaveTextContent(/saved/i));
  });

  // NB: server 422 → field-error mapping is unit-tested directly in
  // useSettingsForm.test.ts; not re-tested through the UI here.
});
