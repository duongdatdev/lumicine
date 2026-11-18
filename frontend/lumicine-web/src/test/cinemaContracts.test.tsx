import React from 'react';
import { afterEach, describe, expect, it, vi } from 'vitest';
import { cleanup, fireEvent, render, screen, waitFor } from '@testing-library/react';
import { BrowserRouter, Route, Routes } from 'react-router-dom';
import { QueryClient, QueryClientProvider } from '@tanstack/react-query';
import { adminApi } from '../api/admin';
import { AdminCinemasPage } from '../features/admin/AdminCinemasPage';
import { AdminRoomSeatsPage } from '../features/admin/AdminRoomSeatsPage';

const cinema = {
  id: 'cinema-1', code: 'LC01', name: 'LumiCine Quận 1', city: 'Hồ Chí Minh',
  address: '1 Nguyễn Huệ', isActive: true,
};

function renderAdmin(element: React.ReactNode, path = '/') {
  window.history.replaceState({}, '', path);
  const queryClient = new QueryClient({ defaultOptions: { queries: { retry: false } } });
  return render(<QueryClientProvider client={queryClient}><BrowserRouter>{element}</BrowserRouter></QueryClientProvider>);
}

describe('Cinema and room management contracts', () => {
  afterEach(() => vi.restoreAllMocks());

  it('submits cinema code together with its details', async () => {
    vi.spyOn(adminApi, 'getCinemas').mockResolvedValue({ items: [cinema], totalCount: 1, page: 1, pageSize: 20 });
    vi.spyOn(adminApi, 'getRoomsForCinema').mockResolvedValue({ items: [], totalCount: 0, page: 1, pageSize: 50 });
    const create = vi.spyOn(adminApi, 'createCinema').mockResolvedValue({ ...cinema, id: 'cinema-2', code: 'LC02' });
    renderAdmin(<AdminCinemasPage />);

    fireEvent.click(await screen.findByRole('button', { name: /Thêm rạp mới/i }));
    fireEvent.change(screen.getByLabelText('Mã cụm rạp'), { target: { value: 'LC02' } });
    fireEvent.change(screen.getByLabelText('Tên cụm rạp'), { target: { value: 'LumiCine Quận 2' } });
    fireEvent.change(screen.getByLabelText('Thành phố / Tỉnh'), { target: { value: 'Hồ Chí Minh' } });
    fireEvent.change(screen.getByLabelText('Địa chỉ chi tiết'), { target: { value: '2 Đồng Khởi' } });
    fireEvent.click(screen.getByRole('button', { name: 'Tạo rạp' }));

    await waitFor(() => expect(create).toHaveBeenCalledWith({
      code: 'LC02', name: 'LumiCine Quận 2', city: 'Hồ Chí Minh', address: '2 Đồng Khởi',
    }));
  });

  it('submits room codes and the API seat layout shape', async () => {
    vi.spyOn(adminApi, 'getCinemas').mockResolvedValue({ items: [cinema], totalCount: 1, page: 1, pageSize: 20 });
    vi.spyOn(adminApi, 'getRoomsForCinema').mockResolvedValue({ items: [], totalCount: 0, page: 1, pageSize: 50 });
    const create = vi.spyOn(adminApi, 'createRoom').mockResolvedValue({
      id: 'room-1', cinemaId: cinema.id, code: 'R01', name: 'Phòng 1', seatCount: 20, isActive: true,
    });
    renderAdmin(<AdminCinemasPage />);

    fireEvent.click(await screen.findByRole('button', { name: /Thêm phòng chiếu/i }));
    fireEvent.change(screen.getByLabelText('Mã phòng chiếu'), { target: { value: 'R01' } });
    fireEvent.change(screen.getByLabelText('Tên phòng chiếu'), { target: { value: 'Phòng 1' } });
    fireEvent.click(screen.getByRole('button', { name: 'Tạo phòng' }));
    await waitFor(() => expect(create).toHaveBeenCalledWith(cinema.id, { code: 'R01', name: 'Phòng 1' }));

    cleanup();
    vi.spyOn(adminApi, 'getRoom').mockResolvedValue({
      id: 'room-1', cinemaId: cinema.id, code: 'R01', name: 'Phòng 1', seatCount: 3, isActive: true,
    });
    vi.spyOn(adminApi, 'getRoomSeats').mockResolvedValue({
      items: [
        { id: 'seat-1', rowLabel: 'A', number: 1, seatType: 'Standard', isActive: true },
        { id: 'seat-2', rowLabel: 'A', number: 2, seatType: 'Standard', isActive: false },
        { id: 'seat-3', rowLabel: 'A', number: 3, seatType: 'VIP', isActive: true },
      ],
      totalCount: 3,
      page: 1,
      pageSize: 100,
    });
    const replaceLayout = vi.spyOn(adminApi, 'replaceRoomSeatLayout').mockResolvedValue();
    renderAdmin(
      <Routes><Route path="/admin/rooms/:id/seats" element={<AdminRoomSeatsPage />} /></Routes>,
      '/admin/rooms/room-1/seats',
    );
    await screen.findByDisplayValue('2');
    fireEvent.click(await screen.findByRole('button', { name: /Lưu thay thế Layout/i }));
    await waitFor(() => expect(replaceLayout).toHaveBeenCalledWith('room-1', [
      { label: 'A', standardSeats: 2, vipSeats: 1 },
    ]));
  });
});
