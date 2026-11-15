import { apiClient } from './client';
import type {
  BookingDetails,
  CreateBookingRequest,
  BookingPaymentLink,
  TicketDetails,
  PagedResult,
} from './types';

export const bookingApi = {
  createBooking: (data: CreateBookingRequest) =>
    apiClient<BookingDetails>('/api/bookings', {
      method: 'POST',
      body: JSON.stringify(data),
      headers: { 'Content-Type': 'application/json' },
    }),

  listBookings: (params: { status?: string; page?: number; pageSize?: number } = {}) => {
    const searchParams = new URLSearchParams();
    if (params.status) searchParams.set('status', params.status);
    if (params.page) searchParams.set('page', params.page.toString());
    if (params.pageSize) searchParams.set('pageSize', params.pageSize.toString());
    return apiClient<PagedResult<BookingDetails>>(`/api/bookings?${searchParams.toString()}`);
  },

  getBooking: (id: string) => apiClient<BookingDetails>(`/api/bookings/${id}`),

  cancelBooking: (id: string) =>
    apiClient<void>(`/api/bookings/${id}/cancel`, {
      method: 'POST',
    }),

  createVnPayPayment: (bookingId: string, requestId: string) =>
    apiClient<BookingPaymentLink>(`/api/bookings/${bookingId}/payments/vnpay`, {
      method: 'POST',
      body: JSON.stringify({ requestId }),
      headers: { 'Content-Type': 'application/json' },
    }),

  listBookingTickets: (bookingId: string) =>
    apiClient<TicketDetails[]>(`/api/bookings/${bookingId}/tickets`),

  resendTicketEmail: (bookingId: string) =>
    apiClient<void>(`/api/bookings/${bookingId}/tickets/resend-email`, {
      method: 'POST',
    }),
};
