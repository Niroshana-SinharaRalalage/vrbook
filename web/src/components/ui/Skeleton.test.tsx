import { render, screen } from '@testing-library/react';
import { describe, expect, it } from 'vitest';

import { Skeleton } from './Skeleton';

describe('<Skeleton />', () => {
  it('renders a placeholder block', () => {
    render(<Skeleton data-testid="sk" />);
    expect(screen.getByTestId('sk')).toBeInTheDocument();
  });

  it('pulses only when motion is allowed (motion-safe)', () => {
    render(<Skeleton data-testid="sk" />);
    expect(screen.getByTestId('sk').className).toContain('motion-safe:animate-pulse');
  });

  it('is hidden from assistive tech (decorative loading state)', () => {
    render(<Skeleton data-testid="sk" />);
    expect(screen.getByTestId('sk')).toHaveAttribute('aria-hidden', 'true');
  });

  it('merges a caller className for sizing', () => {
    render(<Skeleton data-testid="sk" className="h-4 w-24" />);
    const el = screen.getByTestId('sk');
    expect(el.className).toContain('h-4');
    expect(el.className).toContain('w-24');
  });
});
