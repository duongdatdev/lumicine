import React from 'react';
import { cn } from '../../lib/utils';

export interface CardProps extends React.HTMLAttributes<HTMLDivElement> {
  hover?: boolean;
}

export const Card: React.FC<CardProps> = ({ className, hover, children, ...props }) => {
  return (
    <div
      className={cn(
        'rounded-xl border border-border bg-background-surface p-5 shadow-sm transition-all',
        hover && 'hover:shadow-md hover:border-primary/40',
        className
      )}
      {...props}
    >
      {children}
    </div>
  );
};
