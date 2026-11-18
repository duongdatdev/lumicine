import { beforeEach, describe, expect, it, vi } from 'vitest';
import { fireEvent, render, screen, waitFor } from '@testing-library/react';
import React from 'react';
import { MemoryRouter, Route, Routes } from 'react-router-dom';
import { QueryClient, QueryClientProvider } from '@tanstack/react-query';
import { ShowtimeSeatSelectionPage } from '../features/booking/ShowtimeSeatSelectionPage';
import { bookingApi } from '../api/booking';
import { catalogApi } from '../api/catalog';
import { ApiError } from '../api/client';

vi.mock('../auth/useAuth', () => ({ useAuth: () => ({ isAuthenticated: true, user: { id: 'customer-1' } }) }));
vi.mock('../api/realtime', () => ({
  createShowtimeHubConnection: () => ({
    on: vi.fn(), onreconnecting: vi.fn(), onreconnected: vi.fn(), onclose: vi.fn(),
    start: vi.fn(async () => undefined), stop: vi.fn(async () => undefined),
  }),
  joinShowtime: vi.fn(async () => undefined),
  leaveShowtime: vi.fn(async () => undefined),
}));

const availableSeats = [1, 2, 3].map((number) => ({
  seatId: `seat-${number}`, rowLabel: 'A', number, seatType: 'Normal', status: 'Available', price: 100000,
}));
let forceSeatConflict = false;

describe('Booking request reliability', () => {
  beforeEach(() => {
    vi.restoreAllMocks();
    forceSeatConflict = false;
    vi.spyOn(catalogApi, 'getShowtime').mockResolvedValue({
      id: 'show-1', movieId: 'movie-1', movieTitle: 'Film', cinemaId: 'cinema-1', cinemaName: 'Cinema',
      cinemaCity: 'HCMC', roomId: 'room-1', roomName: 'Room 1', startAtUtc: '2026-11-18T12:00:00Z',
      endAtUtc: '2026-11-18T14:00:00Z', basePrice: 100000, status: 'Scheduled',
    });
    vi.spyOn(catalogApi, 'getShowtimeSeats').mockImplementation(async () => {
      return forceSeatConflict ? [
        { ...availableSeats[0], status: 'Sold' }, availableSeats[1], availableSeats[2],
      ] : availableSeats;
    });
    vi.spyOn(bookingApi, 'listBookings').mockResolvedValue({ items: [], page: 1, pageSize: 10, totalCount: 0 });
  });

  it('uses a new id for changed seats, reuses it for an identical retry, and keeps available seats after 409', async () => {
    const createBooking = vi.spyOn(bookingApi, 'createBooking')
      .mockRejectedValueOnce(new ApiError(409, { title: 'Conflict' }))
      .mockRejectedValueOnce(new Error('Temporary failure'))
      .mockRejectedValueOnce(new Error('Temporary failure'));
    const queryClient = new QueryClient({ defaultOptions: { queries: { retry: false } } });

    render(
      <QueryClientProvider client={queryClient}>
        <MemoryRouter initialEntries={['/showtimes/show-1/seats']}>
          <Routes><Route path="/showtimes/:id/seats" element={<ShowtimeSeatSelectionPage />} /></Routes>
        </MemoryRouter>
      </QueryClientProvider>
    );

    const seat1 = await screen.findByRole('button', { name: /Hàng A Ghế 1/ });
    const seat2 = screen.getByRole('button', { name: /Hàng A Ghế 2/ });
    await waitFor(() => expect(catalogApi.getShowtimeSeats).toHaveBeenCalledTimes(2));
    fireEvent.click(seat1);
    fireEvent.click(seat2);
    forceSeatConflict = true;
    fireEvent.click(screen.getByRole('button', { name: 'Giữ ghế & Thanh toán' }));
    await waitFor(() => expect(createBooking).toHaveBeenCalledTimes(1));
    await screen.findByText(/Một hoặc nhiều ghế bạn chọn vừa được người khác giữ/);
    expect(await screen.findByRole('button', { name: /Hàng A Ghế 1.*Đã bán/ })).toBeDisabled();
    expect(screen.getByRole('button', { name: /Hàng A Ghế 2.*Đang chọn/ })).toBeInTheDocument();

    fireEvent.click(screen.getByRole('button', { name: /Hàng A Ghế 3/ }));
    fireEvent.click(screen.getByRole('button', { name: 'Giữ ghế & Thanh toán' }));
    await waitFor(() => expect(createBooking).toHaveBeenCalledTimes(2));
    fireEvent.click(screen.getByRole('button', { name: 'Giữ ghế & Thanh toán' }));
    await waitFor(() => expect(createBooking).toHaveBeenCalledTimes(3));

    const firstId = createBooking.mock.calls[0][0].requestId;
    const changedPayloadId = createBooking.mock.calls[1][0].requestId;
    expect(createBooking.mock.calls[0][0].seatIds).toEqual(['seat-1', 'seat-2']);
    expect(createBooking.mock.calls[1][0].seatIds).toEqual(['seat-2', 'seat-3']);
    expect(changedPayloadId).not.toBe(firstId);
    expect(createBooking.mock.calls[2][0]).toEqual(createBooking.mock.calls[1][0]);
  });

  it('shows a pending hold for another showtime without selecting or pricing its seats here', async () => {
    vi.spyOn(bookingApi, 'listBookings').mockResolvedValue({
      items: [{
        id: 'booking-other', code: 'LC-OTHER', showtimeId: 'show-2', movieId: 'movie-2', movieTitle: 'Other Film',
        cinemaId: 'cinema-2', cinemaName: 'Other Cinema', roomId: 'room-2', roomName: 'Room 2', status: 'Pending',
        holdExpiresAtUtc: '2026-11-18T13:00:00Z', totalAmount: 220000, createdAtUtc: '2026-11-17T10:00:00Z',
        serverTimeUtc: '2026-11-17T10:00:00Z', seats: [{ seatId: 'other-seat', rowLabel: 'Z', number: 9, seatType: 'VIP', unitPrice: 220000 }],
      }],
      page: 1, pageSize: 10, totalCount: 1,
    });
    const queryClient = new QueryClient({ defaultOptions: { queries: { retry: false } } });

    render(
      <QueryClientProvider client={queryClient}>
        <MemoryRouter initialEntries={['/showtimes/show-1/seats']}>
          <Routes><Route path="/showtimes/:id/seats" element={<ShowtimeSeatSelectionPage />} /></Routes>
        </MemoryRouter>
      </QueryClientProvider>
    );

    expect(await screen.findByText('Bạn đang có đơn giữ chỗ chờ thanh toán cho suất chiếu khác')).toBeInTheDocument();
    expect(screen.getByText(/Phim Other Film tại Other Cinema — Room 2/)).toBeInTheDocument();
    expect(screen.getByText('LC-OTHER')).toBeInTheDocument();
    expect(screen.getByText('Ghế đã chọn (0/8):')).toBeInTheDocument();
    expect(screen.queryByText('Z9')).not.toBeInTheDocument();
    expect(screen.getByText('—')).toBeInTheDocument();
    expect(screen.getByRole('link', { name: /Tiếp tục thanh toán/ })).toHaveAttribute('href', '/bookings/booking-other');
  });
});
