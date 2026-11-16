import React, { useState } from 'react';
import { useQuery, useQueryClient } from '@tanstack/react-query';
import { adminApi } from '../../api/admin';
import type { GenreSummary } from '../../api/types';
import { Card } from '../../components/ui/Card';
import { Button } from '../../components/ui/Button';
import { Input } from '../../components/ui/Input';
import { Modal } from '../../components/ui/Modal';
import { Pagination } from '../../components/ui/Pagination';
import { LoadingState } from '../../components/common/LoadingState';
import { EmptyState } from '../../components/common/EmptyState';
import { ErrorAlert } from '../../components/common/ErrorAlert';
import { Plus, Edit2, Trash2, Tags } from 'lucide-react';

export const AdminGenresPage: React.FC = () => {
  const queryClient = useQueryClient();
  const [page, setPage] = useState(1);

  const [isModalOpen, setIsModalOpen] = useState(false);
  const [editingGenre, setEditingGenre] = useState<GenreSummary | null>(null);
  const [name, setName] = useState('');
  const [slug, setSlug] = useState('');
  const [actionError, setActionError] = useState<unknown>(null);
  const [isSubmitting, setIsSubmitting] = useState(false);

  const { data, isLoading, error, refetch } = useQuery({
    queryKey: ['adminGenres', page],
    queryFn: () => adminApi.getGenres(page, 20),
  });

  const handleOpenCreate = () => {
    setEditingGenre(null);
    setName('');
    setSlug('');
    setActionError(null);
    setIsModalOpen(true);
  };

  const handleOpenEdit = (genre: GenreSummary) => {
    setEditingGenre(genre);
    setName(genre.name);
    setSlug(genre.slug);
    setActionError(null);
    setIsModalOpen(true);
  };

  const handleSubmit = async (e: React.FormEvent) => {
    e.preventDefault();
    setActionError(null);
    setIsSubmitting(true);

    try {
      if (editingGenre) {
        await adminApi.updateGenre(editingGenre.id, { name, slug });
      } else {
        await adminApi.createGenre({ name, slug });
      }
      setIsModalOpen(false);
      refetch();
    } catch (err) {
      setActionError(err);
    } finally {
      setIsSubmitting(false);
    }
  };

  const handleDelete = async (id: string) => {
    if (!window.confirm('Bạn có chắc chắn muốn xóa thể loại này?')) return;
    setActionError(null);
    try {
      await adminApi.deleteGenre(id);
      refetch();
    } catch (err) {
      setActionError(err);
    }
  };

  return (
    <div className="space-y-6">
      <div className="flex flex-col sm:flex-row sm:items-center justify-between gap-4">
        <div>
          <h1 className="font-heading text-3xl font-black text-foreground">
            Quản lý Thể loại phim
          </h1>
          <p className="text-sm text-foreground-muted">
            Tạo mới, chỉnh sửa tên và đường dẫn (slug) cho các thể loại.
          </p>
        </div>

        <Button variant="primary" onClick={handleOpenCreate}>
          <Plus className="w-4 h-4 mr-2" /> Thêm thể loại mới
        </Button>
      </div>

      <ErrorAlert error={actionError} />

      {isLoading ? (
        <LoadingState message="Đang tải danh sách thể loại..." />
      ) : error ? (
        <ErrorAlert error={error} title="Không thể tải danh sách thể loại" />
      ) : !data?.items || data.items.length === 0 ? (
        <EmptyState title="Chưa có thể loại nào" />
      ) : (
        <Card className="p-0 overflow-hidden border-border">
          <div className="overflow-x-auto">
            <table className="w-full text-left text-sm">
              <thead className="bg-background-subtle border-b border-border text-xs font-semibold text-foreground-muted uppercase">
                <tr>
                  <th className="px-6 py-3.5">Tên thể loại</th>
                  <th className="px-6 py-3.5">Slug định danh</th>
                  <th className="px-6 py-3.5 text-right">Thao tác</th>
                </tr>
              </thead>
              <tbody className="divide-y divide-border">
                {data.items.map((genre) => (
                  <tr key={genre.id} className="hover:bg-background-subtle/50 transition-colors">
                    <td className="px-6 py-4 font-bold text-foreground flex items-center gap-2">
                      <Tags className="w-4 h-4 text-primary" /> {genre.name}
                    </td>
                    <td className="px-6 py-4 font-mono text-xs text-foreground-muted">
                      {genre.slug}
                    </td>
                    <td className="px-6 py-4 text-right space-x-2">
                      <Button
                        variant="outline"
                        size="sm"
                        onClick={() => handleOpenEdit(genre)}
                      >
                        <Edit2 className="w-3.5 h-3.5 mr-1" /> Sửa
                      </Button>
                      <Button
                        variant="ghost"
                        size="sm"
                        className="text-red-600 hover:text-red-700"
                        onClick={() => handleDelete(genre.id)}
                      >
                        <Trash2 className="w-3.5 h-3.5" />
                      </Button>
                    </td>
                  </tr>
                ))}
              </tbody>
            </table>
          </div>

          <div className="p-4 border-t border-border">
            <Pagination
              currentPage={data.page}
              totalPages={data.totalPages}
              onPageChange={setPage}
            />
          </div>
        </Card>
      )}

      {/* Create / Edit Modal */}
      <Modal
        isOpen={isModalOpen}
        onClose={() => setIsModalOpen(false)}
        title={editingGenre ? 'Chỉnh sửa thể loại' : 'Thêm thể loại mới'}
      >
        <form onSubmit={handleSubmit} className="space-y-4">
          <Input
            label="Tên thể loại"
            required
            value={name}
            onChange={(e) => {
              setName(e.target.value);
              if (!editingGenre) {
                // Auto slug
                setSlug(
                  e.target.value
                    .toLowerCase()
                    .normalize('NFD')
                    .replace(/[\u0300-\u036f]/g, '')
                    .replace(/[^a-z0-9]+/g, '-')
                    .replace(/^-+|-+$/g, '')
                );
              }
            }}
          />

          <Input
            label="Slug (đường dẫn định danh)"
            required
            value={slug}
            onChange={(e) => setSlug(e.target.value)}
          />

          <div className="pt-4 flex justify-end gap-2">
            <Button type="button" variant="outline" onClick={() => setIsModalOpen(false)}>
              Hủy
            </Button>
            <Button type="submit" variant="primary" isLoading={isSubmitting}>
              {editingGenre ? 'Cập nhật' : 'Tạo thể loại'}
            </Button>
          </div>
        </form>
      </Modal>
    </div>
  );
};
