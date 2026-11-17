import { apiClient, apiDownload, apiUpload } from './client';
import type {
  AdminDashboardSummary,
  AdminDashboardOperations,
  AdminReportResult,
  RevenueReportRow,
  RevenueReportTotals,
  ShowtimeReportRow,
  ShowtimeReportTotals,
  GenreSummary,
  MovieSummary,
  CinemaSummary,
  RoomSummary,
  AdminSeatSummary,
  RoomSeatLayoutRow,
  ShowtimeSummary,
  ShowtimeSeatSummary,
  AdminUserPage,
  AdminUserSummary,
  AdminPaymentView,
  PaymentView,
  DiscoveryIndexStatus,
  DiscoveryRebuildResult,
  PagedResult,
} from './types';

export const adminApi = {
  // Dashboard & Operations
  getDashboardSummary: (params: { fromDate?: string; toDate?: string; movieId?: string; cinemaId?: string } = {}) => {
    const sp = new URLSearchParams();
    if (params.fromDate) sp.set('fromDate', params.fromDate);
    if (params.toDate) sp.set('toDate', params.toDate);
    if (params.movieId) sp.set('movieId', params.movieId);
    if (params.cinemaId) sp.set('cinemaId', params.cinemaId);
    return apiClient<AdminDashboardSummary>(`/api/admin/dashboard/summary?${sp.toString()}`);
  },

  getDashboardOperations: (params: { movieId?: string; cinemaId?: string } = {}) => {
    const sp = new URLSearchParams();
    if (params.movieId) sp.set('movieId', params.movieId);
    if (params.cinemaId) sp.set('cinemaId', params.cinemaId);
    return apiClient<AdminDashboardOperations>(`/api/admin/dashboard/operations?${sp.toString()}`);
  },

  // Reports
  getRevenueReport: (params: {
    fromDate?: string;
    toDate?: string;
    movieId?: string;
    cinemaId?: string;
    groupBy?: 'day' | 'movie' | 'cinema' | 'showtime';
    page?: number;
    pageSize?: number;
  } = {}) => {
    const sp = new URLSearchParams();
    if (params.fromDate) sp.set('fromDate', params.fromDate);
    if (params.toDate) sp.set('toDate', params.toDate);
    if (params.movieId) sp.set('movieId', params.movieId);
    if (params.cinemaId) sp.set('cinemaId', params.cinemaId);
    if (params.groupBy) sp.set('groupBy', params.groupBy);
    if (params.page) sp.set('page', params.page.toString());
    if (params.pageSize) sp.set('pageSize', params.pageSize.toString());
    return apiClient<AdminReportResult<RevenueReportRow, RevenueReportTotals>>(
      `/api/admin/reports/revenue?${sp.toString()}`
    );
  },

  getShowtimesReport: (params: {
    fromDate?: string;
    toDate?: string;
    movieId?: string;
    cinemaId?: string;
    page?: number;
    pageSize?: number;
  } = {}) => {
    const sp = new URLSearchParams();
    if (params.fromDate) sp.set('fromDate', params.fromDate);
    if (params.toDate) sp.set('toDate', params.toDate);
    if (params.movieId) sp.set('movieId', params.movieId);
    if (params.cinemaId) sp.set('cinemaId', params.cinemaId);
    if (params.page) sp.set('page', params.page.toString());
    if (params.pageSize) sp.set('pageSize', params.pageSize.toString());
    return apiClient<AdminReportResult<ShowtimeReportRow, ShowtimeReportTotals>>(
      `/api/admin/reports/showtimes?${sp.toString()}`
    );
  },

  exportRevenueReportCsv: (params: {
    fromDate?: string;
    toDate?: string;
    movieId?: string;
    cinemaId?: string;
    groupBy?: 'day' | 'movie' | 'cinema' | 'showtime';
  } = {}) => {
    const sp = new URLSearchParams();
    if (params.fromDate) sp.set('fromDate', params.fromDate);
    if (params.toDate) sp.set('toDate', params.toDate);
    if (params.movieId) sp.set('movieId', params.movieId);
    if (params.cinemaId) sp.set('cinemaId', params.cinemaId);
    if (params.groupBy) sp.set('groupBy', params.groupBy);
    const filename = `revenue-${params.fromDate || 'all'}-to-${params.toDate || 'all'}.csv`;
    return apiDownload(`/api/admin/reports/revenue/export?${sp.toString()}`, filename);
  },

  exportShowtimesReportCsv: (params: {
    fromDate?: string;
    toDate?: string;
    movieId?: string;
    cinemaId?: string;
  } = {}) => {
    const sp = new URLSearchParams();
    if (params.fromDate) sp.set('fromDate', params.fromDate);
    if (params.toDate) sp.set('toDate', params.toDate);
    if (params.movieId) sp.set('movieId', params.movieId);
    if (params.cinemaId) sp.set('cinemaId', params.cinemaId);
    const filename = `showtimes-${params.fromDate || 'all'}-to-${params.toDate || 'all'}.csv`;
    return apiDownload(`/api/admin/reports/showtimes/export?${sp.toString()}`, filename);
  },

  // Genres
  getGenres: (page = 1, pageSize = 50) =>
    apiClient<PagedResult<GenreSummary>>(`/api/genres?page=${page}&pageSize=${pageSize}`),

  createGenre: (data: { name: string; slug: string }) =>
    apiClient<GenreSummary>('/api/admin/genres', {
      method: 'POST',
      body: JSON.stringify(data),
      headers: { 'Content-Type': 'application/json' },
    }),

  updateGenre: (id: string, data: { name: string; slug: string }) =>
    apiClient<GenreSummary>(`/api/admin/genres/${id}`, {
      method: 'PUT',
      body: JSON.stringify(data),
      headers: { 'Content-Type': 'application/json' },
    }),

  deleteGenre: (id: string) =>
    apiClient<void>(`/api/admin/genres/${id}`, {
      method: 'DELETE',
    }),

  // Movies
  getMovies: (params: { page?: number; pageSize?: number; search?: string; genreId?: string } = {}) => {
    const sp = new URLSearchParams();
    if (params.page) sp.set('page', params.page.toString());
    if (params.pageSize) sp.set('pageSize', params.pageSize.toString());
    if (params.search) sp.set('search', params.search);
    if (params.genreId) sp.set('genreId', params.genreId);
    return apiClient<PagedResult<MovieSummary>>(`/api/admin/movies?${sp.toString()}`);
  },

  getMovie: (id: string) => apiClient<MovieSummary>(`/api/admin/movies/${id}`),

  createMovie: (data: {
    title: string;
    originalTitle?: string;
    synopsis?: string;
    durationMinutes: number;
    releaseDate?: string;
    ageRating?: string;
    genreIds: string[];
  }) =>
    apiClient<MovieSummary>('/api/admin/movies', {
      method: 'POST',
      body: JSON.stringify(data),
      headers: { 'Content-Type': 'application/json' },
    }),

  updateMovie: (
    id: string,
    data: {
      title: string;
      originalTitle?: string;
      synopsis?: string;
      durationMinutes: number;
      releaseDate?: string;
      ageRating?: string;
      genreIds: string[];
    }
  ) =>
    apiClient<MovieSummary>(`/api/admin/movies/${id}`, {
      method: 'PUT',
      body: JSON.stringify(data),
      headers: { 'Content-Type': 'application/json' },
    }),

  setMovieStatus: (id: string, isActive: boolean) =>
    apiClient<void>(`/api/admin/movies/${id}/status`, {
      method: 'PUT',
      body: JSON.stringify({ isActive }),
      headers: { 'Content-Type': 'application/json' },
    }),

  assignMovieGenres: (id: string, genreIds: string[]) =>
    apiClient<void>(`/api/admin/movies/${id}/genres`, {
      method: 'PUT',
      body: JSON.stringify({ genreIds }),
      headers: { 'Content-Type': 'application/json' },
    }),

  uploadMoviePoster: (id: string, file: File) => {
    const formData = new FormData();
    formData.append('file', file);
    return apiUpload<{ posterUrl: string }>(`/api/admin/movies/${id}/poster`, formData);
  },

  deleteMoviePoster: (id: string) =>
    apiClient<void>(`/api/admin/movies/${id}/poster`, {
      method: 'DELETE',
    }),

  // Cinemas & Rooms
  getCinemas: (page = 1, pageSize = 50) =>
    apiClient<PagedResult<CinemaSummary>>(`/api/admin/cinemas?page=${page}&pageSize=${pageSize}`),

  getCinema: (id: string) => apiClient<CinemaSummary>(`/api/admin/cinemas/${id}`),

  createCinema: (data: { name: string; city: string; address: string }) =>
    apiClient<CinemaSummary>('/api/admin/cinemas', {
      method: 'POST',
      body: JSON.stringify(data),
      headers: { 'Content-Type': 'application/json' },
    }),

  updateCinema: (id: string, data: { name: string; city: string; address: string }) =>
    apiClient<CinemaSummary>(`/api/admin/cinemas/${id}`, {
      method: 'PUT',
      body: JSON.stringify(data),
      headers: { 'Content-Type': 'application/json' },
    }),

  setCinemaStatus: (id: string, isActive: boolean) =>
    apiClient<void>(`/api/admin/cinemas/${id}/status`, {
      method: 'PUT',
      body: JSON.stringify({ isActive }),
      headers: { 'Content-Type': 'application/json' },
    }),

  getRoomsForCinema: (cinemaId: string, page = 1, pageSize = 50) =>
    apiClient<PagedResult<RoomSummary>>(
      `/api/admin/cinemas/${cinemaId}/rooms?page=${page}&pageSize=${pageSize}`
    ),

  getRoom: (id: string) => apiClient<RoomSummary>(`/api/admin/rooms/${id}`),

  createRoom: (cinemaId: string, data: { name: string }) =>
    apiClient<RoomSummary>(`/api/admin/cinemas/${cinemaId}/rooms`, {
      method: 'POST',
      body: JSON.stringify(data),
      headers: { 'Content-Type': 'application/json' },
    }),

  updateRoom: (id: string, data: { name: string }) =>
    apiClient<RoomSummary>(`/api/admin/rooms/${id}`, {
      method: 'PUT',
      body: JSON.stringify(data),
      headers: { 'Content-Type': 'application/json' },
    }),

  setRoomStatus: (id: string, isActive: boolean) =>
    apiClient<void>(`/api/admin/rooms/${id}/status`, {
      method: 'PUT',
      body: JSON.stringify({ isActive }),
      headers: { 'Content-Type': 'application/json' },
    }),

  getRoomSeats: (roomId: string, page = 1, pageSize = 500) =>
    apiClient<PagedResult<AdminSeatSummary>>(
      `/api/admin/rooms/${roomId}/seats?page=${page}&pageSize=${pageSize}`
    ),

  replaceRoomSeatLayout: (roomId: string, rows: RoomSeatLayoutRow[]) =>
    apiClient<void>(`/api/admin/rooms/${roomId}/seats`, {
      method: 'PUT',
      body: JSON.stringify(rows),
      headers: { 'Content-Type': 'application/json' },
    }),

  setSeatStatus: (seatId: string, isActive: boolean) =>
    apiClient<void>(`/api/admin/seats/${seatId}/status`, {
      method: 'PUT',
      body: JSON.stringify({ isActive }),
      headers: { 'Content-Type': 'application/json' },
    }),

  // Showtimes (Admin + Staff)
  getShowtimes: (params: {
    movieId?: string;
    cinemaId?: string;
    date?: string;
    page?: number;
    pageSize?: number;
  } = {}) => {
    const sp = new URLSearchParams();
    if (params.movieId) sp.set('movieId', params.movieId);
    if (params.cinemaId) sp.set('cinemaId', params.cinemaId);
    if (params.date) sp.set('date', params.date);
    if (params.page) sp.set('page', params.page.toString());
    if (params.pageSize) sp.set('pageSize', params.pageSize.toString());
    return apiClient<PagedResult<ShowtimeSummary>>(`/api/admin/showtimes?${sp.toString()}`);
  },

  getShowtime: (id: string) => apiClient<ShowtimeSummary>(`/api/admin/showtimes/${id}`),

  createShowtime: (data: {
    movieId: string;
    roomId: string;
    startAtUtc: string;
    basePrice: number;
  }) =>
    apiClient<ShowtimeSummary>('/api/admin/showtimes', {
      method: 'POST',
      body: JSON.stringify(data),
      headers: { 'Content-Type': 'application/json' },
    }),

  rescheduleShowtime: (
    id: string,
    data: {
      startAtUtc: string;
      basePrice: number;
    }
  ) =>
    apiClient<ShowtimeSummary>(`/api/admin/showtimes/${id}`, {
      method: 'PUT',
      body: JSON.stringify(data),
      headers: { 'Content-Type': 'application/json' },
    }),

  cancelShowtime: (id: string) =>
    apiClient<void>(`/api/admin/showtimes/${id}/cancel`, {
      method: 'POST',
    }),

  getShowtimeSeats: (id: string) =>
    apiClient<ShowtimeSeatSummary[]>(`/api/admin/showtimes/${id}/seats`),

  // Users
  getUsers: (page = 1, pageSize = 20) =>
    apiClient<AdminUserPage>(`/api/admin/users?page=${page}&pageSize=${pageSize}`),

  getUser: (id: string) => apiClient<AdminUserSummary>(`/api/admin/users/${id}`),

  setUserStatus: (id: string, isActive: boolean) =>
    apiClient<void>(`/api/admin/users/${id}/status`, {
      method: 'PUT',
      body: JSON.stringify({ isActive }),
      headers: { 'Content-Type': 'application/json' },
    }),

  setUserRole: (id: string, role: 'Customer' | 'Staff') =>
    apiClient<void>(`/api/admin/users/${id}/role`, {
      method: 'PUT',
      body: JSON.stringify({ role }),
      headers: { 'Content-Type': 'application/json' },
    }),

  // Payments
  listPayments: (params: {
    bookingId?: string;
    status?: string;
    processingStatus?: string;
    page?: number;
    pageSize?: number;
  } = {}) => {
    const sp = new URLSearchParams();
    if (params.bookingId) sp.set('bookingId', params.bookingId);
    if (params.status) sp.set('status', params.status);
    if (params.processingStatus) sp.set('processingStatus', params.processingStatus);
    if (params.page) sp.set('page', params.page.toString());
    if (params.pageSize) sp.set('pageSize', params.pageSize.toString());
    return apiClient<PagedResult<AdminPaymentView>>(`/api/admin/payments?${sp.toString()}`);
  },

  getPayment: (id: string) => apiClient<AdminPaymentView>(`/api/admin/payments/${id}`),

  recordRefund: (id: string, data: { reference: string; note?: string }) =>
    apiClient<AdminPaymentView>(`/api/admin/payments/${id}/record-refund`, {
      method: 'POST',
      body: JSON.stringify(data),
      headers: { 'Content-Type': 'application/json' },
    }),

  reconcilePayment: (id: string) =>
    apiClient<{ rspCode: string; message: string; payment: PaymentView }>(
      `/api/admin/payments/${id}/reconcile`,
      {
        method: 'POST',
      }
    ),

  // Discovery AI Index
  getDiscoveryIndexStatus: () =>
    apiClient<DiscoveryIndexStatus>('/api/admin/discovery/index'),

  rebuildDiscoveryIndex: () =>
    apiClient<DiscoveryRebuildResult>('/api/admin/discovery/index/rebuild', {
      method: 'POST',
    }),
};
