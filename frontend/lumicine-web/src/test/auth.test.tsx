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
});
