import { afterEach, describe, expect, it, vi } from 'vitest';
import { fireEvent, render, screen, waitFor } from '@testing-library/react';
import React from 'react';
import { MemoryRouter, Route, Routes, useLocation } from 'react-router-dom';
import { QueryClient, QueryClientProvider } from '@tanstack/react-query';
import { AdminUsersPage } from '../features/admin/AdminUsersPage';
import { AdminDiscoveryPage } from '../features/admin/AdminDiscoveryPage';
import { adminApi } from '../api/admin';

const LocationSearch = () => <output data-testid="location-search">{useLocation().search}</output>;

const renderWithQuery = (content: React.ReactNode) => render(
  <QueryClientProvider client={new QueryClient({ defaultOptions: { queries: { retry: false } } })}>
    {content}
  </QueryClientProvider>
);

describe('Admin user and discovery API contracts', () => {
  afterEach(() => {
    vi.restoreAllMocks();
    vi.unstubAllGlobals();
  });

  it('renders AdminUserPage.items and keeps pagination in the URL', async () => {
    const getUsers = vi.spyOn(adminApi, 'getUsers').mockImplementation(async (page = 1, pageSize = 20) => ({
      items: [{ id: 'u-1', email: 'user@example.com', displayName: 'Sample User', roles: ['Customer'], isActive: true, emailConfirmed: true, createdAtUtc: '2026-11-17T00:00:00Z' }],
      totalCount: 30, page, pageSize,
    }));

    renderWithQuery(
      <MemoryRouter initialEntries={['/admin/users?page=2']}>
        <LocationSearch />
        <Routes><Route path="/admin/users" element={<AdminUsersPage />} /></Routes>
      </MemoryRouter>
    );

    expect(await screen.findByText('Sample User')).toBeInTheDocument();
    expect(screen.getByText('Trang 2 / 2')).toBeInTheDocument();
    expect(getUsers).toHaveBeenCalledWith(2, 20);
    fireEvent.click(screen.getByRole('button', { name: 'Trang trước' }));
    await waitFor(() => expect(getUsers).toHaveBeenCalledWith(1, 20));
    expect(screen.getByTestId('location-search')).toHaveTextContent('');
  });

  it('reports index status and rebuild queue submission without claiming indexing completed', async () => {
    vi.spyOn(window, 'confirm').mockReturnValue(true);
    vi.spyOn(adminApi, 'getDiscoveryIndexStatus').mockResolvedValue({
      indexedCount: 18, pendingCount: 4, failedCount: 2, totalMovies: 24, generatedAtUtc: '2026-11-17T12:00:00Z',
    });
    const rebuild = vi.spyOn(adminApi, 'rebuildDiscoveryIndex').mockResolvedValue({
      queuedCount: 6, queuedAtUtc: '2026-11-17T12:30:00Z',
    });

    renderWithQuery(<AdminDiscoveryPage />);
    expect(await screen.findByText('24')).toBeInTheDocument();
    expect(screen.getByText('18')).toBeInTheDocument();
    expect(screen.getByText('4')).toBeInTheDocument();
    expect(screen.getByText('2')).toBeInTheDocument();
    fireEvent.click(screen.getByRole('button', { name: /Xây dựng lại chỉ mục/ }));
    expect(await screen.findByText('Đã đưa yêu cầu vào hàng đợi')).toBeInTheDocument();
    expect(screen.getByText('6')).toBeInTheDocument();
    expect(screen.getByText(/chạy nền/)).toBeInTheDocument();
    expect(screen.queryByText(/Xây dựng chỉ mục hoàn tất/)).not.toBeInTheDocument();
    expect(rebuild).toHaveBeenCalledOnce();
  });
});
