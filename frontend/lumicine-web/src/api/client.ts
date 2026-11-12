import type { ProblemDetails, LoginResponse, UserInfo } from './types';
import { sessionCoordinator } from '../auth/sessionCoordinator';

export class ApiError extends Error {
  public status: number;
  public problem: ProblemDetails;
  public traceId?: string;
  public fieldErrors: Record<string, string[]>;

  constructor(status: number, problem: ProblemDetails) {
    const message = problem.detail || problem.title || `Request failed with status ${status}`;
    super(message);
    this.name = 'ApiError';
    this.status = status;
    this.problem = problem;
    this.traceId = (problem.traceId as string) || ((problem as Record<string, any>)?.extensions?.traceId as string);
    this.fieldErrors = problem.errors || {};
  }

  public getFieldError(field: string): string | undefined {
    const direct = this.fieldErrors[field];
    if (direct && direct.length > 0) return direct[0];
    const lower = Object.keys(this.fieldErrors).find(
      (k) => k.toLowerCase() === field.toLowerCase()
    );
    if (lower && this.fieldErrors[lower]?.length > 0) {
      return this.fieldErrors[lower][0];
    }
    return undefined;
  }
}

let inMemoryAccessToken: string | null = null;
let cachedCsrfToken: string | null = null;
let onAuthExpiredCallback: (() => void) | null = null;
let onTokenRefreshedCallback: ((user: UserInfo) => void) | null = null;

export function setAccessToken(token: string | null) {
  inMemoryAccessToken = token;
}

export function getAccessToken(): string | null {
  return inMemoryAccessToken;
}

export function setOnAuthExpired(callback: () => void) {
  onAuthExpiredCallback = callback;
}

export function setOnTokenRefreshed(callback: (user: UserInfo) => void) {
  onTokenRefreshedCallback = callback;
}

/**
 * Fetch CSRF token from backend
 */
export async function fetchCsrfToken(): Promise<string> {
  const response = await fetch('/api/auth/csrf', {
    method: 'GET',
    credentials: 'include',
    headers: {
      Accept: 'application/json',
    },
  });

  if (!response.ok) {
    throw new Error('Failed to retrieve CSRF token.');
  }

  const data = await response.json();
  cachedCsrfToken = data.requestToken;
  return data.requestToken;
}

export async function getCsrfToken(): Promise<string> {
  if (cachedCsrfToken) return cachedCsrfToken;
  return await fetchCsrfToken();
}

export function clearCsrfToken() {
  cachedCsrfToken = null;
}

/**
 * Coordinated session refresh
 */
export async function refreshSession(): Promise<{ accessToken: string; user: UserInfo } | null> {
  return sessionCoordinator.executeCoordinatedRefresh(async () => {
    try {
      const csrf = await getCsrfToken();
      const response = await fetch('/api/auth/refresh', {
        method: 'POST',
        credentials: 'include',
        headers: {
          'X-CSRF-TOKEN': csrf,
          Accept: 'application/json',
        },
      });

      if (!response.ok) {
        setAccessToken(null);
        return null;
      }

      const data: LoginResponse = await response.json();
      setAccessToken(data.accessToken);
      if (onTokenRefreshedCallback) {
        onTokenRefreshedCallback(data.user);
      }
      return { accessToken: data.accessToken, user: data.user };
    } catch {
      setAccessToken(null);
      return null;
    }
  });
}

export interface RequestOptions extends RequestInit {
  skipAuth?: boolean;
  skipCsrf?: boolean;
  isRetry?: boolean;
  isCsrfRetry?: boolean;
}

/**
 * Central API client
 */
export async function apiClient<T>(url: string, options: RequestOptions = {}): Promise<T> {
  const { skipAuth, skipCsrf, isRetry, isCsrfRetry, headers: customHeaders, ...restOptions } = options;
  const headers = new Headers(customHeaders || {});

  const isAuthRoute = url.startsWith('/api/auth/') && url !== '/api/auth/csrf';

  // Inject CSRF token for /api/auth/ endpoints
  if (isAuthRoute && !skipCsrf) {
    try {
      const csrf = await getCsrfToken();
      headers.set('X-CSRF-TOKEN', csrf);
    } catch {
      // ignore
    }
  }

  // Inject Bearer token for business endpoints
  if (!skipAuth && inMemoryAccessToken && !isAuthRoute) {
    headers.set('Authorization', `Bearer ${inMemoryAccessToken}`);
  }

  if (!headers.has('Accept')) {
    headers.set('Accept', 'application/json, application/problem+json');
  }

  const response = await fetch(url, {
    ...restOptions,
    headers,
    credentials: 'include',
  });

  // Handle 401 Unauthorized with token refresh & retry
  if (response.status === 401 && !isRetry && !isAuthRoute) {
    const refreshed = await refreshSession();
    if (refreshed?.accessToken) {
      return apiClient<T>(url, {
        ...options,
        isRetry: true,
      });
    } else {
      if (onAuthExpiredCallback) {
        onAuthExpiredCallback();
      }
    }
  }

  // Handle 400 Invalid CSRF token retry
  if (response.status === 400 && isAuthRoute && !isCsrfRetry) {
    let problem: ProblemDetails | null = null;
    try {
      const clone = response.clone();
      problem = await clone.json();
    } catch {
      // not json
    }

    if (problem?.title === 'Invalid CSRF token') {
      clearCsrfToken();
      await fetchCsrfToken();
      return apiClient<T>(url, {
        ...options,
        isCsrfRetry: true,
      });
    }
  }

  // Handle HTTP errors
  if (!response.ok) {
    let problem: ProblemDetails = {
      status: response.status,
      title: response.statusText,
    };
    try {
      const parsed = await response.json();
      problem = { ...problem, ...parsed };
    } catch {
      try {
        const text = await response.text();
        if (text) problem.detail = text;
      } catch {
        // ignore
      }
    }
    throw new ApiError(response.status, problem);
  }

  // Handle 204 No Content
  if (response.status === 204) {
    return null as T;
  }

  // Parse JSON
  const contentType = response.headers.get('content-type') || '';
  if (contentType.includes('application/json')) {
    return (await response.json()) as T;
  }

  return (await response.text()) as unknown as T;
}

/**
 * Download file as Blob (e.g. Ticket QR or Admin CSV Export)
 */
export async function apiDownload(url: string, filename?: string): Promise<Blob> {
  const headers = new Headers();
  if (inMemoryAccessToken) {
    headers.set('Authorization', `Bearer ${inMemoryAccessToken}`);
  }

  const response = await fetch(url, {
    method: 'GET',
    headers,
    credentials: 'include',
  });

  if (!response.ok) {
    let problem: ProblemDetails = { status: response.status, title: response.statusText };
    try {
      problem = await response.json();
    } catch {
      // ignore
    }
    throw new ApiError(response.status, problem);
  }

  const blob = await response.blob();

  if (filename && typeof window !== 'undefined') {
    const objectUrl = window.URL.createObjectURL(blob);
    const link = document.createElement('a');
    link.href = objectUrl;
    link.download = filename;
    document.body.appendChild(link);
    link.click();
    document.body.removeChild(link);
    window.URL.revokeObjectURL(objectUrl);
  }

  return blob;
}

/**
 * Multipart file upload (e.g. Movie Poster)
 */
export async function apiUpload<T>(url: string, formData: FormData): Promise<T> {
  const headers = new Headers();
  if (inMemoryAccessToken) {
    headers.set('Authorization', `Bearer ${inMemoryAccessToken}`);
  }

  const response = await fetch(url, {
    method: 'POST',
    headers,
    body: formData,
    credentials: 'include',
  });

  if (!response.ok) {
    let problem: ProblemDetails = { status: response.status, title: response.statusText };
    try {
      problem = await response.json();
    } catch {
      // ignore
    }
    throw new ApiError(response.status, problem);
  }

  return (await response.json()) as T;
}
