import { apiClient } from './client';
import type { PaymentView, VnPayReturnResponse } from './types';

export const paymentsApi = {
  getPayment: (id: string) => apiClient<PaymentView>(`/api/payments/${id}`),

  processVnPayReturn: (queryString: string) =>
    apiClient<VnPayReturnResponse>(`/api/payments/vnpay/return?${queryString}`, {
      skipAuth: true,
    }),
};
