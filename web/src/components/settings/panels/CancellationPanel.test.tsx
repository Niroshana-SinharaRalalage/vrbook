import { render, screen, waitFor, within } from '@testing-library/react';
import userEvent from '@testing-library/user-event';
import { beforeAll, beforeEach, describe, expect, it, vi } from 'vitest';

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

beforeAll(() => {
  Element.prototype.hasPointerCapture ??= () => false;
  Element.prototype.setPointerCapture ??= () => undefined;
  Element.prototype.releasePointerCapture ??= () => undefined;
  Element.prototype.scrollIntoView ??= () => undefined;
});

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

describe('<CancellationForm /> (VRB-215)', () => {
  beforeEach(() => putPropertyCancellation.mockReset());

  it('renders both models as a radio group with per-model descriptions', () => {
    render(<CancellationForm propertyId="p1" initial={initial} />);
    const group = screen.getByRole('radiogroup', { name: /cancellation model/i });
    expect(within(group).getByRole('radio', { name: /tiered refund/i })).toBeChecked();
    const upgrade = within(group).getByRole('radio', { name: /refundable-rate upgrade/i });
    expect(upgrade).toHaveAccessibleDescription(/8% of the subtotal/i);
  });

  it('has no price input (upgrade price is platform-set)', () => {
    render(<CancellationForm propertyId="p1" initial={initial} />);
    expect(screen.queryByRole('spinbutton')).not.toBeInTheDocument();
    expect(screen.queryByRole('textbox')).not.toBeInTheDocument();
  });

  it('updates the guest preview when the model changes', async () => {
    const user = userEvent.setup();
    render(<CancellationForm propertyId="p1" initial={initial} />);
    await user.click(screen.getByRole('radio', { name: /refundable-rate upgrade/i }));
    const preview = screen.getByText(/guests will see/i).parentElement!;
    expect(preview).toHaveTextContent(/non-refundable/i);
  });

  it('switching models prompts a confirm modal, then saves', async () => {
    putPropertyCancellation.mockResolvedValue({ ...initial, model: 'RefundableUpgrade' });
    const user = userEvent.setup();
    render(<CancellationForm propertyId="p1" initial={initial} />);

    await user.click(screen.getByRole('radio', { name: /refundable-rate upgrade/i }));
    await user.click(screen.getByRole('button', { name: /save changes/i }));

    const dialog = await screen.findByRole('dialog');
    expect(dialog).toHaveAccessibleName(/change cancellation policy/i);
    expect(putPropertyCancellation).not.toHaveBeenCalled(); // not until confirmed

    await user.click(within(dialog).getByRole('button', { name: /save policy/i }));
    expect(putPropertyCancellation).toHaveBeenCalledWith('p1', { model: 'RefundableUpgrade' });
    await waitFor(() => expect(screen.getByRole('status')).toHaveTextContent(/saved/i));
  });
});
