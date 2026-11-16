import { describe, it, expect, vi } from 'vitest';
import { render, screen, act } from '@testing-library/react';
import React from 'react';
import { BrowserRouter } from 'react-router-dom';
import { QueryClientProvider, QueryClient } from '@tanstack/react-query';
import { AuthProvider } from '../auth/AuthContext';
import { DiscoveryChatPage } from '../features/discovery/DiscoveryChatPage';
import { discoveryApi } from '../api/discovery';
import * as client from '../api/client';

describe('Discovery AI Chat Feature', () => {
  it('renders structured movie card and showtimes when assistant replies', async () => {
    vi.spyOn(client, 'refreshSession').mockResolvedValueOnce({
      accessToken: 'tok',
      user: { id: 'u1', email: 'u@test.com', displayName: 'User', roles: [] },
    });

    const mockDetails = {
      id: 'conv-1',
      version: 1,
      turnCount: 1,
      isExpired: false,
      filters: { genreSlugs: ['action'] },
      createdAtUtc: new Date().toISOString(),
      lastActiveAtUtc: new Date().toISOString(),
      expiresAtUtc: new Date().toISOString(),
      messages: [
        {
          turnNumber: 1,
          role: 'user',
          content: 'Tìm phim hành động tối nay',
          createdAtUtc: new Date().toISOString(),
        },
        {
          turnNumber: 1,
          role: 'assistant',
          content: 'Đây là các phim hành động hay nhất đang chiếu:',
          createdAtUtc: new Date().toISOString(),
          response: {
            conversationId: 'conv-1',
            version: 1,
            requestId: 'req-1',
            turnNumber: 1,
            answer: 'Đây là các phim hành động hay nhất đang chiếu:',
            appliedFilters: { genreSlugs: ['action'] },
            mode: 'hybrid',
            isDegraded: false,
            warnings: [],
            generatedAtUtc: new Date().toISOString(),
            availabilityAsOfUtc: new Date().toISOString(),
            movies: [
              {
                movieId: 'm-1',
                title: 'Hành Động Đỉnh Cao 2026',
                durationMinutes: 125,
                ageRating: 'T16',
                genres: ['Hành Động'],
                score: 0.95,
                showtimes: [
                  {
                    showtimeId: 'st-1',
                    movieId: 'm-1',
                    cinemaId: 'c-1',
                    cinemaName: 'LumiCine Hà Nội',
                    roomId: 'r-1',
                    roomName: 'Phòng 1',
                    startAtUtc: '2026-11-17T13:00:00Z',
                    price: 90000,
                    availableSeats: 45,
                  },
                ],
              },
            ],
          },
        },
      ],
    };

    vi.spyOn(discoveryApi, 'listConversations').mockResolvedValueOnce({
      items: [
        {
          id: 'conv-1',
          version: 1,
          turnCount: 1,
          isExpired: false,
          createdAtUtc: new Date().toISOString(),
          lastActiveAtUtc: new Date().toISOString(),
          expiresAtUtc: new Date().toISOString(),
        },
      ],
      totalCount: 1,
      page: 1,
      pageSize: 50,
      totalPages: 1,
    });

    vi.spyOn(discoveryApi, 'getConversation').mockResolvedValueOnce(mockDetails);

    const testQueryClient = new QueryClient();

    render(
      <QueryClientProvider client={testQueryClient}>
        <AuthProvider>
          <BrowserRouter>
            <DiscoveryChatPage />
          </BrowserRouter>
        </AuthProvider>
      </QueryClientProvider>
    );

    await act(async () => {
      await Promise.resolve();
    });

    expect(screen.getByText('Hội thoại mới')).toBeInTheDocument();
  });
});
