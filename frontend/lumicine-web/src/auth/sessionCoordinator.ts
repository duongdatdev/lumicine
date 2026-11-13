import type { UserInfo } from '../api/types';

export type AuthSyncMessage =
  | { type: 'LOGOUT' }
  | { type: 'PASSWORD_CHANGED' }
  | { type: 'SESSION_REFRESHED'; accessToken: string; user: UserInfo };

type MessageCallback = (msg: AuthSyncMessage) => void;

class SessionCoordinator {
  private channel: BroadcastChannel | null = null;
  private listeners: Set<MessageCallback> = new Set();
  private inFlightRefreshPromise: Promise<{ accessToken: string; user: UserInfo } | null> | null = null;
  private lastRefreshedAt: number = 0;

  constructor() {
    if (typeof window !== 'undefined' && 'BroadcastChannel' in window) {
      try {
        this.channel = new BroadcastChannel('lumicine-auth-sync');
        this.channel.onmessage = (event: MessageEvent<AuthSyncMessage>) => {
          this.notifyListeners(event.data);
        };
      } catch {
        this.channel = null;
      }
    }
  }

  public subscribe(cb: MessageCallback): () => void {
    this.listeners.add(cb);
    return () => {
      this.listeners.delete(cb);
    };
  }

  public broadcast(message: AuthSyncMessage): void {
    if (this.channel) {
      try {
        this.channel.postMessage(message);
      } catch {
        // BroadcastChannel failed
      }
    }
  }

  private notifyListeners(message: AuthSyncMessage): void {
    this.listeners.forEach((cb) => {
      try {
        cb(message);
      } catch {
        // ignore listener error
      }
    });
  }

  /**
   * Coordinated refresh:
   * 1. Dedupes within the current tab using inFlightRefreshPromise.
   * 2. Coordinates across tabs using Web Locks ('navigator.locks').
   * 3. Broadcasts the refreshed token to other tabs to avoid redundant rotation.
   */
  public async executeCoordinatedRefresh(
    refreshFn: () => Promise<{ accessToken: string; user: UserInfo } | null>
  ): Promise<{ accessToken: string; user: UserInfo } | null> {
    if (this.inFlightRefreshPromise) {
      return this.inFlightRefreshPromise;
    }

    this.inFlightRefreshPromise = (async () => {
      // Use Web Locks if available to ensure only one tab executes refresh at a time
      if (typeof navigator !== 'undefined' && 'locks' in navigator && navigator.locks?.request) {
        return await navigator.locks.request('lumicine-refresh-token-lock', async () => {
          // Check if another tab just completed refresh within the last 1500ms
          const now = Date.now();
          if (now - this.lastRefreshedAt < 1500) {
            // Already refreshed by another tab
            return null;
          }

          const result = await refreshFn();
          if (result) {
            this.lastRefreshedAt = Date.now();
            this.broadcast({
              type: 'SESSION_REFRESHED',
              accessToken: result.accessToken,
              user: result.user,
            });
          }
          return result;
        });
      }

      // Fallback if Web Locks not supported
      const result = await refreshFn();
      if (result) {
        this.lastRefreshedAt = Date.now();
        this.broadcast({
          type: 'SESSION_REFRESHED',
          accessToken: result.accessToken,
          user: result.user,
        });
      }
      return result;
    })().finally(() => {
      this.inFlightRefreshPromise = null;
    });

    return this.inFlightRefreshPromise;
  }
}

export const sessionCoordinator = new SessionCoordinator();
