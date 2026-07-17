import type { Metadata } from 'next';

import { DataSubjectRequestForm } from '@/components/legal/DataSubjectRequestForm';
import { LegalArticle, type LegalSection } from '@/components/legal/LegalArticle';

export const metadata: Metadata = {
  title: 'Privacy Policy',
  description: 'How VrBook collects, uses, and protects your data, and your privacy rights.',
  alternates: { canonical: '/legal/privacy' },
};

const sections: LegalSection[] = [
  {
    id: 'data-we-collect',
    heading: 'Data we collect',
    body: (
      <p>
        Account details (name, email), booking and payment records, messages, and — only with your
        consent — analytics on how you use the site. We do not sell your personal data.
      </p>
    ),
  },
  {
    id: 'cookies-analytics',
    heading: 'Cookies and analytics',
    body: (
      <>
        <p>
          We use <strong>necessary</strong> cookies to run the platform (sign-in, security, booking).
          With your consent we also use <strong>analytics</strong> cookies (Azure Application Insights)
          to understand usage and improve the product — these load <em>only after</em> you accept.
        </p>
        <p>
          You can change your choice any time via <strong>&ldquo;Cookie preferences&rdquo;</strong> in
          the footer.
        </p>
      </>
    ),
  },
  {
    id: 'your-rights',
    heading: 'Your privacy rights (GDPR / CCPA)',
    body: (
      <>
        <p>
          Depending on your location you may have the right to access, correct, export, or delete your
          personal data, and to object to or restrict certain processing. Submit a request below; we
          verify your identity and respond within the statutory window (30 days GDPR / 45 days CCPA).
        </p>
        <DataSubjectRequestForm />
      </>
    ),
  },
  {
    id: 'retention',
    heading: 'Data retention',
    body: (
      <p>
        We keep personal data only as long as needed for the purposes above or as required by law
        (e.g. tax/financial records). Booking and payment records are retained per applicable
        financial-record obligations; account data is deleted on verified request except where a legal
        hold applies. <em>(Retention periods are placeholder pending owner/legal confirmation.)</em>
      </p>
    ),
  },
  {
    id: 'sub-processors',
    heading: 'Sub-processors',
    body: (
      <>
        <p>We share data with vetted sub-processors under data-processing agreements (DPAs):</p>
        <ul className="list-disc space-y-1 pl-5">
          <li><strong>Stripe</strong> — payment processing (card data handled by Stripe, not VrBook).</li>
          <li><strong>Microsoft Azure</strong> — hosting, storage, email (ACS), and analytics (Application Insights).</li>
        </ul>
        <p>Their DPAs and sub-processor lists are referenced in our vendor agreements.</p>
      </>
    ),
  },
  {
    id: 'contact',
    heading: 'Contact',
    body: <p>Privacy questions or requests: privacy@vrbook.example.com (placeholder).</p>,
  },
];

export default function PrivacyPage() {
  return (
    <LegalArticle
      title="Privacy Policy"
      updated="2026-07-16"
      intro={<p>How we collect, use, and protect your data — and how to exercise your rights.</p>}
      sections={sections}
    />
  );
}
