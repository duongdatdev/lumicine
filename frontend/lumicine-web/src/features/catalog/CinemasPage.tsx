import React, { useState } from 'react';
import { useQuery } from '@tanstack/react-query';
import { Link } from 'react-router-dom';
import { catalogApi } from '../../api/catalog';
import type { ShowtimeSummary } from '../../api/types';
import { Card } from '../../components/ui/Card';
import { Badge } from '../../components/ui/Badge';
import { Button } from '../../components/ui/Button';
import { LoadingState } from '../../components/common/LoadingState';
import { EmptyState } from '../../components/common/EmptyState';
import { ErrorAlert } from '../../components/common/ErrorAlert';
import { formatTimeUtc7, formatVND } from '../../lib/utils';
import { Building2, MapPin, Calendar, Film } from 'lucide-react';

export const CinemasPage: React.FC = () => {
  const [selectedCity, setSelectedCity] = useState<string>('');
  const [selectedCinemaId, setSelectedCinemaId] = useState<string | null>(null);

  // Fetch cinemas
  const {
    data: cinemasData,
    isLoading: isCinemasLoading,
    error: cinemasError,
  } = useQuery({
    queryKey: ['cinemas', selectedCity],
    queryFn: () => catalogApi.getCinemas({ city: selectedCity || undefined, pageSize: 50 }),
  });

  const cinemas = cinemasData?.items || [];

  // Extract cities
  const cities = Array.from(new Set(cinemas.map((c) => c.city))).filter(Boolean);

  // Fetch showtimes for selected cinema if any
  const todayStr = new Date().toISOString().split('T')[0];
  const {
    data: showtimesData,
    isLoading: isShowtimesLoading,
  } = useQuery({
    queryKey: ['cinemaShowtimes', selectedCinemaId],
    queryFn: () =>
      catalogApi.getShowtimes({
        cinemaId: selectedCinemaId!,
        date: todayStr,
        pageSize: 100,
      }),
    enabled: !!selectedCinemaId,
  });

  // Group showtimes by movie
  const showtimesByMovie = (showtimesData?.items || []).reduce<
    Record<string, { title: string; list: ShowtimeSummary[] }>
  >((acc, s) => {
    if (!acc[s.movieId]) {
      acc[s.movieId] = { title: s.movieTitle, list: [] };
    }
    acc[s.movieId].list.push(s);
    return acc;
  }, {});

  return (
    <div className="container mx-auto px-4 py-8 space-y-8">
      <div>
        <h1 className="font-heading text-3xl sm:text-4xl font-black text-foreground">Hệ thống Cụm rạp</h1>
        <p className="text-sm text-foreground-muted mt-1">
          Chọn rạp chiếu để xem các suất chiếu hôm nay.
        </p>
      </div>

      {/* City Filters */}
      {cities.length > 0 && (
        <div className="flex gap-2 overflow-x-auto pb-2">
          <button
            onClick={() => setSelectedCity('')}
            className={`px-3.5 py-1.5 rounded-full text-xs font-semibold whitespace-nowrap transition-colors ${
              !selectedCity
                ? 'bg-primary text-white shadow-sm'
                : 'bg-background-subtle border border-border text-foreground hover:bg-border/60'
            }`}
          >
            Tất cả thành phố
          </button>
          {cities.map((city) => (
            <button
              key={city}
              onClick={() => setSelectedCity(city)}
              className={`px-3.5 py-1.5 rounded-full text-xs font-semibold whitespace-nowrap transition-colors ${
                selectedCity === city
                  ? 'bg-primary text-white shadow-sm'
                  : 'bg-background-subtle border border-border text-foreground hover:bg-border/60'
              }`}
            >
              {city}
            </button>
          ))}
        </div>
      )}

      {isCinemasLoading ? (
        <LoadingState message="Đang tải danh sách rạp..." />
      ) : cinemasError ? (
        <ErrorAlert error={cinemasError} title="Không thể tải danh sách rạp" />
      ) : cinemas.length === 0 ? (
        <EmptyState title="Không có rạp chiếu" description="Hiện chưa có rạp chiếu nào tại khu vực này." />
      ) : (
        <div className="grid grid-cols-1 md:grid-cols-3 gap-6">
          {/* Cinema List */}
          <div className="space-y-3 md:col-span-1">
            {cinemas.map((cinema) => {
              const isSelected = selectedCinemaId === cinema.id;
              return (
                <Card
                  key={cinema.id}
                  onClick={() => setSelectedCinemaId(cinema.id)}
                  className={`p-4 cursor-pointer transition-all border ${
                    isSelected
                      ? 'border-primary ring-1 ring-primary bg-primary-light/10 shadow-sm'
                      : 'hover:border-border-strong'
                  }`}
                >
                  <div className="flex items-start gap-3">
                    <Building2 className={`w-5 h-5 shrink-0 mt-0.5 ${isSelected ? 'text-primary' : 'text-foreground-muted'}`} />
                    <div className="space-y-1">
                      <h3 className="font-heading font-bold text-base text-foreground">
                        {cinema.name}
                      </h3>
                      <p className="text-xs text-foreground-muted flex items-center gap-1">
                        <MapPin className="w-3.5 h-3.5 shrink-0" />
                        {cinema.address}, {cinema.city}
                      </p>
                    </div>
                  </div>
                </Card>
              );
            })}
          </div>

          {/* Showtimes for selected cinema */}
          <div className="md:col-span-2">
            {!selectedCinemaId ? (
              <Card className="p-8 text-center text-foreground-muted bg-background-subtle/30 border-dashed">
                <Calendar className="w-8 h-8 mx-auto mb-2 opacity-50" />
                <p className="text-sm font-medium">Chọn một rạp chiếu để xem các suất chiếu hôm nay.</p>
              </Card>
            ) : isShowtimesLoading ? (
              <LoadingState message="Đang tải lịch chiếu hôm nay..." />
            ) : Object.keys(showtimesByMovie).length === 0 ? (
              <EmptyState
                icon={<Film className="w-6 h-6 text-foreground-muted" />}
                title="Hôm nay rạp chưa có suất chiếu"
                description="Vui lòng kiểm tra lại sau hoặc xem các phim sắp chiếu."
              />
            ) : (
              <div className="space-y-6">
                <h2 className="font-heading text-xl font-bold text-foreground">
                  Lịch chiếu hôm nay tại rạp
                </h2>

                <div className="space-y-4">
                  {Object.entries(showtimesByMovie).map(([movieId, movieGroup]) => (
                    <Card key={movieId} className="p-5 space-y-3">
                      <div className="flex items-center justify-between border-b border-border pb-2">
                        <Link
                          to={`/movies/${movieId}`}
                          className="font-heading font-bold text-lg text-foreground hover:text-primary transition-colors"
                        >
                          {movieGroup.title}
                        </Link>
                        <Link to={`/movies/${movieId}`}>
                          <Button variant="ghost" size="sm" className="text-xs text-primary">
                            Xem phim
                          </Button>
                        </Link>
                      </div>

                      <div className="flex flex-wrap gap-2.5">
                        {movieGroup.list.map((st) => (
                          <Link
                            key={st.id}
                            to={`/showtimes/${st.id}/seats`}
                            className="p-2.5 rounded-lg border border-border bg-background hover:border-primary hover:bg-primary-light/20 transition-all text-center focus-visible:outline-none focus-visible:ring-2 focus-visible:ring-primary"
                          >
                            <span className="font-heading font-black text-base text-foreground block">
                              {formatTimeUtc7(st.startAtUtc)}
                            </span>
                            <span className="text-[10px] text-foreground-muted block">
                              {st.roomName}
                            </span>
                            <span className="text-xs font-semibold text-primary block mt-0.5">
                              {formatVND(st.basePrice)}
                            </span>
                          </Link>
                        ))}
                      </div>
                    </Card>
                  ))}
                </div>
              </div>
            )}
          </div>
        </div>
      )}
    </div>
  );
};
