import { cn } from '@/lib/utils/cn';

interface PropertyGalleryProps {
  readonly images: ReadonlyArray<{ url: string; alt: string }>;
  readonly className?: string;
}

/**
 * Placeholder gallery. Agent F1 will replace with a lightbox + keyboard-nav
 * implementation (proposal §12 — property detail). Today we render a simple
 * 2x2 grid + a "+N more" tile.
 */
export const PropertyGallery = ({ images, className }: PropertyGalleryProps) => {
  const visible = images.slice(0, 4);
  const more = Math.max(0, images.length - 4);

  if (visible.length === 0) {
    return (
      <div className={cn('flex aspect-[2/1] items-center justify-center rounded-xl bg-muted text-sm text-muted-foreground', className)}>
        No photos yet
      </div>
    );
  }

  return (
    <div className={cn('grid aspect-[2/1] grid-cols-4 gap-2 overflow-hidden rounded-xl', className)}>
      {visible.map((img, idx) => (
        <div
          key={img.url}
          className={cn(
            'relative bg-muted',
            idx === 0 ? 'col-span-2 row-span-2' : 'col-span-1 row-span-1',
          )}
        >
          {/* eslint-disable-next-line @next/next/no-img-element */}
          <img src={img.url} alt={img.alt} className="h-full w-full object-cover" loading={idx === 0 ? 'eager' : 'lazy'} />
          {idx === 3 && more > 0 && (
            <div className="absolute inset-0 flex items-center justify-center bg-black/50 text-sm font-medium text-white">
              +{more} more
            </div>
          )}
        </div>
      ))}
    </div>
  );
};
