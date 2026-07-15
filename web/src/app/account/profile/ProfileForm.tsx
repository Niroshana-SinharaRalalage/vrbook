'use client';

import { useEffect, useState } from 'react';
import { useForm } from 'react-hook-form';

import { useMe } from '@/hooks/useMe';
import { useUpdateProfile } from '@/hooks/useUpdateProfile';
import { useAuthedQuery } from '@/hooks/useAuthedQuery';
import { useAuth } from '@/lib/auth/useAuth';
import { getMyLoyalty, type LoyaltyAccount } from '@/lib/api/loyalty';
import { ApiProblemError } from '@/lib/api/client';
import { SignInGate } from '@/components/auth/SignInGate';
import { Badge, Button, Field, Input, Skeleton } from '@/components/ui';

interface FormValues {
  displayName: string;
  phone: string;
}

const ProfileSkeleton = () => (
  <div className="mx-auto max-w-xl space-y-6">
    <Skeleton className="h-8 w-40" />
    <div className="space-y-5">
      {[0, 1, 2].map((i) => (
        <div key={i} className="space-y-2">
          <Skeleton className="h-4 w-24" />
          <Skeleton className="h-10 w-full" />
        </div>
      ))}
    </div>
  </div>
);

export const ProfileForm = () => {
  const { isAuthenticated } = useAuth();
  const { data: me, isLoading } = useMe();
  // Authed read must go through useAuthedQuery (arch rule); a guest with no
  // loyalty account yet resolves to data === null (403/404), so the badge
  // simply doesn't render.
  const loyalty = useAuthedQuery<LoyaltyAccount>({
    queryKey: ['me', 'loyalty'],
    queryFn: getMyLoyalty,
  });
  const mutation = useUpdateProfile();
  const [saved, setSaved] = useState(false);

  const form = useForm<FormValues>({
    defaultValues: { displayName: me?.displayName ?? '', phone: me?.phone ?? '' },
  });
  const {
    register,
    handleSubmit,
    reset,
    formState: { errors, isDirty },
  } = form;

  // Populate (and re-baseline) the form when the profile loads / refreshes.
  // `me` is a stable react-query reference between fetches, so this only re-runs
  // when the cached profile actually changes (e.g. after a save invalidation).
  useEffect(() => {
    if (me) reset({ displayName: me.displayName, phone: me.phone ?? '' });
  }, [me, reset]);

  if (!isAuthenticated) {
    return (
      <SignInGate
        title="Sign in to manage your profile"
        description="Your name and contact details for bookings live here."
      />
    );
  }
  if (isLoading || !me) return <ProfileSkeleton />;

  const onSubmit = handleSubmit(async (values) => {
    const body = {
      displayName: values.displayName.trim(),
      phone: values.phone.trim() ? values.phone.trim() : null,
    };
    setSaved(false);
    try {
      await mutation.mutateAsync(body);
      reset({ displayName: body.displayName, phone: body.phone ?? '' });
      setSaved(true);
    } catch {
      // Surfaced below via mutation.error (form-level).
    }
  });

  const formError = mutation.isError
    ? mutation.error instanceof ApiProblemError
      ? (mutation.error.problem.detail ?? mutation.error.problem.title ?? mutation.error.message)
      : 'Could not save your profile. Please try again.'
    : null;

  return (
    <div className="mx-auto max-w-xl space-y-6">
      <header className="space-y-1">
        <h1 className="text-2xl font-semibold tracking-tight">Profile</h1>
        <p className="text-sm text-muted-foreground">
          Your name and contact details for bookings.
        </p>
      </header>

      <form onSubmit={onSubmit} noValidate className="space-y-5">
        <Field label="Display name" error={errors.displayName?.message} required>
          <Input
            autoComplete="name"
            disabled={mutation.isPending}
            {...register('displayName', {
              validate: (v) => v.trim().length > 0 || 'Enter your name.',
            })}
          />
        </Field>

        <Field
          label="Email"
          description="Managed by your sign-in provider — change it there."
        >
          <Input type="email" value={me.email} readOnly aria-readonly="true" />
        </Field>

        <Field label="Phone">
          <Input
            type="tel"
            autoComplete="tel"
            placeholder="Optional"
            disabled={mutation.isPending}
            {...register('phone')}
          />
        </Field>

        {loyalty.data?.tier && (
          <div className="flex items-center gap-2 text-sm">
            <span className="text-muted-foreground">Loyalty tier</span>
            <Badge variant="secondary">{loyalty.data.tier}</Badge>
          </div>
        )}

        {formError && (
          <p role="alert" className="text-sm font-medium text-destructive">
            {formError}
          </p>
        )}

        <div className="flex items-center gap-3">
          <Button type="submit" loading={mutation.isPending} disabled={!isDirty || mutation.isPending}>
            Save changes
          </Button>
          <span role="status" aria-live="polite" className="text-sm font-medium text-success">
            {saved && !isDirty ? 'Saved' : ''}
          </span>
        </div>
      </form>
    </div>
  );
};
