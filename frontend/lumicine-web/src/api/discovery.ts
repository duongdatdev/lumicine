import { apiClient } from './client';
import type {
  DiscoveryConversationSummary,
  DiscoveryConversationDetails,
  DiscoveryChatResponse,
  PagedResult,
} from './types';

export interface SendDiscoveryMessagePayload {
  requestId: string;
  expectedVersion: number;
  message: string;
}

export const discoveryApi = {
  createConversation: () =>
    apiClient<DiscoveryConversationDetails>('/api/discovery/conversations', {
      method: 'POST',
    }),

  listConversations: (params: { page?: number; pageSize?: number } = {}) => {
    const searchParams = new URLSearchParams();
    if (params.page) searchParams.set('page', params.page.toString());
    if (params.pageSize) searchParams.set('pageSize', params.pageSize.toString());
    return apiClient<PagedResult<DiscoveryConversationSummary>>(
      `/api/discovery/conversations?${searchParams.toString()}`
    );
  },

  getConversation: (id: string) =>
    apiClient<DiscoveryConversationDetails>(`/api/discovery/conversations/${id}`),

  deleteConversation: (id: string) =>
    apiClient<void>(`/api/discovery/conversations/${id}`, {
      method: 'DELETE',
    }),

  sendMessage: (id: string, payload: SendDiscoveryMessagePayload) =>
    apiClient<DiscoveryChatResponse>(`/api/discovery/conversations/${id}/messages`, {
      method: 'POST',
      body: JSON.stringify(payload),
      headers: { 'Content-Type': 'application/json' },
    }),
};
