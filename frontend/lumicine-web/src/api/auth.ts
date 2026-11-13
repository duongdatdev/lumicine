import { apiClient } from './client';
import type { CurrentUserProfile } from './types';

export interface RegisterRequest {
  email: string;
  displayName: string;
  password: string;
}

export interface ConfirmEmailRequest {
  userId: string;
  code: string;
}

export interface ResetPasswordRequest {
  email: string;
  code: string;
  newPassword: string;
}

export interface ChangePasswordRequest {
  currentPassword: string;
  newPassword: string;
}

export const authApi = {
  register: (data: RegisterRequest) =>
    apiClient<{ userId: string }>('/api/auth/register', {
      method: 'POST',
      body: JSON.stringify(data),
      headers: { 'Content-Type': 'application/json' },
    }),

  confirmEmail: (data: ConfirmEmailRequest) =>
    apiClient<void>('/api/auth/confirm-email', {
      method: 'POST',
      body: JSON.stringify(data),
      headers: { 'Content-Type': 'application/json' },
    }),

  resendConfirmation: (email: string) =>
    apiClient<void>('/api/auth/resend-confirmation', {
      method: 'POST',
      body: JSON.stringify({ email }),
      headers: { 'Content-Type': 'application/json' },
    }),

  forgotPassword: (email: string) =>
    apiClient<void>('/api/auth/forgot-password', {
      method: 'POST',
      body: JSON.stringify({ email }),
      headers: { 'Content-Type': 'application/json' },
    }),

  resetPassword: (data: ResetPasswordRequest) =>
    apiClient<void>('/api/auth/reset-password', {
      method: 'POST',
      body: JSON.stringify(data),
      headers: { 'Content-Type': 'application/json' },
    }),

  getProfile: () => apiClient<CurrentUserProfile>('/api/users/me'),

  updateProfile: (displayName: string) =>
    apiClient<CurrentUserProfile>('/api/users/me', {
      method: 'PUT',
      body: JSON.stringify({ displayName }),
      headers: { 'Content-Type': 'application/json' },
    }),

  changePassword: (data: ChangePasswordRequest) =>
    apiClient<void>('/api/users/me/change-password', {
      method: 'POST',
      body: JSON.stringify(data),
      headers: { 'Content-Type': 'application/json' },
    }),
};
