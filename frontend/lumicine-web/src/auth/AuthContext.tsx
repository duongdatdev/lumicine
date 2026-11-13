import React, { createContext, useContext, useEffect, useState, useCallback } from 'react';
import type { UserInfo, LoginResponse } from '../api/types';
import {
  setAccessToken,
  refreshSession,
  setOnAuthExpired,
  setOnTokenRefreshed,
  apiClient,
} from '../api/client';
import { sessionCoordinator } from './sessionCoordinator';
import { clearPrivateCache } from '../lib/queryClient';

interface AuthContextType {
  user: UserInfo | null;
  isLoading: boolean;
  isAuthenticated: boolean;
  isAdmin: boolean;
  isStaff: boolean;
  login: (email: string, password: string) => Promise<UserInfo>;
  logout: () => Promise<void>;
  refresh: () => Promise<void>;
  updateUser: (updated: Partial<UserInfo>) => void;
}

const AuthContext = createContext<AuthContextType | undefined>(undefined);

export const AuthProvider: React.FC<{ children: React.ReactNode }> = ({ children }) => {
  const [user, setUser] = useState<UserInfo | null>(null);
  const [isLoading, setIsLoading] = useState(true);

  // Cross-tab message listener & client listeners
  useEffect(() => {
    setOnAuthExpired(() => {
      setUser(null);
      clearPrivateCache();
    });

    setOnTokenRefreshed((refreshedUser) => {
      setUser(refreshedUser);
    });

    const unsubscribe = sessionCoordinator.subscribe((msg) => {
      if (msg.type === 'LOGOUT' || msg.type === 'PASSWORD_CHANGED') {
        setAccessToken(null);
        setUser(null);
        clearPrivateCache();
      } else if (msg.type === 'SESSION_REFRESHED') {
        setAccessToken(msg.accessToken);
        setUser(msg.user);
      }
    });

    // Restore session on initial load
    let isMounted = true;
    refreshSession()
      .then((result) => {
        if (isMounted) {
          if (result) {
            setUser(result.user);
          } else {
            setUser(null);
          }
        }
      })
      .catch(() => {
        if (isMounted) setUser(null);
      })
      .finally(() => {
        if (isMounted) setIsLoading(false);
      });

    return () => {
      isMounted = false;
      unsubscribe();
    };
  }, []);

  const login = useCallback(async (email: string, password: string): Promise<UserInfo> => {
    const res = await apiClient<LoginResponse>('/api/auth/login', {
      method: 'POST',
      body: JSON.stringify({ email, password }),
      headers: {
        'Content-Type': 'application/json',
      },
    });

    setAccessToken(res.accessToken);
    setUser(res.user);
    sessionCoordinator.broadcast({
      type: 'SESSION_REFRESHED',
      accessToken: res.accessToken,
      user: res.user,
    });
    return res.user;
  }, []);

  const logout = useCallback(async (): Promise<void> => {
    try {
      await apiClient('/api/auth/logout', {
        method: 'POST',
      });
    } catch {
      // ignore
    } finally {
      setAccessToken(null);
      setUser(null);
      clearPrivateCache();
      sessionCoordinator.broadcast({ type: 'LOGOUT' });
    }
  }, []);

  const refresh = useCallback(async (): Promise<void> => {
    const result = await refreshSession();
    if (result) {
      setUser(result.user);
    } else {
      setUser(null);
    }
  }, []);

  const updateUser = useCallback((updated: Partial<UserInfo>) => {
    setUser((prev) => (prev ? { ...prev, ...updated } : null));
  }, []);

  const isAuthenticated = !!user;
  const roles = user?.roles || [];
  const isAdmin = roles.includes('Admin');
  const isStaff = roles.includes('Staff') || isAdmin;

  return (
    <AuthContext.Provider
      value={{
        user,
        isLoading,
        isAuthenticated,
        isAdmin,
        isStaff,
        login,
        logout,
        refresh,
        updateUser,
      }}
    >
      {children}
    </AuthContext.Provider>
  );
};

export function useAuth(): AuthContextType {
  const context = useContext(AuthContext);
  if (!context) {
    throw new Error('useAuth must be used within an AuthProvider');
  }
  return context;
}
