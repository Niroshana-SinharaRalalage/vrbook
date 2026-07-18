import { render, screen } from '@testing-library/react';
import { axe, toHaveNoViolations } from 'jest-axe';
import { describe, expect, it, vi } from 'vitest';

import { ImageTile } from './ImageTile';

expect.extend(toHaveNoViolations);

const renderTile = (props = {}) =>
  render(
    <ul role="list">
      <ImageTile
        url="/photo.jpg"
        accessibleName="Photo 1 of 3, cover"
        caption="Front of the house"
        isPrimary
        onDelete={vi.fn()}
        handleProps={{ 'aria-label': 'Reorder Photo 1 of 3, cover' }}
        {...props}
      />
    </ul>,
  );

describe('<ImageTile /> (VRB-110-followup a11y)', () => {
  it('is a listitem with the given accessible name', () => {
    renderTile();
    expect(screen.getByRole('listitem', { name: /photo 1 of 3, cover/i })).toBeInTheDocument();
  });

  it('labels the delete and reorder controls', () => {
    renderTile();
    expect(screen.getByRole('button', { name: /delete photo 1 of 3/i })).toBeInTheDocument();
    expect(screen.getByRole('button', { name: /reorder photo 1 of 3/i })).toBeInTheDocument();
  });

  it('has no axe violations (default / uploading / error states)', async () => {
    const normal = renderTile();
    expect(await axe(normal.container)).toHaveNoViolations();
    normal.unmount();

    const uploading = renderTile({ uploading: true, onDelete: undefined, handleProps: undefined });
    expect(await axe(uploading.container)).toHaveNoViolations();
    uploading.unmount();

    const errored = renderTile({ error: 'Upload failed.', onRetry: vi.fn(), handleProps: undefined });
    expect(await axe(errored.container)).toHaveNoViolations();
  });
});
