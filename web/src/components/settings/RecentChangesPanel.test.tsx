import { render, screen } from '@testing-library/react';
import { axe, toHaveNoViolations } from 'jest-axe';
import { beforeEach, describe, expect, it, vi } from 'vitest';

import { RecentChangesPanel } from './RecentChangesPanel';

expect.extend(toHaveNoViolations);

let queryState: { data: unknown; isLoading: boolean; isError: boolean } = {
  data: [],
  isLoading: false,
  isError: false,
};
vi.mock('@/hooks/useAuthedQuery', () => ({ useAuthedQuery: () => queryState }));

describe('<RecentChangesPanel /> (VRB-110-followup a11y)', () => {
  beforeEach(() => {
    queryState = { data: [], isLoading: false, isError: false };
  });

  it('renders the empty state with no axe violations', async () => {
    const { container } = render(<RecentChangesPanel section="cancellation" />);
    expect(screen.getByText(/no changes recorded yet/i)).toBeInTheDocument();
    expect(await axe(container)).toHaveNoViolations();
  });

  it('renders a change list with no axe violations', async () => {
    queryState = {
      data: [
        { actor: 'admin@example.com', action: 'settings.cancellation.set-model', before: null, after: '{}', at: '2026-07-18T00:00:00Z' },
      ],
      isLoading: false,
      isError: false,
    };
    const { container } = render(<RecentChangesPanel section="cancellation" />);
    expect(screen.getByText(/admin@example.com/)).toBeInTheDocument();
    expect(await axe(container)).toHaveNoViolations();
  });

  it('renders the loading state with no axe violations', async () => {
    queryState = { data: undefined, isLoading: true, isError: false };
    const { container } = render(<RecentChangesPanel section="cancellation" />);
    expect(await axe(container)).toHaveNoViolations();
  });
});
