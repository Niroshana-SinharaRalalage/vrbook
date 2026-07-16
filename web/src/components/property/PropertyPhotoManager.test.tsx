import { QueryClient, QueryClientProvider } from '@tanstack/react-query';
import { render, screen, waitFor, within } from '@testing-library/react';
import userEvent from '@testing-library/user-event';
import { beforeAll, beforeEach, describe, expect, it, vi } from 'vitest';

import { PropertyPhotoManager } from './PropertyPhotoManager';
import type { PropertyImage } from '@/lib/api/catalog';

const upload = vi.fn();
const reorder = vi.fn();
const del = vi.fn();
vi.mock('@/lib/api/catalog', () => ({
  uploadPropertyImage: (...a: unknown[]) => upload(...a),
  reorderPropertyImages: (...a: unknown[]) => reorder(...a),
  deletePropertyImage: (...a: unknown[]) => del(...a),
}));

beforeAll(() => {
  Element.prototype.hasPointerCapture ??= () => false;
  Element.prototype.setPointerCapture ??= () => undefined;
  Element.prototype.releasePointerCapture ??= () => undefined;
  Element.prototype.scrollIntoView ??= () => undefined;
  // jsdom lacks object-URL support.
  URL.createObjectURL ??= () => 'blob:preview';
  URL.revokeObjectURL ??= () => undefined;
});

const images: PropertyImage[] = [
  { id: 'a', url: '/a.jpg', caption: 'Front', sortOrder: 0, isPrimary: true },
  { id: 'b', url: '/b.jpg', caption: null, sortOrder: 1, isPrimary: false },
];

const renderManager = (initial = images) => {
  const qc = new QueryClient({ defaultOptions: { queries: { retry: false } } });
  return render(
    <QueryClientProvider client={qc}>
      <PropertyPhotoManager propertyId="p1" initialImages={initial} />
    </QueryClientProvider>,
  );
};

const fileInput = (c: HTMLElement) => c.querySelector('input[type="file"]') as HTMLInputElement;

describe('<PropertyPhotoManager />', () => {
  beforeEach(() => {
    upload.mockReset();
    reorder.mockReset();
    del.mockReset();
  });

  it('renders the initial gallery with the cover badge on the primary', () => {
    renderManager();
    expect(screen.getAllByRole('listitem')).toHaveLength(2);
    expect(screen.getByText('Cover')).toBeInTheDocument();
    expect(screen.getByLabelText('Photo 1 of 2, cover')).toBeInTheDocument();
  });

  it('shows the empty-state hint when there are no photos', () => {
    renderManager([]);
    expect(screen.getByText(/at least one photo before it can be published/i)).toBeInTheDocument();
  });

  it('rejects an oversize file inline without calling the API', async () => {
    const { container } = renderManager();
    const big = new File([new Uint8Array(11 * 1024 * 1024)], 'big.png', { type: 'image/png' });
    await userEvent.upload(fileInput(container), big);
    expect(await screen.findByText(/≤ 10 MB/i)).toBeInTheDocument();
    expect(upload).not.toHaveBeenCalled();
  });

  it('rejects a disallowed type inline without calling the API', async () => {
    const { container } = renderManager();
    const gif = new File(['x'], 'anim.gif', { type: 'image/gif' });
    await userEvent.upload(fileInput(container), gif);
    expect(await screen.findByText(/JPEG, PNG or WebP/i)).toBeInTheDocument();
    expect(upload).not.toHaveBeenCalled();
  });

  it('uploads a valid file and adds the tile', async () => {
    upload.mockResolvedValue({ id: 'c', url: '/c.jpg', caption: null, sortOrder: 2, isPrimary: false });
    const { container } = renderManager();
    const ok = new File(['x'], 'ok.jpg', { type: 'image/jpeg' });
    await userEvent.upload(fileInput(container), ok);
    expect(upload).toHaveBeenCalledWith('p1', ok);
    await waitFor(() => expect(screen.getAllByRole('listitem')).toHaveLength(3));
  });

  it('deletes a photo through the confirm modal', async () => {
    del.mockResolvedValue(undefined);
    const user = userEvent.setup();
    renderManager();
    await user.click(screen.getByRole('button', { name: /delete photo 1 of 2/i }));
    const dialog = await screen.findByRole('dialog');
    await user.click(within(dialog).getByRole('button', { name: 'Delete photo' }));
    expect(del).toHaveBeenCalledWith('p1', 'a');
    await waitFor(() => expect(screen.getAllByRole('listitem')).toHaveLength(1));
  });
});
