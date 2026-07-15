import { createRef } from 'react';
import { render, screen } from '@testing-library/react';
import userEvent from '@testing-library/user-event';
import { describe, expect, it, vi } from 'vitest';

import { Input } from './Input';

describe('<Input />', () => {
  it('renders a text input', () => {
    render(<Input aria-label="Email" />);
    expect(screen.getByRole('textbox', { name: 'Email' })).toBeInTheDocument();
  });

  it('carries the shared focus-visible ring signature', () => {
    render(<Input aria-label="Email" />);
    expect(screen.getByRole('textbox').className).toContain('focus-visible:ring');
  });

  it('honours the type attribute', () => {
    render(<Input type="email" aria-label="Email" />);
    expect(screen.getByRole('textbox')).toHaveAttribute('type', 'email');
  });

  it('accepts typed input', async () => {
    const onChange = vi.fn();
    render(<Input aria-label="Email" onChange={onChange} />);
    await userEvent.type(screen.getByRole('textbox'), 'a@b.co');
    expect(onChange).toHaveBeenCalled();
    expect(screen.getByRole('textbox')).toHaveValue('a@b.co');
  });

  it('turns the border/ring destructive when aria-invalid is set', () => {
    render(<Input aria-label="Email" aria-invalid="true" />);
    const input = screen.getByRole('textbox');
    expect(input).toHaveAttribute('aria-invalid', 'true');
    // The invalid styling is baked in as an aria-driven variant.
    expect(input.className).toContain('aria-[invalid=true]:border-destructive');
  });

  it('passes through the native disabled attribute', () => {
    render(<Input aria-label="Email" disabled />);
    expect(screen.getByRole('textbox')).toBeDisabled();
  });

  it('forwards a ref to the underlying input element', () => {
    const ref = createRef<HTMLInputElement>();
    render(<Input aria-label="Email" ref={ref} />);
    expect(ref.current).toBeInstanceOf(HTMLInputElement);
  });

  it('merges a caller className', () => {
    render(<Input aria-label="Email" className="w-full" />);
    expect(screen.getByRole('textbox').className).toContain('w-full');
  });
});
