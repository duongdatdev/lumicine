import React, { useState } from 'react';
import { useQuery } from '@tanstack/react-query';
import { Link, useSearchParams } from 'react-router-dom';
import { catalogApi } from '../../api/catalog';
import { MoviePoster } from '../../components/common/PosterPlaceholder';
import { LoadingState } from '../../components/common/LoadingState';
import { EmptyState } from '../../components/common/EmptyState';
import { ErrorAlert } from '../../components/common/ErrorAlert';
import { Button } from '../../components/ui/Button';
import { Badge } from '../../components/ui/Badge';
import { Pagination } from '../../components/ui/Pagination';
import { Search, Sparkles, Clock, Ticket } from 'lucide-react';

export const HomePage: React.FC = () => {
  const [searchParams, setSearchParams] = useSearchParams();
  const search = searchParams.get('search') || '';
  const genreId = searchParams.get('genreId') || '';
  const page = parseInt(searchParams.get('page') || '1', 10);

  const [searchInput, setSearchInput] = useState(search);

  // Fetch genres
  const { data: genresData } = useQuery({
    queryKey: ['genres'],
    queryFn: () => catalogApi.getGenres({ pageSize: 50 }),
  });

  // Fetch movies
  const {
    data: moviesData,
    isLoading,
    error,
  } = useQuery({
    queryKey: ['movies', { page, search, genreId }],
    queryFn: () => catalogApi.getMovies({ page, pageSize: 12, search, genreId }),
  });

  const handleSearchSubmit = (e: React.FormEvent) => {
    e.preventDefault();
    const newParams = new URLSearchParams(searchParams);
    if (searchInput.trim()) {
      newParams.set('search', searchInput.trim());
    } else {
      newParams.delete('search');
    }
    newParams.set('page', '1');
    setSearchParams(newParams);
  };

  const handleGenreSelect = (id: string) => {
    const newParams = new URLSearchParams(searchParams);
    if (genreId === id) {
      newParams.delete('genreId');
    } else {
      newParams.set('genreId', id);
    }
    newParams.set('page', '1');
    setSearchParams(newParams);
  };

  const handlePageChange = (newPage: number) => {
    const newParams = new URLSearchParams(searchParams);
    newParams.set('page', newPage.toString());
    setSearchParams(newParams);
    window.scrollTo({ top: 0, behavior: 'smooth' });
  };

  return (
    <div className="space-y-10 pb-16">
      {/* Hero Banner */}
      <section className="relative overflow-hidden bg-gradient-to-r from-primary-hover via-primary to-accent py-16 text-white">
        <div className="container mx-auto px-4 relative z-10 flex flex-col items-center text-center max-w-3xl space-y-4">
          <Badge className="bg-white/20 text-white border-white/30 hover:bg-white/30 backdrop-blur">
            <Sparkles className="w-3.5 h-3.5 mr-1" /> Trợ lý AI gợi ý phim thông minh
          </Badge>
          <h1 className="font-heading text-4xl sm:text-5xl md:text-6xl font-black tracking-tight">
            Trải nghiệm điện ảnh đỉnh cao tại LumiCine
          </h1>
          <p className="text-white/80 text-base sm:text-lg max-w-xl">
            Đặt vé nhanh chóng, chọn ghế realtime theo ý muốn và khám phá các tác phẩm điện ảnh xuất sắc nhất hôm nay.
          </p>
          <div className="pt-2 flex flex-wrap gap-3 justify-center">
            <Link to="/discover">
              <Button variant="accent" size="lg" className="shadow-lg">
                <Sparkles className="w-5 h-5 mr-2" /> Trò chuyện với AI
              </Button>
            </Link>
            <Link to="/cinemas">
              <Button variant="outline" size="lg" className="bg-white/10 text-white border-white/30 hover:bg-white/20">
                Xem cụm rạp
              </Button>
            </Link>
          </div>
        </div>
      </section>

      {/* Main Catalog Section */}
      <div className="container mx-auto px-4 space-y-6">
        {/* Search & Filters */}
        <div className="flex flex-col md:flex-row gap-4 items-center justify-between">
          {/* Search bar */}
          <form onSubmit={handleSearchSubmit} className="relative w-full md:max-w-md">
            <input
              type="text"
              placeholder="Tìm kiếm phim theo tên..."
              value={searchInput}
              onChange={(e) => setSearchInput(e.target.value)}
              className="w-full h-11 pl-10 pr-4 rounded-xl border border-border bg-background-surface text-sm text-foreground focus:outline-none focus:ring-2 focus:ring-primary focus:border-transparent"
            />
            <Search className="w-4 h-4 text-foreground-muted absolute left-3.5 top-3.5" />
            {searchInput && (
              <button
                type="button"
                onClick={() => {
                  setSearchInput('');
                  const newParams = new URLSearchParams(searchParams);
                  newParams.delete('search');
                  setSearchParams(newParams);
                }}
                className="absolute right-3 top-3 text-xs text-foreground-muted hover:text-foreground"
              >
                Xóa
              </button>
            )}
          </form>

          {/* Quick AI Discover CTA */}
          <Link
            to="/discover"
            className="hidden sm:flex items-center gap-2 text-sm font-semibold text-accent hover:underline"
          >
            <Sparkles className="w-4 h-4" /> Bạn chưa biết xem gì? Hỏi AI ngay
          </Link>
        </div>

        {/* Genre Filter Pills */}
        {genresData?.items && genresData.items.length > 0 && (
          <div className="flex items-center gap-2 overflow-x-auto pb-2 scrollbar-none">
            <button
              onClick={() => handleGenreSelect('')}
              className={`px-3.5 py-1.5 rounded-full text-xs font-semibold whitespace-nowrap transition-colors ${
                !genreId
                  ? 'bg-primary text-white shadow-sm'
                  : 'bg-background-subtle border border-border text-foreground hover:bg-border/60'
              }`}
            >
              Tất cả thể loại
            </button>
            {genresData.items.map((g) => {
              const isSelected = genreId === g.id;
              return (
                <button
                  key={g.id}
                  onClick={() => handleGenreSelect(g.id)}
                  className={`px-3.5 py-1.5 rounded-full text-xs font-semibold whitespace-nowrap transition-colors ${
                    isSelected
                      ? 'bg-primary text-white shadow-sm'
                      : 'bg-background-subtle border border-border text-foreground hover:bg-border/60'
                  }`}
                >
                  {g.name}
                </button>
              );
            })}
          </div>
        )}

        {/* Movie Grid */}
        {isLoading ? (
          <LoadingState message="Đang tải danh sách phim..." />
        ) : error ? (
          <ErrorAlert error={error} title="Không thể tải danh sách phim" />
        ) : !moviesData?.items || moviesData.items.length === 0 ? (
          <EmptyState
            title="Không tìm thấy phim phù hợp"
            description="Hãy thử từ khóa tìm kiếm khác hoặc chuyển sang thể loại khác."
            action={
              <Button
                variant="outline"
                size="sm"
                onClick={() => {
                  setSearchInput('');
                  setSearchParams({});
                }}
              >
                Xóa bộ lọc
              </Button>
            }
          />
        ) : (
          <div>
            <div className="grid grid-cols-2 sm:grid-cols-3 md:grid-cols-4 lg:grid-cols-6 gap-5">
              {moviesData.items.map((movie) => (
                <div
                  key={movie.id}
                  className="group flex flex-col rounded-xl overflow-hidden border border-border bg-background-surface hover:shadow-lg transition-all"
                >
                  <Link to={`/movies/${movie.id}`} className="relative block overflow-hidden">
                    <MoviePoster
                      url={movie.posterUrl}
                      title={movie.title}
                      aspectRatio="poster"
                    />
                    {movie.ageRating && (
                      <span className="absolute top-2 left-2 px-2 py-0.5 rounded text-[10px] font-bold bg-black/70 text-white backdrop-blur">
                        {movie.ageRating}
                      </span>
                    )}
                  </Link>

                  <div className="p-3 flex-1 flex flex-col justify-between space-y-2">
                    <div>
                      <Link
                        to={`/movies/${movie.id}`}
                        className="font-heading font-bold text-base text-foreground line-clamp-1 group-hover:text-primary transition-colors"
                        title={movie.title}
                      >
                        {movie.title}
                      </Link>
                      <div className="flex items-center gap-1 text-xs text-foreground-muted mt-1">
                        <Clock className="w-3.5 h-3.5" />
                        <span>{movie.durationMinutes} phút</span>
                      </div>
                    </div>

                    <div className="space-y-2">
                      <div className="flex flex-wrap gap-1">
                        {movie.genres?.slice(0, 2).map((g) => (
                          <span
                            key={g.id}
                            className="px-1.5 py-0.5 rounded bg-background-subtle text-[10px] text-foreground-muted"
                          >
                            {g.name}
                          </span>
                        ))}
                      </div>

                      <Link to={`/movies/${movie.id}`} className="block w-full">
                        <Button variant="primary" size="sm" className="w-full text-xs">
                          <Ticket className="w-3.5 h-3.5 mr-1" /> Mua vé
                        </Button>
                      </Link>
                    </div>
                  </div>
                </div>
              ))}
            </div>

            <Pagination
              currentPage={moviesData.page}
              totalPages={moviesData.totalPages}
              onPageChange={handlePageChange}
            />
          </div>
        )}
      </div>
    </div>
  );
};
