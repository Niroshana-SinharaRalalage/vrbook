import { render, screen } from '@testing-library/react';
import { describe, expect, it } from 'vitest';

import { Badge } from './Badge';

describe('<Badge />', () => {
  it('renders its label', () => {
    render(<Badge>Confirmed</Badge>);
    expect(screen.getByText('Confirmed')).toBeInTheDocument();
  });

  it('renders a pill (rounded-full) by default', () => {
    render(<Badge>New</Badge>);
    expect(screen.getByText('New').className).toContain('rounded-full');
  });

  it('maps the success variant to the success token (confirmed stays)', () => {
    render(<Badge variant="success">Confirmed</Badge>);
    expect(screen.getByText('Confirmed').className).toContain('success');
  });

  it('maps the warning variant to the warning token (pending stays)', () => {
    render(<Badge variant="warning">Pending</Badge>);
    expect(screen.getByText('Pending').className).toContain('warning');
  });

  it('maps the destructive variant (rejected / cancelled)', () => {
    render(<Badge variant="destructive">Rejected</Badge>);
    expect(screen.getByText('Rejected').className).toContain('destructive');
  });

  it('uses tabular-nums so counts and prices align', () => {
    render(<Badge>12</Badge>);
    expect(screen.getByText('12').className).toContain('tabular-nums');
  });

  it('merges a caller className', () => {
    render(<Badge className="uppercase">x</Badge>);
    expect(screen.getByText('x').className).toContain('uppercase');
  });
});
