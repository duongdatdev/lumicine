import React, { useState, useEffect } from 'react';
import { useParams, Link } from 'react-router-dom';
import { useQuery } from '@tanstack/react-query';
import { adminApi } from '../../api/admin';
import type { RoomSeatLayoutRow, AdminSeatSummary } from '../../api/types';
import { Card } from '../../components/ui/Card';
import { Button } from '../../components/ui/Button';
import { Input } from '../../components/ui/Input';
import { Badge } from '../../components/ui/Badge';
import { Alert } from '../../components/ui/Alert';
import { ErrorAlert } from '../../components/common/ErrorAlert';
import { LoadingState } from '../../components/common/LoadingState';
import {
  ArrowLeft,
  Plus,
  Trash2,
  Armchair,
  Save,
  CheckCircle2,
  AlertTriangle,
  RefreshCw,
} from 'lucide-react';

export const AdminRoomSeatsPage: React.FC = () => {
  const { id: roomId } = useParams<{ id: string }>();

  const [layoutRows, setLayoutRows] = useState<RoomSeatLayoutRow[]>([
    { rowLabel: 'A', seatType: 'Normal', seatCount: 10 },
    { rowLabel: 'B', seatType: 'Normal', seatCount: 10 },
    { rowLabel: 'C', seatType: 'VIP', seatCount: 10 },
    { rowLabel: 'D', seatType: 'Couple', seatCount: 6 },
  ]);

  const [isSavingLayout, setIsSavingLayout] = useState(false);
  const [layoutSuccess, setLayoutSuccess] = useState(false);
  const [layoutError, setLayoutError] = useState<unknown>(null);
  const [seatActionError, setSeatActionError] = useState<unknown>(null);

  // Load room details
  const { data: room, isLoading: isRoomLoading } = useQuery({
    queryKey: ['adminRoom', roomId],
    queryFn: () => adminApi.getRoom(roomId!),
    enabled: !!roomId,
  });

  // Load existing seats
  const {
    data: seatsData,
    isLoading: isSeatsLoading,
    refetch: refetchSeats,
  } = useQuery({
    queryKey: ['adminRoomSeats', roomId],
    queryFn: () => adminApi.getRoomSeats(roomId!, 1, 500),
    enabled: !!roomId,
  });

  const seats = seatsData?.items || [];

  // Group existing seats by rowLabel for preview
  const groupedSeats = seats.reduce<Record<string, AdminSeatSummary[]>>((acc, seat) => {
    if (!acc[seat.rowLabel]) acc[seat.rowLabel] = [];
    acc[seat.rowLabel].push(seat);
    return acc;
  }, {});

  const handleAddRow = () => {
    const nextCharCode = 65 + layoutRows.length; // A, B, C...
    const nextLabel = String.fromCharCode(nextCharCode);
    setLayoutRows((prev) => [
      ...prev,
      { rowLabel: nextLabel, seatType: 'Normal', seatCount: 10 },
    ]);
  };

  const handleRemoveRow = (index: number) => {
    setLayoutRows((prev) => prev.filter((_, i) => i !== index));
  };

  const handleUpdateRow = (
    index: number,
    field: keyof RoomSeatLayoutRow,
    value: string | number
  ) => {
    setLayoutRows((prev) =>
      prev.map((row, i) => (i === index ? { ...row, [field]: value } : row))
    );
  };

  const handleSaveLayout = async (e: React.FormEvent) => {
    e.preventDefault();
    if (!roomId) return;
    setIsSavingLayout(true);
    setLayoutSuccess(false);
    setLayoutError(null);

    try {
      await adminApi.replaceRoomSeatLayout(roomId, layoutRows);
      setLayoutSuccess(true);
      await refetchSeats();
    } catch (err) {
      setLayoutError(err);
    } finally {
      setIsSavingLayout(false);
    }
  };

  const handleToggleSeatActive = async (seat: AdminSeatSummary) => {
    setSeatActionError(null);
    try {
      await adminApi.setSeatStatus(seat.id, !seat.isActive);
      await refetchSeats();
    } catch (err) {
      setSeatActionError(err);
    }
  };

  if (isRoomLoading) {
    return <LoadingState message="Đang tải thông tin phòng chiếu..." />;
  }

  return (
    <div className="space-y-8 max-w-5xl">
      <div>
        <Link
          to="/admin/cinemas"
          className="inline-flex items-center text-sm font-medium text-foreground-muted hover:text-primary transition-colors"
        >
          <ArrowLeft className="w-4 h-4 mr-1" /> Quay lại danh sách rạp
        </Link>
        <div className="mt-2">
          <span className="text-xs font-semibold uppercase tracking-wider text-primary">
            Cấu hình phòng chiếu
          </span>
          <h1 className="font-heading text-3xl font-black text-foreground">
            {room?.name} (Sức chứa hiện tại: {room?.seatCapacity} ghế)
          </h1>
        </div>
      </div>

      <div className="grid grid-cols-1 md:grid-cols-2 gap-8 items-start">
        {/* Left: Configure Rows Layout */}
        <Card className="p-6 space-y-6">
          <div className="flex items-center justify-between border-b border-border pb-3">
            <div>
              <h2 className="font-heading font-bold text-xl text-foreground">
                Thiết lập Layout hàng ghế
              </h2>
              <p className="text-xs text-foreground-muted">
                Định nghĩa các hàng, loại ghế và số lượng ghế mỗi hàng.
              </p>
            </div>
            <Button variant="outline" size="sm" onClick={handleAddRow}>
              <Plus className="w-3.5 h-3.5 mr-1" /> Thêm hàng
            </Button>
          </div>

          {layoutSuccess && (
            <Alert variant="success" title="Đã lưu sơ đồ ghế mới">
              Sơ đồ ghế của phòng chiếu đã được cập nhật thành công.
            </Alert>
          )}

          <ErrorAlert error={layoutError} title="Không thể thay đổi sơ đồ ghế" />

          <form onSubmit={handleSaveLayout} className="space-y-4">
            <div className="space-y-3 max-h-[50vh] overflow-y-auto pr-1">
              {layoutRows.map((row, index) => (
                <div
                  key={index}
                  className="p-3 border border-border rounded-xl bg-background flex items-center gap-3 text-sm"
                >
                  <div className="w-16">
                    <label className="text-[10px] font-bold text-foreground-muted block mb-1">
                      Hàng
                    </label>
                    <input
                      type="text"
                      required
                      value={row.rowLabel}
                      onChange={(e) =>
                        handleUpdateRow(index, 'rowLabel', e.target.value.toUpperCase())
                      }
                      className="w-full h-9 text-center font-bold font-mono rounded-lg border border-border bg-background-surface uppercase"
                    />
                  </div>

                  <div className="flex-1">
                    <label className="text-[10px] font-bold text-foreground-muted block mb-1">
                      Loại ghế
                    </label>
                    <select
                      value={row.seatType}
                      onChange={(e) => handleUpdateRow(index, 'seatType', e.target.value)}
                      className="w-full h-9 rounded-lg border border-border bg-background-surface px-2 text-xs"
                    >
                      <option value="Normal">Normal (Thường)</option>
                      <option value="VIP">VIP (Cao cấp)</option>
                      <option value="Couple">Couple (Đôi)</option>
                    </select>
                  </div>

                  <div className="w-20">
                    <label className="text-[10px] font-bold text-foreground-muted block mb-1">
                      Số ghế
                    </label>
                    <input
                      type="number"
                      min="1"
                      max="40"
                      required
                      value={row.seatCount}
                      onChange={(e) =>
                        handleUpdateRow(index, 'seatCount', parseInt(e.target.value, 10) || 1)
                      }
                      className="w-full h-9 text-center font-bold rounded-lg border border-border bg-background-surface"
                    />
                  </div>

                  <div className="pt-4">
                    <Button
                      type="button"
                      variant="ghost"
                      size="sm"
                      onClick={() => handleRemoveRow(index)}
                      className="text-red-500 hover:text-red-700 h-9 px-2"
                      title="Xóa hàng này"
                    >
                      <Trash2 className="w-4 h-4" />
                    </Button>
                  </div>
                </div>
              ))}
            </div>

            <div className="pt-3 border-t border-border flex justify-end">
              <Button type="submit" variant="primary" isLoading={isSavingLayout}>
                <Save className="w-4 h-4 mr-2" /> Lưu thay thế Layout
              </Button>
            </div>
          </form>
        </Card>

        {/* Right: Individual Seat Toggle */}
        <Card className="p-6 space-y-6">
          <div className="flex items-center justify-between border-b border-border pb-3">
            <div>
              <h2 className="font-heading font-bold text-xl text-foreground">
                Trạng thái từng Ghế
              </h2>
              <p className="text-xs text-foreground-muted">
                Bấm vào một ghế để bật hoặc tạm tắt ghế (hỏng, bảo trì).
              </p>
            </div>
            <Button variant="ghost" size="sm" onClick={() => refetchSeats()}>
              <RefreshCw className="w-4 h-4" />
            </Button>
          </div>

          <ErrorAlert error={seatActionError} title="Không thể đổi trạng thái ghế" />

          {isSeatsLoading ? (
            <LoadingState message="Đang tải danh sách ghế hiện có..." />
          ) : seats.length === 0 ? (
            <div className="text-center py-12 text-foreground-muted text-sm italic">
              Chưa có dữ liệu ghế. Hãy tạo layout hàng ghế ở bên trái.
            </div>
          ) : (
            <div className="space-y-4 max-h-[60vh] overflow-y-auto p-1">
              <div className="flex gap-4 text-xs text-foreground-muted pb-2 border-b border-border">
                <span className="flex items-center gap-1.5">
                  <span className="w-3.5 h-3.5 rounded bg-primary text-white inline-block text-center text-[10px] font-bold">✓</span>{' '}
                  Đang hoạt động ({seats.filter((s) => s.isActive).length})
                </span>
                <span className="flex items-center gap-1.5">
                  <span className="w-3.5 h-3.5 rounded bg-gray-300 inline-block" /> Đã tắt ({seats.filter((s) => !s.isActive).length})
                </span>
              </div>

              {Object.keys(groupedSeats)
                .sort()
                .map((rowLabel) => (
                  <div key={rowLabel} className="flex items-center gap-2">
                    <span className="w-6 font-bold text-xs text-foreground-muted text-center">
                      {rowLabel}
                    </span>
                    <div className="flex flex-wrap gap-1.5">
                      {groupedSeats[rowLabel]
                        .sort((a, b) => a.number - b.number)
                        .map((seat) => (
                          <button
                            key={seat.id}
                            type="button"
                            onClick={() => handleToggleSeatActive(seat)}
                            title={`Hàng ${seat.rowLabel} Ghế ${seat.number} (${seat.seatType}) - Bấm để ${
                              seat.isActive ? 'tắt' : 'bật'
                            }`}
                            className={`w-8 h-8 rounded text-xs font-bold transition-all flex items-center justify-center border ${
                              seat.isActive
                                ? 'bg-primary-light/40 border-primary text-primary hover:bg-primary hover:text-white'
                                : 'bg-gray-100 border-gray-300 text-gray-400 line-through hover:bg-gray-200'
                            }`}
                          >
                            {seat.number}
                          </button>
                        ))}
                    </div>
                  </div>
                ))}
            </div>
          )}
        </Card>
      </div>
    </div>
  );
};
