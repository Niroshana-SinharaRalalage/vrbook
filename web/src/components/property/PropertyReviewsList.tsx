import { Star } from 'lucide-react';
import { listReviewsForProperty } from '@/lib/api/reviews';

const StarRow = ({ rating }: { rating: number }) => (
  <div className="flex items-center gap-0.5" aria-label={`${rating} out of 5 stars`}>
    {[1, 2, 3, 4, 5].map((n) => (
      <Star
        key={n}
        className={`h-3.5 w-3.5 ${
          n <= rating ? 'fill-yellow-400 text-yellow-400' : 'fill-transparent text-muted-foreground/40'
        }`}
        aria-hidden
      />
    ))}
  </div>
);

interface Props {
  readonly propertyId: string;
}

export const PropertyReviewsList = async ({ propertyId }: Props) => {
  const page = await listReviewsForProperty(propertyId);
  if (page.items.length === 0) {
    return (
      <p className="text-sm text-muted-foreground">No reviews yet. Be the first to stay and share your experience.</p>
    );
  }
  return (
    <ul className="space-y-5">
      {page.items.map((r) => (
        <li key={r.id} className="space-y-2 border-b border-border pb-5 last:border-b-0 last:pb-0">
          <div className="flex items-center gap-2">
            <span className="text-sm font-medium">{r.guestDisplayName || 'Guest'}</span>
            <StarRow rating={r.rating} />
            {r.publishedAt && (
              <time
                dateTime={r.publishedAt}
                className="ml-auto text-xs text-muted-foreground"
              >
                {new Date(r.publishedAt).toLocaleDateString(undefined, {
                  year: 'numeric',
                  month: 'short',
                })}
              </time>
            )}
          </div>
          {r.body && <p className="whitespace-pre-line text-sm">{r.body}</p>}
          {r.response && (
            <div className="ml-4 rounded-md bg-muted/40 p-3 text-sm">
              <p className="mb-1 text-xs font-medium text-muted-foreground">Host response</p>
              <p className="whitespace-pre-line">{r.response.body}</p>
            </div>
          )}
        </li>
      ))}
    </ul>
  );
};
