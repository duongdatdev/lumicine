import React from 'react';
import { Button } from './Button';
import { ChevronLeft, ChevronRight } from 'lucide-react';

export interface PaginationProps {
  currentPage: number;
  totalPages: number;
  onPageChange: (page: number) => void;
  className?: string;
}

export const Pagination: React.FC<PaginationProps> = ({
  currentPage,
  totalPages,
  onPageChange,
  className,
}) => {
  if (totalPages <= 1) return null;

  return (
    <div className={`flex items-center justify-center gap-2 mt-6 ${className || ''}`}>
      <Button
        variant="outline"
        size="sm"
        disabled={currentPage <= 1}
        onClick={() => onPageChange(currentPage - 1)}
        aria-label="Trang trước"
      >
        <ChevronLeft className="w-4 h-4 mr-1" /> Trước
      </Button>
      <span className="text-sm font-medium text-foreground px-3">
        Trang {currentPage} / {totalPages}
      </span>
      <Button
        variant="outline"
        size="sm"
        disabled={currentPage >= totalPages}
        onClick={() => onPageChange(currentPage + 1)}
        aria-label="Trang sau"
      >
        Sau <ChevronRight className="w-4 h-4 ml-1" />
      </Button>
    </div>
  );
};
