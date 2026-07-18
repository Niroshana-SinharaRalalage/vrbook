import { render, screen } from '@testing-library/react';
import { axe, toHaveNoViolations } from 'jest-axe';
import { describe, expect, it, vi } from 'vitest';

import { SaveBar } from './SaveBar';

expect.extend(toHaveNoViolations);

const base = {
  isDirty: true,
  errorCount: 0,
  isSaving: false,
  savedAt: null as number | null,
  onSave: vi.fn().mockResolvedValue(true),
  onDiscard: vi.fn(),
};

describe('<SaveBar /> (VRB-110-followup a11y)', () => {
  it('renders the save region with actions when dirty', () => {
    render(<SaveBar {...base} />);
    expect(screen.getByRole('region', { name: /save changes/i })).toBeInTheDocument();
    expect(screen.getByRole('button', { name: /save changes/i })).toBeEnabled();
  });

  it('has no axe violations across states (dirty / errors / saved)', async () => {
    const dirty = render(<SaveBar {...base} />);
    expect(await axe(dirty.container)).toHaveNoViolations();
    dirty.unmount();

    const errors = render(<SaveBar {...base} errorCount={2} />);
    expect(await axe(errors.container)).toHaveNoViolations();
    errors.unmount();

    const saved = render(<SaveBar {...base} isDirty={false} savedAt={Date.now()} />);
    expect(await axe(saved.container)).toHaveNoViolations();
  });
});
