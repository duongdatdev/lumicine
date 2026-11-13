import { apiClient } from './client';
import type {
  MovieSummary,
  GenreSummary,
  CinemaSummary,
  ShowtimeSummary,
  ShowtimeSeatSummary,
  PagedResult,
} from './types';

export const catalogApi = {
  getMovies: (params: { page?: number; pageSize?: number; search?: string; genreId?: string } = {}) => {
    const searchParams = new URLSearchParams();
    if (params.page) searchParams.set('page', params.page.toString());
    if (params.pageSize) searchParams.set('pageSize', params.pageSize.toString());
    if (params.search) searchParams.set('search', params.search);
    if (params.genreId) searchParams.set('genreId', params.genreId);
    return apiClient<PagedResult<MovieSummary>>(`/api/movies?${searchParams.toString()}`);
  },

  getMovie: (id: string) => apiClient<MovieSummary>(`/api/movies/${id}`),

  getGenres: (params: { page?: number; pageSize?: number } = {}) => {
    const searchParams = new URLSearchParams();
    if (params.page) searchParams.set('page', params.page.toString());
    if (params.pageSize) searchParams.set('pageSize', params.pageSize.toString());
    return apiClient<PagedResult<GenreSummary>>(`/api/genres?${searchParams.toString()}`);
  },

  getCinemas: (params: { page?: number; pageSize?: number; city?: string } = {}) => {
    const searchParams = new URLSearchParams();
    if (params.page) searchParams.set('page', params.page.toString());
    if (params.pageSize) searchParams.set('pageSize', params.pageSize.toString());
    if (params.city) searchParams.set('city', params.city);
    return apiClient<PagedResult<CinemaSummary>>(`/api/cinemas?${searchParams.toString()}`);
  },

  getCinema: (id: string) => apiClient<CinemaSummary>(`/api/cinemas/${id}`),

  getShowtimes: (params: { movieId?: string; cinemaId?: string; date?: string; page?: number; pageSize?: number } = {}) => {
    const searchParams = new URLSearchParams();
    if (params.movieId) searchParams.set('movieId', params.movieId);
    if (params.cinemaId) searchParams.set('cinemaId', params.cinemaId);
    if (params.date) searchParams.set('date', params.date);
    if (params.page) searchParams.set('page', params.page.toString());
    if (params.pageSize) searchParams.set('pageSize', params.pageSize.toString());
    return apiClient<PagedResult<ShowtimeSummary>>(`/api/showtimes?${searchParams.toString()}`);
  },

  getShowtime: (id: string) => apiClient<ShowtimeSummary>(`/api/showtimes/${id}`),

  getShowtimeSeats: (showtimeId: string) =>
    apiClient<ShowtimeSeatSummary[]>(`/api/showtimes/${showtimeId}/seats`),
};
