'use client';

import Link from 'next/link';
import { useEffect, useState } from 'react';
import { Plus, MapPin, Pencil, ExternalLink, Home } from 'lucide-react';

import { adminListMyProperties, type AdminPropertySummary } from '@/lib/api/catalog';
import { ApiProblemError } from '@/lib/api/client';

// Slice 1 — owner's property list. Empty-state CTA when no properties yet,
// otherwise a table with status badges and per-row actions.
const AdminPropertiesPage = () => {
  const [items, setItems] = useState<readonly AdminPropertySummary[]>([]);
  const [loading, setLoading] = useState(true);
  const [error, setError] = useState<string | null>(null);

  useEffect(() => {
    (async () => {
      try {
        const list = await adminListMyProperties();
        setItems(list);
      } catch (err) {
        setError(
          err instanceof ApiProblemError
            ? err.problem.detail ?? err.message
            : err instanceof Error
              ? err.message
              : 'Failed to load properties',
        );
      } finally {
        setLoading(false);
      }
    })();
  }, []);

  if (loading) {
    return (
      <div className="space-y-4">
        <h1 className="text-2xl font-semibold tracking-tight">Properties</h1>
        <p className="text-sm text-muted-foreground">Loading…</p>
      </div>
    );
  }

  if (error) {
    return (
      <div className="space-y-4">
        <h1 className="text-2xl font-semibold tracking-tight">Properties</h1>
        <div className="rounded-md border border-destructive/40 bg-destructive/5 p-4 text-sm text-destructive">
          {error}
        </div>
      </div>
    );
  }

  if (items.length === 0) {
    return (
      <div className="space-y-6">
        <div className="flex items-center justify-between">
          <h1 className="text-2xl font-semibold tracking-tight">Properties</h1>
        </div>
        <div className="rounded-xl border border-dashed border-border p-12 text-center">
          <Home className="mx-auto h-10 w-10 text-muted-foreground" aria-hidden />
          <h2 className="mt-4 text-lg font-medium">No properties yet</h2>
          <p className="mt-2 text-sm text-muted-foreground">
            Add your first property — guests will be able to book it as soon as you publish.
          </p>
          <Link
            href="/admin/properties/new"
            className="mt-6 inline-flex items-center gap-2 rounded-md bg-brand-maroon-600 px-4 py-2 text-sm font-medium text-white hover:bg-brand-maroon-700"
          >
            <Plus className="h-4 w-4" aria-hidden /> Add your first property
          </Link>
        </div>
      </div>
    );
  }

  return (
    <div className="space-y-6">
      <div className="flex items-center justify-between">
        <div>
          <h1 className="text-2xl font-semibold tracking-tight">Properties</h1>
          <p className="text-sm text-muted-foreground">
            {items.length} {items.length === 1 ? 'property' : 'properties'} —{' '}
            {items.filter((p) => p.isActive).length} published,{' '}
            {items.filter((p) => !p.isActive).length} draft
          </p>
        </div>
        <Link
          href="/admin/properties/new"
          className="inline-flex items-center gap-2 rounded-md bg-brand-maroon-600 px-4 py-2 text-sm font-medium text-white hover:bg-brand-maroon-700"
        >
          <Plus className="h-4 w-4" aria-hidden /> Add property
        </Link>
      </div>

      <div className="overflow-hidden rounded-xl border border-border">
        <table className="w-full text-sm">
          <thead className="border-b border-border bg-muted/30 text-left text-xs uppercase tracking-wider text-muted-foreground">
            <tr>
              <th className="px-4 py-3">Property</th>
              <th className="px-4 py-3">Location</th>
              <th className="px-4 py-3">Sleeps</th>
              <th className="px-4 py-3">Status</th>
              <th className="px-4 py-3 text-right">Actions</th>
            </tr>
          </thead>
          <tbody>
            {items.map((p) => (
              <tr key={p.id} className="border-b border-border last:border-b-0">
                <td className="px-4 py-3">
                  <div className="flex items-center gap-3">
                    {p.primaryImageUrl ? (
                      // eslint-disable-next-line @next/next/no-img-element
                      <img
                        src={p.primaryImageUrl}
                        alt=""
                        className="h-10 w-14 rounded object-cover"
                      />
                    ) : (
                      <div className="grid h-10 w-14 place-items-center rounded bg-muted text-muted-foreground">
                        <Home className="h-4 w-4" aria-hidden />
                      </div>
                    )}
                    <div>
                      <div className="font-medium">{p.title}</div>
                      <div className="text-xs text-muted-foreground">{p.type}</div>
                    </div>
                  </div>
                </td>
                <td className="px-4 py-3">
                  <span className="inline-flex items-center gap-1 text-muted-foreground">
                    <MapPin className="h-3.5 w-3.5" aria-hidden />
                    {p.city}, {p.country}
                  </span>
                </td>
                <td className="px-4 py-3">{p.maxGuests} guests · {p.bedrooms} br</td>
                <td className="px-4 py-3">
                  {p.isActive ? (
                    <span className="inline-flex items-center rounded-full bg-emerald-100 px-2.5 py-0.5 text-xs font-medium text-emerald-700 dark:bg-emerald-900/40 dark:text-emerald-300">
                      Published
                    </span>
                  ) : (
                    <span className="inline-flex items-center rounded-full bg-muted px-2.5 py-0.5 text-xs font-medium text-muted-foreground">
                      Draft
                    </span>
                  )}
                </td>
                <td className="px-4 py-3 text-right">
                  <div className="inline-flex items-center gap-1">
                    {p.isActive && (
                      <Link
                        href={`/properties/${p.slug}`}
                        target="_blank"
                        className="inline-flex items-center gap-1 rounded-md px-2 py-1 text-xs text-muted-foreground hover:bg-accent hover:text-foreground"
                      >
                        <ExternalLink className="h-3.5 w-3.5" aria-hidden /> View
                      </Link>
                    )}
                    <Link
                      href={`/admin/properties/${p.id}`}
                      className="inline-flex items-center gap-1 rounded-md px-2 py-1 text-xs hover:bg-accent"
                    >
                      <Pencil className="h-3.5 w-3.5" aria-hidden /> Edit
                    </Link>
                  </div>
                </td>
              </tr>
            ))}
          </tbody>
        </table>
      </div>
    </div>
  );
};

export default AdminPropertiesPage;
