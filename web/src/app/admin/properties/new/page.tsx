'use client';

import Link from 'next/link';
import { useRouter } from 'next/navigation';
import { useEffect, useState } from 'react';
import { ArrowLeft, Save } from 'lucide-react';

import {
  createProperty,
  listAmenities,
  updateProperty,
  type Amenity,
  type CreatePropertyBody,
} from '@/lib/api/catalog';
import { ApiProblemError } from '@/lib/api/client';

const PROPERTY_TYPES = ['House', 'Apartment', 'Cabin', 'Cottage', 'Studio', 'Villa'] as const;

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
}

const emptyForm = (): FormState => ({
  title: '',
  description: '',
  type: 'House',
  street: '',
  city: '',
  state: '',
  postalCode: '',
  countryCode: 'US',
  latitude: '0',
  longitude: '0',
  maxGuests: 2,
  bedrooms: 1,
  bathrooms: 1,
  beds: 1,
  checkinFrom: '15:00',
  checkinTo: '22:00',
  checkoutBy: '11:00',
  houseRules: '',
  amenityIds: new Set<string>(),
});

const AdminPropertyCreatePage = () => {
  const router = useRouter();
  const [form, setForm] = useState<FormState>(emptyForm());
  const [amenities, setAmenities] = useState<readonly Amenity[]>([]);
  const [busy, setBusy] = useState(false);
  const [publishAfter, setPublishAfter] = useState(true);
  const [error, setError] = useState<string | null>(null);

  useEffect(() => {
    (async () => {
      try {
        setAmenities(await listAmenities());
      } catch {
        // amenities are optional; UI degrades gracefully
      }
    })();
  }, []);

  const toggleAmenity = (id: string) => {
    setForm((prev) => {
      const next = new Set(prev.amenityIds);
      if (next.has(id)) next.delete(id);
      else next.add(id);
      return { ...prev, amenityIds: next };
    });
  };

  const onSubmit = async (e: React.FormEvent) => {
    e.preventDefault();
    setBusy(true);
    setError(null);
    try {
      const body: CreatePropertyBody = {
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
        checkinFrom: form.checkinFrom,
        checkinTo: form.checkinTo,
        checkoutBy: form.checkoutBy,
        houseRules: form.houseRules
          .split('\n')
          .map((r) => r.trim())
          .filter((r) => r.length > 0),
        amenityIds: Array.from(form.amenityIds),
      };
      const created = await createProperty(body);

      if (publishAfter) {
        await updateProperty(created.id, {
          ...body,
          reviewsEnabled: created.reviewsEnabled,
          dynamicPricingEnabled: created.dynamicPricingEnabled,
          messagingEnabled: created.messagingEnabled,
          isActive: true,
        });
      }

      router.push(`/admin/properties/${created.id}`);
    } catch (err) {
      setError(
        err instanceof ApiProblemError
          ? err.problem.detail ?? err.message
          : err instanceof Error
            ? err.message
            : 'Failed to create property',
      );
      setBusy(false);
    }
  };

  const grouped = amenities.reduce<Record<string, Amenity[]>>((acc, a) => {
    (acc[a.category] ??= []).push(a);
    return acc;
  }, {});

  return (
    <div className="space-y-6">
      <div className="flex items-center gap-2 text-sm text-muted-foreground">
        <Link href="/admin/properties" className="inline-flex items-center gap-1 hover:text-foreground">
          <ArrowLeft className="h-3.5 w-3.5" aria-hidden /> Back to properties
        </Link>
      </div>
      <h1 className="text-2xl font-semibold tracking-tight">Add property</h1>

      <form className="space-y-8" onSubmit={onSubmit}>
        {error && (
          <div className="rounded-md border border-destructive/40 bg-destructive/5 p-3 text-sm text-destructive">
            {error}
          </div>
        )}

        <Section title="Basics">
          <Field label="Title" required>
            <input
              type="text"
              required
              maxLength={120}
              value={form.title}
              onChange={(e) => setForm({ ...form, title: e.target.value })}
              className={inputCls}
              placeholder="Beach Villa with Ocean View"
            />
          </Field>
          <Field label="Type" required>
            <select
              value={form.type}
              onChange={(e) => setForm({ ...form, type: e.target.value })}
              className={inputCls}
            >
              {PROPERTY_TYPES.map((t) => (
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
              placeholder="Describe the property — what makes it special?"
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
          <Field label="Country code (ISO-2)" required>
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
              placeholder={'No smoking\nNo pets\nQuiet hours after 10pm'}
            />
          </Field>
        </Section>

        <div className="flex items-center justify-between rounded-xl border border-border bg-muted/30 p-4">
          <label className="flex items-center gap-2 text-sm">
            <input
              type="checkbox"
              checked={publishAfter}
              onChange={(e) => setPublishAfter(e.target.checked)}
              className="h-4 w-4"
            />
            Publish immediately
            <span className="text-xs text-muted-foreground">
              (uncheck to save as a draft you can refine later)
            </span>
          </label>
          <button
            type="submit"
            disabled={busy}
            className="inline-flex items-center gap-2 rounded-md bg-brand-maroon-600 px-5 py-2.5 text-sm font-medium text-white hover:bg-brand-maroon-700 disabled:opacity-50"
          >
            <Save className="h-4 w-4" aria-hidden /> {busy ? 'Saving…' : publishAfter ? 'Save and publish' : 'Save draft'}
          </button>
        </div>
      </form>
    </div>
  );
};

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

export default AdminPropertyCreatePage;
