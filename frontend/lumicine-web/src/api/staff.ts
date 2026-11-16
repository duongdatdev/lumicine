import { apiClient } from './client';
import type { CheckInRequest, CheckInResult } from './types';

export const staffApi = {
  checkIn: (request: CheckInRequest) =>
    apiClient<CheckInResult>('/api/staff/check-ins', {
      method: 'POST',
      body: JSON.stringify(request),
      headers: { 'Content-Type': 'application/json' },
    }),
};
