import React, { useEffect, useState, useRef, useCallback, useMemo } from 'react';
import { useParams, useNavigate, Link } from 'react-router-dom';
import { useQuery, useQueryClient } from '@tanstack/react-query';
import { catalogApi } from '../../api/catalog';
import { bookingApi } from '../../api/booking';
import { createShowtimeHubConnection, joinShowtime, leaveShowtime } from '../../api/realtime';
import type { ShowtimeSeatSummary, BookingDetails } from '../../api/types';
import { HubConnection, HubConnectionState } from '@microsoft/signalr';
import { ApiError } from '../../api/client';
import { useAuth } from '../../auth/useAuth';
import { Button } from '../../components/ui/Button';
import { Card } from '../../components/ui/Card';
import { Badge } from '../../components/ui/Badge';
import { Alert } from '../../components/ui/Alert';
import { ErrorAlert } from '../../components/common/ErrorAlert';
import { LoadingState } from '../../components/common/LoadingState';
import { CountdownTimer } from '../../components/common/CountdownTimer';
import { formatVND, formatTimeUtc7, formatDateUtc7, generateUUID, cn } from '../../lib/utils';
import { Film, Clock, MapPin, Tv, AlertCircle, RefreshCw, CheckCircle2, Wifi, WifiOff } from 'lucide-react';

const MAX_SEATS = 8;

export const ShowtimeSeatSelectionPage: React.FC = () => {
  const { id: showtimeId } = useParams<{ id: string }>();
  const navigate = useNavigate();
  const queryClient = useQueryClient();
  const { isAuthenticated, user } = useAuth();

  // Local selected seat IDs
  const [selectedSeatIds, setSelectedSeatIds] = useState<string[]>([]);
  const [bookingRequestId, setBookingRequestId] = useState<string>(() => generateUUID());
  const [isSubmitting, setIsSubmitting] = useState(false);
  const [errorNotice, setErrorNotice] = useState<string | null>(null);
  const [apiError, setApiError] = useState<unknown>(null);
  const [lastUpdated, setLastUpdated] = useState<Date>(new Date());
  const [isRealtimeConnected, setIsRealtimeConnected] = useState<boolean>(false);

  // Active pending booking (if user already holds seats)
  const [existingPendingBooking, setExistingPendingBooking] = useState<BookingDetails | null>(null);

  // 1. Fetch Showtime Info
  const { data: showtime, isLoading: isShowtimeLoading } = useQuery({
    queryKey: ['showtime', showtimeId],
    queryFn: () => catalogApi.getShowtime(showtimeId!),
    enabled: !!showtimeId,
  });

  // 2. Fetch Seats Snapshot
  const {
    data: seats,
    isLoading: isSeatsLoading,
    refetch: refetchSeats,
  } = useQuery({
    queryKey: ['showtimeSeats', showtimeId],
    queryFn: async () => {
      const data = await catalogApi.getShowtimeSeats(showtimeId!);
      setLastUpdated(new Date());
      return data;
    },
    enabled: !!showtimeId,
  });

  // Check if user has an existing pending booking for this showtime
  useEffect(() => {
    if (!isAuthenticated || !showtimeId) return;

    bookingApi
      .listBookings({ status: 'Pending', pageSize: 10 })
      .then((res) => {
        const found = res.items.find((b) => b.showtimeId === showtimeId && b.status === 'Pending');
        if (found) {
          setExistingPendingBooking(found);
          // Preselect seats of this booking
          setSelectedSeatIds(found.seats.map((s) => s.seatId));
        }
      })
      .catch(() => {
        // ignore
      });
  }, [isAuthenticated, showtimeId]);

  // 3. Realtime SignalR Connection setup
  const hubRef = useRef<HubConnection | null>(null);

  useEffect(() => {
    if (!showtimeId) return;

    const hub = createShowtimeHubConnection();
    hubRef.current = hub;

    const handleSeatAvailabilityChanged = () => {
      refetchSeats();
    };

    hub.on('SeatAvailabilityChanged', handleSeatAvailabilityChanged);

    hub.onreconnecting(() => {
      setIsRealtimeConnected(false);
    });

    hub.onreconnected(async () => {
      setIsRealtimeConnected(true);
      await joinShowtime(hub, showtimeId);
      refetchSeats();
    });

    hub.onclose(() => {
      setIsRealtimeConnected(false);
    });

    hub
      .start()
      .then(async () => {
        setIsRealtimeConnected(true);
        await joinShowtime(hub, showtimeId);
      })
      .catch(() => {
        setIsRealtimeConnected(false);
      });

    // Polling every 15s and on visibilitychange
    const interval = setInterval(() => {
      refetchSeats();
    }, 15000);

    const handleVisibility = () => {
      if (document.visibilityState === 'visible') {
        refetchSeats();
      }
    };
    document.addEventListener('visibilitychange', handleVisibility);

    return () => {
      clearInterval(interval);
      document.removeEventListener('visibilitychange', handleVisibility);
      leaveShowtime(hub, showtimeId).finally(() => {
        hub.stop();
      });
      hubRef.current = null;
    };
  }, [showtimeId, refetchSeats]);

  // Group seats by row
  const rowsMap = useMemo(() => {
    if (!seats) return {};
    const map: Record<string, ShowtimeSeatSummary[]> = {};
    for (const seat of seats) {
      if (!map[seat.rowLabel]) {
        map[seat.rowLabel] = [];
      }
      map[seat.rowLabel].push(seat);
    }
    // Sort seats by number
    for (const row in map) {
      map[row].sort((a, b) => a.number - b.number);
    }
    return map;
  }, [seats]);

  const sortedRowLabels = useMemo(() => {
    return Object.keys(rowsMap).sort();
  }, [rowsMap]);

  // Toggle seat selection
  const handleSeatClick = (seat: ShowtimeSeatSummary) => {
    // Cannot select Sold or Held (unless held by user's existing pending booking)
    const isHeldByMe = existingPendingBooking?.seats.some((s) => s.seatId === seat.seatId);
    if ((seat.status === 'Sold' || seat.status === 'Held') && !isHeldByMe) {
      return;
    }

    if (existingPendingBooking) {
      // If user already has a pending booking hold, they can proceed to payment or cancel it
      return;
    }

    setErrorNotice(null);
    setSelectedSeatIds((prev) => {
      if (prev.includes(seat.seatId)) {
        return prev.filter((id) => id !== seat.seatId);
      } else {
        if (prev.length >= MAX_SEATS) {
          setErrorNotice(`Bạn chỉ có thể chọn tối đa ${MAX_SEATS} ghế cho một lần đặt.`);
          return prev;
        }
        return [...prev, seat.seatId];
      }
    });
  };

  // Calculate selected total
  const selectedSeats = useMemo(() => {
    if (!seats) return [];
    return seats.filter((s) => selectedSeatIds.includes(s.seatId));
  }, [seats, selectedSeatIds]);

  const totalAmount = useMemo(() => {
    if (existingPendingBooking) return existingPendingBooking.totalAmount;
    return selectedSeats.reduce((sum, s) => sum + s.price, 0);
  }, [existingPendingBooking, selectedSeats]);

  // Submit Booking Hold
  const handleCreateBooking = async () => {
    if (!isAuthenticated) {
      navigate(
        `/account/login?returnTo=${encodeURIComponent(`/showtimes/${showtimeId}/seats`)}`
      );
      return;
    }

    if (selectedSeatIds.length === 0) {
      setErrorNotice('Vui lòng chọn ít nhất một ghế.');
      return;
    }

    setIsSubmitting(true);
    setApiError(null);
    setErrorNotice(null);

    try {
      const booking = await bookingApi.createBooking({
        requestId: bookingRequestId,
        showtimeId: showtimeId!,
        seatIds: selectedSeatIds,
      });

      // Navigate to checkout
      navigate(`/bookings/${booking.id}`);
    } catch (err: unknown) {
      if (err instanceof ApiError && err.status === 409) {
        // Seat status conflict: reload snapshot, retain still-available selections, alert user
        setErrorNotice('Một hoặc nhiều ghế bạn chọn vừa được người khác giữ hoặc đã bán. Sơ đồ ghế đã được cập nhật.');
        const freshSeats = await refetchSeats();
        if (freshSeats.data) {
          const freshAvailableIds = new Set(
            freshSeats.data.filter((s) => s.status === 'Available').map((s) => s.seatId)
          );
          setSelectedSeatIds((prev) => prev.filter((id) => freshAvailableIds.has(id)));
        }
      } else {
        setApiError(err);
      }
    } finally {
      setIsSubmitting(false);
    }
  };

  // Cancel existing pending booking
  const handleCancelPendingBooking = async () => {
    if (!existingPendingBooking) return;
    setIsSubmitting(true);
    try {
      await bookingApi.cancelBooking(existingPendingBooking.id);
      setExistingPendingBooking(null);
      setSelectedSeatIds([]);
      setBookingRequestId(generateUUID());
      refetchSeats();
    } catch (err) {
      setApiError(err);
    } finally {
      setIsSubmitting(false);
    }
  };

  if (isShowtimeLoading || isSeatsLoading) {
    return <LoadingState message="Đang tải sơ đồ ghế..." />;
  }

  return (
    <div className="container mx-auto px-4 py-8 space-y-8">
      {/* Showtime summary header */}
      <div className="flex flex-col md:flex-row md:items-center justify-between gap-4 border-b border-border pb-6">
        <div>
          <div className="flex items-center gap-2 text-xs font-semibold text-primary mb-1 uppercase tracking-wider">
            <Film className="w-4 h-4" /> Suất chiếu phim
          </div>
          <h1 className="font-heading text-2xl sm:text-3xl font-black text-foreground">
            {showtime?.movieTitle}
          </h1>
          <div className="flex flex-wrap items-center gap-3 text-sm text-foreground-muted mt-2">
            <span className="flex items-center gap-1">
              <MapPin className="w-4 h-4 text-primary" /> {showtime?.cinemaName} — {showtime?.roomName}
            </span>
            <span className="flex items-center gap-1">
              <Clock className="w-4 h-4 text-primary" /> {formatDateUtc7(showtime?.startAtUtc)} lúc{' '}
              <strong className="text-foreground">{formatTimeUtc7(showtime?.startAtUtc)}</strong>
            </span>
          </div>
        </div>

        {/* Realtime Status Indicator */}
        <div className="flex items-center gap-3">
          <div
            className={`flex items-center gap-1.5 px-3 py-1.5 rounded-full text-xs font-medium border ${
              isRealtimeConnected
                ? 'bg-green-50 text-green-700 border-green-200'
                : 'bg-amber-50 text-amber-700 border-amber-200'
            }`}
            title={`Cập nhật lúc: ${lastUpdated.toLocaleTimeString('vi-VN')}`}
          >
            {isRealtimeConnected ? (
              <>
                <Wifi className="w-3.5 h-3.5 text-green-600 animate-pulse" />
                <span>Trực tiếp (Realtime)</span>
              </>
            ) : (
              <>
                <WifiOff className="w-3.5 h-3.5 text-amber-600" />
                <span>Mất kết nối realtime — Tự động cập nhật 15s</span>
              </>
            )}
          </div>
          <Button
            variant="ghost"
            size="sm"
            onClick={() => refetchSeats()}
            aria-label="Tải lại sơ đồ ghế"
            className="text-foreground-muted hover:text-foreground"
          >
            <RefreshCw className="w-4 h-4" />
          </Button>
        </div>
      </div>

      {/* Existing Pending Booking Hold Alert */}
      {existingPendingBooking && (
        <Alert variant="warning" title="Bạn đang có đơn giữ chỗ chờ thanh toán cho suất này">
          <div className="flex flex-col sm:flex-row sm:items-center justify-between gap-4 mt-2">
            <div className="space-y-1">
              <p>Mã đặt vé: <strong>{existingPendingBooking.code}</strong></p>
              {existingPendingBooking.holdExpiresAtUtc && (
                <CountdownTimer
                  expiresAtUtc={existingPendingBooking.holdExpiresAtUtc}
                  serverTimeUtc={existingPendingBooking.serverTimeUtc}
                  onExpire={() => {
                    setExistingPendingBooking(null);
                    refetchSeats();
                  }}
                />
              )}
            </div>
            <div className="flex gap-2">
              <Link to={`/bookings/${existingPendingBooking.id}`}>
                <Button variant="primary" size="sm">
                  Tiếp tục thanh toán
                </Button>
              </Link>
              <Button
                variant="outline"
                size="sm"
                onClick={handleCancelPendingBooking}
                isLoading={isSubmitting}
                className="text-red-600 border-red-200 hover:bg-red-50"
              >
                Hủy giữ chỗ
              </Button>
            </div>
          </div>
        </Alert>
      )}

      {errorNotice && <Alert variant="warning">{errorNotice}</Alert>}
      <ErrorAlert error={apiError} />

      {/* Cinema Screen Representation */}
      <div className="space-y-12">
        <div className="flex flex-col items-center">
          <div className="w-full max-w-2xl h-3 bg-gradient-to-r from-transparent via-primary/50 to-transparent rounded-full shadow-sm mb-2" />
          <div className="flex items-center gap-1.5 text-xs uppercase tracking-widest text-foreground-muted font-semibold">
            <Tv className="w-4 h-4" /> Màn hình
          </div>
        </div>

        {/* Seat Map Grid */}
        <div className="overflow-x-auto pb-6">
          <div className="min-w-[600px] max-w-3xl mx-auto space-y-2">
            {sortedRowLabels.map((rowLabel) => {
              const rowSeats = rowsMap[rowLabel] || [];
              return (
                <div key={rowLabel} className="flex items-center justify-center gap-2">
                  {/* Left row label */}
                  <span className="w-6 text-xs font-bold text-foreground-muted text-center">
                    {rowLabel}
                  </span>

                  {/* Seat buttons */}
                  <div className="flex gap-1.5 sm:gap-2">
                    {rowSeats.map((seat) => {
                      const isSelected = selectedSeatIds.includes(seat.seatId);
                      const isHeldByMe = existingPendingBooking?.seats.some(
                        (s) => s.seatId === seat.seatId
                      );
                      const isSold = seat.status === 'Sold';
                      const isHeld = seat.status === 'Held' && !isHeldByMe;
                      const isAvailable = seat.status === 'Available' || isHeldByMe;

                      // Status description for accessibility aria-label
                      const statusDescription = isHeldByMe
                        ? 'Ghế của bạn đang giữ'
                        : isSelected
                        ? 'Đang chọn'
                        : isSold
                        ? 'Đã bán'
                        : isHeld
                        ? 'Đang giữ'
                        : 'Còn trống';

                      const seatLabel = `Hàng ${seat.rowLabel} Ghế ${seat.number}, Loại ${seat.seatType}, Giá ${formatVND(seat.price)}, Trạng thái: ${statusDescription}`;

                      // Seat visual style
                      let seatClass =
                        'w-8 h-8 sm:w-9 sm:h-9 rounded-lg text-xs font-bold flex items-center justify-center transition-all focus-visible:outline-none focus-visible:ring-2 focus-visible:ring-primary ';

                      if (isHeldByMe) {
                        seatClass += 'bg-accent text-white shadow-sm ring-2 ring-accent';
                      } else if (isSelected) {
                        seatClass += 'bg-primary text-white shadow-sm ring-2 ring-primary';
                      } else if (isSold) {
                        seatClass += 'bg-gray-200 text-gray-400 cursor-not-allowed border border-gray-300';
                      } else if (isHeld) {
                        seatClass += 'bg-amber-100 text-amber-600 border border-amber-300 cursor-not-allowed';
                      } else {
                        // Available
                        if (seat.seatType === 'VIP') {
                          seatClass += 'bg-amber-50 text-amber-900 border border-amber-400 hover:bg-amber-100';
                        } else if (seat.seatType === 'Couple') {
                          seatClass += 'bg-pink-50 text-pink-900 border border-pink-400 hover:bg-pink-100';
                        } else {
                          seatClass += 'bg-background-surface text-foreground border border-border hover:border-primary hover:bg-primary-light/20';
                        }
                      }

                      return (
                        <button
                          key={seat.seatId}
                          type="button"
                          disabled={!isAvailable && !isHeldByMe}
                          aria-label={seatLabel}
                          title={seatLabel}
                          onClick={() => handleSeatClick(seat)}
                          className={seatClass}
                        >
                          {seat.number}
                        </button>
                      );
                    })}
                  </div>

                  {/* Right row label */}
                  <span className="w-6 text-xs font-bold text-foreground-muted text-center">
                    {rowLabel}
                  </span>
                </div>
              );
            })}
          </div>
        </div>

        {/* Legend */}
        <div className="flex flex-wrap items-center justify-center gap-6 pt-4 border-t border-border text-xs text-foreground-muted">
          <div className="flex items-center gap-2">
            <span className="w-5 h-5 rounded-md border border-border bg-background-surface inline-block" />
            <span>Ghế trống</span>
          </div>
          <div className="flex items-center gap-2">
            <span className="w-5 h-5 rounded-md border border-amber-400 bg-amber-50 inline-block" />
            <span>Ghế VIP</span>
          </div>
          <div className="flex items-center gap-2">
            <span className="w-5 h-5 rounded-md border border-pink-400 bg-pink-50 inline-block" />
            <span>Ghế Couple</span>
          </div>
          <div className="flex items-center gap-2">
            <span className="w-5 h-5 rounded-md bg-primary text-white inline-block" />
            <span>Đang chọn</span>
          </div>
          <div className="flex items-center gap-2">
            <span className="w-5 h-5 rounded-md bg-amber-100 border border-amber-300 inline-block" />
            <span>Đang giữ chỗ</span>
          </div>
          <div className="flex items-center gap-2">
            <span className="w-5 h-5 rounded-md bg-gray-200 border border-gray-300 inline-block" />
            <span>Đã bán</span>
          </div>
        </div>
      </div>

      {/* Floating Bottom Booking Summary Bar */}
      <div className="sticky bottom-0 z-30 -mx-4 -mb-8 p-4 sm:p-6 bg-background-surface/95 backdrop-blur border-t border-border shadow-lg">
        <div className="container mx-auto flex flex-col sm:flex-row items-center justify-between gap-4">
          <div className="space-y-1 w-full sm:w-auto">
            <div className="flex items-center gap-2 text-xs text-foreground-muted">
              <span>Ghế đã chọn ({selectedSeats.length}/{MAX_SEATS}):</span>
              {selectedSeats.length === 0 ? (
                <span className="italic">Chưa chọn ghế nào</span>
              ) : (
                <div className="flex flex-wrap gap-1">
                  {selectedSeats.map((s) => (
                    <Badge key={s.seatId} variant="default" className="text-xs">
                      {s.rowLabel}{s.number}
                    </Badge>
                  ))}
                </div>
              )}
            </div>
            <p className="text-xs text-foreground-muted">
              Thời điểm bắt đầu giữ ghế: ngay khi bạn bấm xác nhận giữ chỗ.
            </p>
          </div>

          <div className="flex items-center justify-between sm:justify-end gap-6 w-full sm:w-auto">
            <div className="text-right">
              <span className="text-xs text-foreground-muted block">Tổng tiền</span>
              <span className="font-heading font-black text-2xl text-primary block">
                {formatVND(totalAmount)}
              </span>
            </div>

            {existingPendingBooking ? (
              <Link to={`/bookings/${existingPendingBooking.id}`}>
                <Button variant="primary" size="lg" className="min-w-[160px]">
                  Xem đơn giữ chỗ
                </Button>
              </Link>
            ) : (
              <Button
                variant="primary"
                size="lg"
                disabled={selectedSeatIds.length === 0 || isSubmitting}
                isLoading={isSubmitting}
                onClick={handleCreateBooking}
                className="min-w-[160px]"
              >
                Giữ ghế & Thanh toán
              </Button>
            )}
          </div>
        </div>
      </div>
    </div>
  );
};
