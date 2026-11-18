import React, { useState } from 'react';
import { useSearchParams } from 'react-router-dom';
import { useQuery } from '@tanstack/react-query';
import { adminApi } from '../../api/admin';
import type { AdminPaymentView } from '../../api/types';
import { Card } from '../../components/ui/Card';
import { Button } from '../../components/ui/Button';
import { Badge } from '../../components/ui/Badge';
import { Input } from '../../components/ui/Input';
import { Modal } from '../../components/ui/Modal';
import { Textarea } from '../../components/ui/Textarea';
import { Pagination } from '../../components/ui/Pagination';
import { LoadingState } from '../../components/common/LoadingState';
import { EmptyState } from '../../components/common/EmptyState';
import { ErrorAlert } from '../../components/common/ErrorAlert';
import { formatVND, formatDateTimeUtc7, parsePositivePage } from '../../lib/utils';
import {
  CreditCard,
  Search,
  RefreshCw,
  RotateCcw,
  AlertTriangle,
  CheckCircle2,
  XCircle,
  Clock,
  ShieldAlert,
} from 'lucide-react';

export const AdminPaymentsPage: React.FC = () => {
  const [searchParams, setSearchParams] = useSearchParams();
  const page = parsePositivePage(searchParams.get('page'));
  const statusFilter = searchParams.get('status') ?? '';
  const processingStatusFilter = searchParams.get('processingStatus') ?? '';
  const bookingIdSearch = searchParams.get('bookingId')?.trim() ?? '';
  const validBookingId = !bookingIdSearch || /^[0-9a-f]{8}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{12}$/i.test(bookingIdSearch);
  const updateFilters = (updates: Record<string, string>) => {
    const next = new URLSearchParams(searchParams);
    Object.entries(updates).forEach(([key, value]) => value ? next.set(key, value) : next.delete(key));
    setSearchParams(next, { replace: true });
  };

  // Refund modal
  const [isRefundModalOpen, setIsRefundModalOpen] = useState(false);
  const [selectedPayment, setSelectedPayment] = useState<AdminPaymentView | null>(null);
  const [refundReference, setRefundReference] = useState('');
  const [refundNote, setRefundNote] = useState('');
  const [isRefunding, setIsRefunding] = useState(false);

  // Reconcile state
  const [reconcileResult, setReconcileResult] = useState<string | null>(null);
  const [isReconciling, setIsReconciling] = useState<string | null>(null);

  const [actionError, setActionError] = useState<unknown>(null);

  const { data, isLoading, error, refetch } = useQuery({
    queryKey: [
      'adminPayments',
      { status: statusFilter, processingStatus: processingStatusFilter, bookingId: bookingIdSearch, page },
    ],
    enabled: validBookingId,
    queryFn: () =>
      adminApi.listPayments({
        status: statusFilter || undefined,
        processingStatus: processingStatusFilter || undefined,
        bookingId: validBookingId ? bookingIdSearch || undefined : undefined,
        page,
        pageSize: 15,
      }),
  });

  const handleOpenRefundModal = (item: AdminPaymentView) => {
    setSelectedPayment(item);
    setRefundReference(`REF-${Date.now()}`);
    setRefundNote('');
    setActionError(null);
    setIsRefundModalOpen(true);
  };

  const handleRefundSubmit = async (e: React.FormEvent) => {
    e.preventDefault();
    if (!selectedPayment) return;
    setIsRefunding(true);
    setActionError(null);
    try {
      await adminApi.recordRefund(selectedPayment.payment.id, {
        reference: refundReference,
        note: refundNote || undefined,
      });
      setIsRefundModalOpen(false);
      refetch();
    } catch (err) {
      setActionError(err);
    } finally {
      setIsRefunding(false);
    }
  };

  const handleReconcile = async (paymentId: string) => {
    setIsReconciling(paymentId);
    setActionError(null);
    setReconcileResult(null);
    try {
      const res = await adminApi.reconcilePayment(paymentId);
      setReconcileResult(`Đối soát VNPay: ${res.message} (RspCode: ${res.rspCode})`);
      refetch();
    } catch (err) {
      setActionError(err);
    } finally {
      setIsReconciling(null);
    }
  };

  return (
    <div className="space-y-6">
      <div className="flex flex-col sm:flex-row sm:items-center justify-between gap-4">
        <div>
          <h1 className="font-heading text-3xl font-black text-foreground">
            Quản lý Giao dịch & Đối soát
          </h1>
          <p className="text-sm text-foreground-muted">
            Tra cứu thanh toán, kiểm tra trạng thái VNPay và ghi nhận hoàn tiền cho khách.
          </p>
        </div>

        <Button
          variant={processingStatusFilter === 'RequiresRefundReview' ? 'danger' : 'outline'}
          size="sm"
          onClick={() => {
            if (processingStatusFilter === 'RequiresRefundReview') {
              updateFilters({ processingStatus: '', page: '' });
            } else {
              updateFilters({ processingStatus: 'RequiresRefundReview', page: '' });
            }
          }}
        >
          <AlertTriangle className="w-4 h-4 mr-1.5" />
          {processingStatusFilter === 'RequiresRefundReview'
            ? 'Đang lọc: Cần đối soát hoàn tiền'
            : 'Lọc giao dịch cần đối soát'}
        </Button>
      </div>

      <ErrorAlert error={actionError} />

      {reconcileResult && (
        <Card className="p-4 bg-blue-50 border-blue-200 text-blue-900 text-sm">
          {reconcileResult}
        </Card>
      )}

      {/* Filter Bar */}
      <Card className="p-4 bg-background-surface">
        <div className="grid grid-cols-1 sm:grid-cols-4 gap-4 items-end">
          <div>
            <label className="block text-sm font-medium text-foreground mb-1.5">Trạng thái thanh toán</label>
            <select
              value={statusFilter}
              onChange={(e) => updateFilters({ status: e.target.value, page: '' })}
              className="h-10 w-full rounded-lg border border-border bg-background px-3 text-sm"
            >
              <option value="">Tất cả trạng thái</option>
              <option value="Pending">Pending (Chờ xử lý)</option>
              <option value="Succeeded">Thành công</option>
              <option value="Failed">Failed (Thất bại)</option>
            </select>
          </div>

          <Input
            label="Tìm theo mã Booking (ID)"
            placeholder="Nhập GUID booking..."
            value={bookingIdSearch}
            onChange={(e) => updateFilters({ bookingId: e.target.value, page: '' })}
          />
          {!validBookingId && <p className="text-xs text-red-600 sm:col-span-4">Nhập đầy đủ mã Booking dạng GUID để tìm kiếm.</p>}

          <div>
            <Button variant="outline" className="w-full" disabled={!validBookingId} onClick={() => refetch()}>
              <RefreshCw className="w-4 h-4 mr-1.5" /> Làm mới
            </Button>
          </div>
        </div>
      </Card>

      {/* Payments Table */}
      {isLoading ? (
        <LoadingState message="Đang tải danh sách giao dịch..." />
      ) : error ? (
        <ErrorAlert error={error} title="Không thể tải giao dịch" />
      ) : !data?.items || data.items.length === 0 ? (
        <EmptyState title="Không tìm thấy giao dịch nào" />
      ) : (
        <Card className="p-0 overflow-hidden border-border">
          <div className="overflow-x-auto">
            <table className="w-full text-left text-sm">
              <thead className="bg-background-subtle border-b border-border text-xs font-semibold text-foreground-muted uppercase">
                <tr>
                  <th className="px-4 py-3.5">Mã tham chiếu / Booking</th>
                  <th className="px-4 py-3.5">Khách hàng</th>
                  <th className="px-4 py-3.5">Số tiền</th>
                  <th className="px-4 py-3.5">VNPay TrxId</th>
                  <th className="px-4 py-3.5">Trạng thái</th>
                  <th className="px-4 py-3.5">Thời gian</th>
                  <th className="px-4 py-3.5 text-right">Thao tác</th>
                </tr>
              </thead>
              <tbody className="divide-y divide-border">
                {data.items.map((item) => {
                  const p = item.payment;
                  const isSuccess = p.status === 'Succeeded';
                  const isRequiresRefund = p.processingStatus === 'RequiresRefundReview';

                  return (
                    <tr
                      key={p.id}
                      className={`hover:bg-background-subtle/50 transition-colors ${
                        isRequiresRefund ? 'bg-amber-50/50' : ''
                      }`}
                    >
                      <td className="px-4 py-3">
                        <p className="font-mono font-bold text-foreground text-xs">
                          {p.merchantReference}
                        </p>
                        <p className="text-xs text-foreground-muted">
                          Booking: #{item.bookingCode}
                        </p>
                      </td>
                      <td className="px-4 py-3 text-xs text-foreground-muted">
                        {item.customerEmail || '—'}
                      </td>
                      <td className="px-4 py-3 font-bold text-primary">
                        {formatVND(p.amount)}
                      </td>
                      <td className="px-4 py-3 font-mono text-xs text-foreground-muted">
                        {p.vnPayTransactionId || '—'}
                      </td>
                      <td className="px-4 py-3">
                        <Badge
                          variant={
                            isSuccess
                              ? 'success'
                              : isRequiresRefund
                              ? 'warning'
                              : p.status === 'Failed'
                              ? 'danger'
                              : 'neutral'
                          }
                          className="text-xs"
                        >
                          {p.status}
                        </Badge>
                      </td>
                      <td className="px-4 py-3 text-xs text-foreground-muted">
                        {formatDateTimeUtc7(p.createdAtUtc)}
                      </td>
                      <td className="px-4 py-3 text-right space-x-2">
                        <Button
                          variant="outline"
                          size="sm"
                          onClick={() => handleReconcile(p.id)}
                          isLoading={isReconciling === p.id}
                          title="Truy vấn đối soát trực tiếp từ VNPay"
                          className="text-xs"
                        >
                          Đối soát
                        </Button>

                        {isRequiresRefund && (
                          <Button
                            variant="primary"
                            size="sm"
                            onClick={() => handleOpenRefundModal(item)}
                            className="text-xs bg-amber-700 hover:bg-amber-800"
                          >
                            Ghi nhận hoàn tiền
                          </Button>
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
              currentPage={data.page}
              totalPages={Math.ceil(data.totalCount / data.pageSize)}
              onPageChange={(nextPage) => updateFilters({ page: String(nextPage) })}
            />
          </div>
        </Card>
      )}

      {/* Manual Refund Modal */}
      <Modal
        isOpen={isRefundModalOpen}
        onClose={() => setIsRefundModalOpen(false)}
        title="Ghi nhận hoàn tiền thủ công"
        description={`Đơn thanh toán: ${selectedPayment?.payment.merchantReference} (${formatVND(
          selectedPayment?.payment.amount || 0
        )})`}
      >
        <form onSubmit={handleRefundSubmit} className="space-y-4">
          <Input
            label="Mã tham chiếu hoàn tiền (Reference code)"
            required
            value={refundReference}
            onChange={(e) => setRefundReference(e.target.value)}
          />

          <Textarea
            label="Ghi chú hoàn tiền (Tài khoản ngân hàng / Lý do)"
            rows={3}
            placeholder="Hoàn tiền qua chuyển khoản Vietcombank cho khách..."
            value={refundNote}
            onChange={(e) => setRefundNote(e.target.value)}
          />

          <div className="pt-4 flex justify-end gap-2">
            <Button type="button" variant="outline" onClick={() => setIsRefundModalOpen(false)}>
              Hủy
            </Button>
            <Button type="submit" variant="primary" isLoading={isRefunding}>
              Xác nhận ghi nhận hoàn tiền
            </Button>
          </div>
        </form>
      </Modal>
    </div>
  );
};
