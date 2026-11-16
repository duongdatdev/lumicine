import React, { useState, useEffect, useRef } from 'react';
import { BrowserQRCodeReader } from '@zxing/browser';
import { staffApi } from '../../api/staff';
import { ticketsApi } from '../../api/tickets';
import type { CheckInResult, TicketDetails } from '../../api/types';
import { Card } from '../../components/ui/Card';
import { Button } from '../../components/ui/Button';
import { Input } from '../../components/ui/Input';
import { Badge } from '../../components/ui/Badge';
import { Alert } from '../../components/ui/Alert';
import { ErrorAlert } from '../../components/common/ErrorAlert';
import { formatTimeUtc7, formatDateUtc7, generateUUID } from '../../lib/utils';
import {
  QrCode,
  Camera,
  CameraOff,
  CheckCircle2,
  AlertTriangle,
  XCircle,
  RefreshCw,
  Film,
  MapPin,
  Clock,
  Ticket,
} from 'lucide-react';

export const StaffCheckInPage: React.FC = () => {
  const [isCameraActive, setIsCameraActive] = useState(false);
  const [cameraError, setCameraError] = useState<string | null>(null);
  const [manualTicketCode, setManualTicketCode] = useState('');
  const [isScannerLocked, setIsScannerLocked] = useState(false);

  // Request ID management: retained across network retries, refreshed on next ticket
  const [checkInRequestId, setCheckInRequestId] = useState<string>(() => generateUUID());
  const [lastAttemptedCode, setLastAttemptedCode] = useState<string>('');

  const [isSubmitting, setIsSubmitting] = useState(false);
  const [checkInResult, setCheckInResult] = useState<CheckInResult | null>(null);
  const [ticketDetails, setTicketDetails] = useState<TicketDetails | null>(null);
  const [apiError, setApiError] = useState<unknown>(null);

  const videoRef = useRef<HTMLVideoElement | null>(null);
  const codeReaderRef = useRef<BrowserQRCodeReader | null>(null);
  const controlsRef = useRef<{ stop: () => void } | null>(null);

  // Stop camera when unmounting
  useEffect(() => {
    return () => {
      stopCamera();
    };
  }, []);

  const startCamera = async () => {
    setCameraError(null);
    try {
      const codeReader = new BrowserQRCodeReader();
      codeReaderRef.current = codeReader;

      if (!videoRef.current) return;

      setIsCameraActive(true);

      const controls = await codeReader.decodeFromConstraints(
        {
          video: {
            facingMode: { ideal: 'environment' }, // Prefer back camera
          },
        },
        videoRef.current,
        (result, error) => {
          if (result && !isScannerLocked) {
            const scannedText = result.getText();
            if (scannedText) {
              // Lock scanner immediately upon first QR detect
              setIsScannerLocked(true);
              handleProcessCheckIn(scannedText);
            }
          }
        }
      );

      controlsRef.current = controls;
    } catch (err: unknown) {
      setIsCameraActive(false);
      setCameraError('Không thể mở camera hoặc quyền truy cập camera bị từ chối.');
    }
  };

  const stopCamera = () => {
    if (controlsRef.current) {
      try {
        controlsRef.current.stop();
      } catch {
        // ignore
      }
      controlsRef.current = null;
    }
    setIsCameraActive(false);
  };

  const handleProcessCheckIn = async (ticketCode: string, isRetry = false) => {
    const trimmedCode = ticketCode.trim();
    if (!trimmedCode) return;

    setIsSubmitting(true);
    setApiError(null);
    setLastAttemptedCode(trimmedCode);

    // If it's a retry, we reuse the exact same checkInRequestId
    const currentReqId = isRetry ? checkInRequestId : checkInRequestId;

    try {
      const result = await staffApi.checkIn({
        requestId: currentReqId,
        ticketCode: trimmedCode,
      });

      setCheckInResult(result);

      // If checkIn has a ticketId, fetch ticket details for full UI display
      if (result.ticketId) {
        try {
          const details = await ticketsApi.getTicket(result.ticketId);
          setTicketDetails(details);
        } catch {
          // ignore
        }
      }
    } catch (err: unknown) {
      setApiError(err);
    } finally {
      setIsSubmitting(false);
    }
  };

  const handleManualSubmit = (e: React.FormEvent) => {
    e.preventDefault();
    if (!manualTicketCode.trim()) return;
    setIsScannerLocked(true);
    handleProcessCheckIn(manualTicketCode.trim());
  };

  // Reset to scan next ticket
  const handleNextTicket = () => {
    setCheckInResult(null);
    setTicketDetails(null);
    setApiError(null);
    setManualTicketCode('');
    setLastAttemptedCode('');
    setIsScannerLocked(false);
    // Generate a fresh new RequestId for the next ticket
    setCheckInRequestId(generateUUID());
  };

  // Retry the current ticket with the same RequestId
  const handleRetryCurrent = () => {
    if (lastAttemptedCode) {
      handleProcessCheckIn(lastAttemptedCode, true);
    }
  };

  const isSuccess = checkInResult?.outcome === 'Success';
  const isDuplicate = checkInResult?.outcome === 'DuplicateScan';
  const isRejected = checkInResult && !isSuccess && !isDuplicate;

  return (
    <div className="max-w-4xl mx-auto space-y-6">
      <div>
        <h1 className="font-heading text-3xl font-black text-foreground">
          Soát vé Check-in
        </h1>
        <p className="text-sm text-foreground-muted">
          Quét mã QR bằng camera thiết bị hoặc nhập mã vé thủ công để xác nhận vào rạp.
        </p>
      </div>

      <div className="grid grid-cols-1 md:grid-cols-2 gap-6">
        {/* Left: Camera & Manual Input */}
        <div className="space-y-4">
          <Card className="p-4 space-y-4">
            <div className="flex items-center justify-between border-b border-border pb-3">
              <span className="font-heading font-bold text-base flex items-center gap-1.5">
                <Camera className="w-4 h-4 text-primary" /> Camera quét mã QR
              </span>
              <Button
                variant={isCameraActive ? 'danger' : 'primary'}
                size="sm"
                onClick={isCameraActive ? stopCamera : startCamera}
              >
                {isCameraActive ? (
                  <>
                    <CameraOff className="w-4 h-4 mr-1.5" /> Tắt Camera
                  </>
                ) : (
                  <>
                    <Camera className="w-4 h-4 mr-1.5" /> Bật Camera
                  </>
                )}
              </Button>
            </div>

            {cameraError && (
              <Alert variant="warning" title="Lỗi Camera">
                {cameraError} Bạn có thể sử dụng ô nhập mã vé thủ công bên dưới.
              </Alert>
            )}

            {/* Video Viewport */}
            <div className="relative aspect-video rounded-xl bg-black overflow-hidden flex items-center justify-center">
              <video
                ref={videoRef}
                className={`w-full h-full object-cover ${!isCameraActive ? 'hidden' : ''}`}
                muted
                playsInline
              />

              {!isCameraActive && (
                <div className="text-center text-white/60 p-4 space-y-2">
                  <QrCode className="w-12 h-12 mx-auto opacity-40" />
                  <p className="text-xs">Camera đang tắt. Bấm "Bật Camera" để quét mã vé.</p>
                </div>
              )}

              {isCameraActive && isScannerLocked && (
                <div className="absolute inset-0 bg-black/70 flex flex-col items-center justify-center text-white p-4 space-y-2">
                  <CheckCircle2 className="w-10 h-10 text-green-400" />
                  <p className="text-sm font-bold">Đã nhận mã QR!</p>
                  <p className="text-xs text-white/80">Scanner tạm khóa trong khi xử lý.</p>
                </div>
              )}
            </div>
          </Card>

          {/* Manual Input Card */}
          <Card className="p-4 space-y-3">
            <h3 className="font-heading font-bold text-sm text-foreground">
              Nhập mã vé thủ công
            </h3>
            <form onSubmit={handleManualSubmit} className="flex gap-2">
              <input
                type="text"
                placeholder="Nhập mã vé (VD: TC-ABCD-1234)"
                value={manualTicketCode}
                onChange={(e) => setManualTicketCode(e.target.value)}
                disabled={isSubmitting}
                className="flex-1 h-10 px-3 rounded-lg border border-border bg-background text-sm font-mono uppercase"
              />
              <Button type="submit" variant="primary" size="md" isLoading={isSubmitting}>
                Kiểm tra
              </Button>
            </form>
          </Card>
        </div>

        {/* Right: Check-In Result Panel */}
        <div className="space-y-4">
          <Card className="p-6 h-full flex flex-col justify-between space-y-6">
            <div className="space-y-4">
              <h2 className="font-heading font-bold text-lg text-foreground border-b border-border pb-3">
                Kết quả kiểm tra vé
              </h2>

              <ErrorAlert error={apiError} />

              {!checkInResult && !apiError && !isSubmitting && (
                <div className="text-center py-12 text-foreground-muted space-y-2">
                  <Ticket className="w-12 h-12 mx-auto opacity-30" />
                  <p className="text-sm">Chưa có kết quả soát vé.</p>
                  <p className="text-xs">Hướng camera về phía mã QR hoặc nhập mã vé để bắt đầu.</p>
                </div>
              )}

              {isSubmitting && (
                <div className="text-center py-12 space-y-3">
                  <RefreshCw className="w-8 h-8 mx-auto animate-spin text-primary" />
                  <p className="text-sm font-medium">Đang xử lý soát vé...</p>
                </div>
              )}

              {/* Success Result */}
              {isSuccess && (
                <div className="p-4 rounded-xl bg-green-50 border border-green-200 text-green-900 space-y-3 animate-in fade-in">
                  <div className="flex items-center gap-2">
                    <CheckCircle2 className="w-6 h-6 text-green-600 shrink-0" />
                    <div>
                      <h3 className="font-heading font-bold text-lg">CHECK-IN HỢP LỆ!</h3>
                      <p className="text-xs text-green-700">Vé đã được xác nhận vào rạp thành công.</p>
                    </div>
                  </div>

                  {ticketDetails && (
                    <div className="pt-2 border-t border-green-200 text-xs space-y-1.5">
                      <p className="font-heading font-bold text-sm text-foreground">
                        {ticketDetails.movieTitle}
                      </p>
                      <p className="text-foreground-muted">
                        {ticketDetails.cinemaName} — {ticketDetails.roomName}
                      </p>
                      <p className="font-mono font-bold text-foreground">
                        Vị trí ghế: Hàng {ticketDetails.rowLabel} - Ghế {ticketDetails.number} ({ticketDetails.seatType})
                      </p>
                      <p className="text-foreground-muted">
                        Suất chiếu: {formatDateUtc7(ticketDetails.startAtUtc)} lúc {formatTimeUtc7(ticketDetails.startAtUtc)}
                      </p>
                    </div>
                  )}
                </div>
              )}

              {/* Duplicate Scan Result */}
              {isDuplicate && (
                <div className="p-4 rounded-xl bg-amber-50 border border-amber-200 text-amber-900 space-y-3 animate-in fade-in">
                  <div className="flex items-center gap-2">
                    <AlertTriangle className="w-6 h-6 text-amber-600 shrink-0" />
                    <div>
                      <h3 className="font-heading font-bold text-lg">VÉ ĐÃ QUA SỬ DỤNG!</h3>
                      <p className="text-xs text-amber-700">Mã vé này đã được check-in trước đó.</p>
                    </div>
                  </div>

                  {checkInResult.usedAtUtc && (
                    <p className="text-xs font-semibold">
                      Thời điểm đã check-in:{' '}
                      {formatTimeUtc7(checkInResult.usedAtUtc)} ngày {formatDateUtc7(checkInResult.usedAtUtc)}
                    </p>
                  )}

                  {ticketDetails && (
                    <div className="pt-2 border-t border-amber-200 text-xs space-y-1 text-foreground-muted">
                      <p className="font-bold text-foreground">{ticketDetails.movieTitle}</p>
                      <p>
                        Ghế: Hàng {ticketDetails.rowLabel} - {ticketDetails.number} ({ticketDetails.roomName})
                      </p>
                    </div>
                  )}
                </div>
              )}

              {/* Other Rejections */}
              {isRejected && (
                <div className="p-4 rounded-xl bg-red-50 border border-red-200 text-red-900 space-y-2 animate-in fade-in">
                  <div className="flex items-center gap-2">
                    <XCircle className="w-6 h-6 text-red-600 shrink-0" />
                    <h3 className="font-heading font-bold text-lg">CHECK-IN THẤT BẠI</h3>
                  </div>
                  <p className="text-xs font-medium">Lý do từ chối: {checkInResult.outcome}</p>
                </div>
              )}
            </div>

            {/* Bottom Actions for CheckIn Result */}
            {(checkInResult || Boolean(apiError)) && (
              <div className="pt-4 border-t border-border flex flex-col sm:flex-row gap-2">
                <Button
                  variant="primary"
                  className="flex-1"
                  onClick={handleNextTicket}
                >
                  <QrCode className="w-4 h-4 mr-2" /> Quét vé tiếp theo
                </Button>

                {Boolean(apiError) && lastAttemptedCode && (
                  <Button
                    variant="outline"
                    onClick={handleRetryCurrent}
                    isLoading={isSubmitting}
                  >
                    <RefreshCw className="w-4 h-4 mr-1.5" /> Thử lại mã này
                  </Button>
                )}
              </div>
            )}
          </Card>
        </div>
      </div>
    </div>
  );
};
