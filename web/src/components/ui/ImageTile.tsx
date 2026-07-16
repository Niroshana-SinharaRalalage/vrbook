'use client';

import { type CSSProperties } from 'react';
import { GripVertical, Star, Trash2 } from 'lucide-react';

import { cn } from '@/lib/utils/cn';
import { Badge } from './Badge';

export interface ImageTileProps {
  readonly url: string;
  readonly accessibleName: string;
  readonly caption?: string | null;
  readonly isPrimary?: boolean;
  readonly uploading?: boolean;
  readonly error?: string | null;
  readonly onDelete?: () => void;
  readonly onRetry?: () => void;
  // dnd-kit wiring (omitted for the uploading placeholder tile).
  readonly setNodeRef?: (el: HTMLElement | null) => void;
  readonly style?: CSSProperties;
  readonly isDragging?: boolean;
  readonly handleProps?: Record<string, unknown>;
}

/**
 * VRB-101 — a single photo tile in the gallery manager. Presentational; the
 * gallery wraps it with `useSortable`. Rendered as a `role="listitem"` with an
 * accessible name ("Photo 1, cover") so keyboard + SR users can tell tiles
 * apart. The cover photo carries an amber badge (no `brand-orange` token exists
 * in the design system yet — using the `warning` ramp).
 */
export const ImageTile = ({
  url,
  accessibleName,
  caption,
  isPrimary = false,
  uploading = false,
  error = null,
  onDelete,
  onRetry,
  setNodeRef,
  style,
  isDragging = false,
  handleProps,
}: ImageTileProps) => (
  <li
    ref={setNodeRef}
    style={style}
    aria-label={accessibleName}
    className={cn(
      'group relative aspect-[4/3] overflow-hidden rounded-lg border border-border bg-muted',
      // Drag ghost: a soft maroon shadow to match the brand system.
      isDragging && 'z-10 opacity-80 shadow-[0_8px_30px_rgba(122,30,45,0.35)]',
    )}
  >
    {url ? (
      // eslint-disable-next-line @next/next/no-img-element -- SAS/blob urls, not a static asset
      <img src={url} alt={caption ?? ''} className="h-full w-full object-cover" />
    ) : (
      <div className="h-full w-full animate-pulse bg-muted" />
    )}

    {isPrimary && !uploading && !error && (
      <Badge variant="warning" className="absolute left-2 top-2 gap-1">
        <Star className="h-3 w-3" aria-hidden="true" /> Cover
      </Badge>
    )}

    {uploading && (
      <div className="absolute inset-0 grid place-items-center bg-background/60 text-xs font-medium">
        Uploading…
      </div>
    )}

    {error && (
      <div className="absolute inset-0 flex flex-col items-center justify-center gap-1 bg-destructive/10 p-2 text-center text-xs text-destructive">
        <span>{error}</span>
        {onRetry && (
          <button type="button" onClick={onRetry} className="underline underline-offset-2">
            Retry
          </button>
        )}
      </div>
    )}

    {handleProps && !uploading && !error && (
      <button
        type="button"
        {...handleProps}
        aria-label={`Reorder ${accessibleName}`}
        className="absolute bottom-2 left-2 cursor-grab rounded bg-background/80 p-1 text-muted-foreground opacity-0 transition-opacity hover:text-foreground focus-visible:opacity-100 active:cursor-grabbing group-hover:opacity-100"
      >
        <GripVertical className="h-4 w-4" aria-hidden="true" />
      </button>
    )}

    {onDelete && !uploading && (
      <button
        type="button"
        onClick={onDelete}
        aria-label={`Delete ${accessibleName}`}
        className="absolute right-2 top-2 rounded bg-background/80 p-1 text-destructive opacity-0 transition-opacity hover:bg-destructive/10 focus-visible:opacity-100 group-hover:opacity-100"
      >
        <Trash2 className="h-4 w-4" aria-hidden="true" />
      </button>
    )}
  </li>
);
