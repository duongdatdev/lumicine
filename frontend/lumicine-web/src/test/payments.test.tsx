import { afterEach, describe, it, expect, vi } from 'vitest';
import { render, screen, act } from '@testing-library/react';
import React from 'react';
import { BrowserRouter } from 'react-router-dom';
import { AuthProvider } from '../auth/AuthContext';
import { PaymentReturnPage } from '../features/payments/PaymentReturnPage';
import { paymentsApi } from '../api/payments';
import { bookingApi } from '../api/booking';
import * as client from '../api/client';
import type { BookingDetails } from '../api/types';

const booking: BookingDetails = {
  id: 'book-1', code: 'LC-001', showtimeId: 's-1', movieId: 'm-1', movieTitle: 'LumiCine Demo',
  cinemaId: 'c-1', cinemaName: 'LumiCine', roomId: 'r-1', roomName: 'Phòng 1', status: 'Confirmed',
  holdExpiresAtUtc: null, totalAmount: 160000, createdAtUtc: '2026-11-17T10:30:00Z',
  serverTimeUtc: '2026-11-17T10:30:00Z', seats: [],
};

describe('Payment Return States and Flow', () => {
  afterEach(() => vi.restoreAllMocks());

  it('displays Success state when payment is confirmed', async () => {
    vi.spyOn(client, 'refreshSession').mockResolvedValueOnce(null);

    const mockPayment = {
      id: 'pay-1',
      bookingId: 'book-1',
      requestId: 'req-1',
      merchantReference: 'LUMICINE-001',
      amount: 160000,
      currency: 'VND',
      status: 'Succeeded',
      processingStatus: 'Fulfilled',
      createdAtUtc: new Date().toISOString(),
      expiresAtUtc: new Date().toISOString(),
    };

    vi.spyOn(paymentsApi, 'processVnPayReturn').mockResolvedValueOnce({
      rspCode: '00',
      message: 'Giao dịch thành công',
      payment: { ...mockPayment, status: 'Pending', processingStatus: 'None' },
    });
    vi.spyOn(paymentsApi, 'getPayment').mockResolvedValueOnce(mockPayment);
    vi.spyOn(bookingApi, 'getBooking').mockResolvedValueOnce(booking);

    render(
      <AuthProvider>
        <BrowserRouter>
          <PaymentReturnPage />
        </BrowserRouter>
      </AuthProvider>
    );

    await act(async () => {
      await Promise.resolve();
    });

    expect(screen.getByText('Thanh toán thành công!')).toBeInTheDocument();
    expect(screen.getByText('LUMICINE-001')).toBeInTheDocument();
    expect(screen.getByText('Xem vé điện tử của tôi')).toBeInTheDocument();
  });

  it('displays RequiresRefundReview state when payment needs manual refund review', async () => {
    vi.spyOn(client, 'refreshSession').mockResolvedValueOnce(null);

    const mockPayment = {
      id: 'pay-2',
      bookingId: 'book-2',
      requestId: 'req-2',
      merchantReference: 'LUMICINE-002',
      amount: 240000,
      currency: 'VND',
      status: 'Succeeded',
      processingStatus: 'RequiresRefundReview',
      createdAtUtc: new Date().toISOString(),
      expiresAtUtc: new Date().toISOString(),
    };

    vi.spyOn(paymentsApi, 'processVnPayReturn').mockResolvedValueOnce({
      rspCode: '07',
      message: 'Cần đối soát hoàn tiền',
      payment: mockPayment,
    });
    vi.spyOn(paymentsApi, 'getPayment').mockResolvedValueOnce(mockPayment);
    vi.spyOn(bookingApi, 'getBooking').mockResolvedValueOnce({ ...booking, id: 'book-2', status: 'Expired' });

    render(
      <AuthProvider>
        <BrowserRouter>
          <PaymentReturnPage />
        </BrowserRouter>
      </AuthProvider>
    );

    await act(async () => {
      await Promise.resolve();
    });

    expect(screen.getByText('Giao dịch cần đối soát hoàn tiền')).toBeInTheDocument();
    expect(screen.queryByText('Thanh toán thành công!')).not.toBeInTheDocument();
  });
});
