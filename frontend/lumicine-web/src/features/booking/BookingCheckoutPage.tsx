import React, { useState } from 'react';
import { useParams, useNavigate, Link } from 'react-router-dom';
import { useQuery } from '@tanstack/react-query';
import { bookingApi } from '../../api/booking';
import { Button } from '../../components/ui/Button';
import { Card } from '../../components/ui/Card';
import { Badge } from '../../components/ui/Badge';
import { Alert } from '../../components/ui/Alert';
import { ErrorAlert } from '../../components/common/ErrorAlert';
import { LoadingState } from '../../components/common/LoadingState';
import { CountdownTimer } from '../../components/common/CountdownTimer';
import { formatVND, formatDateTimeUtc7, generateUUID } from '../../lib/utils';
import { Ticket, Film, MapPin, Calendar, Clock, CreditCard, ArrowLeft, XCircle, CheckCircle2 } from 'lucide-react';

export const BookingCheckoutPage: React.FC = () => {
  const { id: bookingId } = useParams<{ id: string }>();
  const navigate = useNavigate();

  const [paymentRequestId] = useState<string>(() => generateUUID());
  const [isProcessingPayment, setIsProcessingPayment] = useState(false);
  const [isCancelling, setIsCancelling] = useState(false);
  const [apiError, setApiError] = useState<unknown>(null);

  const {
    data: booking,
    isLoading,
    error,
    refetch,
  } = useQuery({
    queryKey: ['booking', bookingId],
    queryFn: () => bookingApi.getBooking(bookingId!),
    enabled: !!bookingId,
  });

  const handleStartVnPay = async () => {
    if (!bookingId) return;
    setIsProcessingPayment(true);
    setApiError(null);

    try {
      const link = await bookingApi.createVnPayPayment(bookingId, paymentRequestId);
      // Store paymentId and requestId in sessionStorage for recovery after redirect
      sessionStorage.setItem('lumicine_last_payment_id', link.paymentId);
      sessionStorage.setItem('lumicine_last_booking_id', bookingId);
      sessionStorage.setItem('lumicine_last_payment_request_id', paymentRequestId);

      // Redirect to VNPay Sandbox
      window.location.href = link.paymentUrl;
    } catch (err) {
      setApiError(err);
      setIsProcessingPayment(false);
    }
  };

  const handleCancelBooking = async () => {
    if (!bookingId) return;
    if (!window.confirm('Bạn có chắc chắn muốn hủy giữ chỗ cho đơn đặt vé này?')) return;

    setIsCancelling(true);
    setApiError(null);

    try {
      await bookingApi.cancelBooking(bookingId);
      await refetch();
    } catch (err) {
      setApiError(err);
    } finally {
      setIsCancelling(false);
    }
  };

  if (isLoading) {
    return <LoadingState message="Đang tải thông tin đơn đặt vé..." />;
  }

  if (error || !booking) {
    return (
      <div className="container mx-auto px-4 py-12 max-w-xl">
        <ErrorAlert error={error || 'Không tìm thấy thông tin đặt vé'} />
        <div className="mt-4">
          <Link to="/">
            <Button variant="outline">
              <ArrowLeft className="w-4 h-4 mr-1.5" /> Về trang chủ
            </Button>
          </Link>
        </div>
      </div>
    );
  }

  const isPending = booking.status === 'Pending';
  const isConfirmed = booking.status === 'Confirmed';
  const isCancelled = booking.status === 'Cancelled';
  const isExpired = booking.status === 'Expired';

  return (
    <div className="container mx-auto px-4 py-8 max-w-3xl space-y-6">
      {/* Top Navigation */}
      <div>
        <Link
          to={`/showtimes/${booking.showtimeId}/seats`}
          className="inline-flex items-center text-sm font-medium text-foreground-muted hover:text-primary transition-colors"
        >
          <ArrowLeft className="w-4 h-4 mr-1" /> Quay lại sơ đồ ghế
        </Link>
      </div>

      <div className="flex flex-col sm:flex-row sm:items-center justify-between gap-4 border-b border-border pb-4">
        <div>
          <span className="text-xs font-semibold uppercase tracking-wider text-primary">
            Chi tiết giữ chỗ
          </span>
          <h1 className="font-heading text-3xl font-black text-foreground">
            Mã đơn: {booking.code}
          </h1>
        </div>

        <div>
          {isPending && (
            <Badge variant="warning" className="text-sm px-3 py-1">
              Đang chờ thanh toán
            </Badge>
          )}
          {isConfirmed && (
            <Badge variant="success" className="text-sm px-3 py-1">
              Đã xác nhận thanh toán
            </Badge>
          )}
          {isCancelled && (
            <Badge variant="danger" className="text-sm px-3 py-1">
              Đã hủy
            </Badge>
          )}
          {isExpired && (
            <Badge variant="neutral" className="text-sm px-3 py-1">
              Đã hết hạn
            </Badge>
          )}
        </div>
      </div>

      {/* Countdown Alert for Pending */}
      {isPending && booking.holdExpiresAtUtc && (
        <Alert variant="warning" title="Thời gian giữ ghế có hạn">
          <div className="flex flex-col sm:flex-row sm:items-center justify-between gap-2 mt-1">
            <p className="text-xs sm:text-sm">
              Vui lòng hoàn tất thanh toán trước khi thời gian giữ chỗ kết thúc để đảm bảo giữ được ghế đã chọn.
            </p>
            <CountdownTimer
              expiresAtUtc={booking.holdExpiresAtUtc}
              serverTimeUtc={booking.serverTimeUtc}
              onExpire={() => refetch()}
            />
          </div>
        </Alert>
      )}

      {isCancelled && (
        <Alert variant="error" title="Đơn đặt vé đã bị hủy">
          Đơn đặt vé này đã được hủy và các ghế đã được mở lại cho người khác.
        </Alert>
      )}

      {isExpired && (
        <Alert variant="error" title="Thời gian giữ ghế đã hết hạn">
          Đơn đặt vé đã hết thời gian giữ chỗ. Vui lòng chọn lại suất chiếu và ghế mới.
        </Alert>
      )}

      {isConfirmed && (
        <Alert variant="success" title="Đặt vé thành công!">
          Vé điện tử của bạn đã sẵn sàng. Bạn có thể xem vé và mã QR ngay bây giờ.
        </Alert>
      )}

      <ErrorAlert error={apiError} />

      {/* Booking Details Card */}
      <Card className="p-6 space-y-6">
        {/* Movie Info */}
        <div className="space-y-2 border-b border-border pb-4">
          <div className="flex items-center gap-2 text-foreground-muted text-xs">
            <Film className="w-4 h-4 text-primary" /> Phim chiếu
          </div>
          <h2 className="font-heading font-black text-2xl text-foreground">
            {booking.movieTitle}
          </h2>
          <div className="flex flex-wrap items-center gap-4 text-sm text-foreground-muted pt-1">
            <span className="flex items-center gap-1">
              <MapPin className="w-4 h-4 text-primary" /> {booking.cinemaName} — {booking.roomName}
            </span>
            <span className="flex items-center gap-1">
              <Calendar className="w-4 h-4 text-primary" /> Tạo lúc: {formatDateTimeUtc7(booking.createdAtUtc)}
            </span>
          </div>
        </div>

        {/* Seats Breakdown */}
        <div className="space-y-3">
          <h3 className="font-heading font-bold text-lg text-foreground">
            Danh sách ghế ({booking.seats.length} ghế)
          </h3>
          <div className="divide-y divide-border border border-border rounded-xl overflow-hidden bg-background">
            {booking.seats.map((seat) => (
              <div
                key={seat.seatId}
                className="flex items-center justify-between p-3 text-sm"
              >
                <div className="flex items-center gap-2">
                  <span className="font-bold font-mono px-2 py-0.5 rounded bg-background-surface border border-border">
                    {seat.rowLabel}{seat.number}
                  </span>
                  <Badge variant={seat.seatType === 'VIP' ? 'warning' : 'outline'} className="text-[10px]">
                    {seat.seatType}
                  </Badge>
                </div>
                <span className="font-semibold text-foreground">
                  {formatVND(seat.unitPrice)}
                </span>
              </div>
            ))}
          </div>
        </div>

        {/* Total Amount */}
        <div className="pt-4 border-t border-border flex items-center justify-between">
          <span className="text-base font-semibold text-foreground">Tổng cộng</span>
          <span className="font-heading font-black text-3xl text-primary">
            {formatVND(booking.totalAmount)}
          </span>
        </div>

        {/* Actions */}
        <div className="pt-4 flex flex-col sm:flex-row gap-3">
          {isPending && (
            <>
              <Button
                variant="primary"
                size="lg"
                className="flex-1"
                onClick={handleStartVnPay}
                isLoading={isProcessingPayment}
              >
                <CreditCard className="w-5 h-5 mr-2" /> Thanh toán qua VNPay Sandbox
              </Button>
              <Button
                variant="outline"
                size="lg"
                onClick={handleCancelBooking}
                isLoading={isCancelling}
                className="text-red-600 border-red-200 hover:bg-red-50"
              >
                <XCircle className="w-5 h-5 mr-1.5" /> Hủy giữ chỗ
              </Button>
            </>
          )}

          {isConfirmed && (
            <Link to={`/tickets`} className="w-full">
              <Button variant="primary" size="lg" className="w-full">
                <Ticket className="w-5 h-5 mr-2" /> Xem danh sách vé điện tử
              </Button>
            </Link>
          )}

          {(isCancelled || isExpired) && (
            <Link to={`/movies/${booking.movieId}`} className="w-full">
              <Button variant="primary" size="lg" className="w-full">
                Đặt lại suất chiếu khác
              </Button>
            </Link>
          )}
        </div>
      </Card>
    </div>
  );
};
