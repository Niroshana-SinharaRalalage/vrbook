'use client';

import { useState } from 'react';
import { useRouter } from 'next/navigation';
import { Star } from 'lucide-react';
import { submitReview } from '@/lib/api/reviews';
import { ApiProblemError } from '@/lib/api/client';

interface Props {
  readonly bookingId: string;
}

export const ReviewSubmitForm = ({ bookingId }: Props) => {
  const router = useRouter();
  const [rating, setRating] = useState(0);
  const [hoverRating, setHoverRating] = useState(0);
  const [body, setBody] = useState('');
  const [busy, setBusy] = useState(false);
  const [error, setError] = useState<string | null>(null);
  const [submitted, setSubmitted] = useState(false);

  const display = hoverRating || rating;

  const onSubmit = async (e: React.FormEvent) => {
    e.preventDefault();
    if (rating < 1) {
      setError('Pick a rating from 1 to 5 stars.');
      return;
    }
    setBusy(true);
    setError(null);
    try {
      await submitReview(bookingId, { rating, body });
      setSubmitted(true);
      router.refresh();
    } catch (err) {
      setError(
        err instanceof ApiProblemError
          ? err.problem.detail ?? err.message
          : err instanceof Error
            ? err.message
            : 'Submit failed',
      );
    } finally {
      setBusy(false);
    }
  };

  if (submitted) {
    return (
      <div className="rounded-md border border-green-500/30 bg-green-50 p-3 text-sm text-green-900 dark:bg-green-900/20 dark:text-green-200">
        Thanks for your review! It&apos;s now visible on the property page.
      </div>
    );
  }

  return (
    <form onSubmit={onSubmit} className="space-y-3">
      <div className="flex items-center gap-1" onMouseLeave={() => setHoverRating(0)}>
        {[1, 2, 3, 4, 5].map((n) => (
          <button
            key={n}
            type="button"
            onClick={() => setRating(n)}
            onMouseEnter={() => setHoverRating(n)}
            className="p-1"
            aria-label={`Rate ${n} star${n > 1 ? 's' : ''}`}
          >
            <Star
              className={`h-6 w-6 transition-colors ${
                n <= display
                  ? 'fill-yellow-400 text-yellow-400'
                  : 'fill-transparent text-muted-foreground'
              }`}
            />
          </button>
        ))}
        <span className="ml-2 text-xs text-muted-foreground">
          {rating > 0 ? `${rating}/5` : 'Tap to rate'}
        </span>
      </div>

      <label className="block text-xs">
        <span className="block text-muted-foreground">How was your stay? (optional)</span>
        <textarea
          value={body}
          onChange={(e) => setBody(e.target.value)}
          rows={4}
          maxLength={4000}
          className="mt-1 w-full rounded-md border border-border bg-background px-2 py-1.5 text-sm"
          placeholder="Share what you liked and what could be better."
        />
        <span className="mt-1 block text-right text-[10px] text-muted-foreground/70">
          {body.length}/4000
        </span>
      </label>

      {error && (
        <div className="rounded-md border border-destructive/30 bg-destructive/5 p-3 text-xs text-destructive">
          {error}
        </div>
      )}

      <button
        type="submit"
        disabled={busy || rating < 1}
        className="w-full rounded-md bg-brand-maroon-700 px-3 py-2 text-sm font-medium text-white hover:bg-brand-maroon-800 disabled:opacity-50"
      >
        {busy ? 'Posting…' : 'Post review'}
      </button>
    </form>
  );
};
