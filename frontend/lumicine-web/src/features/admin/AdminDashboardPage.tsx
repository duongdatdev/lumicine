import React, { useState } from 'react';
import { useQuery } from '@tanstack/react-query';
import { useSearchParams } from 'react-router-dom';
import { adminApi } from '../../api/admin';
import type { ShowtimeReportRow } from '../../api/types';
import { Card } from '../../components/ui/Card';
import { Button } from '../../components/ui/Button';
import { Badge } from '../../components/ui/Badge';
import { Input } from '../../components/ui/Input';
import { Tabs } from '../../components/ui/Tabs';
import { LoadingState } from '../../components/common/LoadingState';
import { ErrorAlert } from '../../components/common/ErrorAlert';
import { Pagination } from '../../components/ui/Pagination';
import { formatVND, formatDateUtc7 } from '../../lib/utils';
import {
  DollarSign,
  Ticket,
  TrendingUp,
  AlertTriangle,
  Download,
  Calendar,
  Users,
  Film,
  Building2,
  RefreshCw,
} from 'lucide-react';
import {
  ResponsiveContainer,
  BarChart,
  Bar,
  XAxis,
  YAxis,
  Tooltip,
  Legend,
  CartesianGrid,
} from 'recharts';

export const AdminDashboardPage: React.FC = () => {
  const [searchParams, setSearchParams] = useSearchParams();
  const today = new Intl.DateTimeFormat('en-CA', { timeZone: 'Asia/Ho_Chi_Minh', year: 'numeric', month: '2-digit', day: '2-digit' }).format(new Date());
  const past30 = new Date(`${today}T00:00:00Z`);
  past30.setUTCDate(past30.getUTCDate() - 29);
  const fromDate = searchParams.get('fromDate') || past30.toISOString().slice(0, 10);
  const toDate = searchParams.get('toDate') || today;
  const movieId = searchParams.get('movieId') || '';
  const cinemaId = searchParams.get('cinemaId') || '';
  const showtimePage = Math.max(1, Number(searchParams.get('page')) || 1);
  const groupBy = (searchParams.get('groupBy') as 'day' | 'movie' | 'cinema' | 'showtime') || 'day';
  const [isExporting, setIsExporting] = useState(false);
  const [exportError, setExportError] = useState<unknown>(null);
  const updateParams = (updates: Record<string, string>) => {
    const params = new URLSearchParams(searchParams);
    for (const [key, value] of Object.entries(updates)) value ? params.set(key, value) : params.delete(key);
    setSearchParams(params);
  };

  const { data: movies } = useQuery({ queryKey: ['adminReportMovies'], queryFn: () => adminApi.getMovies({ pageSize: 100 }) });
  const { data: cinemas } = useQuery({ queryKey: ['adminReportCinemas'], queryFn: () => adminApi.getCinemas(1, 100) });

  // Summary KPI Query
  const {
    data: summary,
    isLoading: isSummaryLoading,
    error: summaryError,
    refetch: refetchSummary,
  } = useQuery({
    queryKey: ['adminDashboardSummary', { fromDate, toDate, movieId, cinemaId }],
    queryFn: () => adminApi.getDashboardSummary({ fromDate, toDate, movieId: movieId || undefined, cinemaId: cinemaId || undefined }),
  });

  // Operations KPI Query
  const { data: operations, refetch: refetchOperations } = useQuery({
    queryKey: ['adminDashboardOperations', { movieId, cinemaId }],
    queryFn: () => adminApi.getDashboardOperations({ movieId: movieId || undefined, cinemaId: cinemaId || undefined }),
  });

  // Revenue Report Query
  const {
    data: revenueReport,
    isLoading: isRevenueLoading,
    error: revenueError,
    refetch: refetchRevenue,
  } = useQuery({
    queryKey: ['adminRevenueReport', { fromDate, toDate, movieId, cinemaId, groupBy }],
    queryFn: () => adminApi.getRevenueReport({ fromDate, toDate, movieId: movieId || undefined, cinemaId: cinemaId || undefined, groupBy, pageSize: 50 }),
  });

  const { data: showtimesReport, isLoading: isShowtimesLoading, error: showtimesError } = useQuery({
    queryKey: ['adminShowtimesReport', { fromDate, toDate, movieId, cinemaId, showtimePage }],
    queryFn: () => adminApi.getShowtimesReport({ fromDate, toDate, movieId: movieId || undefined, cinemaId: cinemaId || undefined, page: showtimePage, pageSize: 20 }),
  });

  const handleExportRevenue = async () => {
    setIsExporting(true);
    try {
      await adminApi.exportRevenueReportCsv({ fromDate, toDate, movieId: movieId || undefined, cinemaId: cinemaId || undefined, groupBy });
    } catch (error) {
      setExportError(error);
    } finally {
      setIsExporting(false);
    }
  };

  const handleExportShowtimes = async () => {
    setIsExporting(true);
    try {
      await adminApi.exportShowtimesReportCsv({ fromDate, toDate, movieId: movieId || undefined, cinemaId: cinemaId || undefined });
    } catch (error) {
      setExportError(error);
    } finally {
      setIsExporting(false);
    }
  };

  const chartData = (revenueReport?.rows ?? []).map((row) => ({
    name: row.label || row.key,
    'Doanh thu vé': Number(row.ticketSalesAmount),
    'Thực thu': Number(row.collectedAmount),
    'Hoàn tiền': Number(row.recordedRefundAmount),
  }));

  const groupByTabs = [
    { id: 'day', label: 'Theo Ngày' },
    { id: 'movie', label: 'Theo Phim' },
    { id: 'cinema', label: 'Theo Rạp' },
    { id: 'showtime', label: 'Theo Suất chiếu' },
  ];

  return (
    <div className="space-y-8">
      {/* Top Header */}
      <div className="flex flex-col sm:flex-row sm:items-center justify-between gap-4">
        <div>
          <h1 className="font-heading text-3xl font-black text-foreground">
            Tổng quan & Báo cáo Doanh thu
          </h1>
          <p className="text-sm text-foreground-muted">
            Thống kê doanh thu, số lượng vé bán, trạng thái vận hành và xuất dữ liệu báo cáo.
          </p>
        </div>

        <div className="flex flex-wrap gap-2">
          <Button variant="outline" size="sm" onClick={handleExportRevenue} isLoading={isExporting}>
            <Download className="w-4 h-4 mr-1.5" /> Xuất CSV Doanh thu
          </Button>
          <Button variant="outline" size="sm" onClick={handleExportShowtimes} isLoading={isExporting}>
            <Download className="w-4 h-4 mr-1.5" /> Xuất CSV Suất chiếu
          </Button>
        </div>
      </div>
      {exportError ? <ErrorAlert error={exportError} /> : null}

      {/* Date Filter Bar */}
      <Card className="p-4 bg-background-surface">
        <div className="flex flex-wrap items-end gap-4">
          <Input
            label="Từ ngày"
            type="date"
            value={fromDate}
            onChange={(e) => updateParams({ fromDate: e.target.value, page: '' })}
            className="w-44"
          />
          <Input
            label="Đến ngày"
            type="date"
            value={toDate}
            onChange={(e) => updateParams({ toDate: e.target.value, page: '' })}
            className="w-44"
          />
          <label className="w-52 space-y-1.5 text-sm font-medium">Phim
            <select className="flex h-10 w-full rounded-lg border border-border bg-background-surface px-3 text-sm" value={movieId} onChange={(e) => updateParams({ movieId: e.target.value, page: '' })}>
              <option value="">Tất cả phim</option>{movies?.items.map((movie) => <option key={movie.id} value={movie.id}>{movie.title}</option>)}
            </select>
          </label>
          <label className="w-52 space-y-1.5 text-sm font-medium">Rạp
            <select className="flex h-10 w-full rounded-lg border border-border bg-background-surface px-3 text-sm" value={cinemaId} onChange={(e) => updateParams({ cinemaId: e.target.value, page: '' })}>
              <option value="">Tất cả rạp</option>{cinemas?.items.map((cinema) => <option key={cinema.id} value={cinema.id}>{cinema.name}</option>)}
            </select>
          </label>
          <Button
            variant="primary"
            onClick={() => {
              refetchSummary();
              refetchRevenue();
              refetchOperations();
              updateParams({ page: '' });
            }}
          >
            <RefreshCw className="w-4 h-4 mr-1.5" /> Áp dụng bộ lọc
          </Button>
        </div>
      </Card>

      <Card className="p-0 overflow-hidden">
        <div className="p-4 border-b border-border bg-background-subtle flex flex-wrap items-center justify-between gap-2">
          <div><h2 className="font-heading font-bold text-lg text-foreground">Báo cáo suất chiếu</h2><p className="text-sm text-foreground-muted">{showtimesReport?.totalCount ?? 0} suất chiếu trong khoảng đã chọn</p></div>
          {showtimesReport && <p className="text-sm text-foreground-muted">Sức chứa {showtimesReport.totals.capacity} · Đã bán {showtimesReport.totals.soldSeats} · Lấp đầy {showtimesReport.totals.occupancyRate.toFixed(1)}%</p>}
        </div>
        {isShowtimesLoading ? <LoadingState message="Đang tải báo cáo suất chiếu..." /> : showtimesError ? <ErrorAlert error={showtimesError} /> : (
          <>
            <div className="overflow-x-auto"><table className="w-full text-left text-sm">
              <thead className="bg-background-subtle/50 border-b border-border text-xs font-semibold text-foreground-muted uppercase"><tr><th className="px-4 py-3">Suất chiếu</th><th className="px-4 py-3">Rạp / Phòng</th><th className="px-4 py-3">Bắt đầu</th><th className="px-4 py-3">Trạng thái</th><th className="px-4 py-3 text-right">Ghế bán / sức chứa</th><th className="px-4 py-3 text-right">Lấp đầy</th><th className="px-4 py-3 text-right">Vé đã check-in</th><th className="px-4 py-3 text-right">Doanh thu vé</th></tr></thead>
              <tbody className="divide-y divide-border">{showtimesReport?.rows.map((row: ShowtimeReportRow) => <tr key={row.showtimeId} className="hover:bg-background-subtle/40"><td className="px-4 py-3 font-medium">{row.movieTitle}</td><td className="px-4 py-3">{row.cinemaName} · {row.roomName}</td><td className="px-4 py-3">{formatDateUtc7(row.startAtUtc)}</td><td className="px-4 py-3"><Badge>{row.status}</Badge></td><td className="px-4 py-3 text-right">{row.soldSeats} / {row.capacity}</td><td className="px-4 py-3 text-right">{row.occupancyRate.toFixed(1)}%</td><td className="px-4 py-3 text-right">{row.usedTickets} / {row.validTickets}</td><td className="px-4 py-3 text-right">{formatVND(row.issuedTicketAmount)}</td></tr>)}</tbody>
            </table></div>
            {!showtimesReport?.rows.length && <p className="p-8 text-center text-sm text-foreground-muted">Không có suất chiếu trong khoảng thời gian này.</p>}
            {showtimesReport && <Pagination currentPage={showtimesReport.page} totalPages={Math.ceil(showtimesReport.totalCount / showtimesReport.pageSize)} onPageChange={(page) => updateParams({ page: page > 1 ? String(page) : '' })} />}
          </>
        )}
      </Card>

      {/* Operations Warning Bar if any pending action required */}
      {operations && (operations.refundReviewCount > 0 || operations.pendingIndexCount > 0) && (
        <div className="grid grid-cols-1 md:grid-cols-2 gap-4">
          {operations.refundReviewCount > 0 && (
            <Card className="p-4 bg-amber-50 border-amber-200 text-amber-900 flex items-center justify-between">
              <div className="flex items-center gap-3">
                <AlertTriangle className="w-6 h-6 text-amber-600 shrink-0" />
                <div>
                  <p className="font-bold text-sm">
                    {operations.refundReviewCount} giao dịch cần đối soát hoàn tiền
                  </p>
                  <p className="text-xs text-amber-700">
                    Tổng số tiền cần xem xét: {formatVND(operations.refundReviewAmount)}
                  </p>
                </div>
              </div>
              <a href="/admin/payments?processingStatus=RequiresRefundReview">
                <Button variant="outline" size="sm" className="bg-white text-amber-900 border-amber-300">
                  Xử lý ngay
                </Button>
              </a>
            </Card>
          )}

          {operations.pendingIndexCount > 0 && (
            <Card className="p-4 bg-blue-50 border-blue-200 text-blue-900 flex items-center justify-between">
              <div className="flex items-center gap-3">
                <Film className="w-6 h-6 text-blue-600 shrink-0" />
                <div>
                  <p className="font-bold text-sm">
                    {operations.pendingIndexCount} phim chờ cập nhật chỉ mục AI
                  </p>
                  <p className="text-xs text-blue-700">Chỉ mục tìm kiếm ngữ nghĩa cần xây dựng lại.</p>
                </div>
              </div>
              <a href="/admin/discovery">
                <Button variant="outline" size="sm" className="bg-white text-blue-900 border-blue-300">
                  Xem chỉ mục
                </Button>
              </a>
            </Card>
          )}
        </div>
      )}

      {/* KPI Cards Grid */}
      {isSummaryLoading ? (
        <LoadingState message="Đang tải dữ liệu tổng quan..." />
      ) : summaryError ? (
        <ErrorAlert error={summaryError} />
      ) : summary ? (
        <div className="grid grid-cols-1 sm:grid-cols-2 lg:grid-cols-4 gap-5">
          <Card className="p-5 space-y-2">
            <span className="text-xs font-semibold text-foreground-muted uppercase tracking-wider block">
              Tổng tiền bán vé
            </span>
            <div className="flex items-center justify-between">
              <span className="font-heading font-black text-2xl text-foreground">
                {formatVND(summary.ticketSalesAmount)}
              </span>
              <div className="p-2 rounded-xl bg-primary/10 text-primary">
                <DollarSign className="w-5 h-5" />
              </div>
            </div>
          </Card>

          <Card className="p-5 space-y-2">
            <span className="text-xs font-semibold text-foreground-muted uppercase tracking-wider block">
              Thực thu thuần (Net)
            </span>
            <div className="flex items-center justify-between">
              <span className="font-heading font-black text-2xl text-primary">
                {formatVND(summary.netCollectedAmount)}
              </span>
              <div className="p-2 rounded-xl bg-green-100 text-green-700">
                <TrendingUp className="w-5 h-5" />
              </div>
            </div>
            <p className="text-[11px] text-foreground-muted">
              Đã trừ hoàn tiền: {formatVND(summary.recordedRefundAmount)}
            </p>
          </Card>

          <Card className="p-5 space-y-2">
            <span className="text-xs font-semibold text-foreground-muted uppercase tracking-wider block">
              Số vé phát hành
            </span>
            <div className="flex items-center justify-between">
              <span className="font-heading font-black text-2xl text-foreground">
                {summary.ticketsIssuedCount} vé
              </span>
              <div className="p-2 rounded-xl bg-blue-100 text-blue-700">
                <Ticket className="w-5 h-5" />
              </div>
            </div>
            <p className="text-[11px] text-foreground-muted">
              Đã check-in vào rạp: {summary.ticketsUsedCount} vé
            </p>
          </Card>

          <Card className="p-5 space-y-2">
            <span className="text-xs font-semibold text-foreground-muted uppercase tracking-wider block">
              Giao dịch thành công
            </span>
            <div className="flex items-center justify-between">
              <span className="font-heading font-black text-2xl text-foreground">
                {summary.successfulPaymentCount}
              </span>
              <div className="p-2 rounded-xl bg-purple-100 text-purple-700">
                <Calendar className="w-5 h-5" />
              </div>
            </div>
            <p className="text-[11px] text-foreground-muted">
              Thất bại / Hủy: {summary.failedPaymentCount}
            </p>
          </Card>
        </div>
      ) : null}

      {/* Daily Revenue Chart */}
      <Card className="p-6 space-y-4">
        <div className="flex flex-col sm:flex-row sm:items-center justify-between gap-4">
          <h2 className="font-heading font-bold text-xl text-foreground">
            Biểu đồ doanh thu {groupBy === 'day' ? 'theo ngày' : groupBy === 'movie' ? 'theo phim' : 'theo rạp'}
          </h2>
          <Tabs
            tabs={groupByTabs}
            activeTab={groupBy}
            onChange={(t) => updateParams({ groupBy: t as string })}
          />
        </div>

        <div className="h-80 w-full pt-4">
          {isRevenueLoading ? (
            <LoadingState message="Đang vẽ biểu đồ..." />
          ) : chartData.length === 0 ? (
            <div className="h-full flex items-center justify-center text-sm text-foreground-muted">
              Không có dữ liệu trong khoảng thời gian này.
            </div>
          ) : (
            <ResponsiveContainer width="100%" height="100%">
              <BarChart data={chartData} margin={{ top: 10, right: 10, left: 20, bottom: 20 }}>
                <CartesianGrid strokeDasharray="3 3" vertical={false} stroke="#E4E4E7" />
                <XAxis dataKey="name" stroke="#71717A" fontSize={12} tickLine={false} />
                <YAxis
                  stroke="#71717A"
                  fontSize={12}
                  tickLine={false}
                  tickFormatter={(val) => `${(val / 1000).toLocaleString('vi-VN')}k`}
                />
                <Tooltip
                  formatter={(value: any) => formatVND(Number(value))}
                  contentStyle={{
                    backgroundColor: '#FFFFFF',
                    borderRadius: '0.75rem',
                    border: '1px solid #E4E4E7',
                  }}
                />
                <Legend />
                <Bar dataKey="Doanh thu vé" fill="#166534" radius={[4, 4, 0, 0]} />
                <Bar dataKey="Thực thu" fill="#0F766E" radius={[4, 4, 0, 0]} />
                <Bar dataKey="Hoàn tiền" fill="#DC2626" radius={[4, 4, 0, 0]} />
              </BarChart>
            </ResponsiveContainer>
          )}
        </div>
      </Card>

      {/* Breakdown Table */}
      <Card className="p-0 overflow-hidden">
        <div className="p-4 border-b border-border bg-background-subtle">
          <h3 className="font-heading font-bold text-lg text-foreground">
            Bảng chi tiết doanh thu
          </h3>
        </div>

        <div className="overflow-x-auto">
          <table className="w-full text-left text-sm">
            <thead className="bg-background-subtle/50 border-b border-border text-xs font-semibold text-foreground-muted uppercase">
              <tr>
                <th className="px-4 py-3.5">Tiêu thức</th>
                <th className="px-4 py-3.5">Doanh thu vé</th>
                <th className="px-4 py-3.5">Thực thu</th>
                <th className="px-4 py-3.5">Hoàn tiền</th>
                <th className="px-4 py-3.5">Thực thu thuần</th>
                <th className="px-4 py-3.5 text-right">GD thành công</th>
              </tr>
            </thead>
            <tbody className="divide-y divide-border">
              {revenueReport?.rows.map((row) => (
                <tr key={row.key} className="hover:bg-background-subtle/40 transition-colors">
                  <td className="px-4 py-3 font-medium text-foreground">{row.label || row.key}</td>
                  <td className="px-4 py-3 font-semibold text-foreground">
                    {formatVND(row.ticketSalesAmount)}
                  </td>
                  <td className="px-4 py-3 text-primary font-semibold">
                    {formatVND(row.collectedAmount)}
                  </td>
                  <td className="px-4 py-3 text-red-600">
                    {formatVND(row.recordedRefundAmount)}
                  </td>
                  <td className="px-4 py-3 font-bold text-primary">
                    {formatVND(row.netCollectedAmount)}
                  </td>
                  <td className="px-4 py-3 text-right font-mono">
                    {row.successfulPaymentCount}
                  </td>
                </tr>
              ))}
            </tbody>
          </table>
        </div>
      </Card>
    </div>
  );
};
