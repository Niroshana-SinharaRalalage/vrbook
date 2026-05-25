import Link from 'next/link';
import { Star } from 'lucide-react';

import { cn } from '@/lib/utils/cn';
import { formatCurrency } from '@/lib/utils/currency';

export interface PropertyCardModel {
  readonly slug: string;
  readonly title: string;
  readonly location: string;
  readonly nightlyRate: number;
  readonly currency: string;
  readonly ratingAvg: number | null;
  readonly ratingCount: number;
  readonly coverImageUrl: string | null;
}

interface PropertyCardProps {
  readonly property: PropertyCardModel;
  readonly className?: string;
}

export const PropertyCard = ({ property, className }: PropertyCardProps) => {
  return (
    <Link
      href={`/properties/${property.slug}`}
      className={cn(
        'group block overflow-hidden rounded-xl border border-border bg-card transition-shadow hover:shadow-md',
        className,
      )}
    >
      <div className="relative aspect-[4/3] w-full bg-muted">
        {property.coverImageUrl ? (
          // Plain <img> to avoid next/image config dependency in scaffold; F1 swaps to next/image.
          // eslint-disable-next-line @next/next/no-img-element
          <img
            src={property.coverImageUrl}
            alt={property.title}
            className="h-full w-full object-cover transition-transform group-hover:scale-[1.02]"
            loading="lazy"
          />
        ) : (
          <div className="flex h-full w-full items-center justify-center text-xs text-muted-foreground">
            No image
          </div>
        )}
      </div>
      <div className="space-y-1 p-4">
        <div className="flex items-start justify-between gap-2">
          <h3 className="line-clamp-1 text-sm font-medium text-foreground">{property.title}</h3>
          {property.ratingAvg !== null && (
            <span className="flex shrink-0 items-center gap-1 text-xs text-muted-foreground">
              <Star className="h-3 w-3 fill-current text-brand-orange-500" aria-hidden />
              {property.ratingAvg.toFixed(1)}
              <span className="text-muted-foreground/70">({property.ratingCount})</span>
            </span>
          )}
        </div>
        <p className="line-clamp-1 text-xs text-muted-foreground">{property.location}</p>
        <p className="pt-1 text-sm">
          <span className="font-semibold text-foreground">
            {formatCurrency(property.nightlyRate, property.currency)}
          </span>
          <span className="text-muted-foreground"> / night</span>
        </p>
      </div>
    </Link>
  );
};
