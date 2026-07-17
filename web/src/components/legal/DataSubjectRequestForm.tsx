'use client';

import { useState } from 'react';

import { Button } from '@/components/ui';

const PRIVACY_EMAIL = 'privacy@vrbook.example.com'; // OWNER-PENDING: real inbox

type RequestType = 'access' | 'delete' | 'correct' | 'export';

/**
 * VRB-311 — GDPR/CCPA data-subject request (DSAR) intake. Launch posture (per
 * TL/architect): a form that composes an email to the privacy inbox — a
 * documented operator process, no automated deletion pipeline (deferred
 * post-launch). Zero backend: on submit it opens the visitor's mail client with
 * a prefilled request; the operator runbook (docs/runbooks/data_subject_requests.md)
 * covers fulfilment within the statutory window.
 */
export const DataSubjectRequestForm = () => {
  const [type, setType] = useState<RequestType>('access');
  const [email, setEmail] = useState('');
  const [details, setDetails] = useState('');

  const onSubmit = (e: React.FormEvent) => {
    e.preventDefault();
    const subject = `Data subject request: ${type}`;
    const body = [
      `Request type: ${type}`,
      `Account email: ${email}`,
      '',
      'Details:',
      details,
    ].join('\n');
    window.location.href = `mailto:${PRIVACY_EMAIL}?subject=${encodeURIComponent(subject)}&body=${encodeURIComponent(body)}`;
  };

  return (
    <form onSubmit={onSubmit} className="space-y-4 rounded-lg border border-border p-4">
      <div className="space-y-1.5">
        <label htmlFor="dsar-type" className="text-xs font-medium text-muted-foreground">
          Request type
        </label>
        <select
          id="dsar-type"
          value={type}
          onChange={(e) => setType(e.target.value as RequestType)}
          className="w-full rounded-md border border-input bg-background px-3 py-2 text-sm"
        >
          <option value="access">Access a copy of my data</option>
          <option value="export">Export my data (portability)</option>
          <option value="correct">Correct my data</option>
          <option value="delete">Delete my data</option>
        </select>
      </div>

      <div className="space-y-1.5">
        <label htmlFor="dsar-email" className="text-xs font-medium text-muted-foreground">
          Account email
        </label>
        <input
          id="dsar-email"
          type="email"
          required
          value={email}
          onChange={(e) => setEmail(e.target.value)}
          className="w-full rounded-md border border-input bg-background px-3 py-2 text-sm"
          placeholder="you@example.com"
        />
      </div>

      <div className="space-y-1.5">
        <label htmlFor="dsar-details" className="text-xs font-medium text-muted-foreground">
          Details (optional)
        </label>
        <textarea
          id="dsar-details"
          rows={3}
          value={details}
          onChange={(e) => setDetails(e.target.value)}
          className="w-full rounded-md border border-input bg-background px-3 py-2 text-sm"
          placeholder="Anything that helps us locate your data."
        />
      </div>

      <Button type="submit" variant="primary" size="sm">
        Submit request
      </Button>
      <p className="text-xs text-muted-foreground">
        This opens your email client to send the request to{' '}
        <span className="font-mono">{PRIVACY_EMAIL}</span>. We respond within the statutory window
        (30 days GDPR / 45 days CCPA).
      </p>
    </form>
  );
};
