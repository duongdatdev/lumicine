import { QueryClient } from '@tanstack/react-query';

export const queryClient = new QueryClient({
  defaultOptions: {
    queries: {
      retry: 1,
      refetchOnWindowFocus: false,
      staleTime: 1000 * 30, // 30 seconds
    },
    mutations: {
      retry: 0,
    },
  },
});

export function clearPrivateCache() {
  queryClient.removeQueries({ queryKey: ['currentUser'] });
  queryClient.removeQueries({ queryKey: ['userBookings'] });
  queryClient.removeQueries({ queryKey: ['userTickets'] });
  queryClient.removeQueries({ queryKey: ['discovery'] });
}
