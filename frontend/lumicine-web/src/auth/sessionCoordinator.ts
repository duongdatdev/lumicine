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
  private latestSession: { accessToken: string; user: UserInfo; at: number } | null = null;
  private sessionGeneration = 0;

  constructor() {
    if (typeof window !== 'undefined' && 'BroadcastChannel' in window) {
      try {
        this.channel = new BroadcastChannel('lumicine-auth-sync');
        this.channel.onmessage = (event: MessageEvent<AuthSyncMessage>) => {
          this.receive(event.data);
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
    this.receive(message, false);
    if (this.channel) {
      try {
        this.channel.postMessage(message);
      } catch {
        // BroadcastChannel failed
      }
    }
  }

  private receive(message: AuthSyncMessage, notify = true): void {
    if (message.type === 'SESSION_REFRESHED') {
      if (!this.latestSession || this.latestSession.user.id !== message.user.id) this.sessionGeneration++;
      this.latestSession = { accessToken: message.accessToken, user: message.user, at: Date.now() };
    } else {
      this.latestSession = null;
      this.sessionGeneration++;
    }
    if (notify) this.notifyListeners(message);
  }

  private getRecentSession(): { accessToken: string; user: UserInfo } | null {
    if (!this.latestSession || Date.now() - this.latestSession.at > 3000) return null;
    return { accessToken: this.latestSession.accessToken, user: this.latestSession.user };
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
          const recent = this.getRecentSession();
          if (recent) return recent;

          // Let a preceding lock holder's BroadcastChannel result arrive before rotating again.
          await new Promise((resolve) => setTimeout(resolve, 100));
          const afterWait = this.getRecentSession();
          if (afterWait) return afterWait;

          const generation = this.sessionGeneration;
          const result = await refreshFn();
          if (generation !== this.sessionGeneration) return this.getRecentSession();
          if (result) {
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
      const recent = this.getRecentSession();
      if (recent) return recent;
      const generation = this.sessionGeneration;
      const result = await refreshFn();
      if (generation !== this.sessionGeneration) return this.getRecentSession();
      if (result) {
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
