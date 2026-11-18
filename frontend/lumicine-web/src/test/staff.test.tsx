import { afterEach, describe, it, expect, vi } from 'vitest';
import { render, screen, fireEvent, act } from '@testing-library/react';
import React from 'react';
import { BrowserRouter } from 'react-router-dom';
import { StaffCheckInPage } from '../features/staff/StaffCheckInPage';
import { staffApi } from '../api/staff';
import { adminApi } from '../api/admin';
import { toUtc7Offset, utcToUtc7Input } from '../features/staff/StaffSchedulePage';

describe('Staff Ticket Check-In', () => {
  afterEach(() => vi.restoreAllMocks());

  it('submits manual ticket code and displays check-in success result', async () => {
    vi.spyOn(staffApi, 'checkIn').mockResolvedValueOnce({
      requestId: 'req-check-1',
      outcome: 'Success',
      ticketId: 't-1',
      ticketCode: 'TC-1234',
      bookingId: 'b-1',
      serverTimeUtc: new Date().toISOString(),
    });

    render(
      <BrowserRouter>
        <StaffCheckInPage />
      </BrowserRouter>
    );

    const input = screen.getByPlaceholderText(/Nhập mã vé/i);
    fireEvent.change(input, { target: { value: 'TC-1234' } });

    const submitBtn = screen.getByRole('button', { name: /Kiểm tra/i });
    fireEvent.click(submitBtn);

    await act(async () => {
      await Promise.resolve();
    });

    expect(screen.getByText('CHECK-IN HỢP LỆ!')).toBeInTheDocument();
  });

  it('displays duplicate scan warning when ticket was already used', async () => {
    const usedTime = new Date('2026-11-17T10:30:00Z');
    vi.spyOn(globalThis, 'fetch').mockImplementationOnce(async (_, options) =>
      Response.json({
        requestId: JSON.parse(String(options?.body)).requestId,
        outcome: 'DuplicateScan',
        ticketId: 't-2',
        ticketCode: 'TC-DUPLICATE',
        usedAtUtc: usedTime.toISOString(),
        serverTimeUtc: new Date().toISOString(),
        ticket: { movieTitle: 'LumiCine Demo', roomName: 'Phòng 2', rowLabel: 'A', number: 2 },
      }, { status: 409 }),
    );

    render(
      <BrowserRouter>
        <StaffCheckInPage />
      </BrowserRouter>
    );

    const input = screen.getByPlaceholderText(/Nhập mã vé/i);
    fireEvent.change(input, { target: { value: 'TC-DUPLICATE' } });

    const submitBtn = screen.getByRole('button', { name: /Kiểm tra/i });
    fireEvent.click(submitBtn);

    await act(async () => {
      await Promise.resolve();
    });

    expect(screen.getByText('VÉ ĐÃ QUA SỬ DỤNG!')).toBeInTheDocument();
    expect(screen.getByText('LumiCine Demo')).toBeInTheDocument();
  });
});

describe('Staff schedule time handling', () => {
  afterEach(() => vi.restoreAllMocks());

  it('keeps datetime-local values anchored to UTC+7 when round-tripping UTC', () => {
    expect(toUtc7Offset('2026-11-17T19:30')).toBe('2026-11-17T12:30:00.000Z');
    expect(utcToUtc7Input('2026-11-17T12:30:00Z')).toBe('2026-11-17T19:30');
  });

  it('sends the management contract field startAt', async () => {
    let requestBody = '';
    vi.spyOn(globalThis, 'fetch').mockImplementation(async (_input, init) => {
      requestBody = String(init?.body);
      return Response.json({ id: 'showtime-1' });
    });
    await adminApi.createShowtime({ movieId: 'movie-1', roomId: 'room-1', startAt: toUtc7Offset('2026-11-17T19:30'), basePrice: 80000 });
    expect(JSON.parse(requestBody)).toMatchObject({ startAt: '2026-11-17T12:30:00.000Z' });
    expect(JSON.parse(requestBody)).not.toHaveProperty('startAtUtc');
  });
});
