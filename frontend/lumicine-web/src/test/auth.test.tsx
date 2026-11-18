import { describe, it, expect, vi, beforeEach, afterEach } from 'vitest';
import { render, screen, act } from '@testing-library/react';
import React from 'react';
import { AuthProvider, useAuth } from '../auth/AuthContext';
import { sessionCoordinator } from '../auth/sessionCoordinator';
import * as client from '../api/client';

describe('Auth Session Coordination', () => {
  beforeEach(() => {
    vi.restoreAllMocks();
  });

  it('restores session on page mount by calling refreshSession', async () => {
    const mockUser = {
      id: 'user-1',
      email: 'test@lumicine.vn',
      displayName: 'Nguyễn Văn A',
      roles: ['Customer'],
    };

    vi.spyOn(client, 'refreshSession').mockResolvedValueOnce({
      accessToken: 'test-token',
      user: mockUser,
    });

    const TestComponent = () => {
      const { user, isLoading, isAuthenticated } = useAuth();
      if (isLoading) return <div>Loading...</div>;
      return (
        <div>
          <span>{isAuthenticated ? 'Authenticated' : 'Not Authenticated'}</span>
          <span>User: {user?.displayName}</span>
        </div>
      );
    };

    render(
      <AuthProvider>
        <TestComponent />
      </AuthProvider>
    );

    expect(screen.getByText('Loading...')).toBeInTheDocument();

    await act(async () => {
      await Promise.resolve();
    });

    expect(screen.getByText('Authenticated')).toBeInTheDocument();
    expect(screen.getByText('User: Nguyễn Văn A')).toBeInTheDocument();
  });

  it('synchronizes logout across tabs via BroadcastChannel message', async () => {
    const mockUser = {
      id: 'user-1',
      email: 'test@lumicine.vn',
      displayName: 'Nguyễn Văn A',
      roles: ['Customer'],
    };

    vi.spyOn(client, 'refreshSession').mockResolvedValueOnce({
      accessToken: 'test-token',
      user: mockUser,
    });

    const TestComponent = () => {
      const { user, isAuthenticated } = useAuth();
      return <div>{isAuthenticated ? `Logged in: ${user?.displayName}` : 'Logged out'}</div>;
    };

    render(
      <AuthProvider>
        <TestComponent />
      </AuthProvider>
    );

    await act(async () => {
      await Promise.resolve();
    });

    expect(screen.getByText('Logged in: Nguyễn Văn A')).toBeInTheDocument();

    // Simulate BroadcastChannel receiving a LOGOUT message from another tab
    act(() => {
      // @ts-expect-error accessing private method for test simulation
      sessionCoordinator.notifyListeners({ type: 'LOGOUT' });
    });

    expect(screen.getByText('Logged out')).toBeInTheDocument();
  });

  it('deduplicates simultaneous token refreshes within a tab', async () => {
    let callCount = 0;
    const mockRefresh = vi.fn().mockImplementation(async () => {
      callCount++;
      await new Promise((resolve) => setTimeout(resolve, 50));
      return { accessToken: 'new-token', user: { id: 'u1', email: 'e', displayName: 'd', roles: [] } };
    });

    // Fire 3 simultaneous refresh calls
    const [res1, res2, res3] = await Promise.all([
      sessionCoordinator.executeCoordinatedRefresh(mockRefresh),
      sessionCoordinator.executeCoordinatedRefresh(mockRefresh),
      sessionCoordinator.executeCoordinatedRefresh(mockRefresh),
    ]);

    expect(callCount).toBe(1);
    expect(res1?.accessToken).toBe('new-token');
    expect(res2?.accessToken).toBe('new-token');
    expect(res3?.accessToken).toBe('new-token');
  });

  it('replays a recent successful refresh after acquiring the cross-tab lock', async () => {
    sessionCoordinator.broadcast({ type: 'LOGOUT' });
    const lockRequest = vi.fn(async (_name: string, callback: () => Promise<unknown>) => callback());
    Object.defineProperty(navigator, 'locks', { configurable: true, value: { request: lockRequest } });
    const result = { accessToken: 'other-tab-token', user: { id: 'u2', email: 'e', displayName: 'd', roles: [] } };
    const refresh = vi.fn(async () => null);
    setTimeout(() => sessionCoordinator.broadcast({ type: 'SESSION_REFRESHED', ...result }), 10);

    const refreshed = await sessionCoordinator.executeCoordinatedRefresh(refresh);
    expect(refreshed).toEqual(result);
    expect(refresh).not.toHaveBeenCalled();
    expect(lockRequest).toHaveBeenCalledOnce();
    sessionCoordinator.broadcast({ type: 'LOGOUT' });
  });

  it('discards an in-flight refresh when logout arrives before it completes', async () => {
    sessionCoordinator.broadcast({ type: 'LOGOUT' });
    let finishRefresh!: (value: { accessToken: string; user: { id: string; email: string; displayName: string; roles: string[] } }) => void;
    const refresh = vi.fn(() => new Promise<{ accessToken: string; user: { id: string; email: string; displayName: string; roles: string[] } }>((resolve) => { finishRefresh = resolve; }));
    const pending = sessionCoordinator.executeCoordinatedRefresh(refresh);
    await new Promise((resolve) => setTimeout(resolve, 120));
    sessionCoordinator.broadcast({ type: 'LOGOUT' });
    finishRefresh({ accessToken: 'stale-token', user: { id: 'u1', email: 'e', displayName: 'd', roles: [] } });
    expect(await pending).toBeNull();
    sessionCoordinator.broadcast({ type: 'LOGOUT' });
  });

  it('retries multipart uploads once after refresh with the same form data', async () => {
    sessionCoordinator.broadcast({ type: 'LOGOUT' });
    client.clearCsrfToken();
    client.setAccessToken('old-token');
    const form = new FormData();
    form.append('file', new Blob(['poster']), 'poster.png');
    const requests: RequestInit[] = [];
    vi.spyOn(globalThis, 'fetch').mockImplementation(async (input, init) => {
      if (String(input) === '/api/auth/csrf') return Response.json({ requestToken: 'csrf' });
      if (String(input) === '/api/auth/refresh') return Response.json({ accessToken: 'retry-token', user: { id: 'u1', email: 'e', displayName: 'd', roles: [] }, tokenType: 'Bearer', expiresIn: 300 });
      requests.push(init ?? {});
      return requests.length === 1
        ? Response.json({ title: 'Unauthorized' }, { status: 401 })
        : Response.json({ posterUrl: '/poster.png' });
    });

    await expect(client.apiUpload('/api/admin/movies/1/poster', form)).resolves.toEqual({ posterUrl: '/poster.png' });
    expect(requests).toHaveLength(2);
    expect(requests[0].body).toBe(form);
    expect(requests[1].body).toBe(form);
    expect(new Headers(requests[1].headers).get('Authorization')).toBe('Bearer retry-token');
  });

  it('shares concurrent initial CSRF token requests', async () => {
    client.clearCsrfToken();
    let resolveFetch!: (response: Response) => void;
    const fetch = vi.spyOn(globalThis, 'fetch').mockImplementation(() => new Promise((resolve) => { resolveFetch = resolve; }));
    const first = client.fetchCsrfToken();
    const second = client.fetchCsrfToken();
    expect(fetch).toHaveBeenCalledOnce();
    resolveFetch(Response.json({ requestToken: 'one-csrf-token' }));
    await expect(Promise.all([first, second])).resolves.toEqual(['one-csrf-token', 'one-csrf-token']);
  });

  it('retries downloads once with the refreshed bearer token', async () => {
    sessionCoordinator.broadcast({ type: 'LOGOUT' });
    client.clearCsrfToken();
    client.setAccessToken('old-token');
    let downloads = 0;
    vi.spyOn(globalThis, 'fetch').mockImplementation(async (input) => {
      if (String(input) === '/api/auth/csrf') return Response.json({ requestToken: 'csrf' });
      if (String(input) === '/api/auth/refresh') return Response.json({ accessToken: 'download-token', user: { id: 'u1', email: 'e', displayName: 'd', roles: [] }, tokenType: 'Bearer', expiresIn: 300 });
      downloads++;
      return downloads === 1 ? Response.json({ title: 'Unauthorized' }, { status: 401 }) : new Response('csv-data');
    });

    const blob = await client.apiDownload('/api/admin/reports/export');
    expect(blob.size).toBe(8);
    expect(downloads).toBe(2);
  });

  it('retries JSON requests once with the unchanged payload', async () => {
    sessionCoordinator.broadcast({ type: 'LOGOUT' });
    client.clearCsrfToken();
    client.setAccessToken('old-token');
    const bodies: unknown[] = [];
    let attempts = 0;
    vi.spyOn(globalThis, 'fetch').mockImplementation(async (input, init) => {
      if (String(input) === '/api/auth/csrf') return Response.json({ requestToken: 'csrf' });
      if (String(input) === '/api/auth/refresh') return Response.json({ accessToken: 'json-token', user: { id: 'u1', email: 'e', displayName: 'd', roles: [] }, tokenType: 'Bearer', expiresIn: 300 });
      bodies.push(init?.body);
      attempts++;
      return attempts === 1 ? Response.json({ title: 'Unauthorized' }, { status: 401 }) : Response.json({ ok: true });
    });

    const payload = JSON.stringify({ movieId: 'movie-1' });
    await expect(client.apiClient('/api/business', { method: 'POST', body: payload })).resolves.toEqual({ ok: true });
    expect(attempts).toBe(2);
    expect(bodies).toEqual([payload, payload]);
  });
});
