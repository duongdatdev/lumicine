import { describe, it, expect, vi, beforeEach, afterEach } from 'vitest';
import { render, screen, act } from '@testing-library/react';
import React from 'react';
import { CountdownTimer } from '../components/common/CountdownTimer';

describe('Booking & Countdown Component Tests', () => {
  beforeEach(() => {
    vi.useFakeTimers();
  });

  afterEach(() => {
    vi.useRealTimers();
  });

  it('displays accurate countdown and triggers onExpire when deadline passes', () => {
    const onExpire = vi.fn();
    const serverTime = new Date('2026-11-17T12:00:00Z');
    const expiresAt = new Date('2026-11-17T12:05:00Z'); // 5 minutes remaining

    // Freeze Date.now to match serverTime
    vi.setSystemTime(serverTime);

    render(
      <CountdownTimer
        expiresAtUtc={expiresAt.toISOString()}
        serverTimeUtc={serverTime.toISOString()}
        onExpire={onExpire}
      />
    );

    expect(screen.getByRole('timer')).toHaveTextContent('05:00');

    // Advance 60 seconds
    act(() => {
      vi.advanceTimersByTime(60000);
    });

    expect(screen.getByRole('timer')).toHaveTextContent('04:00');

    // Advance 240 seconds (reaches 0)
    act(() => {
      vi.advanceTimersByTime(240000);
    });

    expect(screen.getByRole('timer')).toHaveTextContent('Đã hết thời gian giữ');
    expect(onExpire).toHaveBeenCalled();
  });

  it('resynchronizes timer when tab visibility changes back to visible', () => {
    const serverTime = new Date('2026-11-17T12:00:00Z');
    const expiresAt = new Date('2026-11-17T12:10:00Z');

    vi.setSystemTime(serverTime);

    render(
      <CountdownTimer
        expiresAtUtc={expiresAt.toISOString()}
        serverTimeUtc={serverTime.toISOString()}
      />
    );

    expect(screen.getByRole('timer')).toHaveTextContent('10:00');

    // Fast-forward system time while timer was paused in background
    vi.setSystemTime(new Date('2026-11-17T12:08:00Z'));

    // Trigger visibilitychange event
    act(() => {
      Object.defineProperty(document, 'visibilityState', { value: 'visible', configurable: true });
      document.dispatchEvent(new Event('visibilitychange'));
    });

    expect(screen.getByRole('timer')).toHaveTextContent('02:00');
  });
});
