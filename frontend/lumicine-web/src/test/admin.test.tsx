import { describe, it, expect, vi } from 'vitest';
import { render, screen, act } from '@testing-library/react';
import React from 'react';
import { BrowserRouter } from 'react-router-dom';
import { QueryClientProvider, QueryClient } from '@tanstack/react-query';
import { AuthProvider } from '../auth/AuthContext';
import { AdminDashboardPage } from '../features/admin/AdminDashboardPage';
import { adminApi } from '../api/admin';
import * as client from '../api/client';

describe('Admin Workspace Features', () => {
  it('renders dashboard summary KPIs correctly', async () => {
    vi.spyOn(client, 'refreshSession').mockResolvedValueOnce({
      accessToken: 'admin-token',
      user: { id: 'admin-1', email: 'admin@lumicine.vn', displayName: 'Admin User', roles: ['Admin'] },
    });

    vi.spyOn(adminApi, 'getDashboardSummary').mockResolvedValueOnce({
      metadata: {
        fromDate: '2026-10-18',
        toDate: '2026-11-17',
        generatedAtUtc: new Date().toISOString(),
      },
      ticketSalesAmount: 50000000,
      collectedAmount: 50000000,
      recordedRefundAmount: 2000000,
      netCollectedAmount: 48000000,
      successfulPaymentCount: 350,
      failedPaymentCount: 15,
      paymentsMissingRequiredTimestampCount: 0,
      bookingsCreatedCount: 400,
      bookingStatuses: { Confirmed: 350, Cancelled: 50 },
      ticketsIssuedCount: 520,
      ticketsUsedCount: 480,
      rejectedCheckInCount: 5,
      rejectedCheckIns: { DuplicateScan: 3, OutsideWindow: 2 },
    });

    vi.spyOn(adminApi, 'getDashboardOperations').mockResolvedValueOnce({
      asOfUtc: new Date().toISOString(),
      refundReviewCount: 2,
      refundReviewAmount: 320000,
      refundReviewPaymentIds: ['p-1', 'p-2'],
      failedEmailCount: 0,
      failedEmailIds: [],
      dueEmailCount: 0,
      dueEmailIds: [],
      pendingIndexCount: 0,
      pendingIndexMovieIds: [],
      failedIndexCount: 0,
      failedIndexMovieIds: [],
      paymentsMissingProcessedAtCount: 0,
      paymentsMissingProcessedAtIds: [],
    });

    vi.spyOn(adminApi, 'getRevenueReport').mockResolvedValueOnce({
      metadata: {
        fromDate: '2026-10-18',
        toDate: '2026-11-17',
        generatedAtUtc: new Date().toISOString(),
      },
      rows: [
          {
            key: '2026-11-17',
            label: '17/11/2026',
            ticketSalesAmount: 10000000,
            collectedAmount: 10000000,
            recordedRefundAmount: 0,
            netCollectedAmount: 10000000,
            successfulPaymentCount: 75,
            failedPaymentCount: 2,
          },
      ],
      totalCount: 1,
      page: 1,
      pageSize: 50,
      totals: {
        ticketSalesAmount: 10000000,
        collectedAmount: 10000000,
        recordedRefundAmount: 0,
        netCollectedAmount: 10000000,
        successfulPaymentCount: 75,
        failedPaymentCount: 2,
      },
    });

    const testQueryClient = new QueryClient();

    render(
      <QueryClientProvider client={testQueryClient}>
        <AuthProvider>
          <BrowserRouter>
            <AdminDashboardPage />
          </BrowserRouter>
        </AuthProvider>
      </QueryClientProvider>
    );

    await act(async () => {
      await Promise.resolve();
    });

    expect(screen.getByText('Tổng quan & Báo cáo Doanh thu')).toBeInTheDocument();
    expect(screen.getByText('520 vé')).toBeInTheDocument();
    expect(screen.getByText('350')).toBeInTheDocument();
    expect(screen.getByText(/2 giao dịch cần đối soát hoàn tiền/i)).toBeInTheDocument();
    expect(await screen.findByRole('cell', { name: '17/11/2026' })).toBeInTheDocument();
  });
});
