import React, { forwardRef } from 'react';
import { cn } from '../../lib/utils';
import { Spinner } from './Spinner';

export interface ButtonProps extends React.ButtonHTMLAttributes<HTMLButtonElement> {
  variant?: 'primary' | 'secondary' | 'outline' | 'ghost' | 'danger' | 'accent';
  size?: 'sm' | 'md' | 'lg';
  isLoading?: boolean;
}

export const Button = forwardRef<HTMLButtonElement, ButtonProps>(
  ({ className, variant = 'primary', size = 'md', isLoading = false, disabled, children, ...props }, ref) => {
    const baseStyles =
      'inline-flex items-center justify-center font-medium transition-colors focus-visible:outline-none focus-visible:ring-2 focus-visible:ring-primary focus-visible:ring-offset-2 disabled:opacity-50 disabled:pointer-events-none rounded-lg select-none';

    const variants = {
      primary: 'bg-primary text-white hover:bg-primary-hover shadow-sm',
      secondary: 'bg-background-subtle text-foreground hover:bg-border border border-border',
      outline: 'border border-border text-foreground hover:bg-background-subtle',
      ghost: 'text-foreground hover:bg-background-subtle',
      danger: 'bg-red-600 text-white hover:bg-red-700 shadow-sm',
      accent: 'bg-accent text-white hover:bg-accent-hover shadow-sm',
    };

    const sizes = {
      sm: 'h-8 px-3 text-xs gap-1.5',
      md: 'h-10 px-4 text-sm gap-2',
      lg: 'h-12 px-6 text-base gap-2.5',
    };

    return (
      <button
        ref={ref}
        disabled={disabled || isLoading}
        className={cn(baseStyles, variants[variant], sizes[size], className)}
        {...props}
      >
        {isLoading && <Spinner className="w-4 h-4 text-current" />}
        {children}
      </button>
    );
  }
);

Button.displayName = 'Button';
