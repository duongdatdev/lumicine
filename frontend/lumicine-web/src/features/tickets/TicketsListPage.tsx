import React, { useState } from 'react';
import { useQuery } from '@tanstack/react-query';
import { Link } from 'react-router-dom';
import { bookingApi } from '../../api/booking';
import { Card } from '../../components/ui/Card';
import { Button } from '../../components/ui/Button';
import { Badge } from '../../components/ui/Badge';
import { Tabs } from '../../components/ui/Tabs';
import { Pagination } from '../../components/ui/Pagination';
import { LoadingState } from '../../components/common/LoadingState';
import { EmptyState } from '../../components/common/EmptyState';
import { ErrorAlert } from '../../components/common/ErrorAlert';
import { formatVND, formatDateTimeUtc7 } from '../../lib/utils';
import { Ticket, Film, MapPin, Calendar, ArrowRight } from 'lucide-react';

export const TicketsListPage: React.FC = () => {
  const [activeTab, setActiveTab] = useState<string>('all');
  const [page, setPage] = useState<number>(1);

  const statusFilter = activeTab === 'all' ? undefined : activeTab;

  const { data, isLoading, error } = useQuery({
    queryKey: ['userBookings', { status: statusFilter, page }],
    queryFn: () => bookingApi.listBookings({ status: statusFilter, page, pageSize: 10 }),
  });

  const tabs = [
    { id: 'all', label: 'Tất cả' },
    { id: 'Confirmed', label: 'Đã xác nhận' },
    { id: 'Pending', label: 'Chờ thanh toán' },
    { id: 'Cancelled', label: 'Đã hủy / Hết hạn' },
  ];

  return (
    <div className="container mx-auto px-4 py-8 max-w-4xl space-y-6">
      <div className="flex flex-col sm:flex-row sm:items-center justify-between gap-4">
        <div>
          <h1 className="font-heading text-3xl font-black text-foreground">Vé & Đơn đặt của tôi</h1>
          <p className="text-sm text-foreground-muted mt-1">
            Quản lý lịch sử đặt vé, xem mã QR vào rạp và kiểm tra trạng thái thanh toán.
          </p>
        </div>
      </div>

      <Tabs tabs={tabs} activeTab={activeTab} onChange={(t) => { setActiveTab(t); setPage(1); }} />

      {isLoading ? (
        <LoadingState message="Đang tải lịch sử đơn đặt vé..." />
      ) : error ? (
        <ErrorAlert error={error} title="Không thể tải lịch sử vé" />
      ) : !data?.items || data.items.length === 0 ? (
        <EmptyState
          icon={<Ticket className="w-8 h-8 text-foreground-muted" />}
          title="Chưa có đơn đặt vé nào"
          description="Bạn chưa có vé nào trong mục này. Hãy khám phá các bộ phim đang chiếu ngay!"
          action={
            <Link to="/movies">
              <Button variant="primary">Khám phá phim ngay</Button>
            </Link>
          }
        />
      ) : (
        <div className="space-y-4">
          {data.items.map((booking) => {
            const isConfirmed = booking.status === 'Confirmed';
            const isPending = booking.status === 'Pending';
            const isCancelled = booking.status === 'Cancelled' || booking.status === 'Expired';

            return (
              <Card key={booking.id} className="p-5 hover:shadow-md transition-all">
                <div className="flex flex-col sm:flex-row sm:items-center justify-between gap-4 border-b border-border pb-4">
                  <div className="space-y-1">
                    <div className="flex items-center gap-2">
                      <span className="font-mono font-bold text-sm text-foreground">
                        #{booking.code}
                      </span>
                      <Badge
                        variant={
                          isConfirmed
                            ? 'success'
                            : isPending
                            ? 'warning'
                            : 'neutral'
                        }
                      >
                        {booking.status}
                      </Badge>
                    </div>
                    <span className="text-xs text-foreground-muted block">
                      Đặt lúc: {formatDateTimeUtc7(booking.createdAtUtc)}
                    </span>
                  </div>

                  <div className="text-right sm:text-right">
                    <span className="text-xs text-foreground-muted block">Tổng thanh toán</span>
                    <span className="font-heading font-black text-xl text-primary">
                      {formatVND(booking.totalAmount)}
                    </span>
                  </div>
                </div>

                <div className="py-4 space-y-2">
                  <h3 className="font-heading font-bold text-lg text-foreground flex items-center gap-2">
                    <Film className="w-4 h-4 text-primary" /> {booking.movieTitle}
                  </h3>
                  <div className="flex flex-wrap items-center gap-4 text-xs text-foreground-muted">
                    <span className="flex items-center gap-1">
                      <MapPin className="w-3.5 h-3.5 text-primary" /> {booking.cinemaName} — {booking.roomName}
                    </span>
                    <span className="flex items-center gap-1">
                      <Ticket className="w-3.5 h-3.5 text-primary" />
                      {booking.seats.length} ghế:{' '}
                      <span className="font-semibold text-foreground">
                        {booking.seats.map((s) => `${s.rowLabel}${s.number}`).join(', ')}
                      </span>
                    </span>
                  </div>
                </div>

                <div className="pt-3 border-t border-border flex justify-end gap-2">
                  {isConfirmed && (
                    <Link to={`/tickets/${booking.id}`}>
                      <Button variant="primary" size="sm">
                        <Ticket className="w-4 h-4 mr-1.5" /> Xem vé điện tử & QR
                      </Button>
                    </Link>
                  )}

                  {isPending && (
                    <Link to={`/bookings/${booking.id}`}>
                      <Button variant="primary" size="sm">
                        Tiếp tục thanh toán <ArrowRight className="w-4 h-4 ml-1.5" />
                      </Button>
                    </Link>
                  )}

                  {isCancelled && (
                    <Link to={`/movies/${booking.movieId}`}>
                      <Button variant="outline" size="sm">
                        Đặt lại suất khác
                      </Button>
                    </Link>
                  )}
                </div>
              </Card>
            );
          })}

          <Pagination
            currentPage={data.page}
            totalPages={data.totalPages}
            onPageChange={setPage}
          />
        </div>
      )}
    </div>
  );
};
