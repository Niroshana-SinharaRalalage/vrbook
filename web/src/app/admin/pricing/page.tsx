'use client';

import { useEffect, useMemo, useState } from 'react';
import {
  DndContext,
  type DragEndEvent,
  PointerSensor,
  closestCenter,
  useSensor,
  useSensors,
} from '@dnd-kit/core';
import { SortableContext, arrayMove, verticalListSortingStrategy } from '@dnd-kit/sortable';
import { Plus, RefreshCw } from 'lucide-react';
import { adminListMyProperties, type AdminPropertySummary } from '@/lib/api/catalog';
import {
  createPricingRule,
  deletePricingRule,
  getPricingPlan,
  reorderPricingRules,
  setPricingRuleEnabled,
  updatePricingRule,
  type CreatePricingRuleRequest,
  type PricingPlan,
  type PricingRule,
} from '@/lib/api/pricing';
import { ApiProblemError } from '@/lib/api/client';
import { formatCurrency } from '@/lib/utils/currency';
import { useAuthedQuery } from '@/hooks/useAuthedQuery';
import { SignInGate } from '@/components/auth/SignInGate';
import RuleEditorModal from '@/components/pricing/RuleEditorModal';
import SortableRuleRow from '@/components/pricing/SortableRuleRow';
import QuotePreviewPane from '@/components/pricing/QuotePreviewPane';

const extractErr = (e: unknown, fallback: string): string => {
  if (e instanceof ApiProblemError) return e.problem.detail ?? e.message;
  if (e instanceof Error) return e.message;
  return fallback;
};

const AdminPricingPage = () => {
  // Slice OPS.M.10.2 F11.7.4.7b — property list on useAuthedQuery; per-
  // property plan fetch stays in its own useEffect (fires on selection
  // change, after MSAL is already initialized).
  const propertiesQ = useAuthedQuery<readonly AdminPropertySummary[]>({
    queryKey: ['admin', 'properties', 'mine'],
    queryFn: adminListMyProperties,
  });
  const properties = propertiesQ.data ?? [];
  const [selectedPropertyId, setSelectedPropertyId] = useState<string | null>(null);
  const [plan, setPlan] = useState<PricingPlan | null>(null);
  const [rules, setRules] = useState<readonly PricingRule[]>([]);
  const [loading, setLoading] = useState(true);
  const [error, setError] = useState<string | null>(null);
  const [busyId, setBusyId] = useState<string | null>(null);
  const [editing, setEditing] = useState<PricingRule | null>(null);
  const [creating, setCreating] = useState(false);

  const sensors = useSensors(useSensor(PointerSensor, { activationConstraint: { distance: 4 } }));

  // Default the selected property to the first as soon as the list lands.
  useEffect(() => {
    if (!selectedPropertyId && properties.length > 0) {
      const first = properties[0];
      if (first) setSelectedPropertyId(first.id);
    }
  }, [properties, selectedPropertyId]);

  useEffect(() => {
    if (propertiesQ.isError) {
      setError(extractErr(propertiesQ.error, 'Failed to load properties.'));
    }
    if (!propertiesQ.isLoading) setLoading(false);
  }, [propertiesQ.isError, propertiesQ.error, propertiesQ.isLoading]);

  const reloadPlan = async (propertyId: string) => {
    setError(null);
    try {
      const p = await getPricingPlan(propertyId);
      setPlan(p);
      setRules([...p.rules].sort((a, b) => a.priority - b.priority));
    } catch (e) {
      if (e instanceof ApiProblemError && e.problem.status === 404) {
        setPlan(null);
        setRules([]);
        setError('No pricing plan exists for this property yet. Set the base rate via the proposal’s plan editor first.');
      } else {
        setError(extractErr(e, 'Failed to load pricing plan.'));
      }
    }
  };

  useEffect(() => {
    if (selectedPropertyId) {
      void reloadPlan(selectedPropertyId);
    }
  }, [selectedPropertyId]);

  const onDragEnd = async (event: DragEndEvent) => {
    const { active, over } = event;
    if (!over || active.id === over.id || !selectedPropertyId) return;

    const oldIndex = rules.findIndex((r) => r.id === active.id);
    const newIndex = rules.findIndex((r) => r.id === over.id);
    if (oldIndex < 0 || newIndex < 0) return;

    const reordered = arrayMove([...rules], oldIndex, newIndex);
    setRules(reordered); // optimistic
    try {
      const refreshed = await reorderPricingRules(selectedPropertyId, reordered.map((r) => r.id));
      setPlan(refreshed);
      setRules([...refreshed.rules].sort((a, b) => a.priority - b.priority));
    } catch (e) {
      setError(extractErr(e, 'Reorder failed.'));
      setRules(rules); // revert
    }
  };

  const onToggleEnabled = async (rule: PricingRule, isEnabled: boolean) => {
    if (!selectedPropertyId) return;
    setBusyId(rule.id);
    try {
      const updated = await setPricingRuleEnabled(selectedPropertyId, rule.id, isEnabled);
      setRules((prev) => prev.map((r) => (r.id === rule.id ? updated : r)));
    } catch (e) {
      setError(extractErr(e, 'Toggle failed.'));
    } finally {
      setBusyId(null);
    }
  };

  const onDelete = async (rule: PricingRule) => {
    if (!selectedPropertyId) return;
    if (!window.confirm(`Delete this ${rule.kind} rule?`)) return;
    setBusyId(rule.id);
    try {
      await deletePricingRule(selectedPropertyId, rule.id);
      await reloadPlan(selectedPropertyId);
    } catch (e) {
      setError(extractErr(e, 'Delete failed.'));
    } finally {
      setBusyId(null);
    }
  };

  const onSaveNew = async (body: CreatePricingRuleRequest) => {
    if (!selectedPropertyId) return;
    // Append at end: priority becomes max(existing)+1 so we don't disturb the order.
    const nextPriority = rules.length === 0 ? 0 : Math.max(...rules.map((r) => r.priority)) + 1;
    await createPricingRule(selectedPropertyId, { ...body, priority: nextPriority });
    setCreating(false);
    await reloadPlan(selectedPropertyId);
  };

  const onSaveEdit = async (body: CreatePricingRuleRequest) => {
    if (!selectedPropertyId || !editing) return;
    await updatePricingRule(selectedPropertyId, editing.id, body);
    setEditing(null);
    await reloadPlan(selectedPropertyId);
  };

  const currency = useMemo(() => plan?.currency ?? 'USD', [plan?.currency]);

  if (loading) {
    return <p className="text-sm text-muted-foreground">Loading…</p>;
  }

  if (propertiesQ.needsSignIn) {
    return <SignInGate title="Sign in to manage pricing" />;
  }

  return (
    <div className="space-y-6">
      <header className="space-y-2">
        <h1 className="text-2xl font-semibold tracking-tight">Pricing</h1>
        <p className="text-sm text-muted-foreground">
          Owners set base + weekend rates and a stack of rules that fire in
          priority order (lower number = applied first). Drag rows to reorder.
          Toggle a rule off to stage a draft without affecting live quotes.
        </p>
      </header>

      {error && (
        <div className="rounded-md border border-destructive/30 bg-destructive/5 p-3 text-sm text-destructive">
          {error}
        </div>
      )}

      <div className="flex flex-wrap items-end gap-3">
        <label className="block text-sm">
          <span className="text-muted-foreground">Property</span>
          <select
            value={selectedPropertyId ?? ''}
            onChange={(e) => setSelectedPropertyId(e.target.value || null)}
            className="mt-1 w-64 rounded-md border border-border bg-background px-3 py-2 text-sm"
          >
            {properties.map((p) => (
              <option key={p.id} value={p.id}>
                {p.title}
              </option>
            ))}
          </select>
        </label>
        <button
          type="button"
          onClick={() => selectedPropertyId && void reloadPlan(selectedPropertyId)}
          className="inline-flex items-center gap-1 rounded-md border border-border px-3 py-2 text-sm hover:bg-accent"
        >
          <RefreshCw className="h-4 w-4" /> Reload
        </button>
      </div>

      <div className="grid gap-4 lg:grid-cols-3">
        <div className="space-y-4 lg:col-span-2">
          {plan && (
            <section className="rounded-xl border border-border bg-card p-4">
              <h2 className="text-sm font-medium">Plan basics</h2>
              <dl className="mt-2 grid grid-cols-2 gap-x-6 gap-y-1 text-sm sm:grid-cols-4">
                <div>
                  <dt className="text-xs text-muted-foreground">Base / night</dt>
                  <dd className="tabular-nums">{formatCurrency(plan.baseNightlyRate, plan.currency)}</dd>
                </div>
                <div>
                  <dt className="text-xs text-muted-foreground">Weekend</dt>
                  <dd className="tabular-nums">{formatCurrency(plan.weekendRate, plan.currency)}</dd>
                </div>
                <div>
                  <dt className="text-xs text-muted-foreground">Currency</dt>
                  <dd>{plan.currency}</dd>
                </div>
                <div>
                  <dt className="text-xs text-muted-foreground">Min / max stay</dt>
                  <dd>
                    {plan.minStayNights} / {plan.maxStayNights}
                  </dd>
                </div>
              </dl>
            </section>
          )}

          <section className="rounded-xl border border-border bg-card">
            <div className="flex items-center justify-between border-b border-border p-3">
              <h2 className="text-sm font-medium">Rules</h2>
              <button
                type="button"
                onClick={() => setCreating(true)}
                disabled={!plan}
                className="inline-flex items-center gap-1 rounded-md bg-brand-maroon-700 px-3 py-1.5 text-xs text-white hover:bg-brand-maroon-800 disabled:opacity-50"
              >
                <Plus className="h-3 w-3" /> Add rule
              </button>
            </div>

            {rules.length === 0 ? (
              <p className="px-4 py-6 text-sm text-muted-foreground">
                No rules yet. Add one to apply a seasonal uplift, last-minute discount, or
                length-of-stay tier.
              </p>
            ) : (
              <div className="overflow-x-auto">
                <table className="w-full text-left">
                  <thead className="bg-muted/40 text-xs text-muted-foreground">
                    <tr>
                      <th className="px-2 py-2 w-8" aria-label="Drag handle" />
                      <th className="px-2 py-2 w-12">Pri</th>
                      <th className="px-2 py-2">Kind</th>
                      <th className="px-2 py-2">Window</th>
                      <th className="px-2 py-2">Adjustment</th>
                      <th className="px-2 py-2">Enabled</th>
                      <th className="px-2 py-2 text-right">Actions</th>
                    </tr>
                  </thead>
                  <DndContext sensors={sensors} collisionDetection={closestCenter} onDragEnd={onDragEnd}>
                    <SortableContext items={rules.map((r) => r.id)} strategy={verticalListSortingStrategy}>
                      <tbody>
                        {rules.map((rule) => (
                          <SortableRuleRow
                            key={rule.id}
                            rule={rule}
                            currency={currency}
                            busy={busyId === rule.id}
                            onToggleEnabled={(isEnabled) => void onToggleEnabled(rule, isEnabled)}
                            onEdit={() => setEditing(rule)}
                            onDelete={() => void onDelete(rule)}
                          />
                        ))}
                      </tbody>
                    </SortableContext>
                  </DndContext>
                </table>
              </div>
            )}
          </section>
        </div>

        {selectedPropertyId && <QuotePreviewPane propertyId={selectedPropertyId} />}
      </div>

      {creating && (
        <RuleEditorModal
          rule={null}
          currency={currency}
          onCancel={() => setCreating(false)}
          onSave={onSaveNew}
        />
      )}
      {editing && (
        <RuleEditorModal
          rule={editing}
          currency={currency}
          onCancel={() => setEditing(null)}
          onSave={onSaveEdit}
        />
      )}
    </div>
  );
};

export default AdminPricingPage;
