import { createRef } from 'react';
import { render, screen } from '@testing-library/react';
import userEvent from '@testing-library/user-event';
import { describe, expect, it, vi } from 'vitest';

import { Button, buttonVariants } from './Button';

describe('<Button />', () => {
  it('renders its children inside a real <button>', () => {
    render(<Button>Book now</Button>);
    const btn = screen.getByRole('button', { name: 'Book now' });
    expect(btn.tagName).toBe('BUTTON');
  });

  it('defaults type to "button" so it never submits a form by accident', () => {
    render(<Button>Save</Button>);
    expect(screen.getByRole('button')).toHaveAttribute('type', 'button');
  });

  it('honours an explicit type="submit"', () => {
    render(<Button type="submit">Confirm booking</Button>);
    expect(screen.getByRole('button')).toHaveAttribute('type', 'submit');
  });

  it('applies the primary (brand orange) variant by default', () => {
    render(<Button>Go</Button>);
    expect(screen.getByRole('button').className).toContain('bg-primary');
  });

  it('applies the secondary (maroon) variant', () => {
    render(<Button variant="secondary">Go</Button>);
    expect(screen.getByRole('button').className).toContain('bg-secondary');
  });

  it('applies the destructive variant', () => {
    render(<Button variant="destructive">Delete</Button>);
    expect(screen.getByRole('button').className).toContain('bg-destructive');
  });

  it('applies the success variant (confirm/approve actions)', () => {
    render(<Button variant="success">Approve</Button>);
    expect(screen.getByRole('button').className).toContain('bg-success');
  });

  it('carries the shared focus-visible ring signature', () => {
    render(<Button>Go</Button>);
    expect(screen.getByRole('button').className).toContain('focus-visible:ring');
  });

  it('reflects the size prop', () => {
    render(<Button size="lg">Go</Button>);
    expect(screen.getByRole('button').className).toContain('h-11');
  });

  it('fires onClick when pressed', async () => {
    const onClick = vi.fn();
    render(<Button onClick={onClick}>Go</Button>);
    await userEvent.click(screen.getByRole('button'));
    expect(onClick).toHaveBeenCalledTimes(1);
  });

  it('is disabled and does not fire onClick when loading', async () => {
    const onClick = vi.fn();
    render(
      <Button loading onClick={onClick}>
        Go
      </Button>,
    );
    const btn = screen.getByRole('button');
    expect(btn).toBeDisabled();
    expect(btn).toHaveAttribute('aria-busy', 'true');
    await userEvent.click(btn);
    expect(onClick).not.toHaveBeenCalled();
  });

  it('shows an aria-hidden spinner while loading and keeps the label readable', () => {
    render(<Button loading>Saving</Button>);
    expect(screen.getByRole('button')).toHaveTextContent('Saving');
    expect(screen.getByTestId('button-spinner')).toHaveAttribute('aria-hidden', 'true');
  });

  it('passes through the native disabled attribute', () => {
    render(<Button disabled>Go</Button>);
    expect(screen.getByRole('button')).toBeDisabled();
  });

  it('forwards a ref to the underlying button element', () => {
    const ref = createRef<HTMLButtonElement>();
    render(<Button ref={ref}>Go</Button>);
    expect(ref.current).toBeInstanceOf(HTMLButtonElement);
  });

  it('merges a caller className without dropping variant classes', () => {
    render(<Button className="w-full">Go</Button>);
    const btn = screen.getByRole('button');
    expect(btn.className).toContain('w-full');
    expect(btn.className).toContain('bg-primary');
  });

  it('exposes buttonVariants() so a link can borrow the button look', () => {
    const cls = buttonVariants({ variant: 'outline', size: 'sm' });
    expect(cls).toContain('border');
    expect(cls).toContain('h-9');
  });
});
