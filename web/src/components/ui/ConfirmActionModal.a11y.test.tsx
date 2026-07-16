import { render, screen } from '@testing-library/react';
import userEvent from '@testing-library/user-event';
import { axe, toHaveNoViolations } from 'jest-axe';
import { beforeAll, describe, expect, it, vi } from 'vitest';

import { ConfirmActionModal, type ConfirmActionModalProps } from './ConfirmActionModal';

expect.extend(toHaveNoViolations);

beforeAll(() => {
  Element.prototype.hasPointerCapture ??= () => false;
  Element.prototype.setPointerCapture ??= () => undefined;
  Element.prototype.releasePointerCapture ??= () => undefined;
  Element.prototype.scrollIntoView ??= () => undefined;
});

const setup = (over: Partial<ConfirmActionModalProps> = {}) => {
  const onCancel = vi.fn();
  const onConfirm = vi.fn();
  render(
    <ConfirmActionModal
      open
      title="Reject this booking?"
      description="The guest will be notified and the hold released."
      confirmLabel="Reject booking"
      confirmVariant="destructive"
      onCancel={onCancel}
      onConfirm={onConfirm}
      {...over}
    />,
  );
  return { onCancel, onConfirm };
};

describe('<ConfirmActionModal /> (VRB-110 a11y)', () => {
  it('does not render when closed', () => {
    render(
      <ConfirmActionModal
        open={false}
        title="x"
        description="y"
        confirmLabel="Go"
        onCancel={vi.fn()}
        onConfirm={vi.fn()}
      />,
    );
    expect(screen.queryByRole('dialog')).not.toBeInTheDocument();
  });

  it('is a dialog named by its title with the description wired', () => {
    setup();
    const dialog = screen.getByRole('dialog');
    expect(dialog).toHaveAccessibleName('Reject this booking?');
    expect(dialog).toHaveAccessibleDescription(/guest will be notified/i);
  });

  it('traps focus inside the modal on open', () => {
    setup();
    expect(screen.getByRole('dialog').contains(document.activeElement)).toBe(true);
  });

  it('reports a cancel on Escape (controlled — the parent then flips `open`)', async () => {
    const user = userEvent.setup();
    const { onCancel } = setup();
    await user.keyboard('{Escape}');
    expect(onCancel).toHaveBeenCalledTimes(1);
  });

  it('fires onConfirm / onCancel from the footer buttons', async () => {
    const user = userEvent.setup();
    const { onCancel, onConfirm } = setup();
    await user.click(screen.getByRole('button', { name: 'Reject booking' }));
    expect(onConfirm).toHaveBeenCalledTimes(1);
    await user.click(screen.getByRole('button', { name: 'Cancel' }));
    expect(onCancel).toHaveBeenCalledTimes(1);
  });

  it('does NOT cancel on Escape while an action is in flight', async () => {
    const user = userEvent.setup();
    const { onCancel } = setup({ busy: true, busyLabel: 'Rejecting…' });
    await user.keyboard('{Escape}');
    expect(onCancel).not.toHaveBeenCalled();
    expect(screen.getByRole('button', { name: /rejecting/i })).toHaveAttribute('aria-busy', 'true');
  });

  it('shows an error banner inside the modal', () => {
    setup({ error: 'The booking was already confirmed.' });
    expect(screen.getByRole('alert')).toHaveTextContent('The booking was already confirmed.');
  });

  it('has no axe violations when open', async () => {
    setup();
    expect(await axe(screen.getByRole('dialog'))).toHaveNoViolations();
  });
});
