import React, { useState } from 'react';
import { useParams, Link } from 'react-router-dom';
import { useQuery } from '@tanstack/react-query';
import { catalogApi } from '../../api/catalog';
import type { ShowtimeSummary } from '../../api/types';
import { MoviePoster } from '../../components/common/PosterPlaceholder';
import { LoadingState } from '../../components/common/LoadingState';
import { EmptyState } from '../../components/common/EmptyState';
import { ErrorAlert } from '../../components/common/ErrorAlert';
import { Button } from '../../components/ui/Button';
import { Badge } from '../../components/ui/Badge';
import { Card } from '../../components/ui/Card';
import { formatTimeUtc7, formatDateUtc7, formatVND } from '../../lib/utils';
import { Clock, Calendar, Film, Sparkles, MapPin, Ticket } from 'lucide-react';

export const MovieDetailPage: React.FC = () => {
  const { id } = useParams<{ id: string }>();

  // Generate next 7 days for the date selector
  const today = new Date();
  const dateTabs = Array.from({ length: 7 }).map((_, i) => {
    const d = new Date();
    d.setDate(today.getDate() + i);
    const dateStr = d.toISOString().split('T')[0];
    return {
      dateStr,
      label: i === 0 ? 'Hôm nay' : i === 1 ? 'Ngày mai' : formatDateUtc7(d),
      dayOfWeek: d.toLocaleDateString('vi-VN', { weekday: 'short' }),
    };
  });

  const [selectedDate, setSelectedDate] = useState<string>(dateTabs[0].dateStr);

  // Fetch movie details
  const {
    data: movie,
    isLoading: isMovieLoading,
    error: movieError,
  } = useQuery({
    queryKey: ['movie', id],
    queryFn: () => catalogApi.getMovie(id!),
    enabled: !!id,
  });

  // Fetch showtimes for this movie and selected date
  const {
    data: showtimesData,
    isLoading: isShowtimesLoading,
    error: showtimesError,
  } = useQuery({
    queryKey: ['showtimes', { movieId: id, date: selectedDate }],
    queryFn: () => catalogApi.getShowtimes({ movieId: id, date: selectedDate, pageSize: 100 }),
    enabled: !!id,
  });

  if (isMovieLoading) {
    return <LoadingState message="Đang tải thông tin phim..." />;
  }

  if (movieError || !movie) {
    return (
      <div className="container mx-auto px-4 py-12">
        <ErrorAlert error={movieError || 'Không tìm thấy phim yêu cầu'} />
      </div>
    );
  }

  // Group showtimes by cinema
  const showtimesByCinema = (showtimesData?.items || []).reduce<
    Record<string, { cinemaName: string; cinemaCity: string; list: ShowtimeSummary[] }>
  >((acc, s) => {
    if (!acc[s.cinemaId]) {
      acc[s.cinemaId] = {
        cinemaName: s.cinemaName,
        cinemaCity: s.cinemaCity,
        list: [],
      };
    }
    acc[s.cinemaId].list.push(s);
    return acc;
  }, {});

  return (
    <div className="container mx-auto px-4 py-8 space-y-10">
      {/* Movie Info Header */}
      <div className="grid grid-cols-1 md:grid-cols-4 gap-8 items-start">
        <div className="md:col-span-1 max-w-[260px] mx-auto md:mx-0 w-full">
          <MoviePoster url={movie.posterUrl} title={movie.title} className="shadow-lg" />
        </div>

        <div className="md:col-span-3 space-y-4">
          <div className="space-y-1">
            <div className="flex flex-wrap items-center gap-2">
              <h1 className="font-heading text-3xl sm:text-4xl font-black text-foreground">
                {movie.title}
              </h1>
              {movie.ageRating && (
                <Badge variant="warning" className="font-bold">
                  {movie.ageRating}
                </Badge>
              )}
            </div>
            {movie.originalTitle && (
              <p className="text-base text-foreground-muted italic">{movie.originalTitle}</p>
            )}
          </div>

          <div className="flex flex-wrap items-center gap-4 text-sm text-foreground-muted">
            <span className="flex items-center gap-1.5">
              <Clock className="w-4 h-4 text-primary" />
              <strong>{movie.durationMinutes} phút</strong>
            </span>
            {movie.releaseDate && (
              <span className="flex items-center gap-1.5">
                <Calendar className="w-4 h-4 text-primary" />
                Khởi chiếu: {formatDateUtc7(movie.releaseDate)}
              </span>
            )}
          </div>

          <div className="flex flex-wrap gap-1.5">
            {movie.genres?.map((g) => (
              <Badge key={g.id} variant="neutral">
                {g.name}
              </Badge>
            ))}
          </div>

          {movie.synopsis && (
            <div className="space-y-1 pt-2">
              <h3 className="text-sm font-semibold text-foreground uppercase tracking-wider">
                Tóm tắt nội dung
              </h3>
              <p className="text-sm text-foreground-muted leading-relaxed whitespace-pre-line">
                {movie.synopsis}
              </p>
            </div>
          )}

          <div className="pt-2">
            <Link to={`/discover?q=${encodeURIComponent(`Gợi ý lịch chiếu cho phim ${movie.title}`)}`}>
              <Button variant="outline" size="sm" className="text-accent border-accent/30 hover:bg-accent/5">
                <Sparkles className="w-4 h-4 mr-1.5 text-accent" /> Hỏi AI về các suất chiếu phù hợp
              </Button>
            </Link>
          </div>
        </div>
      </div>

      {/* Showtimes Schedule Section */}
      <div className="space-y-6 pt-6 border-t border-border">
        <div className="flex flex-col sm:flex-row sm:items-center justify-between gap-4">
          <div>
            <h2 className="font-heading text-2xl font-bold text-foreground">Lịch chiếu & Đặt vé</h2>
            <p className="text-sm text-foreground-muted">Chọn ngày và rạp chiếu để chọn ghế</p>
          </div>
        </div>

        {/* Date Selector Tabs */}
        <div className="flex gap-2 overflow-x-auto pb-2 scrollbar-none">
          {dateTabs.map((tab) => {
            const isSelected = selectedDate === tab.dateStr;
            return (
              <button
                key={tab.dateStr}
                onClick={() => setSelectedDate(tab.dateStr)}
                className={`px-4 py-2.5 rounded-xl text-center min-w-[100px] transition-all border ${
                  isSelected
                    ? 'bg-primary text-white border-primary shadow-sm'
                    : 'bg-background-surface border-border text-foreground hover:bg-background-subtle'
                }`}
              >
                <div className="text-xs uppercase font-medium opacity-80">{tab.dayOfWeek}</div>
                <div className="text-sm font-bold mt-0.5">{tab.label}</div>
              </button>
            );
          })}
        </div>

        {/* Showtimes List */}
        {isShowtimesLoading ? (
          <LoadingState message="Đang tải danh sách suất chiếu..." />
        ) : showtimesError ? (
          <ErrorAlert error={showtimesError} title="Không thể tải suất chiếu" />
        ) : Object.keys(showtimesByCinema).length === 0 ? (
          <EmptyState
            icon={<Ticket className="w-6 h-6 text-foreground-muted" />}
            title="Chưa có suất chiếu vào ngày này"
            description="Vui lòng chọn ngày khác hoặc quay lại sau."
          />
        ) : (
          <div className="space-y-4">
            {Object.entries(showtimesByCinema).map(([cinemaId, group]) => (
              <Card key={cinemaId} className="p-5 space-y-4">
                <div className="flex items-center gap-2 border-b border-border pb-3">
                  <MapPin className="w-5 h-5 text-primary" />
                  <div>
                    <h3 className="font-heading font-bold text-lg text-foreground">
                      {group.cinemaName}
                    </h3>
                    <p className="text-xs text-foreground-muted">{group.cinemaCity}</p>
                  </div>
                </div>

                <div className="grid grid-cols-2 sm:grid-cols-3 md:grid-cols-4 lg:grid-cols-6 gap-3">
                  {group.list.map((st) => (
                    <Link
                      key={st.id}
                      to={`/showtimes/${st.id}/seats`}
                      className="group flex flex-col items-center justify-center p-3 rounded-xl border border-border bg-background hover:border-primary hover:bg-primary-light/20 transition-all text-center focus-visible:outline-none focus-visible:ring-2 focus-visible:ring-primary"
                    >
                      <span className="font-heading font-black text-xl text-foreground group-hover:text-primary">
                        {formatTimeUtc7(st.startAtUtc)}
                      </span>
                      <span className="text-[11px] text-foreground-muted mt-0.5">
                        {st.roomName}
                      </span>
                      <span className="text-xs font-semibold text-primary mt-1">
                        {formatVND(st.basePrice)}
                      </span>
                    </Link>
                  ))}
                </div>
              </Card>
            ))}
          </div>
        )}
      </div>
    </div>
  );
};
