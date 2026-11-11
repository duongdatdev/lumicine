import React from 'react';
import { Film } from 'lucide-react';
import { cn } from '../../lib/utils';

export interface PosterProps {
  url?: string | null;
  title: string;
  className?: string;
  aspectRatio?: 'poster' | 'video' | 'square';
}

export const MoviePoster: React.FC<PosterProps> = ({
  url,
  title,
  className,
  aspectRatio = 'poster',
}) => {
  const aspectClasses = {
    poster: 'aspect-[2/3]',
    video: 'aspect-video',
    square: 'aspect-square',
  };

  if (url) {
    return (
      <div
        className={cn(
          'relative overflow-hidden rounded-xl bg-background-subtle border border-border shadow-sm',
          aspectClasses[aspectRatio],
          className
        )}
      >
        <img
          src={url}
          alt={`Áp phích phim ${title}`}
          className="w-full h-full object-cover object-center transition-transform duration-300 hover:scale-105"
          loading="lazy"
        />
      </div>
    );
  }

  return (
    <div
      role="img"
      aria-label={`Khung thay thế áp phích cho phim ${title}`}
      className={cn(
        'relative flex flex-col items-center justify-center p-4 rounded-xl bg-gradient-to-b from-background-subtle to-border/40 border border-border text-foreground-muted select-none',
        aspectClasses[aspectRatio],
        className
      )}
    >
      <Film className="w-10 h-10 mb-2 opacity-40 text-primary" aria-hidden="true" />
      <span className="text-xs font-semibold text-center line-clamp-3 px-2 text-foreground">
        {title}
      </span>
      <span className="text-[10px] text-foreground-subtle mt-1">LumiCine Cinema</span>
    </div>
  );
};
