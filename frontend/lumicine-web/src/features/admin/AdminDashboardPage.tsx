import React, { useState } from 'react';
import { useQuery } from '@tanstack/react-query';
import { adminApi } from '../../api/admin';
import { Card } from '../../components/ui/Card';
import { Button } from '../../components/ui/Button';
import { Badge } from '../../components/ui/Badge';
import { Input } from '../../components/ui/Input';
import { Tabs } from '../../components/ui/Tabs';
import { LoadingState } from '../../components/common/LoadingState';
import { ErrorAlert } from '../../components/common/ErrorAlert';
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
  // Default date filter: past 30 days
  const today = new Date();
  const past30 = new Date();
  past30.setDate(today.getDate() - 29);

  const [fromDate, setFromDate] = useState<string>(past30.toISOString().split('T')[0]);
  const [toDate, setToDate] = useState<string>(today.toISOString().split('T')[0]);
  const [groupBy, setGroupBy] = useState<'day' | 'movie' | 'cinema' | 'showtime'>('day');
  const [isExporting, setIsExporting] = useState(false);

  // Summary KPI Query
  const {
    data: summary,
    isLoading: isSummaryLoading,
    error: summaryError,
    refetch: refetchSummary,
  } = useQuery({
    queryKey: ['adminDashboardSummary', { fromDate, toDate }],
    queryFn: () => adminApi.getDashboardSummary({ fromDate, toDate }),
  });

  // Operations KPI Query
  const { data: operations, refetch: refetchOperations } = useQuery({
    queryKey: ['adminDashboardOperations'],
    queryFn: () => adminApi.getDashboardOperations(),
  });

  // Revenue Report Query
  const {
    data: revenueReport,
    isLoading: isRevenueLoading,
    error: revenueError,
    refetch: refetchRevenue,
  } = useQuery({
    queryKey: ['adminRevenueReport', { fromDate, toDate, groupBy }],
    queryFn: () => adminApi.getRevenueReport({ fromDate, toDate, groupBy, pageSize: 50 }),
  });

  const handleExportRevenue = async () => {
    setIsExporting(true);
    try {
      await adminApi.exportRevenueReportCsv({ fromDate, toDate, groupBy });
    } catch {
      // ignore
    } finally {
      setIsExporting(false);
    }
  };

  const handleExportShowtimes = async () => {
    setIsExporting(true);
    try {
      await adminApi.exportShowtimesReportCsv({ fromDate, toDate });
    } catch {
      // ignore
    } finally {
      setIsExporting(false);
    }
  };

  const chartData = (revenueReport?.rows?.items || []).map((row) => ({
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

      {/* Date Filter Bar */}
      <Card className="p-4 bg-background-surface">
        <div className="flex flex-wrap items-end gap-4">
          <Input
            label="Từ ngày"
            type="date"
            value={fromDate}
            onChange={(e) => setFromDate(e.target.value)}
            className="w-44"
          />
          <Input
            label="Đến ngày"
            type="date"
            value={toDate}
            onChange={(e) => setToDate(e.target.value)}
            className="w-44"
          />
          <Button
            variant="primary"
            onClick={() => {
              refetchSummary();
              refetchRevenue();
              refetchOperations();
            }}
          >
            <RefreshCw className="w-4 h-4 mr-1.5" /> Áp dụng bộ lọc
          </Button>
        </div>
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
            onChange={(t) => setGroupBy(t as typeof groupBy)}
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
              {revenueReport?.rows?.items?.map((row, idx) => (
                <tr key={idx} className="hover:bg-background-subtle/40 transition-colors">
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
