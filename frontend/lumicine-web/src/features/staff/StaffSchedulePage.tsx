import React, { useState } from 'react';
import { useSearchParams } from 'react-router-dom';
import { useQuery, useMutation, useQueryClient } from '@tanstack/react-query';
import { adminApi } from '../../api/admin';
import { catalogApi } from '../../api/catalog';
import type { ShowtimeSummary, ShowtimeSeatSummary, CinemaSummary, MovieSummary } from '../../api/types';
import { Card } from '../../components/ui/Card';
import { Button } from '../../components/ui/Button';
import { Badge } from '../../components/ui/Badge';
import { Input } from '../../components/ui/Input';
import { Modal } from '../../components/ui/Modal';
import { Pagination } from '../../components/ui/Pagination';
import { LoadingState } from '../../components/common/LoadingState';
import { EmptyState } from '../../components/common/EmptyState';
import { ErrorAlert } from '../../components/common/ErrorAlert';
import { formatTimeUtc7, formatDateUtc7, formatVND } from '../../lib/utils';
import { Calendar, Plus, Clock, MapPin, Film, Eye, XCircle, RefreshCw } from 'lucide-react';

export const StaffSchedulePage: React.FC = () => {
  const queryClient = useQueryClient();
  const [searchParams, setSearchParams] = useSearchParams();

  const dateFilter = searchParams.get('date') ?? new Intl.DateTimeFormat('en-CA', { timeZone: 'Asia/Ho_Chi_Minh' }).format(new Date());
  const cinemaFilter = searchParams.get('cinemaId') ?? '';
  const movieFilter = searchParams.get('movieId') ?? '';
  const page = Math.max(1, Number(searchParams.get('page')) || 1);
  const updateFilters = (updates: Record<string, string>) => {
    const next = new URLSearchParams(searchParams);
    Object.entries(updates).forEach(([key, value]) => value ? next.set(key, value) : next.delete(key));
    setSearchParams(next, { replace: true });
  };

  // Modal states
  const [isCreateOpen, setIsCreateOpen] = useState(false);
  const [isRescheduleOpen, setIsRescheduleOpen] = useState(false);
  const [isSeatsOpen, setIsSeatsOpen] = useState(false);
  const [selectedShowtime, setSelectedShowtime] = useState<ShowtimeSummary | null>(null);

  // Form states for Create
  const [createMovieId, setCreateMovieId] = useState('');
  const [createCinemaId, setCreateCinemaId] = useState('');
  const [createRoomId, setCreateRoomId] = useState('');
  const [createStartTime, setCreateStartTime] = useState('');
  const [createPrice, setCreatePrice] = useState('80000');

  // Form states for Reschedule
  const [rescheduleStartTime, setRescheduleStartTime] = useState('');
  const [reschedulePrice, setReschedulePrice] = useState('');

  const [actionError, setActionError] = useState<unknown>(null);

  // Load cinemas for filter & create
  const { data: cinemasData } = useQuery({
    queryKey: ['staffCinemas'],
    queryFn: () => catalogApi.getCinemas({ pageSize: 100 }),
  });

  // Load movies for filter & create
  const { data: moviesData } = useQuery({
    queryKey: ['staffMovies'],
    queryFn: () => catalogApi.getMovies({ pageSize: 100 }),
  });

  // Load rooms when cinema is selected in Create
  const { data: roomsData } = useQuery({
    queryKey: ['staffRooms', createCinemaId],
    queryFn: () => adminApi.getRoomsForCinema(createCinemaId, 1, 50),
    enabled: !!createCinemaId,
  });

  // Load showtimes
  const {
    data: showtimesData,
    isLoading,
    error,
    refetch,
  } = useQuery({
    queryKey: ['staffShowtimes', { date: dateFilter, cinemaId: cinemaFilter, movieId: movieFilter, page }],
    queryFn: () =>
      adminApi.getShowtimes({
        date: dateFilter || undefined,
        cinemaId: cinemaFilter || undefined,
        movieId: movieFilter || undefined,
        page,
        pageSize: 15,
      }),
  });

  // Load seats snapshot for selected showtime
  const { data: seatsSnapshot, isLoading: isSeatsLoading } = useQuery({
    queryKey: ['staffShowtimeSeats', selectedShowtime?.id],
    queryFn: () => adminApi.getShowtimeSeats(selectedShowtime!.id),
    enabled: !!selectedShowtime && isSeatsOpen,
  });

  const handleCreateSubmit = async (e: React.FormEvent) => {
    e.preventDefault();
    setActionError(null);
    try {
      // Convert local date time to ISO UTC
      const startAt = toUtc7Offset(createStartTime);
      await adminApi.createShowtime({
        movieId: createMovieId,
        roomId: createRoomId,
        startAt,
        basePrice: parseFloat(createPrice),
      });
      setIsCreateOpen(false);
      refetch();
    } catch (err) {
      setActionError(err);
    }
  };

  const handleRescheduleSubmit = async (e: React.FormEvent) => {
    e.preventDefault();
    if (!selectedShowtime) return;
    setActionError(null);
    try {
      const startAt = toUtc7Offset(rescheduleStartTime);
      await adminApi.rescheduleShowtime(selectedShowtime.id, {
        startAt,
        basePrice: parseFloat(reschedulePrice),
      });
      setIsRescheduleOpen(false);
      refetch();
    } catch (err) {
      setActionError(err);
    }
  };

  const handleCancel = async (id: string) => {
    if (!window.confirm('Bạn có chắc chắn muốn hủy suất chiếu này? Hành động không thể hoàn tác nếu đã có vé.')) return;
    setActionError(null);
    try {
      await adminApi.cancelShowtime(id);
      refetch();
    } catch (err) {
      setActionError(err);
    }
  };

  return (
    <div className="space-y-6">
      <div className="flex flex-col sm:flex-row sm:items-center justify-between gap-4">
        <div>
          <h1 className="font-heading text-3xl font-black text-foreground">Quản lý Lịch chiếu</h1>
          <p className="text-sm text-foreground-muted">
            Theo dõi, tạo mới, điều chỉnh giờ chiếu và kiểm tra snapshot ghế phòng chiếu.
          </p>
        </div>

        <Button variant="primary" onClick={() => setIsCreateOpen(true)}>
          <Plus className="w-4 h-4 mr-2" /> Tạo suất chiếu mới
        </Button>
      </div>

      <ErrorAlert error={actionError} />

      {/* Filters Bar */}
      <Card className="p-4 bg-background-surface">
        <div className="grid grid-cols-1 sm:grid-cols-4 gap-4 items-end">
          <Input
            label="Ngày chiếu"
            type="date"
            value={dateFilter}
            onChange={(e) => updateFilters({ date: e.target.value, page: '' })}
          />

          <div>
            <label className="block text-sm font-medium text-foreground mb-1.5">Rạp chiếu</label>
            <select
              value={cinemaFilter}
              onChange={(e) => updateFilters({ cinemaId: e.target.value, page: '' })}
              className="h-10 w-full rounded-lg border border-border bg-background-surface px-3 py-2 text-sm"
            >
              <option value="">Tất cả các rạp</option>
              {cinemasData?.items?.map((c) => (
                <option key={c.id} value={c.id}>
                  {c.name} ({c.city})
                </option>
              ))}
            </select>
          </div>

          <div>
            <label className="block text-sm font-medium text-foreground mb-1.5">Phim</label>
            <select
              value={movieFilter}
              onChange={(e) => updateFilters({ movieId: e.target.value, page: '' })}
              className="h-10 w-full rounded-lg border border-border bg-background-surface px-3 py-2 text-sm"
            >
              <option value="">Tất cả các phim</option>
              {moviesData?.items?.map((m) => (
                <option key={m.id} value={m.id}>
                  {m.title}
                </option>
              ))}
            </select>
          </div>

          <div>
            <Button variant="outline" className="w-full" onClick={() => refetch()}>
              <RefreshCw className="w-4 h-4 mr-2" /> Làm mới
            </Button>
          </div>
        </div>
      </Card>

      {/* Showtimes Table */}
      {isLoading ? (
        <LoadingState message="Đang tải lịch chiếu..." />
      ) : error ? (
        <ErrorAlert error={error} title="Không thể tải danh sách suất chiếu" />
      ) : !showtimesData?.items || showtimesData.items.length === 0 ? (
        <EmptyState
          title="Không tìm thấy suất chiếu nào"
          description="Thử đổi bộ lọc ngày, rạp hoặc tạo một suất chiếu mới."
        />
      ) : (
        <Card className="p-0 overflow-hidden border-border">
          <div className="overflow-x-auto">
            <table className="w-full text-left text-sm">
              <thead className="bg-background-subtle border-b border-border text-xs font-semibold text-foreground-muted uppercase tracking-wider">
                <tr>
                  <th className="px-4 py-3.5">Giờ chiếu (UTC+7)</th>
                  <th className="px-4 py-3.5">Phim</th>
                  <th className="px-4 py-3.5">Rạp & Phòng</th>
                  <th className="px-4 py-3.5">Giá vé</th>
                  <th className="px-4 py-3.5">Trạng thái</th>
                  <th className="px-4 py-3.5 text-right">Thao tác</th>
                </tr>
              </thead>
              <tbody className="divide-y divide-border">
                {showtimesData.items.map((st) => {
                  const isCancelled = st.status === 'Cancelled';
                  return (
                    <tr key={st.id} className="hover:bg-background-subtle/50 transition-colors">
                      <td className="px-4 py-3.5 font-bold font-mono">
                        {formatDateUtc7(st.startAtUtc)} {formatTimeUtc7(st.startAtUtc)}
                      </td>
                      <td className="px-4 py-3.5 font-medium text-foreground max-w-[200px] truncate">
                        {st.movieTitle}
                      </td>
                      <td className="px-4 py-3.5 text-foreground-muted text-xs">
                        {st.cinemaName} — <strong className="text-foreground">{st.roomName}</strong>
                      </td>
                      <td className="px-4 py-3.5 font-bold text-primary">
                        {formatVND(st.basePrice)}
                      </td>
                      <td className="px-4 py-3.5">
                        <Badge variant={isCancelled ? 'danger' : 'success'}>
                          {st.status}
                        </Badge>
                      </td>
                      <td className="px-4 py-3.5 text-right space-x-2">
                        <Button
                          variant="outline"
                          size="sm"
                          onClick={() => {
                            setSelectedShowtime(st);
                            setIsSeatsOpen(true);
                          }}
                          title="Xem snapshot ghế"
                        >
                          <Eye className="w-3.5 h-3.5" />
                        </Button>

                        {!isCancelled && (
                          <>
                            <Button
                              variant="outline"
                              size="sm"
                              onClick={() => {
                                setSelectedShowtime(st);
                                setRescheduleStartTime(
                                  utcToUtc7Input(st.startAtUtc)
                                );
                                setReschedulePrice(st.basePrice.toString());
                                setIsRescheduleOpen(true);
                              }}
                            >
                              Đổi giờ/giá
                            </Button>

                            <Button
                              variant="ghost"
                              size="sm"
                              className="text-red-600 hover:text-red-700"
                              onClick={() => handleCancel(st.id)}
                            >
                              <XCircle className="w-4 h-4" />
                            </Button>
                          </>
                        )}
                      </td>
                    </tr>
                  );
                })}
              </tbody>
            </table>
          </div>

          <div className="p-4 border-t border-border">
            <Pagination
              currentPage={showtimesData.page}
              totalPages={Math.ceil(showtimesData.totalCount / showtimesData.pageSize)}
              onPageChange={(nextPage) => updateFilters({ page: String(nextPage) })}
            />
          </div>
        </Card>
      )}

      {/* Create Showtime Modal */}
      <Modal
        isOpen={isCreateOpen}
        onClose={() => setIsCreateOpen(false)}
        title="Tạo suất chiếu mới"
        description="Chọn phim, rạp, phòng chiếu và giờ bắt đầu."
      >
        <form onSubmit={handleCreateSubmit} className="space-y-4">
          <div>
            <label className="block text-sm font-medium text-foreground mb-1.5">Chọn Phim</label>
            <select
              required
              value={createMovieId}
              onChange={(e) => setCreateMovieId(e.target.value)}
              className="h-10 w-full rounded-lg border border-border bg-background px-3 text-sm"
            >
              <option value="">-- Chọn phim --</option>
              {moviesData?.items?.map((m) => (
                <option key={m.id} value={m.id}>
                  {m.title} ({m.durationMinutes} phút)
                </option>
              ))}
            </select>
          </div>

          <div>
            <label className="block text-sm font-medium text-foreground mb-1.5">Chọn Rạp</label>
            <select
              required
              value={createCinemaId}
              onChange={(e) => {
                setCreateCinemaId(e.target.value);
                setCreateRoomId('');
              }}
              className="h-10 w-full rounded-lg border border-border bg-background px-3 text-sm"
            >
              <option value="">-- Chọn rạp chiếu --</option>
              {cinemasData?.items?.map((c) => (
                <option key={c.id} value={c.id}>
                  {c.name} ({c.city})
                </option>
              ))}
            </select>
          </div>

          <div>
            <label className="block text-sm font-medium text-foreground mb-1.5">Chọn Phòng chiếu</label>
            <select
              required
              disabled={!createCinemaId}
              value={createRoomId}
              onChange={(e) => setCreateRoomId(e.target.value)}
              className="h-10 w-full rounded-lg border border-border bg-background px-3 text-sm disabled:opacity-50"
            >
              <option value="">-- Chọn phòng chiếu --</option>
              {roomsData?.items?.map((r) => (
                <option key={r.id} value={r.id}>
                  {r.name} ({r.seatCount} ghế)
                </option>
              ))}
            </select>
          </div>

          <Input
            label="Thời gian bắt đầu chiếu (Giờ địa phương)"
            type="datetime-local"
            required
            value={createStartTime}
            onChange={(e) => setCreateStartTime(e.target.value)}
          />

          <Input
            label="Giá vé cơ bản (VND)"
            type="number"
            min="10000"
            step="5000"
            required
            value={createPrice}
            onChange={(e) => setCreatePrice(e.target.value)}
          />

          <div className="pt-4 flex justify-end gap-2">
            <Button type="button" variant="outline" onClick={() => setIsCreateOpen(false)}>
              Hủy
            </Button>
            <Button type="submit" variant="primary">
              Tạo suất chiếu
            </Button>
          </div>
        </form>
      </Modal>

      {/* Reschedule Modal */}
      <Modal
        isOpen={isRescheduleOpen}
        onClose={() => setIsRescheduleOpen(false)}
        title="Điều chỉnh giờ chiếu / giá vé"
        description={`Suất chiếu phim: ${selectedShowtime?.movieTitle}`}
      >
        <form onSubmit={handleRescheduleSubmit} className="space-y-4">
          <Input
            label="Thời gian bắt đầu mới"
            type="datetime-local"
            required
            value={rescheduleStartTime}
            onChange={(e) => setRescheduleStartTime(e.target.value)}
          />

          <Input
            label="Giá vé cơ bản mới (VND)"
            type="number"
            min="10000"
            step="5000"
            required
            value={reschedulePrice}
            onChange={(e) => setReschedulePrice(e.target.value)}
          />

          <div className="pt-4 flex justify-end gap-2">
            <Button type="button" variant="outline" onClick={() => setIsRescheduleOpen(false)}>
              Hủy
            </Button>
            <Button type="submit" variant="primary">
              Lưu thay đổi
            </Button>
          </div>
        </form>
      </Modal>

      {/* Seats Snapshot Modal */}
      <Modal
        isOpen={isSeatsOpen}
        onClose={() => setIsSeatsOpen(false)}
        title={`Snapshot ghế: ${selectedShowtime?.movieTitle}`}
        description={`${selectedShowtime?.cinemaName} — ${selectedShowtime?.roomName}`}
        className="max-w-2xl"
      >
        {isSeatsLoading ? (
          <LoadingState message="Đang tải dữ liệu ghế..." />
        ) : !seatsSnapshot || seatsSnapshot.length === 0 ? (
          <EmptyState title="Không có dữ liệu ghế" />
        ) : (
          <div className="space-y-4 max-h-[60vh] overflow-y-auto p-2">
            <div className="flex flex-wrap gap-4 text-xs font-semibold pb-2 border-b">
              <span className="flex items-center gap-1.5">
                <span className="w-3 h-3 rounded bg-green-500 inline-block" /> Trống:{' '}
                {seatsSnapshot.filter((s) => s.status === 'Available').length}
              </span>
              <span className="flex items-center gap-1.5">
                <span className="w-3 h-3 rounded bg-amber-500 inline-block" /> Đang giữ:{' '}
                {seatsSnapshot.filter((s) => s.status === 'Held').length}
              </span>
              <span className="flex items-center gap-1.5">
                <span className="w-3 h-3 rounded bg-red-500 inline-block" /> Đã bán:{' '}
                {seatsSnapshot.filter((s) => s.status === 'Sold').length}
              </span>
            </div>

            <div className="grid grid-cols-8 sm:grid-cols-10 gap-1.5 text-center">
              {seatsSnapshot.map((seat) => (
                <div
                  key={seat.seatId}
                  className={`p-1.5 rounded text-[10px] font-bold border ${
                    seat.status === 'Sold'
                      ? 'bg-red-100 text-red-800 border-red-300'
                      : seat.status === 'Held'
                      ? 'bg-amber-100 text-amber-800 border-amber-300'
                      : 'bg-green-50 text-green-800 border-green-300'
                  }`}
                  title={`Hàng ${seat.rowLabel} Ghế ${seat.number} - ${seat.status}`}
                >
                  {seat.rowLabel}{seat.number}
                </div>
              ))}
            </div>
          </div>
        )}
      </Modal>
    </div>
  );
};

export function toUtc7Offset(datetimeLocal: string): string {
  return new Date(`${datetimeLocal}:00+07:00`).toISOString();
}

export function utcToUtc7Input(utcDateTime: string): string {
  return new Date(new Date(utcDateTime).getTime() + 7 * 60 * 60 * 1000).toISOString().slice(0, 16);
}
