import React, { useEffect, useState } from 'react';
import { useParams, Link } from 'react-router-dom';
import { useQuery } from '@tanstack/react-query';
import { bookingApi } from '../../api/booking';
import { ticketsApi } from '../../api/tickets';
import type { TicketDetails } from '../../api/types';
import { Card } from '../../components/ui/Card';
import { Button } from '../../components/ui/Button';
import { Badge } from '../../components/ui/Badge';
import { Alert } from '../../components/ui/Alert';
import { ErrorAlert } from '../../components/common/ErrorAlert';
import { LoadingState } from '../../components/common/LoadingState';
import { formatTimeUtc7, formatDateUtc7, formatVND } from '../../lib/utils';
import {
  Ticket,
  Film,
  MapPin,
  Calendar,
  Download,
  Mail,
  ArrowLeft,
  CheckCircle2,
  Clock,
  Ban,
} from 'lucide-react';

export const TicketDetailPage: React.FC = () => {
  const { id } = useParams<{ id: string }>();

  // Fetch tickets for this booking
  const {
    data: tickets,
    isLoading,
    error,
  } = useQuery({
    queryKey: ['bookingTickets', id],
    queryFn: async () => {
      try {
        // Try bookingId first
        return await bookingApi.listBookingTickets(id!);
      } catch {
        // If not bookingId, try ticketId
        const single = await ticketsApi.getTicket(id!);
        return [single];
      }
    },
    enabled: !!id,
  });

  const [resendEmailSuccess, setResendEmailSuccess] = useState(false);
  const [isResending, setIsResending] = useState(false);
  const [resendError, setResendError] = useState<unknown>(null);

  const handleResendEmail = async () => {
    if (!tickets || tickets.length === 0) return;
    const bookingId = tickets[0].bookingId;

    setIsResending(true);
    setResendError(null);
    setResendEmailSuccess(false);

    try {
      await bookingApi.resendTicketEmail(bookingId);
      setResendEmailSuccess(true);
    } catch (err) {
      setResendError(err);
    } finally {
      setIsResending(false);
    }
  };

  if (isLoading) {
    return <LoadingState message="Đang tải thông tin vé điện tử..." />;
  }

  if (error || !tickets || tickets.length === 0) {
    return (
      <div className="container mx-auto px-4 py-12 max-w-xl">
        <ErrorAlert error={error || 'Không tìm thấy vé'} />
        <Link to="/tickets" className="mt-4 block">
          <Button variant="outline">
            <ArrowLeft className="w-4 h-4 mr-2" /> Về danh sách vé
          </Button>
        </Link>
      </div>
    );
  }

  const sample = tickets[0];

  return (
    <div className="container mx-auto px-4 py-8 max-w-4xl space-y-8">
      {/* Back button */}
      <div>
        <Link
          to="/tickets"
          className="inline-flex items-center text-sm font-medium text-foreground-muted hover:text-primary transition-colors"
        >
          <ArrowLeft className="w-4 h-4 mr-1" /> Quay lại danh sách vé
        </Link>
      </div>

      <div className="flex flex-col sm:flex-row sm:items-center justify-between gap-4 border-b border-border pb-4">
        <div>
          <span className="text-xs font-semibold uppercase tracking-wider text-primary">
            Vé điện tử LumiCine
          </span>
          <h1 className="font-heading text-3xl font-black text-foreground">
            Mã đơn: {sample.bookingCode}
          </h1>
          <p className="text-sm text-foreground-muted mt-0.5">
            Xuất trình mã QR tại quầy soát vé hoặc cổng vào phòng chiếu.
          </p>
        </div>

        <div className="flex items-center gap-2">
          <Button
            variant="outline"
            size="sm"
            onClick={handleResendEmail}
            isLoading={isResending}
          >
            <Mail className="w-4 h-4 mr-1.5" /> Gửi lại email vé
          </Button>
        </div>
      </div>

      {resendEmailSuccess && (
        <Alert variant="success" title="Đã gửi lại email xác nhận">
          Thông tin vé điện tử đã được gửi lại vào email của bạn.
        </Alert>
      )}
      <ErrorAlert error={resendError} />

      {/* Grid of tickets (each seat has its own ticket & QR) */}
      <div className="grid grid-cols-1 md:grid-cols-2 gap-6">
        {tickets.map((ticket) => (
          <SingleTicketCard key={ticket.id} ticket={ticket} />
        ))}
      </div>
    </div>
  );
};

// Component that fetches QR blob with Bearer token and revokes blob URL on unmount
const SingleTicketCard: React.FC<{ ticket: TicketDetails }> = ({ ticket }) => {
  const [qrUrl, setQrUrl] = useState<string | null>(null);
  const [qrLoading, setQrLoading] = useState(true);
  const [qrError, setQrError] = useState(false);

  useEffect(() => {
    let active = true;
    let createdUrl: string | null = null;

    setQrLoading(true);
    ticketsApi
      .getTicketQrBlob(ticket.id)
      .then((blob) => {
        if (!active) return;
        createdUrl = window.URL.createObjectURL(blob);
        setQrUrl(createdUrl);
      })
      .catch(() => {
        if (active) setQrError(true);
      })
      .finally(() => {
        if (active) setQrLoading(false);
      });

    // Cleanup: revoke blob URL when leaving screen
    return () => {
      active = false;
      if (createdUrl) {
        window.URL.revokeObjectURL(createdUrl);
      }
    };
  }, [ticket.id]);

  const handleDownloadQr = () => {
    ticketsApi.downloadTicketQr(ticket.id, `LumiCine-Ticket-${ticket.ticketCode}.png`);
  };

  const isValid = ticket.status === 'Valid';
  const isUsed = ticket.status === 'Used';
  const isCancelled = ticket.status === 'Cancelled';

  return (
    <Card className="p-6 flex flex-col justify-between border-2 border-primary/20 space-y-6 bg-gradient-to-b from-background-surface to-background-subtle/30 shadow-md">
      {/* Header Info */}
      <div className="space-y-3">
        <div className="flex items-center justify-between border-b border-border pb-3">
          <div className="space-y-0.5">
            <span className="text-xs font-mono font-bold text-foreground">
              Mã vé: {ticket.ticketCode}
            </span>
            <div className="text-[11px] text-foreground-muted">
              Loại ghế: <strong className="text-foreground">{ticket.seatType}</strong>
            </div>
          </div>
          <div>
            {isValid && (
              <Badge variant="success" className="text-xs">
                <CheckCircle2 className="w-3.5 h-3.5 mr-1" /> Hợp lệ
              </Badge>
            )}
            {isUsed && (
              <Badge variant="neutral" className="text-xs">
                <Clock className="w-3.5 h-3.5 mr-1" /> Đã sử dụng
              </Badge>
            )}
            {isCancelled && (
              <Badge variant="danger" className="text-xs">
                <Ban className="w-3.5 h-3.5 mr-1" /> Đã vô hiệu
              </Badge>
            )}
          </div>
        </div>

        <div>
          <h3 className="font-heading font-black text-xl text-foreground line-clamp-1">
            {ticket.movieTitle}
          </h3>
          <div className="space-y-1 text-xs text-foreground-muted mt-2">
            <p className="flex items-center gap-1">
              <MapPin className="w-3.5 h-3.5 text-primary shrink-0" />
              {ticket.cinemaName} — {ticket.roomName}
            </p>
            <p className="flex items-center gap-1">
              <Calendar className="w-3.5 h-3.5 text-primary shrink-0" />
              {formatDateUtc7(ticket.startAtUtc)} lúc{' '}
              <strong className="text-foreground">{formatTimeUtc7(ticket.startAtUtc)}</strong>
            </p>
          </div>
        </div>

        {/* Seat Number Box */}
        <div className="p-3 rounded-xl bg-primary/10 border border-primary/20 text-center">
          <span className="text-xs text-primary font-medium block">Vị trí ghế ngồi</span>
          <span className="font-heading font-black text-3xl text-primary block">
            Hàng {ticket.rowLabel} — Ghế {ticket.number}
          </span>
        </div>
      </div>

      {/* QR Code Presentation */}
      <div className="flex flex-col items-center justify-center space-y-3 pt-2">
        <div className="w-48 h-48 rounded-xl border-2 border-dashed border-border bg-white flex items-center justify-center p-2 shadow-inner overflow-hidden">
          {qrLoading ? (
            <div className="text-center text-xs text-foreground-muted">
              <LoadingState message="Đang nạp QR..." />
            </div>
          ) : qrError || !qrUrl ? (
            <div className="text-center text-xs text-red-600 p-2">
              Không thể tải mã QR
            </div>
          ) : (
            <img
              src={qrUrl}
              alt={`Mã QR cho vé ${ticket.ticketCode}`}
              className={`w-full h-full object-contain ${
                isUsed || isCancelled ? 'opacity-40 grayscale' : ''
              }`}
            />
          )}
        </div>

        {isUsed && ticket.usedAtUtc && (
          <p className="text-xs text-foreground-muted font-medium">
            Đã check-in lúc: {formatTimeUtc7(ticket.usedAtUtc)} ngày {formatDateUtc7(ticket.usedAtUtc)}
          </p>
        )}

        <Button
          variant="outline"
          size="sm"
          onClick={handleDownloadQr}
          disabled={!qrUrl}
          className="text-xs"
        >
          <Download className="w-3.5 h-3.5 mr-1.5" /> Tải ảnh mã QR
        </Button>
      </div>
    </Card>
  );
};
