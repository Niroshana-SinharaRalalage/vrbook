import { render, screen } from '@testing-library/react';
import userEvent from '@testing-library/user-event';
import { describe, expect, it, vi } from 'vitest';

import { Dropzone } from './Dropzone';

describe('<Dropzone />', () => {
  it('is a labelled button describing the limits', () => {
    render(<Dropzone onFiles={vi.fn()} description="JPEG, PNG or WebP · up to 10 MB each." />);
    const button = screen.getByRole('button', { name: /drag photos or browse/i });
    expect(button).toHaveAccessibleDescription(/up to 10 MB/i);
  });

  it('emits the chosen files', async () => {
    const onFiles = vi.fn();
    const { container } = render(<Dropzone onFiles={onFiles} />);
    const input = container.querySelector('input[type="file"]') as HTMLInputElement;
    const file = new File(['x'], 'photo.png', { type: 'image/png' });
    await userEvent.upload(input, file);
    expect(onFiles).toHaveBeenCalledTimes(1);
    expect(onFiles).toHaveBeenCalledWith([file]);
  });

  it('does not emit when disabled and clicked', async () => {
    const onFiles = vi.fn();
    render(<Dropzone onFiles={onFiles} disabled />);
    await userEvent.click(screen.getByRole('button'));
    expect(onFiles).not.toHaveBeenCalled();
  });
});
