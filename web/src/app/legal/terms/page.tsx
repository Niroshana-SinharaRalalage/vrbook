import type { Metadata } from 'next';

import { LegalArticle, type LegalSection } from '@/components/legal/LegalArticle';

export const metadata: Metadata = {
  title: 'Terms of Service',
  description: 'The terms governing your use of VrBook.',
  alternates: { canonical: '/legal/terms' },
};

const sections: LegalSection[] = [
  {
    id: 'acceptance',
    heading: 'Acceptance of terms',
    body: (
      <p>
        By accessing or using VrBook you agree to these Terms of Service. If you do not agree, do not
        use the platform. These terms apply to guests and to property owners (tenants) using the
        platform.
      </p>
    ),
  },
  {
    id: 'bookings',
    heading: 'Bookings and payments',
    body: (
      <p>
        VrBook is a commission-free direct-booking platform. Bookings are a contract between the guest
        and the property owner. Payments are processed by Stripe; VrBook does not store card details.
        Applicable taxes are calculated at checkout.
      </p>
    ),
  },
  {
    id: 'cancellations',
    heading: 'Cancellations',
    body: (
      <p>
        Each listing displays its cancellation policy, which forms part of the booking contract. See
        the <a href="/legal/cancellation" className="text-primary underline-offset-4 hover:underline">Cancellation Policy</a>{' '}
        page for how the policy models work.
      </p>
    ),
  },
  {
    id: 'conduct',
    heading: 'Acceptable use',
    body: <p>You agree not to misuse the platform, infringe others&rsquo; rights, or violate applicable law.</p>,
  },
  {
    id: 'liability',
    heading: 'Disclaimers and limitation of liability',
    body: (
      <p>
        The platform is provided &ldquo;as is.&rdquo; To the maximum extent permitted by law, VrBook
        disclaims warranties and limits liability for the direct-booking relationship between guest and
        owner.
      </p>
    ),
  },
  {
    id: 'governing-law',
    heading: 'Governing law',
    body: (
      <p>
        These terms are governed by the laws of the <strong>State of Ohio, United States</strong>,
        without regard to its conflict-of-laws rules. The exclusive venue for disputes is the state and
        federal courts located in Ohio.
      </p>
    ),
  },
  {
    id: 'contact',
    heading: 'Contact',
    body: <p>Questions about these terms: legal@vrbook.example.com (placeholder).</p>,
  },
];

export default function TermsPage() {
  return (
    <LegalArticle
      title="Terms of Service"
      updated="2026-07-16"
      intro={<p>These terms govern your use of VrBook as a guest or property owner.</p>}
      sections={sections}
    />
  );
}
