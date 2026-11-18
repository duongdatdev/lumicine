import { afterEach, describe, it, expect, vi } from 'vitest';
import { render, screen, act } from '@testing-library/react';
import React from 'react';
import { MemoryRouter } from 'react-router-dom';
import { PaymentReturnPage } from '../features/payments/PaymentReturnPage';
import { paymentsApi } from '../api/payments';
import { bookingApi } from '../api/booking';
import type { BookingDetails } from '../api/types';

const authState = vi.hoisted(() => ({ isAuthenticated: true, isLoading: false }));
vi.mock('../auth/useAuth', () => ({ useAuth: () => authState }));

const booking: BookingDetails = {
  id: 'book-1', code: 'LC-001', showtimeId: 's-1', movieId: 'm-1', movieTitle: 'LumiCine Demo',
  cinemaId: 'c-1', cinemaName: 'LumiCine', roomId: 'r-1', roomName: 'Phòng 1', status: 'Confirmed',
  holdExpiresAtUtc: null, totalAmount: 160000, createdAtUtc: '2026-11-17T10:30:00Z',
  serverTimeUtc: '2026-11-17T10:30:00Z', seats: [],
};

describe('Payment Return States and Flow', () => {
  afterEach(() => {
    vi.restoreAllMocks();
    vi.useRealTimers();
    authState.isAuthenticated = true;
    authState.isLoading = false;
    sessionStorage.clear();
  });

  it('displays Success state when payment is confirmed', async () => {

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
      <MemoryRouter initialEntries={['/payments/vnpay/return?vnp_ResponseCode=00']}>
          <PaymentReturnPage />
      </MemoryRouter>
    );

    await act(async () => {
      await Promise.resolve();
    });

    expect(screen.getByText('Thanh toán thành công!')).toBeInTheDocument();
    expect(screen.getByText('LUMICINE-001')).toBeInTheDocument();
    expect(screen.getByText('Xem vé điện tử của tôi')).toBeInTheDocument();
  });

  it('displays RequiresRefundReview state when payment needs manual refund review', async () => {

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
      <MemoryRouter initialEntries={['/payments/vnpay/return?vnp_ResponseCode=07']}>
          <PaymentReturnPage />
      </MemoryRouter>
    );

    await act(async () => {
      await Promise.resolve();
    });

    expect(screen.getByText('Giao dịch cần đối soát hoàn tiền')).toBeInTheDocument();
    expect(screen.queryByText('Thanh toán thành công!')).not.toBeInTheDocument();
  });

  it('restores the saved owned payment when the callback has no query parameters and recognizes expired holds', async () => {
    sessionStorage.setItem('lumicine_last_payment_id', 'pay-saved');
    const pendingPayment = {
      id: 'pay-saved', bookingId: 'book-1', requestId: 'req-saved', merchantReference: 'LUMICINE-SAVED',
      amount: 160000, currency: 'VND', status: 'Pending', processingStatus: 'None',
      createdAtUtc: new Date().toISOString(), expiresAtUtc: new Date().toISOString(),
    };
    const processReturn = vi.spyOn(paymentsApi, 'processVnPayReturn');
    const getPayment = vi.spyOn(paymentsApi, 'getPayment').mockResolvedValueOnce(pendingPayment);
    vi.spyOn(bookingApi, 'getBooking').mockResolvedValueOnce({ ...booking, status: 'Expired' });

    render(<MemoryRouter initialEntries={['/payments/vnpay/return']}><PaymentReturnPage /></MemoryRouter>);

    expect(await screen.findByText('Giao dịch đã hết hạn')).toBeInTheDocument();
    expect(processReturn).not.toHaveBeenCalled();
    expect(getPayment).toHaveBeenCalledWith('pay-saved');
    expect(sessionStorage.getItem('lumicine_last_payment_id')).toBe('pay-saved');
    expect(screen.getByRole('button', { name: /Kiểm tra lại/ })).toBeInTheDocument();
  });

  it('waits for the auth provider before restoring a saved payment', async () => {
    authState.isLoading = true;
    sessionStorage.setItem('lumicine_last_payment_id', 'pay-auth');
    const getPayment = vi.spyOn(paymentsApi, 'getPayment').mockResolvedValue({
      id: 'pay-auth', bookingId: 'book-1', requestId: 'req-auth', merchantReference: 'LUMICINE-AUTH',
      amount: 160000, currency: 'VND', status: 'Succeeded', processingStatus: 'Fulfilled',
      createdAtUtc: new Date().toISOString(), expiresAtUtc: new Date().toISOString(),
    });
    vi.spyOn(bookingApi, 'getBooking').mockResolvedValue(booking);
    const { rerender } = render(<MemoryRouter initialEntries={['/payments/vnpay/return']}><PaymentReturnPage /></MemoryRouter>);
    expect(screen.getByText(/Đang kiểm tra kết quả giao dịch/)).toBeInTheDocument();
    expect(getPayment).not.toHaveBeenCalled();

    authState.isLoading = false;
    rerender(<MemoryRouter initialEntries={['/payments/vnpay/return']}><PaymentReturnPage /></MemoryRouter>);
    expect(await screen.findByText('Thanh toán thành công!')).toBeInTheDocument();
    expect(getPayment).toHaveBeenCalledWith('pay-auth');
  });

  it('stops automatic pending checks after 60 seconds while leaving manual retry available', async () => {
    vi.useFakeTimers();
    vi.setSystemTime(new Date('2026-11-17T12:00:00Z'));
    sessionStorage.setItem('lumicine_last_payment_id', 'pay-pending');
    const pendingPayment = {
      id: 'pay-pending', bookingId: 'book-1', requestId: 'req-pending', merchantReference: 'LUMICINE-PENDING',
      amount: 160000, currency: 'VND', status: 'Pending', processingStatus: 'None',
      createdAtUtc: new Date().toISOString(), expiresAtUtc: new Date().toISOString(),
    };
    const getPayment = vi.spyOn(paymentsApi, 'getPayment').mockResolvedValue(pendingPayment);
    vi.spyOn(bookingApi, 'getBooking').mockResolvedValue({ ...booking, status: 'Pending' });

    render(<MemoryRouter initialEntries={['/payments/vnpay/return']}><PaymentReturnPage /></MemoryRouter>);
    await act(async () => { await Promise.resolve(); await Promise.resolve(); });
    expect(screen.getByRole('button', { name: /Kiểm tra lại/ })).toBeInTheDocument();

    await act(async () => { await vi.advanceTimersByTimeAsync(60000); });
    expect(screen.getByText(/Chưa nhận được kết quả cuối cùng/)).toBeInTheDocument();
    const countAtTimeout = getPayment.mock.calls.length;
    await act(async () => { await vi.advanceTimersByTimeAsync(9000); });
    expect(getPayment).toHaveBeenCalledTimes(countAtTimeout);
    expect(screen.getByRole('button', { name: /Kiểm tra lại/ })).toBeInTheDocument();
  });
});
