import { describe, it, expect, vi } from 'vitest';
import { render, screen, fireEvent, act } from '@testing-library/react';
import React from 'react';
import { BrowserRouter } from 'react-router-dom';
import { StaffCheckInPage } from '../features/staff/StaffCheckInPage';
import { staffApi } from '../api/staff';

describe('Staff Ticket Check-In', () => {
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
    vi.spyOn(staffApi, 'checkIn').mockResolvedValueOnce({
      requestId: 'req-check-2',
      outcome: 'DuplicateScan',
      ticketId: 't-2',
      ticketCode: 'TC-DUPLICATE',
      usedAtUtc: usedTime.toISOString(),
      serverTimeUtc: new Date().toISOString(),
    });

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
  });
});
