'use client';

import Link from 'next/link';
import { useParams, useRouter } from 'next/navigation';
import { useEffect, useState } from 'react';
import { ArrowLeft, ExternalLink, Save } from 'lucide-react';

import {
  adminGetPropertyById,
  listAmenities,
  updateProperty,
  type Amenity,
  type PropertyDetail,
  type UpdatePropertyBody,
} from '@/lib/api/catalog';
import { ApiProblemError } from '@/lib/api/client';
import { useAuthedQuery } from '@/hooks/useAuthedQuery';
import { SignInGate } from '@/components/auth/SignInGate';

interface FormState {
  title: string;
  description: string;
  type: string;
  street: string;
  city: string;
  state: string;
  postalCode: string;
  countryCode: string;
  latitude: string;
  longitude: string;
  maxGuests: number;
  bedrooms: number;
  bathrooms: number;
  beds: number;
  checkinFrom: string;
  checkinTo: string;
  checkoutBy: string;
  houseRules: string;
  amenityIds: Set<string>;
  reviewsEnabled: boolean;
  dynamicPricingEnabled: boolean;
  messagingEnabled: boolean;
  isActive: boolean;
}

const detailToForm = (p: PropertyDetail): FormState => ({
  title: p.title,
  description: p.description,
  type: p.type,
  street: p.address.street,
  city: p.address.city,
  state: p.address.state,
  postalCode: p.address.postalCode,
  countryCode: p.address.countryCode,
  latitude: String(p.address.latitude),
  longitude: String(p.address.longitude),
  maxGuests: p.maxGuests,
  bedrooms: p.bedrooms,
  bathrooms: p.bathrooms,
  beds: p.beds,
  checkinFrom: p.checkinFrom.slice(0, 5),
  checkinTo: p.checkinTo.slice(0, 5),
  checkoutBy: p.checkoutBy.slice(0, 5),
  houseRules: p.houseRules.join('\n'),
  amenityIds: new Set(p.amenities.map((a) => a.id)),
  reviewsEnabled: p.reviewsEnabled,
  dynamicPricingEnabled: p.dynamicPricingEnabled,
  messagingEnabled: p.messagingEnabled,
  isActive: p.isActive,
});

const AdminPropertyEditPage = () => {
  const router = useRouter();
  const { id } = useParams<{ id: string }>();

  // Slice OPS.M.10.2 F11.7.4.7b — fetches gated on useAuthedQuery.
  // The form state is initialized from `detail` via the useEffect
  // below; saving invalidates the query so the page rehydrates after
  // a successful PATCH.
  const detailQ = useAuthedQuery<PropertyDetail>({
    queryKey: ['admin', 'property', id],
    queryFn: () => adminGetPropertyById(id),
  });
  const amenitiesQ = useAuthedQuery<readonly Amenity[]>({
    queryKey: ['amenities', 'all'],
    queryFn: listAmenities,
  });
  const detail = detailQ.data ?? null;
  const amenities = amenitiesQ.data ?? [];
  const queryError = detailQ.isError
    ? detailQ.error instanceof ApiProblemError
      ? detailQ.error.problem.detail ?? detailQ.error.message
      : detailQ.error instanceof Error
        ? detailQ.error.message
        : 'Failed to load'
    : null;
  const loading = detailQ.isLoading || amenitiesQ.isLoading;

  const [form, setForm] = useState<FormState | null>(null);
  const [saving, setSaving] = useState(false);
  const [error, setError] = useState<string | null>(queryError);

  useEffect(() => {
    if (detail && !form) {
      setForm(detailToForm(detail));
    }
    if (queryError) setError(queryError);
  }, [detail, form, queryError]);

  if (detailQ.needsSignIn) {
    return <SignInGate title="Sign in to edit your property" />;
  }

  if (loading || !form || !detail) {
    return (
      <div className="space-y-4">
        <h1 className="text-2xl font-semibold tracking-tight">Property</h1>
        <p className="text-sm text-muted-foreground">{error ?? 'Loading…'}</p>
      </div>
    );
  }

  const toggleAmenity = (aid: string) => {
    setForm((prev) => {
      if (!prev) return prev;
      const next = new Set(prev.amenityIds);
      if (next.has(aid)) next.delete(aid);
      else next.add(aid);
      return { ...prev, amenityIds: next };
    });
  };

  const onSubmit = async (e: React.FormEvent) => {
    e.preventDefault();
    setSaving(true);
    setError(null);
    try {
      const body: UpdatePropertyBody = {
        title: form.title.trim(),
        description: form.description.trim(),
        type: form.type,
        address: {
          street: form.street.trim(),
          city: form.city.trim(),
          state: form.state.trim(),
          postalCode: form.postalCode.trim(),
          countryCode: form.countryCode.trim().toUpperCase(),
          latitude: Number(form.latitude),
          longitude: Number(form.longitude),
        },
        maxGuests: form.maxGuests,
        bedrooms: form.bedrooms,
        bathrooms: form.bathrooms,
        beds: form.beds,
        // HTML <input type="time"> returns "HH:mm" but .NET TimeOnly's
        // System.Text.Json converter wants "HH:mm:ss" — append ":00".
        checkinFrom: ensureSeconds(form.checkinFrom),
        checkinTo: ensureSeconds(form.checkinTo),
        checkoutBy: ensureSeconds(form.checkoutBy),
        houseRules: form.houseRules
          .split('\n')
          .map((r) => r.trim())
          .filter((r) => r.length > 0),
        amenityIds: Array.from(form.amenityIds),
        reviewsEnabled: form.reviewsEnabled,
        dynamicPricingEnabled: form.dynamicPricingEnabled,
        messagingEnabled: form.messagingEnabled,
        isActive: form.isActive,
      };
      await updateProperty(id, body);
      router.push('/admin/properties');
      return;
    } catch (err) {
      setError(
        err instanceof ApiProblemError
          ? err.problem.detail ?? err.message
          : err instanceof Error
            ? err.message
            : 'Failed to save',
      );
    } finally {
      setSaving(false);
    }
  };

  const grouped = amenities.reduce<Record<string, Amenity[]>>((acc, a) => {
    (acc[a.category] ??= []).push(a);
    return acc;
  }, {});

  return (
    <div className="space-y-6">
      <div className="flex items-center justify-between text-sm text-muted-foreground">
        <Link href="/admin/properties" className="inline-flex items-center gap-1 hover:text-foreground">
          <ArrowLeft className="h-3.5 w-3.5" aria-hidden /> Back to properties
        </Link>
        {detail.isActive && (
          <Link
            href={`/properties/${detail.slug}`}
            target="_blank"
            className="inline-flex items-center gap-1 hover:text-foreground"
          >
            View live <ExternalLink className="h-3.5 w-3.5" aria-hidden />
          </Link>
        )}
      </div>

      <div className="flex items-end justify-between gap-4">
        <div>
          <h1 className="text-2xl font-semibold tracking-tight">{detail.title}</h1>
          <p className="text-sm text-muted-foreground">
            {detail.isActive ? 'Published' : 'Draft'} · slug:{' '}
            <span className="font-mono">{detail.slug}</span>
          </p>
        </div>
      </div>

      <form className="space-y-8" onSubmit={onSubmit}>
        {error && (
          <div className="rounded-md border border-destructive/40 bg-destructive/5 p-3 text-sm text-destructive">
            {error}
          </div>
        )}

        <Section title="Status">
          <Toggle
            label="Published"
            description="Guests can find and book this property"
            value={form.isActive}
            onChange={(v) => setForm({ ...form, isActive: v })}
          />
          <Toggle
            label="Reviews"
            description="Allow guests to leave reviews after their stay"
            value={form.reviewsEnabled}
            onChange={(v) => setForm({ ...form, reviewsEnabled: v })}
          />
          <Toggle
            label="Dynamic pricing"
            description="Apply pricing rules (seasonal, last-minute, etc.)"
            value={form.dynamicPricingEnabled}
            onChange={(v) => setForm({ ...form, dynamicPricingEnabled: v })}
          />
          <Toggle
            label="Messaging"
            description="Let guests message you through the platform"
            value={form.messagingEnabled}
            onChange={(v) => setForm({ ...form, messagingEnabled: v })}
          />
        </Section>

        <Section title="Basics">
          <Field label="Title" required>
            <input
              type="text"
              required
              value={form.title}
              onChange={(e) => setForm({ ...form, title: e.target.value })}
              className={inputCls}
            />
          </Field>
          <Field label="Type" required>
            <select
              value={form.type}
              onChange={(e) => setForm({ ...form, type: e.target.value })}
              className={inputCls}
            >
              {['House', 'Apartment', 'Cabin', 'Cottage', 'Studio', 'Villa'].map((t) => (
                <option key={t} value={t}>
                  {t}
                </option>
              ))}
            </select>
          </Field>
          <Field label="Description" required full>
            <textarea
              required
              rows={4}
              value={form.description}
              onChange={(e) => setForm({ ...form, description: e.target.value })}
              className={inputCls}
            />
          </Field>
        </Section>

        <Section title="Capacity">
          <Field label="Max guests" required>
            <input
              type="number"
              min={1}
              max={20}
              required
              value={form.maxGuests}
              onChange={(e) => setForm({ ...form, maxGuests: Number(e.target.value) })}
              className={inputCls}
            />
          </Field>
          <Field label="Bedrooms" required>
            <input
              type="number"
              min={0}
              max={20}
              required
              value={form.bedrooms}
              onChange={(e) => setForm({ ...form, bedrooms: Number(e.target.value) })}
              className={inputCls}
            />
          </Field>
          <Field label="Beds" required>
            <input
              type="number"
              min={1}
              max={40}
              required
              value={form.beds}
              onChange={(e) => setForm({ ...form, beds: Number(e.target.value) })}
              className={inputCls}
            />
          </Field>
          <Field label="Bathrooms" required>
            <input
              type="number"
              min={1}
              max={20}
              required
              value={form.bathrooms}
              onChange={(e) => setForm({ ...form, bathrooms: Number(e.target.value) })}
              className={inputCls}
            />
          </Field>
        </Section>

        <Section title="Location">
          <Field label="Street" required full>
            <input
              type="text"
              required
              value={form.street}
              onChange={(e) => setForm({ ...form, street: e.target.value })}
              className={inputCls}
            />
          </Field>
          <Field label="City" required>
            <input
              type="text"
              required
              value={form.city}
              onChange={(e) => setForm({ ...form, city: e.target.value })}
              className={inputCls}
            />
          </Field>
          <Field label="State / Region" required>
            <input
              type="text"
              required
              value={form.state}
              onChange={(e) => setForm({ ...form, state: e.target.value })}
              className={inputCls}
            />
          </Field>
          <Field label="Postal code" required>
            <input
              type="text"
              required
              value={form.postalCode}
              onChange={(e) => setForm({ ...form, postalCode: e.target.value })}
              className={inputCls}
            />
          </Field>
          <Field label="Country code" required>
            <input
              type="text"
              required
              maxLength={2}
              value={form.countryCode}
              onChange={(e) => setForm({ ...form, countryCode: e.target.value.toUpperCase() })}
              className={inputCls}
            />
          </Field>
        </Section>

        <Section title="Check-in / Check-out">
          <Field label="Check-in from" required>
            <input
              type="time"
              required
              value={form.checkinFrom}
              onChange={(e) => setForm({ ...form, checkinFrom: e.target.value })}
              className={inputCls}
            />
          </Field>
          <Field label="Check-in until" required>
            <input
              type="time"
              required
              value={form.checkinTo}
              onChange={(e) => setForm({ ...form, checkinTo: e.target.value })}
              className={inputCls}
            />
          </Field>
          <Field label="Check-out by" required>
            <input
              type="time"
              required
              value={form.checkoutBy}
              onChange={(e) => setForm({ ...form, checkoutBy: e.target.value })}
              className={inputCls}
            />
          </Field>
        </Section>

        {amenities.length > 0 && (
          <Section title="Amenities">
            <div className="col-span-full space-y-4">
              {Object.entries(grouped)
                .sort(([a], [b]) => a.localeCompare(b))
                .map(([category, list]) => (
                  <div key={category}>
                    <div className="mb-2 text-xs font-medium uppercase tracking-wider text-muted-foreground">
                      {category}
                    </div>
                    <div className="flex flex-wrap gap-2">
                      {list.map((a) => {
                        const checked = form.amenityIds.has(a.id);
                        return (
                          <button
                            key={a.id}
                            type="button"
                            onClick={() => toggleAmenity(a.id)}
                            className={`rounded-full border px-3 py-1 text-xs ${
                              checked
                                ? 'border-brand-maroon-600 bg-brand-maroon-50 text-brand-maroon-700 dark:bg-brand-maroon-900/30 dark:text-brand-orange-200'
                                : 'border-border text-muted-foreground hover:border-foreground/30 hover:text-foreground'
                            }`}
                          >
                            {a.name}
                          </button>
                        );
                      })}
                    </div>
                  </div>
                ))}
            </div>
          </Section>
        )}

        <Section title="House rules">
          <Field label="One rule per line" full>
            <textarea
              rows={4}
              value={form.houseRules}
              onChange={(e) => setForm({ ...form, houseRules: e.target.value })}
              className={inputCls}
            />
          </Field>
        </Section>

        <div className="sticky bottom-0 -mx-6 -mb-6 flex items-center justify-end gap-2 border-t border-border bg-background px-6 py-3">
          <Link
            href="/admin/properties"
            className="rounded-md border border-input bg-background px-4 py-2 text-sm hover:bg-accent"
          >
            Cancel
          </Link>
          <button
            type="submit"
            disabled={saving}
            className="inline-flex items-center gap-2 rounded-md bg-brand-maroon-600 px-5 py-2.5 text-sm font-medium text-white hover:bg-brand-maroon-700 disabled:opacity-50"
          >
            <Save className="h-4 w-4" aria-hidden /> {saving ? 'Saving…' : 'Save changes'}
          </button>
        </div>
      </form>
    </div>
  );
};

const ensureSeconds = (hhmm: string): string =>
  /^\d{2}:\d{2}$/.test(hhmm) ? `${hhmm}:00` : hhmm;

const inputCls =
  'w-full rounded-md border border-input bg-background px-3 py-2 text-sm focus:border-brand-maroon-600 focus:outline-none focus:ring-1 focus:ring-brand-maroon-600';

const Section = ({ title, children }: { title: string; children: React.ReactNode }) => (
  <section className="rounded-xl border border-border bg-card p-6">
    <h2 className="mb-4 text-base font-medium">{title}</h2>
    <div className="grid gap-4 md:grid-cols-2">{children}</div>
  </section>
);

const Field = ({
  label,
  required = false,
  full = false,
  children,
}: {
  label: string;
  required?: boolean;
  full?: boolean;
  children: React.ReactNode;
}) => (
  <label className={`space-y-1.5 ${full ? 'md:col-span-2' : ''}`}>
    <span className="text-xs font-medium text-muted-foreground">
      {label} {required && <span className="text-destructive">*</span>}
    </span>
    {children}
  </label>
);

const Toggle = ({
  label,
  description,
  value,
  onChange,
}: {
  label: string;
  description: string;
  value: boolean;
  onChange: (v: boolean) => void;
}) => (
  <label className="flex items-start gap-3 rounded-lg border border-border p-3">
    <input
      type="checkbox"
      checked={value}
      onChange={(e) => onChange(e.target.checked)}
      className="mt-1 h-4 w-4"
    />
    <div className="flex-1">
      <div className="text-sm font-medium">{label}</div>
      <div className="text-xs text-muted-foreground">{description}</div>
    </div>
  </label>
);

export default AdminPropertyEditPage;
