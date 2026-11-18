import React, { useState } from 'react';
import { useQuery } from '@tanstack/react-query';
import { Armchair, BookOpen, Building2, ChevronRight, Film, Search } from 'lucide-react';
import { adminApi } from '../../api/admin';
import type {
  AdminSeatSummary,
  CinemaSummary,
  GenreSummary,
  MovieSummary,
  PagedResult,
  RoomSummary,
} from '../../api/types';
import { EmptyState } from '../../components/common/EmptyState';
import { ErrorAlert } from '../../components/common/ErrorAlert';
import { LoadingState } from '../../components/common/LoadingState';
import { Badge } from '../../components/ui/Badge';
import { Card } from '../../components/ui/Card';
import { Input } from '../../components/ui/Input';
import { Pagination } from '../../components/ui/Pagination';

type Section = 'movies' | 'genres' | 'cinemas' | 'rooms';
const PAGE_SIZE = 12;
const sections: { id: Section; label: string; icon: React.ReactNode }[] = [
  { id: 'movies', label: 'Phim', icon: <Film className="h-4 w-4" /> },
  { id: 'genres', label: 'Thể loại', icon: <BookOpen className="h-4 w-4" /> },
  { id: 'cinemas', label: 'Rạp chiếu', icon: <Building2 className="h-4 w-4" /> },
  { id: 'rooms', label: 'Phòng & ghế', icon: <Armchair className="h-4 w-4" /> },
];

export const StaffCatalogPage: React.FC = () => {
  const [section, setSection] = useState<Section>('movies');
  const [page, setPage] = useState(1);
  const [search, setSearch] = useState('');
  const [cinema, setCinema] = useState<CinemaSummary | null>(null);
  const [room, setRoom] = useState<RoomSummary | null>(null);

  const movies = useQuery({
    queryKey: ['staffCatalogMovies', page, search],
    queryFn: () => adminApi.getMovies({ page, pageSize: PAGE_SIZE, search: search || undefined }),
  });
  const genres = useQuery({
    queryKey: ['staffCatalogGenres', page],
    queryFn: () => adminApi.getGenres(page, PAGE_SIZE),
  });
  const cinemas = useQuery({
    queryKey: ['staffCatalogCinemas', page],
    queryFn: () => adminApi.getCinemas(page, PAGE_SIZE),
  });
  const allCinemas = useQuery({
    queryKey: ['staffCatalogAllCinemas'],
    queryFn: () => fetchAll((p, size) => adminApi.getCinemas(p, size)),
  });
  const rooms = useQuery({
    queryKey: ['staffCatalogRooms', cinema?.id, page],
    queryFn: () => adminApi.getRoomsForCinema(cinema!.id, page, PAGE_SIZE),
    enabled: section === 'rooms' && !!cinema,
  });
  const seats = useQuery({
    queryKey: ['staffCatalogSeats', room?.id],
    queryFn: () => fetchAll((p, size) => adminApi.getRoomSeats(room!.id, p, size)),
    enabled: !!room,
  });

  const changeSection = (next: Section) => {
    setSection(next);
    setPage(1);
    setSearch('');
  };
  const data = section === 'movies' ? movies.data
    : section === 'genres' ? genres.data
      : section === 'cinemas' ? cinemas.data : rooms.data;
  const totalPages = Math.ceil((data?.totalCount ?? 0) / PAGE_SIZE);

  return (
    <div className="space-y-6">
      <header>
        <p className="text-xs font-bold uppercase tracking-[.2em] text-amber-700">LumiCine · Nhân viên</p>
        <h1 className="mt-1 font-heading text-3xl font-black">Danh mục hệ thống</h1>
        <p className="mt-1 text-sm text-foreground-muted">
          Tra cứu phim, thể loại, rạp, phòng chiếu và sơ đồ ghế.
        </p>
      </header>

      <div className="grid grid-cols-2 gap-2 sm:grid-cols-4" aria-label="Danh mục">
        {sections.map((tab) => (
          <button
            key={tab.id}
            type="button"
            aria-pressed={section === tab.id}
            onClick={() => changeSection(tab.id)}
            className={`flex min-h-11 items-center justify-center gap-2 rounded-xl border px-3 text-sm font-semibold transition-colors ${section === tab.id
              ? 'border-amber-300 bg-amber-100 text-amber-950'
              : 'border-border bg-background-surface text-foreground-muted hover:bg-background-subtle'}`}
          >
            {tab.icon}{tab.label}
          </button>
        ))}
      </div>

      {section === 'movies' && (
        <div className="relative">
          <Search className="absolute left-3 top-3 h-4 w-4 text-foreground-muted" />
          <Input
            aria-label="Tìm phim"
            placeholder="Tìm theo tên phim..."
            className="pl-9"
            value={search}
            onChange={(event) => { setSearch(event.target.value); setPage(1); }}
          />
        </div>
      )}

      {section === 'rooms' && (
        <Card className="space-y-3 p-4">
          <label className="block text-sm font-medium">
            Rạp chiếu
            <select
              className="mt-1 h-10 w-full rounded-lg border border-border bg-background-surface px-3"
              value={cinema?.id ?? ''}
              disabled={allCinemas.isLoading || !!allCinemas.error}
              onChange={(event) => {
                const selected = allCinemas.data?.find((item) => item.id === event.target.value) ?? null;
                setCinema(selected);
                setRoom(null);
                setPage(1);
              }}
            >
              <option value="">{allCinemas.isLoading ? 'Đang tải rạp...' : 'Chọn rạp'}</option>
              {allCinemas.data?.map((item) => (
                <option key={item.id} value={item.id}>{item.name} · {item.city}</option>
              ))}
            </select>
          </label>
          {allCinemas.error && <ErrorAlert error={allCinemas.error} title="Không thể tải danh sách rạp" />}
          {room && (
            <button
              type="button"
              onClick={() => setRoom(null)}
              className="inline-flex items-center gap-1 text-sm font-semibold text-amber-800 hover:underline"
            >
              {room.name} <span aria-hidden="true">×</span>
            </button>
          )}
        </Card>
      )}

      {section === 'cinemas' && (
        <div className="flex items-center justify-between text-sm text-foreground-muted">
          <span>Danh sách rạp đang hoạt động và ngừng hoạt động</span>
          <span>{cinemas.data?.totalCount ?? 0} rạp</span>
        </div>
      )}

      {section === 'rooms' && room && (
        <SeatMap query={seats} seats={seats.data} room={room} />
      )}

      {section === 'rooms' && !cinema && (
        <EmptyState title="Chọn rạp để xem phòng" description="Chọn rạp ở phía trên để tra cứu phòng chiếu và sơ đồ ghế." />
      )}
      {section === 'rooms' && cinema && !room && (
        <CatalogList query={rooms} items={rooms.data?.items} empty="Rạp chưa có phòng chiếu nào">
          {(item) => (
            <button
              key={item.id}
              type="button"
              onClick={() => setRoom(item)}
              className="flex w-full items-center justify-between gap-3 p-4 text-left hover:bg-background-subtle"
            >
              <div>
                <p className="font-semibold">{item.name} <span className="text-xs text-foreground-muted">· {item.code}</span></p>
                <p className="mt-1 text-xs text-foreground-muted">
                  {item.seatCount} ghế · {item.isActive ? 'Đang hoạt động' : 'Ngừng hoạt động'}
                </p>
              </div>
              <ChevronRight className="h-4 w-4 shrink-0 text-foreground-muted" />
            </button>
          )}
        </CatalogList>
      )}

      {section === 'movies' && (
        <CatalogList query={movies} items={movies.data?.items} empty="Chưa có phim nào">
          {(item) => <MovieRow key={item.id} item={item} />}
        </CatalogList>
      )}
      {section === 'genres' && (
        <CatalogList query={genres} items={genres.data?.items} empty="Chưa có thể loại nào">
          {(item) => <GenreRow key={item.id} item={item} />}
        </CatalogList>
      )}
      {section === 'cinemas' && (
        <CatalogList query={cinemas} items={cinemas.data?.items} empty="Chưa có rạp chiếu nào">
          {(item) => <CinemaRow key={item.id} item={item} />}
        </CatalogList>
      )}

      {(section !== 'rooms' || (cinema && !room)) && (
        <Pagination currentPage={page} totalPages={totalPages} onPageChange={setPage} />
      )}
    </div>
  );
};

function SeatMap({
  query,
  seats,
  room,
}: {
  query: { isLoading: boolean; error: unknown };
  seats: AdminSeatSummary[] | undefined;
  room: RoomSummary;
}) {
  if (query.isLoading) return <LoadingState message="Đang tải sơ đồ ghế..." />;
  if (query.error) return <ErrorAlert error={query.error} />;
  if (!seats?.length) return <EmptyState title="Phòng chưa có ghế" />;

  const rows = seats.reduce<Record<string, AdminSeatSummary[]>>((groups, seat) => {
    (groups[seat.rowLabel] ??= []).push(seat);
    return groups;
  }, {});

  return (
    <section className="space-y-3">
      <h2 className="font-heading text-xl font-bold">Sơ đồ ghế · {room.name}</h2>
      <Card className="space-y-4 p-4 sm:p-6">
        <div className="mx-auto w-3/4 rounded-b-[50%] border-b-4 border-slate-300 pb-2 text-center text-xs font-bold tracking-[.25em] text-foreground-muted">
          MÀN HÌNH
        </div>
        <div className="overflow-x-auto pb-2">
          <div className="min-w-max space-y-2">
            {Object.entries(rows).sort(([a], [b]) => a.localeCompare(b)).map(([row, rowSeats]) => (
              <div key={row} className="flex items-center justify-center gap-1.5 sm:gap-2">
                <span className="w-5 text-xs font-bold text-foreground-muted">{row}</span>
                {rowSeats.sort((a, b) => a.number - b.number).map((seat) => (
                  <span
                    key={seat.id}
                    title={`${seat.rowLabel}${seat.number} · ${seat.seatType}${seat.isActive ? '' : ' · Ngừng hoạt động'}`}
                    className={`flex h-7 min-w-7 items-center justify-center rounded-t-md border px-1 text-[10px] font-bold sm:h-8 sm:min-w-8 sm:text-xs ${seat.isActive
                      ? seat.seatType === 'VIP'
                        ? 'border-amber-300 bg-amber-100 text-amber-900'
                        : 'border-slate-300 bg-slate-100 text-slate-700'
                      : 'border-slate-200 bg-white text-slate-400 line-through'}`}
                  >
                    {seat.number}
                  </span>
                ))}
              </div>
            ))}
          </div>
        </div>
        <div className="flex flex-wrap justify-center gap-4 border-t pt-4 text-xs text-foreground-muted">
          <span>Ghế thường</span>
          <span className="text-amber-800">Ghế VIP</span>
          <span className="text-slate-400">Ngừng hoạt động</span>
        </div>
      </Card>
    </section>
  );
}

function CatalogList<T extends { id: string }>({
  query,
  items,
  empty,
  children,
}: {
  query: { isLoading: boolean; error: unknown };
  items: T[] | undefined;
  empty: string;
  children: (item: T) => React.ReactNode;
}) {
  if (query.isLoading) return <LoadingState message="Đang tải danh mục..." />;
  if (query.error) return <ErrorAlert error={query.error} title="Không thể tải danh mục" />;
  if (!items?.length) return <EmptyState title={empty} />;
  return <Card className="divide-y divide-border overflow-hidden p-0">{items.map(children)}</Card>;
}

function MovieRow({ item }: { item: MovieSummary }) {
  return (
    <div className="flex items-start justify-between gap-4 p-4">
      <div className="min-w-0">
        <p className="font-semibold">{item.title}</p>
        <p className="mt-1 text-xs text-foreground-muted">
          {item.durationMinutes} phút{item.ageRating ? ` · ${item.ageRating}` : ''}
        </p>
        <div className="mt-2 flex flex-wrap gap-1">
          {item.genres.map((genre) => <Badge key={genre.id} variant="default">{genre.name}</Badge>)}
        </div>
      </div>
      <Badge variant={item.isActive ? 'success' : 'default'}>
        {item.isActive ? 'Đang chiếu' : 'Ngừng chiếu'}
      </Badge>
    </div>
  );
}

function GenreRow({ item }: { item: GenreSummary }) {
  return <div className="p-4"><p className="font-semibold">{item.name}</p><p className="text-xs text-foreground-muted">{item.slug}</p></div>;
}

function CinemaRow({ item }: { item: CinemaSummary }) {
  return (
    <div className="flex items-center justify-between gap-3 p-4">
      <div>
        <p className="font-semibold">{item.name} <span className="text-xs text-foreground-muted">· {item.code}</span></p>
        <p className="mt-1 text-xs text-foreground-muted">{item.city} · {item.address}</p>
      </div>
      <Badge variant={item.isActive ? 'success' : 'default'}>
        {item.isActive ? 'Hoạt động' : 'Tạm ngưng'}
      </Badge>
    </div>
  );
}

async function fetchAll<T>(fetchPage: (page: number, pageSize: number) => Promise<PagedResult<T>>): Promise<T[]> {
  const pageSize = 100;
  const first = await fetchPage(1, pageSize);
  const additionalPages = await Promise.all(
    Array.from({ length: Math.ceil(first.totalCount / pageSize) - 1 }, (_, index) =>
      fetchPage(index + 2, pageSize)
    )
  );
  return [...first.items, ...additionalPages.flatMap((page) => page.items)];
}
