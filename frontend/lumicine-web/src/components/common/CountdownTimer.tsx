import React, { useEffect, useState, useCallback, useRef } from 'react';
import { Clock } from 'lucide-react';
import { cn } from '../../lib/utils';

export interface CountdownTimerProps {
  expiresAtUtc: string | Date;
  serverTimeUtc?: string | Date;
  onExpire?: () => void;
  className?: string;
}

export const CountdownTimer: React.FC<CountdownTimerProps> = ({
  expiresAtUtc,
  serverTimeUtc,
  onExpire,
  className,
}) => {
  // Store the fixed clock delta (ServerTime - ClientTimeWhenReceived)
  const serverOffsetRef = useRef<number>(
    serverTimeUtc ? new Date(serverTimeUtc).getTime() - Date.now() : 0
  );

  useEffect(() => {
    if (serverTimeUtc) {
      serverOffsetRef.current = new Date(serverTimeUtc).getTime() - Date.now();
    }
  }, [serverTimeUtc]);

  const computeTimeLeft = useCallback(() => {
    const target = new Date(expiresAtUtc).getTime();
    const now = Date.now();
    const currentAdjustedTime = now + serverOffsetRef.current;
    const remaining = Math.max(0, Math.floor((target - currentAdjustedTime) / 1000));
    return remaining;
  }, [expiresAtUtc]);

  const [secondsLeft, setSecondsLeft] = useState<number>(computeTimeLeft);

  useEffect(() => {
    // Immediate compute
    const initial = computeTimeLeft();
    setSecondsLeft(initial);
    if (initial <= 0 && onExpire) {
      onExpire();
    }

    const timer = setInterval(() => {
      const remaining = computeTimeLeft();
      setSecondsLeft(remaining);
      if (remaining <= 0) {
        clearInterval(timer);
        if (onExpire) {
          onExpire();
        }
      }
    }, 1000);

    // Resynchronize when tab returns to foreground
    const handleVisibilityChange = () => {
      if (document.visibilityState === 'visible') {
        const remaining = computeTimeLeft();
        setSecondsLeft(remaining);
        if (remaining <= 0 && onExpire) {
          onExpire();
        }
      }
    };

    document.addEventListener('visibilitychange', handleVisibilityChange);

    return () => {
      clearInterval(timer);
      document.removeEventListener('visibilitychange', handleVisibilityChange);
    };
  }, [computeTimeLeft, onExpire]);

  const minutes = Math.floor(secondsLeft / 60);
  const seconds = secondsLeft % 60;
  const isUrgent = secondsLeft > 0 && secondsLeft <= 120; // under 2 minutes
  const isExpired = secondsLeft <= 0;

  return (
    <div
      className={cn(
        'inline-flex items-center gap-1.5 px-3 py-1.5 rounded-lg text-sm font-semibold border transition-colors',
        isExpired
          ? 'bg-red-50 text-red-700 border-red-200'
          : isUrgent
          ? 'bg-amber-50 text-amber-800 border-amber-300 animate-pulse'
          : 'bg-primary-light/40 text-primary border-primary/20',
        className
      )}
      role="timer"
      aria-live="polite"
    >
      <Clock className="w-4 h-4 shrink-0" />
      <span>
        {isExpired ? (
          'Đã hết thời gian giữ'
        ) : (
          <>
            Thời gian giữ ghế:{' '}
            <span className="font-mono">
              {String(minutes).padStart(2, '0')}:{String(seconds).padStart(2, '0')}
            </span>
          </>
        )}
      </span>
    </div>
  );
};
