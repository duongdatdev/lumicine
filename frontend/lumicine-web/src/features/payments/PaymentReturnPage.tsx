import React, { useEffect, useState, useRef, useCallback } from 'react';
import { useLocation, Link, useNavigate } from 'react-router-dom';
import { paymentsApi } from '../../api/payments';
import { bookingApi } from '../../api/booking';
import type { PaymentView, BookingDetails } from '../../api/types';
import { useAuth } from '../../auth/useAuth';
import { Card } from '../../components/ui/Card';
import { Button } from '../../components/ui/Button';
import { Badge } from '../../components/ui/Badge';
import { Alert } from '../../components/ui/Alert';
import { ErrorAlert } from '../../components/common/ErrorAlert';
import { LoadingState } from '../../components/common/LoadingState';
import { formatVND } from '../../lib/utils';
import {
  CheckCircle2,
  XCircle,
  Clock,
  AlertTriangle,
  RefreshCw,
  Ticket,
  ArrowRight,
  ShieldCheck,
} from 'lucide-react';

export const PaymentReturnPage: React.FC = () => {
  const location = useLocation();
  const navigate = useNavigate();
  const { isAuthenticated, refresh: refreshAuth } = useAuth();

  const [isLoading, setIsLoading] = useState(true);
  const [isPolling, setIsPolling] = useState(false);
  const [pollSeconds, setPollSeconds] = useState(0);
  const [payment, setPayment] = useState<PaymentView | null>(null);
  const [booking, setBooking] = useState<BookingDetails | null>(null);
  const [apiError, setApiError] = useState<unknown>(null);
  const [message, setMessage] = useState<string>('');

  const pollIntervalRef = useRef<NodeJS.Timeout | null>(null);
  const pollTimeoutRef = useRef<NodeJS.Timeout | null>(null);

  // Restore session and verify return
  const verifyReturn = useCallback(async () => {
    setIsLoading(true);
    setApiError(null);

    try {
      // Refresh auth to ensure session is active
      await refreshAuth();

      // Call backend VNPay return validator
      const queryString = location.search.startsWith('?')
        ? location.search.substring(1)
        : location.search;

      const result = await paymentsApi.processVnPayReturn(queryString);
      setMessage(result.message);

      if (result.payment) {
        const ownedPayment = await paymentsApi.getPayment(result.payment.id);
        const ownedBooking = await bookingApi.getBooking(ownedPayment.bookingId);
        setPayment(ownedPayment);
        setBooking(ownedBooking);
      }
    } catch (err) {
      setApiError(err);
    } finally {
      setIsLoading(false);
    }
  }, [location.search, refreshAuth]);

  useEffect(() => {
    verifyReturn();
  }, [verifyReturn]);

  // Polling loop for non-final states (Pending)
  useEffect(() => {
    if (payment?.status === 'Pending') {
      setIsPolling(true);
      setPollSeconds(0);

      const startTime = Date.now();

      pollIntervalRef.current = setInterval(async () => {
        const elapsed = Math.floor((Date.now() - startTime) / 1000);
        setPollSeconds(elapsed);

        if (elapsed >= 60) {
          // Stop polling after 60 seconds
          if (pollIntervalRef.current) clearInterval(pollIntervalRef.current);
          setIsPolling(false);
          return;
        }

        try {
          const freshPayment = await paymentsApi.getPayment(payment.id);
          setPayment(freshPayment);

          if (freshPayment.bookingId) {
            const freshBooking = await bookingApi.getBooking(freshPayment.bookingId);
            setBooking(freshBooking);
          }

          if (freshPayment.status !== 'Pending') {
            if (pollIntervalRef.current) clearInterval(pollIntervalRef.current);
            setIsPolling(false);
          }
        } catch {
          // ignore transient poll error
        }
      }, 3000);
    }

    return () => {
      if (pollIntervalRef.current) clearInterval(pollIntervalRef.current);
      if (pollTimeoutRef.current) clearTimeout(pollTimeoutRef.current);
    };
  }, [payment?.id, payment?.status]);

  if (isLoading) {
    return <LoadingState message="Đang kiểm tra kết quả giao dịch từ cổng thanh toán..." />;
  }

  const status = payment?.status || 'Unknown';
  const isRequiresRefundReview = payment?.processingStatus === 'RequiresRefundReview';
  const isRefundRecorded = payment?.processingStatus === 'RefundRecorded';
  const isSuccess = !isRequiresRefundReview && !isRefundRecorded &&
    (payment?.processingStatus === 'Fulfilled' || booking?.status === 'Confirmed');
  const isFailed = status === 'Failed';
  const isExpired = status === 'Pending' && booking?.status === 'Expired';
  const isPending = status === 'Pending' && !isExpired && !isSuccess;

  return (
    <div className="container mx-auto px-4 py-12 max-w-2xl">
      <Card className="p-8 shadow-xl text-center space-y-6 border-border">
        {/* Status Icon & Title */}
        {isSuccess && (
          <div className="space-y-3">
            <div className="inline-flex p-4 rounded-full bg-green-100 text-green-700">
              <CheckCircle2 className="w-16 h-16" />
            </div>
            <h1 className="font-heading text-3xl font-black text-foreground">
              Thanh toán thành công!
            </h1>
            <p className="text-sm text-foreground-muted max-w-md mx-auto">
              Giao dịch của bạn đã được xác nhận thành công. Vé điện tử đã được phát hành và sẵn sàng sử dụng.
            </p>
          </div>
        )}

        {isFailed && (
          <div className="space-y-3">
            <div className="inline-flex p-4 rounded-full bg-red-100 text-red-600">
              <XCircle className="w-16 h-16" />
            </div>
            <h1 className="font-heading text-3xl font-black text-foreground">
              Giao dịch không thành công
            </h1>
            <p className="text-sm text-foreground-muted max-w-md mx-auto">
              {message || 'Thanh toán bị hủy hoặc không thành công từ ngân hàng.'}
            </p>
          </div>
        )}

        {isExpired && (
          <div className="space-y-3">
            <div className="inline-flex p-4 rounded-full bg-gray-100 text-gray-600">
              <Clock className="w-16 h-16" />
            </div>
            <h1 className="font-heading text-3xl font-black text-foreground">
              Giao dịch đã hết hạn
            </h1>
            <p className="text-sm text-foreground-muted max-w-md mx-auto">
              Thời gian xử lý thanh toán đã quá hạn. Chỗ ngồi đã được mở lại.
            </p>
          </div>
        )}

        {isRequiresRefundReview && (
          <div className="space-y-3">
            <div className="inline-flex p-4 rounded-full bg-amber-100 text-amber-700">
              <AlertTriangle className="w-16 h-16" />
            </div>
            <h1 className="font-heading text-3xl font-black text-foreground">
              Giao dịch cần đối soát hoàn tiền
            </h1>
            <p className="text-sm text-foreground-muted max-w-md mx-auto">
              Tiền đã trừ nhưng vé không thể giữ do hết hạn hoặc xung đột. Ban quản trị đã ghi nhận giao dịch để xử lý hoàn tiền cho bạn.
            </p>
          </div>
        )}

        {isPending && (
          <div className="space-y-4">
            <div className="inline-flex p-4 rounded-full bg-blue-100 text-blue-700">
              <RefreshCw className={`w-16 h-16 ${isPolling ? 'animate-spin' : ''}`} />
            </div>
            <h1 className="font-heading text-3xl font-black text-foreground">
              Đang xác nhận kết quả thanh toán...
            </h1>
            <p className="text-sm text-foreground-muted max-w-md mx-auto">
              {isPolling
                ? `Hệ thống đang tự động kiểm tra trạng thái với VNPay (${pollSeconds}s / 60s)...`
                : 'Chưa nhận được kết quả cuối cùng từ VNPay. Bạn có thể bấm kiểm tra lại.'}
            </p>
          </div>
        )}

        {isRefundRecorded && <Alert variant="info">Khoản hoàn tiền đã được ghi nhận.</Alert>}

        <ErrorAlert error={apiError} />

        {/* Transaction Information details */}
        {payment && (
          <div className="border border-border rounded-xl p-4 bg-background-subtle/50 text-left space-y-2 text-sm">
            <div className="flex justify-between">
              <span className="text-foreground-muted">Mã tham chiếu:</span>
              <span className="font-mono font-bold text-foreground">{payment.merchantReference}</span>
            </div>
            <div className="flex justify-between">
              <span className="text-foreground-muted">Số tiền thanh toán:</span>
              <span className="font-bold text-primary font-heading text-lg">
                {formatVND(payment.amount)}
              </span>
            </div>
            {payment.vnPayTransactionId && (
              <div className="flex justify-between">
                <span className="text-foreground-muted">Mã giao dịch VNPay:</span>
                <span className="font-mono text-foreground">{payment.vnPayTransactionId}</span>
              </div>
            )}
            <div className="flex justify-between items-center pt-1">
              <span className="text-foreground-muted">Trạng thái:</span>
              <Badge
                variant={
                  isSuccess
                    ? 'success'
                    : isFailed
                    ? 'danger'
                    : isRequiresRefundReview
                    ? 'warning'
                    : 'info'
                }
              >
                {status}
              </Badge>
            </div>
          </div>
        )}

        {/* Action Buttons */}
        <div className="pt-4 flex flex-col sm:flex-row gap-3 justify-center">
          {isSuccess && (
            <Link to="/tickets">
              <Button variant="primary" size="lg" className="w-full sm:w-auto">
                <Ticket className="w-5 h-5 mr-2" /> Xem vé điện tử của tôi
              </Button>
            </Link>
          )}

          {isPending && (
            <Button
              variant="outline"
              size="lg"
              onClick={verifyReturn}
              isLoading={isLoading}
              className="w-full sm:w-auto"
            >
              <RefreshCw className="w-4 h-4 mr-2" /> Kiểm tra lại
            </Button>
          )}

          {isRequiresRefundReview && (
            <div className="space-y-2 w-full">
              <Alert variant="info" className="text-xs text-left">
                Nhân viên chăm sóc khách hàng của LumiCine sẽ liên hệ qua email tài khoản của bạn để xác nhận thông tin hoàn trả trong vòng 24 giờ.
              </Alert>
              <Link to="/tickets" className="block">
                <Button variant="outline" className="w-full">
                  Xem danh sách vé
                </Button>
              </Link>
            </div>
          )}

          {(isFailed || isExpired) && (
            <div className="flex gap-3 w-full justify-center">
              {booking && (
                <Link to={`/showtimes/${booking.showtimeId}/seats`}>
                  <Button variant="primary" size="lg">
                    Chọn lại ghế
                  </Button>
                </Link>
              )}
              <Link to="/movies">
                <Button variant="outline" size="lg">
                  Về trang phim
                </Button>
              </Link>
            </div>
          )}
        </div>
      </Card>
    </div>
  );
};
