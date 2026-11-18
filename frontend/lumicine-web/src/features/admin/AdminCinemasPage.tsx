import React, { useState } from 'react';
import { useQuery, useQueryClient } from '@tanstack/react-query';
import { Link } from 'react-router-dom';
import { adminApi } from '../../api/admin';
import type { CinemaSummary, RoomSummary } from '../../api/types';
import { Card } from '../../components/ui/Card';
import { Button } from '../../components/ui/Button';
import { Input } from '../../components/ui/Input';
import { Modal } from '../../components/ui/Modal';
import { Badge } from '../../components/ui/Badge';
import { Pagination } from '../../components/ui/Pagination';
import { LoadingState } from '../../components/common/LoadingState';
import { EmptyState } from '../../components/common/EmptyState';
import { ErrorAlert } from '../../components/common/ErrorAlert';
import {
  Building2,
  Plus,
  Edit2,
  MapPin,
  Tv,
  Armchair,
  Check,
  X,
  ChevronRight,
} from 'lucide-react';

export const AdminCinemasPage: React.FC = () => {
  const queryClient = useQueryClient();
  const [page, setPage] = useState(1);
  const [selectedCinemaId, setSelectedCinemaId] = useState<string | null>(null);

  // Cinema modal
  const [isCinemaModalOpen, setIsCinemaModalOpen] = useState(false);
  const [editingCinema, setEditingCinema] = useState<CinemaSummary | null>(null);
  const [cinemaName, setCinemaName] = useState('');
  const [cinemaCity, setCinemaCity] = useState('');
  const [cinemaAddress, setCinemaAddress] = useState('');

  // Room modal
  const [isRoomModalOpen, setIsRoomModalOpen] = useState(false);
  const [editingRoom, setEditingRoom] = useState<RoomSummary | null>(null);
  const [roomName, setRoomName] = useState('');

  const [actionError, setActionError] = useState<unknown>(null);
  const [isSubmitting, setIsSubmitting] = useState(false);

  // Load cinemas
  const { data: cinemasData, isLoading, error, refetch: refetchCinemas } = useQuery({
    queryKey: ['adminCinemas', page],
    queryFn: () => adminApi.getCinemas(page, 20),
  });

  const cinemas = cinemasData?.items || [];

  // If no selected cinema, pick first one
  const activeCinema =
    cinemas.find((c) => c.id === selectedCinemaId) || (cinemas.length > 0 ? cinemas[0] : null);

  // Load rooms for active cinema
  const {
    data: roomsData,
    isLoading: isRoomsLoading,
    refetch: refetchRooms,
  } = useQuery({
    queryKey: ['adminCinemaRooms', activeCinema?.id],
    queryFn: () => adminApi.getRoomsForCinema(activeCinema!.id, 1, 50),
    enabled: !!activeCinema?.id,
  });

  // Cinema handlers
  const handleOpenCreateCinema = () => {
    setEditingCinema(null);
    setCinemaName('');
    setCinemaCity('');
    setCinemaAddress('');
    setActionError(null);
    setIsCinemaModalOpen(true);
  };

  const handleOpenEditCinema = (cinema: CinemaSummary, e: React.MouseEvent) => {
    e.stopPropagation();
    setEditingCinema(cinema);
    setCinemaName(cinema.name);
    setCinemaCity(cinema.city);
    setCinemaAddress(cinema.address);
    setActionError(null);
    setIsCinemaModalOpen(true);
  };

  const handleCinemaSubmit = async (e: React.FormEvent) => {
    e.preventDefault();
    setActionError(null);
    setIsSubmitting(true);
    try {
      if (editingCinema) {
        await adminApi.updateCinema(editingCinema.id, {
          name: cinemaName,
          city: cinemaCity,
          address: cinemaAddress,
        });
      } else {
        const created = await adminApi.createCinema({
          name: cinemaName,
          city: cinemaCity,
          address: cinemaAddress,
        });
        setSelectedCinemaId(created.id);
      }
      setIsCinemaModalOpen(false);
      refetchCinemas();
    } catch (err) {
      setActionError(err);
    } finally {
      setIsSubmitting(false);
    }
  };

  const handleToggleCinemaStatus = async (cinema: CinemaSummary, e: React.MouseEvent) => {
    e.stopPropagation();
    setActionError(null);
    try {
      await adminApi.setCinemaStatus(cinema.id, !cinema.isActive);
      refetchCinemas();
    } catch (err) {
      setActionError(err);
    }
  };

  // Room handlers
  const handleOpenCreateRoom = () => {
    setEditingRoom(null);
    setRoomName('');
    setActionError(null);
    setIsRoomModalOpen(true);
  };

  const handleOpenEditRoom = (room: RoomSummary) => {
    setEditingRoom(room);
    setRoomName(room.name);
    setActionError(null);
    setIsRoomModalOpen(true);
  };

  const handleRoomSubmit = async (e: React.FormEvent) => {
    e.preventDefault();
    if (!activeCinema) return;
    setActionError(null);
    setIsSubmitting(true);
    try {
      if (editingRoom) {
        await adminApi.updateRoom(editingRoom.id, { name: roomName });
      } else {
        await adminApi.createRoom(activeCinema.id, { name: roomName });
      }
      setIsRoomModalOpen(false);
      refetchRooms();
    } catch (err) {
      setActionError(err);
    } finally {
      setIsSubmitting(false);
    }
  };

  const handleToggleRoomStatus = async (room: RoomSummary) => {
    setActionError(null);
    try {
      await adminApi.setRoomStatus(room.id, !room.isActive);
      refetchRooms();
    } catch (err) {
      setActionError(err);
    }
  };

  return (
    <div className="space-y-6">
      <div className="flex flex-col sm:flex-row sm:items-center justify-between gap-4">
        <div>
          <h1 className="font-heading text-3xl font-black text-foreground">
            Quản lý Cụm rạp & Phòng chiếu
          </h1>
          <p className="text-sm text-foreground-muted">
            Thiết lập danh sách rạp, phòng chiếu và cấu hình hàng ghế.
          </p>
        </div>

        <Button variant="primary" onClick={handleOpenCreateCinema}>
          <Plus className="w-4 h-4 mr-2" /> Thêm rạp mới
        </Button>
      </div>

      <ErrorAlert error={actionError} />

      {isLoading ? (
        <LoadingState message="Đang tải danh sách rạp..." />
      ) : error ? (
        <ErrorAlert error={error} title="Không thể tải danh sách rạp" />
      ) : (
        <div className="grid grid-cols-1 md:grid-cols-3 gap-6">
          {/* Cinema list */}
          <div className="space-y-3">
            <h2 className="font-heading font-bold text-lg text-foreground">Danh sách Cụm rạp</h2>
            <div className="space-y-2">
              {cinemas.map((cinema) => {
                const isSelected = activeCinema?.id === cinema.id;
                return (
                  <Card
                    key={cinema.id}
                    onClick={() => setSelectedCinemaId(cinema.id)}
                    className={`p-4 cursor-pointer transition-all border ${
                      isSelected
                        ? 'border-primary ring-1 ring-primary bg-primary-light/10 shadow-sm'
                        : 'hover:border-border-strong'
                    }`}
                  >
                    <div className="flex items-start justify-between gap-2">
                      <div className="space-y-1 flex-1">
                        <div className="flex items-center gap-2">
                          <h3 className="font-heading font-bold text-base text-foreground">
                            {cinema.name}
                          </h3>
                          <Badge variant={cinema.isActive ? 'success' : 'neutral'} className="text-[10px]">
                            {cinema.isActive ? 'Hoạt động' : 'Tạm dừng'}
                          </Badge>
                        </div>
                        <p className="text-xs text-foreground-muted flex items-center gap-1">
                          <MapPin className="w-3.5 h-3.5 shrink-0" />
                          {cinema.address}, {cinema.city}
                        </p>
                      </div>

                      <div className="flex items-center gap-1">
                        <Button
                          variant="ghost"
                          size="sm"
                          onClick={(e) => handleOpenEditCinema(cinema, e)}
                          className="p-1 h-7 text-foreground-muted hover:text-foreground"
                          title="Sửa rạp"
                        >
                          <Edit2 className="w-3.5 h-3.5" />
                        </Button>
                        <button
                          onClick={(e) => handleToggleCinemaStatus(cinema, e)}
                          title={cinema.isActive ? 'Tắt rạp' : 'Bật rạp'}
                          className={`p-1 rounded text-xs font-bold ${
                            cinema.isActive ? 'text-green-700' : 'text-gray-400'
                          }`}
                        >
                          {cinema.isActive ? <Check className="w-4 h-4" /> : <X className="w-4 h-4" />}
                        </button>
                      </div>
                    </div>
                  </Card>
                );
              })}
            </div>

            <Pagination
              currentPage={cinemasData?.page || 1}
              totalPages={Math.ceil((cinemasData?.totalCount ?? 0) / (cinemasData?.pageSize ?? 1))}
              onPageChange={setPage}
            />
          </div>

          {/* Rooms for Active Cinema */}
          <div className="md:col-span-2 space-y-4">
            {!activeCinema ? (
              <EmptyState title="Chọn một rạp để quản lý phòng chiếu" />
            ) : (
              <Card className="p-6 space-y-6">
                <div className="flex flex-col sm:flex-row sm:items-center justify-between gap-4 border-b border-border pb-4">
                  <div>
                    <span className="text-xs font-semibold uppercase text-primary tracking-wider">
                      Cụm rạp đang chọn
                    </span>
                    <h2 className="font-heading font-black text-2xl text-foreground">
                      {activeCinema.name}
                    </h2>
                    <p className="text-xs text-foreground-muted">
                      {activeCinema.address}, {activeCinema.city}
                    </p>
                  </div>

                  <Button variant="primary" size="sm" onClick={handleOpenCreateRoom}>
                    <Plus className="w-4 h-4 mr-1.5" /> Thêm phòng chiếu
                  </Button>
                </div>

                {isRoomsLoading ? (
                  <LoadingState message="Đang tải danh sách phòng chiếu..." />
                ) : !roomsData?.items || roomsData.items.length === 0 ? (
                  <EmptyState
                    icon={<Tv className="w-8 h-8 text-foreground-muted" />}
                    title="Chưa có phòng chiếu nào tại rạp này"
                    description="Bấm 'Thêm phòng chiếu' để bắt đầu thiết lập."
                  />
                ) : (
                  <div className="grid grid-cols-1 sm:grid-cols-2 gap-4">
                    {roomsData.items.map((room) => (
                      <Card
                        key={room.id}
                        className="p-4 border border-border bg-background hover:shadow-md transition-all flex flex-col justify-between space-y-4"
                      >
                        <div className="space-y-1">
                          <div className="flex items-center justify-between">
                            <h4 className="font-heading font-bold text-lg text-foreground flex items-center gap-2">
                              <Tv className="w-4 h-4 text-primary" /> {room.name}
                            </h4>
                            <Badge variant={room.isActive ? 'success' : 'neutral'} className="text-[10px]">
                              {room.isActive ? 'Đang hoạt động' : 'Tạm tắt'}
                            </Badge>
                          </div>
                          <p className="text-xs text-foreground-muted flex items-center gap-1.5">
                            <Armchair className="w-3.5 h-3.5 text-primary" /> Sức chứa: {room.seatCapacity} ghế
                          </p>
                        </div>

                        <div className="pt-2 border-t border-border flex items-center justify-between gap-2">
                          <Link to={`/admin/rooms/${room.id}/seats`}>
                            <Button variant="outline" size="sm" className="text-xs">
                              <Armchair className="w-3.5 h-3.5 mr-1" /> Sơ đồ & Ghế
                            </Button>
                          </Link>

                          <div className="flex items-center gap-1">
                            <Button
                              variant="ghost"
                              size="sm"
                              onClick={() => handleOpenEditRoom(room)}
                              className="text-xs"
                            >
                              Sửa tên
                            </Button>
                            <Button
                              variant="ghost"
                              size="sm"
                              onClick={() => handleToggleRoomStatus(room)}
                              className={`text-xs ${room.isActive ? 'text-red-600' : 'text-green-600'}`}
                            >
                              {room.isActive ? 'Tắt' : 'Bật'}
                            </Button>
                          </div>
                        </div>
                      </Card>
                    ))}
                  </div>
                )}
              </Card>
            )}
          </div>
        </div>
      )}

      {/* Cinema Modal */}
      <Modal
        isOpen={isCinemaModalOpen}
        onClose={() => setIsCinemaModalOpen(false)}
        title={editingCinema ? 'Chỉnh sửa Cụm rạp' : 'Thêm Cụm rạp mới'}
      >
        <form onSubmit={handleCinemaSubmit} className="space-y-4">
          <Input
            label="Tên cụm rạp"
            required
            value={cinemaName}
            onChange={(e) => setCinemaName(e.target.value)}
          />

          <Input
            label="Thành phố / Tỉnh"
            required
            placeholder="Hà Nội, TP. Hồ Chí Minh, Đà Nẵng..."
            value={cinemaCity}
            onChange={(e) => setCinemaCity(e.target.value)}
          />

          <Input
            label="Địa chỉ chi tiết"
            required
            placeholder="Số nhà, tên đường, phường/xã..."
            value={cinemaAddress}
            onChange={(e) => setCinemaAddress(e.target.value)}
          />

          <div className="pt-4 flex justify-end gap-2">
            <Button type="button" variant="outline" onClick={() => setIsCinemaModalOpen(false)}>
              Hủy
            </Button>
            <Button type="submit" variant="primary" isLoading={isSubmitting}>
              {editingCinema ? 'Cập nhật rạp' : 'Tạo rạp'}
            </Button>
          </div>
        </form>
      </Modal>

      {/* Room Modal */}
      <Modal
        isOpen={isRoomModalOpen}
        onClose={() => setIsRoomModalOpen(false)}
        title={editingRoom ? 'Sửa tên phòng chiếu' : 'Thêm phòng chiếu mới'}
      >
        <form onSubmit={handleRoomSubmit} className="space-y-4">
          <Input
            label="Tên phòng chiếu"
            required
            placeholder="Phòng 1, Phòng IMAX, Phòng VIP..."
            value={roomName}
            onChange={(e) => setRoomName(e.target.value)}
          />

          <div className="pt-4 flex justify-end gap-2">
            <Button type="button" variant="outline" onClick={() => setIsRoomModalOpen(false)}>
              Hủy
            </Button>
            <Button type="submit" variant="primary" isLoading={isSubmitting}>
              {editingRoom ? 'Lưu tên' : 'Tạo phòng'}
            </Button>
          </div>
        </form>
      </Modal>
    </div>
  );
};
