import { apiFetch } from './client';

// Mirror VrBook.Contracts.Dtos.UserDto.
// OPS.M.8 §3.10 (D10) — bumped with isPlatformAdmin so the web client
// can show/hide the Platform nav group without a second round trip.
export interface CurrentUser {
  readonly id: string;
  readonly email: string;
  readonly displayName: string;
  readonly phone: string | null;
  readonly isOwner: boolean;
  readonly isAdmin: boolean;
  readonly isPlatformAdmin: boolean;
  readonly emailVerified: boolean;
  readonly createdAt: string;
  readonly lastLoginAt: string | null;
}

export const getCurrentUser = (): Promise<CurrentUser> =>
  apiFetch<CurrentUser>('/api/v1/me');

// VRB-108 — editable guest profile. Mirrors UpdateProfileCommand(DisplayName, Phone).
export interface UpdateProfileBody {
  readonly displayName: string;
  readonly phone: string | null;
}

export const updateProfile = (body: UpdateProfileBody): Promise<CurrentUser> =>
  apiFetch<CurrentUser>('/api/v1/me', { method: 'PUT', body });
