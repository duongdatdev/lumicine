import React from 'react';
import { Spinner } from '../ui/Spinner';
import { cn } from '../../lib/utils';

export interface LoadingStateProps {
  message?: string;
  className?: string;
}

export const LoadingState: React.FC<LoadingStateProps> = ({
  message = 'Đang tải dữ liệu...',
  className,
}) => {
  return (
    <div
      className={cn(
        'flex flex-col items-center justify-center p-12 space-y-3 text-foreground-muted',
        className
      )}
    >
      <Spinner className="w-8 h-8 text-primary" />
      <p className="text-sm font-medium">{message}</p>
    </div>
  );
};
