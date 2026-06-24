import { apiFetch } from './client';

// Mirror VrBook.Contracts.Dtos.UserDto.
export interface CurrentUser {
  readonly id: string;
  readonly email: string;
  readonly displayName: string;
  readonly phone: string | null;
  readonly isOwner: boolean;
  readonly isAdmin: boolean;
  readonly emailVerified: boolean;
  readonly createdAt: string;
  readonly lastLoginAt: string | null;
}

export const getCurrentUser = (): Promise<CurrentUser> =>
  apiFetch<CurrentUser>('/api/v1/me');
