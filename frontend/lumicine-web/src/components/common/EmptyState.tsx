import React from 'react';
import { Sparkles } from 'lucide-react';
import { cn } from '../../lib/utils';

export interface EmptyStateProps {
  icon?: React.ReactNode;
  title: string;
  description?: string;
  action?: React.ReactNode;
  className?: string;
}

export const EmptyState: React.FC<EmptyStateProps> = ({
  icon,
  title,
  description,
  action,
  className,
}) => {
  return (
    <div
      className={cn(
        'flex flex-col items-center justify-center text-center p-8 rounded-2xl border border-dashed border-border bg-background-subtle/40',
        className
      )}
    >
      <div className="p-3 rounded-full bg-background-surface border border-border shadow-sm mb-3 text-primary">
        {icon || <Sparkles className="w-6 h-6" />}
      </div>
      <h3 className="font-heading font-bold text-lg text-foreground mb-1">{title}</h3>
      {description && <p className="text-sm text-foreground-muted max-w-sm mb-4">{description}</p>}
      {action && <div className="mt-2">{action}</div>}
    </div>
  );
};
