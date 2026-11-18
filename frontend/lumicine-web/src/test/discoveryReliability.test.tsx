import { afterEach, describe, expect, it, vi } from 'vitest';
import { fireEvent, render, screen, waitFor } from '@testing-library/react';
import React from 'react';
import { MemoryRouter, Route, Routes } from 'react-router-dom';
import { QueryClient, QueryClientProvider } from '@tanstack/react-query';
import { AuthProvider } from '../auth/AuthContext';
import { DiscoveryChatPage } from '../features/discovery/DiscoveryChatPage';
import { discoveryApi } from '../api/discovery';
import { ApiError, apiClient } from '../api/client';
import * as client from '../api/client';

describe('Discovery retry behavior', () => {
  afterEach(() => {
    vi.restoreAllMocks();
    Reflect.deleteProperty(HTMLElement.prototype, 'scrollIntoView');
  });

  it('reads Retry-After from an HTTP response as a delta or date', async () => {
    vi.spyOn(globalThis, 'fetch').mockResolvedValueOnce(new Response(JSON.stringify({ title: 'Rate limited' }), {
      status: 429,
      headers: { 'content-type': 'application/problem+json', 'Retry-After': '11' },
    }));
    await expect(apiClient('/api/discovery/test')).rejects.toMatchObject({ retryAfterSeconds: 11 });

    const retryDate = new Date(Date.now() + 5000).toUTCString();
    const dateError = new ApiError(429, { title: 'Rate limited' }, retryDate);
    expect(dateError.retryAfterSeconds).toBeGreaterThanOrEqual(4);
    expect(dateError.retryAfterSeconds).toBeLessThanOrEqual(5);
  });

  it('uses fresh request IDs for message changes and preserves IDs for exact conflict retries', async () => {
    Object.defineProperty(HTMLElement.prototype, 'scrollIntoView', { configurable: true, value: vi.fn() });
    vi.spyOn(client, 'refreshSession').mockResolvedValue({
      accessToken: 'token', user: { id: 'user-1', email: 'user@example.com', displayName: 'User', roles: [] },
    });
    vi.spyOn(discoveryApi, 'listConversations').mockResolvedValue({ items: [], totalCount: 0, page: 1, pageSize: 50 });
    vi.spyOn(discoveryApi, 'getConversation').mockResolvedValue({
      id: 'conversation-1', version: 1, turnCount: 0, isExpired: false, filters: { genreSlugs: [] },
      createdAtUtc: '2026-11-17T00:00:00Z', lastActiveAtUtc: '2026-11-17T00:00:00Z',
      expiresAtUtc: '2026-11-18T00:00:00Z', messages: [],
    });
    const sendMessage = vi.spyOn(discoveryApi, 'sendMessage')
      .mockRejectedValueOnce(new ApiError(409, { detail: 'This requestId was already used with a different message.' }))
      .mockRejectedValueOnce(new ApiError(409, { detail: 'Conversation was updated concurrently; refresh and retry.' }))
      .mockRejectedValueOnce(new ApiError(409, { detail: 'Conversation was updated concurrently; refresh and retry.' }));
    const queryClient = new QueryClient({ defaultOptions: { queries: { retry: false } } });

    render(
      <QueryClientProvider client={queryClient}>
        <AuthProvider>
          <MemoryRouter initialEntries={['/discover/conversation-1']}>
            <Routes><Route path="/discover/:id" element={<DiscoveryChatPage />} /></Routes>
          </MemoryRouter>
        </AuthProvider>
      </QueryClientProvider>
    );

    const input = await screen.findByPlaceholderText('Nhập câu hỏi hoặc yêu cầu tìm phim...');
    fireEvent.change(input, { target: { value: 'find action movies' } });
    fireEvent.click(screen.getByRole('button', { name: 'Gửi' }));
    await waitFor(() => expect(sendMessage).toHaveBeenCalledTimes(1));
    await screen.findByText(/Mã yêu cầu này đã được dùng/);

    fireEvent.change(input, { target: { value: 'find comedy movies' } });
    fireEvent.click(screen.getByRole('button', { name: 'Gửi' }));
    await waitFor(() => expect(sendMessage).toHaveBeenCalledTimes(2));
    await screen.findByText(/Hội thoại có lượt phản hồi mới/);
    fireEvent.click(screen.getByRole('button', { name: 'Gửi' }));
    await waitFor(() => expect(sendMessage).toHaveBeenCalledTimes(3));

    const firstRequest = sendMessage.mock.calls[0][1];
    const changedRequest = sendMessage.mock.calls[1][1];
    expect(changedRequest.requestId).not.toBe(firstRequest.requestId);
    expect(sendMessage.mock.calls[2][1]).toEqual(changedRequest);
  });
});
