import type { Metadata } from 'next';

import { LegalArticle, type LegalSection } from '@/components/legal/LegalArticle';

export const metadata: Metadata = {
  title: 'Cancellation Policy',
  description: 'How cancellations work on VrBook and the policy models owners can choose.',
  alternates: { canonical: '/legal/cancellation' },
};

const sections: LegalSection[] = [
  {
    id: 'overview',
    heading: 'How cancellations work',
    body: (
      <p>
        Each listing sets its own cancellation policy, shown on the listing and at checkout, which
        forms part of your booking contract. The refund you receive on cancellation depends on that
        policy and when you cancel.
      </p>
    ),
  },
  {
    id: 'models',
    heading: 'Policy models',
    body: (
      <ul className="list-disc space-y-1 pl-5">
        <li>
          <strong>Flexible</strong> — full refund up to a short window before check-in.
        </li>
        <li>
          <strong>Moderate / Strict</strong> — partial or no refund closer to check-in, per the
          policy shown on the listing.
        </li>
      </ul>
    ),
  },
  {
    id: 'per-listing',
    heading: 'Per-listing policy',
    body: (
      <p>
        The exact windows and refund percentages are set by the property owner and displayed on the
        listing before you book. Always review the policy on the specific listing.
        <em> (Exact model definitions are placeholder pending the cancellation-policy engine, VRB-102.)</em>
      </p>
    ),
  },
  {
    id: 'how-to-cancel',
    heading: 'How to cancel',
    body: (
      <p>
        Cancel from <a href="/account/bookings" className="text-primary underline-offset-4 hover:underline">My trips</a>;
        any refund is processed to your original payment method per the listing&rsquo;s policy.
      </p>
    ),
  },
];

export default function CancellationPage() {
  return (
    <LegalArticle
      title="Cancellation Policy"
      updated="2026-07-16"
      intro={<p>How cancellations and refunds work, and the policy models owners can choose.</p>}
      sections={sections}
    />
  );
}
