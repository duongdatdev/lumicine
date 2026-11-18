import { ApiError, apiClient } from './client';
import type { CheckInRequest, CheckInResult } from './types';

export const staffApi = {
  checkIn: (request: CheckInRequest) =>
    apiClient<CheckInResult>('/api/staff/check-ins', {
      method: 'POST',
      body: JSON.stringify(request),
      headers: { 'Content-Type': 'application/json' },
    }).catch((error: unknown) => {
      if (error instanceof ApiError && error.status === 409 &&
          error.problem.outcome === 'DuplicateScan' && error.problem.requestId === request.requestId) {
        return error.problem as unknown as CheckInResult;
      }
      throw error;
    }),
};
