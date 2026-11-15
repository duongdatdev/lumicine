import { apiClient, apiDownload } from './client';
import type { TicketDetails } from './types';

export const ticketsApi = {
  getTicket: (id: string) => apiClient<TicketDetails>(`/api/tickets/${id}`),

  getTicketQrBlob: (id: string): Promise<Blob> =>
    apiDownload(`/api/tickets/${id}/qr`),

  downloadTicketQr: async (id: string, filename: string): Promise<void> => {
    await apiDownload(`/api/tickets/${id}/qr`, filename);
  },
};
