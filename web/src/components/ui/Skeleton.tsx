import type { HTMLAttributes } from 'react';

import { cn } from '@/lib/utils/cn';

/**
 * VrBook Skeleton — a loading placeholder. Decorative, so it is hidden from
 * assistive tech (`aria-hidden`); announce loading state elsewhere with a live
 * region. Pulses only under `motion-safe` so `prefers-reduced-motion` users
 * see a still block. Size it with utility classes: `<Skeleton className="h-4 w-24" />`.
 */
export const Skeleton = ({ className, ...props }: HTMLAttributes<HTMLDivElement>) => (
  <div
    aria-hidden="true"
    className={cn('rounded-md bg-muted motion-safe:animate-pulse', className)}
    {...props}
  />
);
