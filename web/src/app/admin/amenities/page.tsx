'use client';

import { useMemo, useState } from 'react';
import { useQueryClient } from '@tanstack/react-query';
import { Pencil, Plus, Trash2, X } from 'lucide-react';
import {
  adminCreateAmenity,
  adminDeleteAmenity,
  adminDisableAmenity,
  adminEnableAmenity,
  adminListAmenities,
  adminUpdateAmenity,
  type Amenity,
} from '@/lib/api/catalog';
import { ApiProblemError } from '@/lib/api/client';
import { useAuthedQuery } from '@/hooks/useAuthedQuery';
import { SignInGate } from '@/components/auth/SignInGate';

// Client component — admin CRUD page for the amenity catalog (A2.2).
// Slice OPS.M.10.2 F11.7.4.7b — list fetch on useAuthedQuery; mutations
// invalidate the query.
const QK = ['admin', 'amenities'] as const;

const AdminAmenitiesPage = () => {
  const qc = useQueryClient();
  const { data, isLoading, isError, error: queryError, needsSignIn } = useAuthedQuery<readonly Amenity[]>({
    queryKey: [...QK],
    queryFn: adminListAmenities,
  });
  const amenities = useMemo(() => {
    const list = data ?? [];
    return [...list].sort((a, b) => {
      const c = a.category.localeCompare(b.category);
      return c !== 0 ? c : a.name.localeCompare(b.name);
    });
  }, [data]);
  const loading = isLoading;
  const [error, setError] = useState<string | null>(null);
  const queryErrorMsg = isError
    ? queryError instanceof ApiProblemError
      ? queryError.problem.detail ?? queryError.message
      : queryError instanceof Error
        ? queryError.message
        : 'Failed to load'
    : null;
  const displayError = error ?? queryErrorMsg;
  const reload = () => {
    setError(null);
    return qc.invalidateQueries({ queryKey: [...QK] });
  };
  const [editingId, setEditingId] = useState<string | null>(null);
  const [showCreate, setShowCreate] = useState(false);
  const [filter, setFilter] = useState('');

  const filtered = useMemo(() => {
    if (!filter) return amenities;
    const f = filter.toLowerCase();
    return amenities.filter(
      (a) =>
        a.code.toLowerCase().includes(f) ||
        a.name.toLowerCase().includes(f) ||
        a.category.toLowerCase().includes(f),
    );
  }, [amenities, filter]);

  const grouped = useMemo(() => {
    const map = new Map<string, Amenity[]>();
    for (const a of filtered) {
      const list = map.get(a.category) ?? [];
      list.push(a);
      map.set(a.category, list);
    }
    return Array.from(map.entries()).sort((a, b) => a[0].localeCompare(b[0]));
  }, [filtered]);

  const onToggle = async (a: Amenity) => {
    try {
      if (a.isActive) await adminDisableAmenity(a.id);
      else await adminEnableAmenity(a.id);
      await reload();
    } catch (err) {
      setError(err instanceof ApiProblemError ? err.problem.detail ?? err.message : err instanceof Error ? err.message : 'Toggle failed');
    }
  };

  const onDelete = async (a: Amenity) => {
    // Per A2.2 the backend refuses with 409 if any property still uses this amenity —
    // surface that message in the same banner the rest of the page uses.
    if (!window.confirm(`Delete "${a.name}"? This cannot be undone.\n\nIf any property still uses this amenity the delete will be refused — disable it first.`)) {
      return;
    }
    try {
      await adminDeleteAmenity(a.id);
      await reload();
    } catch (err) {
      setError(err instanceof ApiProblemError ? err.problem.detail ?? err.message : err instanceof Error ? err.message : 'Delete failed');
    }
  };

  if (needsSignIn) {
    return <SignInGate title="Sign in to manage amenities" />;
  }

  return (
    <div className="space-y-6">
      <header className="flex items-end justify-between">
        <div>
          <h1 className="text-2xl font-semibold tracking-tight">Amenities</h1>
          <p className="mt-1 text-sm text-muted-foreground">
            {amenities.length} total · {amenities.filter((a) => a.isActive).length} active. Disabled
            amenities are hidden from owners and search but stay attached to existing properties.
          </p>
        </div>
        <button
          type="button"
          onClick={() => setShowCreate(true)}
          className="inline-flex items-center gap-1 rounded-md bg-brand-maroon-700 px-3 py-2 text-sm font-medium text-white hover:bg-brand-maroon-800"
        >
          <Plus className="h-4 w-4" />
          Add amenity
        </button>
      </header>

      <input
        type="search"
        value={filter}
        onChange={(e) => setFilter(e.target.value)}
        placeholder="Filter by name, code, category…"
        className="w-full max-w-md rounded-md border border-border bg-background px-3 py-2 text-sm"
      />

      {displayError && (
        <div className="rounded-md border border-destructive/30 bg-destructive/5 p-3 text-sm text-destructive">
          {displayError}
        </div>
      )}

      {showCreate && (
        <CreateAmenityForm
          onClose={() => setShowCreate(false)}
          onCreated={() => {
            setShowCreate(false);
            void reload();
          }}
        />
      )}

      {loading ? (
        <div className="text-sm text-muted-foreground">Loading…</div>
      ) : (
        <div className="space-y-6">
          {grouped.map(([category, list]) => (
            <section key={category}>
              <h2 className="mb-2 text-sm font-medium text-muted-foreground">
                {category} ({list.length})
              </h2>
              <ul className="divide-y divide-border rounded-md border border-border">
                {list.map((a) => (
                  <li key={a.id} className="flex items-center gap-3 px-3 py-2 text-sm">
                    {editingId === a.id ? (
                      <EditAmenityRow
                        amenity={a}
                        onCancel={() => setEditingId(null)}
                        onSaved={() => {
                          void reload();
                          setEditingId(null);
                        }}
                      />
                    ) : (
                      <>
                        <div className="flex-1">
                          <div className="flex items-center gap-2">
                            <span className={a.isActive ? 'font-medium' : 'font-medium text-muted-foreground line-through'}>
                              {a.name}
                            </span>
                            <span className="text-xs text-muted-foreground">{a.code}</span>
                            {a.icon && (
                              <span className="text-xs text-muted-foreground/70">· {a.icon}</span>
                            )}
                          </div>
                        </div>
                        <button
                          type="button"
                          onClick={() => setEditingId(a.id)}
                          className="inline-flex items-center gap-1 rounded-md border border-border px-2.5 py-1 text-xs text-muted-foreground hover:bg-accent hover:text-foreground"
                          title="Edit"
                        >
                          <Pencil className="h-3.5 w-3.5" />
                          Edit
                        </button>
                        <button
                          type="button"
                          onClick={() => void onToggle(a)}
                          className={
                            a.isActive
                              ? 'inline-flex items-center rounded-md border border-amber-300 bg-amber-50 px-2.5 py-1 text-xs font-medium text-amber-800 hover:bg-amber-100 dark:border-amber-700 dark:bg-amber-950 dark:text-amber-200'
                              : 'inline-flex items-center rounded-md border border-emerald-300 bg-emerald-50 px-2.5 py-1 text-xs font-medium text-emerald-800 hover:bg-emerald-100 dark:border-emerald-700 dark:bg-emerald-950 dark:text-emerald-200'
                          }
                        >
                          {a.isActive ? 'Disable' : 'Enable'}
                        </button>
                        <button
                          type="button"
                          onClick={() => void onDelete(a)}
                          className="inline-flex items-center gap-1 rounded-md border border-destructive/40 px-2.5 py-1 text-xs font-medium text-destructive hover:bg-destructive/10"
                          title="Delete"
                        >
                          <Trash2 className="h-3.5 w-3.5" />
                          Delete
                        </button>
                      </>
                    )}
                  </li>
                ))}
              </ul>
            </section>
          ))}
        </div>
      )}
    </div>
  );
};

// ---- Inline editor ------------------------------------------------------

interface EditProps {
  readonly amenity: Amenity;
  readonly onCancel: () => void;
  readonly onSaved: (a: Amenity) => void;
}

const EditAmenityRow = ({ amenity, onCancel, onSaved }: EditProps) => {
  const [name, setName] = useState(amenity.name);
  const [icon, setIcon] = useState(amenity.icon ?? '');
  const [category, setCategory] = useState(amenity.category);
  const [busy, setBusy] = useState(false);
  const [error, setError] = useState<string | null>(null);

  const onSubmit = async (e: React.FormEvent) => {
    e.preventDefault();
    setBusy(true);
    setError(null);
    try {
      const updated = await adminUpdateAmenity(amenity.id, { name, icon: icon || null, category });
      onSaved(updated);
    } catch (err) {
      setError(err instanceof ApiProblemError ? err.problem.detail ?? err.message : err instanceof Error ? err.message : 'Save failed');
    } finally {
      setBusy(false);
    }
  };

  return (
    <form onSubmit={onSubmit} className="flex flex-1 items-center gap-2">
      <input value={name} onChange={(e) => setName(e.target.value)} className="flex-1 rounded-md border border-border bg-background px-2 py-1 text-sm" placeholder="Name" required />
      <input value={icon} onChange={(e) => setIcon(e.target.value)} className="w-32 rounded-md border border-border bg-background px-2 py-1 text-sm" placeholder="icon" />
      <input value={category} onChange={(e) => setCategory(e.target.value)} className="w-40 rounded-md border border-border bg-background px-2 py-1 text-sm" placeholder="Category" required />
      <button type="submit" disabled={busy} className="rounded-md bg-brand-maroon-700 px-2 py-1 text-xs text-white hover:bg-brand-maroon-800 disabled:opacity-50">
        {busy ? '…' : 'Save'}
      </button>
      <button type="button" onClick={onCancel} className="rounded-md p-1 text-muted-foreground hover:bg-accent">
        <X className="h-4 w-4" />
      </button>
      {error && <span className="text-xs text-destructive">{error}</span>}
    </form>
  );
};

// ---- Create form --------------------------------------------------------

interface CreateProps {
  readonly onClose: () => void;
  readonly onCreated: (a: Amenity) => void;
}

const CreateAmenityForm = ({ onClose, onCreated }: CreateProps) => {
  const [code, setCode] = useState('');
  const [name, setName] = useState('');
  const [icon, setIcon] = useState('');
  const [category, setCategory] = useState('');
  const [busy, setBusy] = useState(false);
  const [error, setError] = useState<string | null>(null);

  const onSubmit = async (e: React.FormEvent) => {
    e.preventDefault();
    setBusy(true);
    setError(null);
    try {
      const a = await adminCreateAmenity({ code, name, icon: icon || null, category });
      onCreated(a);
    } catch (err) {
      setError(err instanceof ApiProblemError ? err.problem.detail ?? err.message : err instanceof Error ? err.message : 'Create failed');
    } finally {
      setBusy(false);
    }
  };

  return (
    <form
      onSubmit={onSubmit}
      className="space-y-3 rounded-md border border-border bg-card p-4 shadow-sm"
    >
      <div className="flex items-center justify-between">
        <h2 className="text-sm font-medium">Add amenity</h2>
        <button type="button" onClick={onClose} className="rounded-md p-1 text-muted-foreground hover:bg-accent">
          <X className="h-4 w-4" />
        </button>
      </div>
      <div className="grid grid-cols-2 gap-3">
        <label className="block text-xs">
          <span className="block text-muted-foreground">Code (lowercase, immutable)</span>
          <input value={code} onChange={(e) => setCode(e.target.value)} className="mt-1 w-full rounded-md border border-border bg-background px-2 py-1 text-sm" required placeholder="wifi" />
        </label>
        <label className="block text-xs">
          <span className="block text-muted-foreground">Name</span>
          <input value={name} onChange={(e) => setName(e.target.value)} className="mt-1 w-full rounded-md border border-border bg-background px-2 py-1 text-sm" required placeholder="Wi-Fi" />
        </label>
        <label className="block text-xs">
          <span className="block text-muted-foreground">Icon</span>
          <input value={icon} onChange={(e) => setIcon(e.target.value)} className="mt-1 w-full rounded-md border border-border bg-background px-2 py-1 text-sm" placeholder="wifi" />
        </label>
        <label className="block text-xs">
          <span className="block text-muted-foreground">Category</span>
          <input value={category} onChange={(e) => setCategory(e.target.value)} className="mt-1 w-full rounded-md border border-border bg-background px-2 py-1 text-sm" required placeholder="Essentials" />
        </label>
      </div>
      {error && <div className="rounded-md border border-destructive/30 bg-destructive/5 p-2 text-xs text-destructive">{error}</div>}
      <div className="flex justify-end gap-2">
        <button type="button" onClick={onClose} className="rounded-md border border-border px-3 py-1.5 text-sm hover:bg-accent">
          Cancel
        </button>
        <button type="submit" disabled={busy} className="rounded-md bg-brand-maroon-700 px-3 py-1.5 text-sm text-white hover:bg-brand-maroon-800 disabled:opacity-50">
          {busy ? 'Saving…' : 'Create amenity'}
        </button>
      </div>
    </form>
  );
};

export default AdminAmenitiesPage;
