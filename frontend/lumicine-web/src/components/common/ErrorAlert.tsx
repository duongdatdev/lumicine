import React from 'react';
import { Alert } from '../ui/Alert';
import { ApiError } from '../../api/client';

export interface ErrorAlertProps {
  error: unknown;
  className?: string;
  title?: string;
}

export const ErrorAlert: React.FC<ErrorAlertProps> = ({ error, className, title = 'Có lỗi xảy ra' }) => {
  if (!error) return null;

  let message = 'Đã xảy ra lỗi không xác định. Vui lòng thử lại sau.';
  let traceId: string | undefined;
  let validationErrors: string[] = [];

  if (error instanceof ApiError) {
    message = error.message;
    traceId = error.traceId;
    if (error.fieldErrors) {
      validationErrors = Object.entries(error.fieldErrors).flatMap(([field, msgs]) =>
        msgs.map((m) => `${field}: ${m}`)
      );
    }
  } else if (error instanceof Error) {
    message = error.message;
  } else if (typeof error === 'string') {
    message = error;
  }

  return (
    <Alert variant="error" title={title} className={className}>
      <div className="space-y-2">
        <p>{message}</p>
        {validationErrors.length > 0 && (
          <ul className="list-disc list-inside text-xs space-y-1 mt-2 text-red-700">
            {validationErrors.map((err, idx) => (
              <li key={idx}>{err}</li>
            ))}
          </ul>
        )}
        {traceId && (
          <div className="text-xs text-red-800/80 font-mono pt-1">
            Mã tra cứu (traceId): <span className="font-semibold">{traceId}</span>
          </div>
        )}
      </div>
    </Alert>
  );
};
