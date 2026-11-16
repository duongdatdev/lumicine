import React, { useState } from 'react';
import { useQuery, useQueryClient } from '@tanstack/react-query';
import { adminApi } from '../../api/admin';
import type { MovieSummary, GenreSummary } from '../../api/types';
import { Card } from '../../components/ui/Card';
import { Button } from '../../components/ui/Button';
import { Badge } from '../../components/ui/Badge';
import { Input } from '../../components/ui/Input';
import { Textarea } from '../../components/ui/Textarea';
import { Modal } from '../../components/ui/Modal';
import { Pagination } from '../../components/ui/Pagination';
import { MoviePoster } from '../../components/common/PosterPlaceholder';
import { LoadingState } from '../../components/common/LoadingState';
import { EmptyState } from '../../components/common/EmptyState';
import { ErrorAlert } from '../../components/common/ErrorAlert';
import { Alert } from '../../components/ui/Alert';
import { formatDateUtc7 } from '../../lib/utils';
import {
  Plus,
  Edit2,
  Image,
  Trash2,
  Search,
  Check,
  X,
  Upload,
  AlertCircle,
  Eye,
} from 'lucide-react';

const MAX_POSTER_BYTES = 6 * 1024 * 1024; // 6 MiB

export const AdminMoviesPage: React.FC = () => {
  const queryClient = useQueryClient();

  const [page, setPage] = useState(1);
  const [search, setSearch] = useState('');
  const [searchInput, setSearchInput] = useState('');

  // Movie Create/Edit modal
  const [isMovieModalOpen, setIsMovieModalOpen] = useState(false);
  const [editingMovie, setEditingMovie] = useState<MovieSummary | null>(null);

  // Form fields
  const [title, setTitle] = useState('');
  const [originalTitle, setOriginalTitle] = useState('');
  const [synopsis, setSynopsis] = useState('');
  const [durationMinutes, setDurationMinutes] = useState('120');
  const [releaseDate, setReleaseDate] = useState('');
  const [ageRating, setAgeRating] = useState('P');
  const [selectedGenreIds, setSelectedGenreIds] = useState<string[]>([]);

  // Poster modal
  const [isPosterModalOpen, setIsPosterModalOpen] = useState(false);
  const [posterMovie, setPosterMovie] = useState<MovieSummary | null>(null);
  const [posterFile, setPosterFile] = useState<File | null>(null);
  const [posterPreview, setPosterPreview] = useState<string | null>(null);
  const [posterError, setPosterError] = useState<string | null>(null);
  const [isUploadingPoster, setIsUploadingPoster] = useState(false);

  const [actionError, setActionError] = useState<unknown>(null);
  const [isSubmitting, setIsSubmitting] = useState(false);

  // Load genres
  const { data: genresData } = useQuery({
    queryKey: ['adminGenresList'],
    queryFn: () => adminApi.getGenres(1, 100),
  });

  // Load movies
  const { data: moviesData, isLoading, error, refetch } = useQuery({
    queryKey: ['adminMovies', { page, search }],
    queryFn: () => adminApi.getMovies({ page, pageSize: 15, search: search || undefined }),
  });

  const handleSearchSubmit = (e: React.FormEvent) => {
    e.preventDefault();
    setSearch(searchInput.trim());
    setPage(1);
  };

  const handleOpenCreate = () => {
    setEditingMovie(null);
    setTitle('');
    setOriginalTitle('');
    setSynopsis('');
    setDurationMinutes('120');
    setReleaseDate('');
    setAgeRating('P');
    setSelectedGenreIds([]);
    setActionError(null);
    setIsMovieModalOpen(true);
  };

  const handleOpenEdit = (movie: MovieSummary) => {
    setEditingMovie(movie);
    setTitle(movie.title);
    setOriginalTitle(movie.originalTitle || '');
    setSynopsis(movie.synopsis || '');
    setDurationMinutes(movie.durationMinutes.toString());
    setReleaseDate(movie.releaseDate ? movie.releaseDate.split('T')[0] : '');
    setAgeRating(movie.ageRating || 'P');
    setSelectedGenreIds(movie.genres?.map((g) => g.id) || []);
    setActionError(null);
    setIsMovieModalOpen(true);
  };

  const handleMovieSubmit = async (e: React.FormEvent) => {
    e.preventDefault();
    setActionError(null);
    setIsSubmitting(true);

    const payload = {
      title,
      originalTitle: originalTitle || undefined,
      synopsis: synopsis || undefined,
      durationMinutes: parseInt(durationMinutes, 10),
      releaseDate: releaseDate || undefined,
      ageRating: ageRating || undefined,
      genreIds: selectedGenreIds,
    };

    try {
      if (editingMovie) {
        await adminApi.updateMovie(editingMovie.id, payload);
      } else {
        await adminApi.createMovie(payload);
      }
      setIsMovieModalOpen(false);
      refetch();
    } catch (err) {
      setActionError(err);
    } finally {
      setIsSubmitting(false);
    }
  };

  const handleToggleStatus = async (movie: MovieSummary) => {
    setActionError(null);
    try {
      await adminApi.setMovieStatus(movie.id, !movie.isActive);
      refetch();
    } catch (err) {
      setActionError(err);
    }
  };

  // Poster Management
  const handleOpenPosterModal = (movie: MovieSummary) => {
    setPosterMovie(movie);
    setPosterFile(null);
    setPosterPreview(movie.posterUrl || null);
    setPosterError(null);
    setIsPosterModalOpen(true);
  };

  const handleFileChange = (e: React.ChangeEvent<HTMLInputElement>) => {
    const file = e.target.files?.[0];
    if (!file) return;

    if (!file.type.startsWith('image/')) {
      setPosterError('Chỉ chấp nhận định dạng tệp ảnh (JPEG, PNG, WebP).');
      return;
    }

    if (file.size > MAX_POSTER_BYTES) {
      setPosterError('Dung lượng ảnh phải nhỏ hơn hoặc bằng 6 MiB.');
      return;
    }

    setPosterError(null);
    setPosterFile(file);
    setPosterPreview(URL.createObjectURL(file));
  };

  const handleUploadPoster = async () => {
    if (!posterMovie || !posterFile) return;

    setIsUploadingPoster(true);
    setPosterError(null);

    try {
      await adminApi.uploadMoviePoster(posterMovie.id, posterFile);
      setIsPosterModalOpen(false);
      refetch();
    } catch (err: unknown) {
      setPosterError(err instanceof Error ? err.message : 'Upload poster thất bại');
    } finally {
      setIsUploadingPoster(false);
    }
  };

  const handleDeletePoster = async () => {
    if (!posterMovie) return;
    if (!window.confirm('Bạn có chắc chắn muốn xóa poster của phim này?')) return;

    setIsUploadingPoster(true);
    try {
      await adminApi.deleteMoviePoster(posterMovie.id);
      setIsPosterModalOpen(false);
      refetch();
    } catch (err) {
      setPosterError('Không thể xóa poster');
    } finally {
      setIsUploadingPoster(false);
    }
  };

  const toggleGenre = (genreId: string) => {
    setSelectedGenreIds((prev) =>
      prev.includes(genreId) ? prev.filter((id) => id !== genreId) : [...prev, genreId]
    );
  };

  return (
    <div className="space-y-6">
      <div className="flex flex-col sm:flex-row sm:items-center justify-between gap-4">
        <div>
          <h1 className="font-heading text-3xl font-black text-foreground">Quản lý Phim & Poster</h1>
          <p className="text-sm text-foreground-muted">
            Tạo mới, chỉnh sửa thông tin phim, gán thể loại, bật/tắt hoạt động và tải poster.
          </p>
        </div>

        <Button variant="primary" onClick={handleOpenCreate}>
          <Plus className="w-4 h-4 mr-2" /> Thêm phim mới
        </Button>
      </div>

      <ErrorAlert error={actionError} />

      {/* Search Bar */}
      <form onSubmit={handleSearchSubmit} className="flex gap-2 max-w-md">
        <input
          type="text"
          placeholder="Tìm phim theo tiêu đề..."
          value={searchInput}
          onChange={(e) => setSearchInput(e.target.value)}
          className="flex-1 h-10 px-3 rounded-lg border border-border bg-background text-sm"
        />
        <Button type="submit" variant="primary">
          <Search className="w-4 h-4 mr-1.5" /> Tìm
        </Button>
      </form>

      {/* Movies Table */}
      {isLoading ? (
        <LoadingState message="Đang tải danh sách phim..." />
      ) : error ? (
        <ErrorAlert error={error} title="Không thể tải danh sách phim" />
      ) : !moviesData?.items || moviesData.items.length === 0 ? (
        <EmptyState title="Không tìm thấy phim nào" />
      ) : (
        <Card className="p-0 overflow-hidden border-border">
          <div className="overflow-x-auto">
            <table className="w-full text-left text-sm">
              <thead className="bg-background-subtle border-b border-border text-xs font-semibold text-foreground-muted uppercase">
                <tr>
                  <th className="px-4 py-3.5">Poster</th>
                  <th className="px-4 py-3.5">Tựa phim</th>
                  <th className="px-4 py-3.5">Thời lượng</th>
                  <th className="px-4 py-3.5">Độ tuổi</th>
                  <th className="px-4 py-3.5">Thể loại</th>
                  <th className="px-4 py-3.5">Trạng thái</th>
                  <th className="px-4 py-3.5 text-right">Thao tác</th>
                </tr>
              </thead>
              <tbody className="divide-y divide-border">
                {moviesData.items.map((movie) => (
                  <tr key={movie.id} className="hover:bg-background-subtle/50 transition-colors">
                    <td className="px-4 py-3">
                      <div className="w-12">
                        <MoviePoster url={movie.posterUrl} title={movie.title} aspectRatio="poster" />
                      </div>
                    </td>
                    <td className="px-4 py-3">
                      <p className="font-bold text-foreground">{movie.title}</p>
                      {movie.originalTitle && (
                        <p className="text-xs text-foreground-muted italic">{movie.originalTitle}</p>
                      )}
                    </td>
                    <td className="px-4 py-3 text-xs text-foreground-muted">
                      {movie.durationMinutes} phút
                    </td>
                    <td className="px-4 py-3">
                      <Badge variant="warning" className="text-xs">
                        {movie.ageRating || 'P'}
                      </Badge>
                    </td>
                    <td className="px-4 py-3">
                      <div className="flex flex-wrap gap-1 max-w-[180px]">
                        {movie.genres?.map((g) => (
                          <span
                            key={g.id}
                            className="text-[10px] px-1.5 py-0.5 rounded bg-background-subtle border border-border"
                          >
                            {g.name}
                          </span>
                        ))}
                      </div>
                    </td>
                    <td className="px-4 py-3">
                      <button
                        onClick={() => handleToggleStatus(movie)}
                        className={`px-2 py-1 rounded-full text-xs font-bold transition-colors ${
                          movie.isActive
                            ? 'bg-green-100 text-green-800 hover:bg-green-200'
                            : 'bg-gray-100 text-gray-600 hover:bg-gray-200'
                        }`}
                      >
                        {movie.isActive ? 'Đang chiếu' : 'Đã ẩn'}
                      </button>
                    </td>
                    <td className="px-4 py-3 text-right space-x-1.5">
                      <Button
                        variant="outline"
                        size="sm"
                        onClick={() => handleOpenPosterModal(movie)}
                        title="Quản lý Poster"
                      >
                        <Image className="w-3.5 h-3.5 mr-1" /> Poster
                      </Button>
                      <Button
                        variant="outline"
                        size="sm"
                        onClick={() => handleOpenEdit(movie)}
                        title="Chỉnh sửa"
                      >
                        <Edit2 className="w-3.5 h-3.5" />
                      </Button>
                    </td>
                  </tr>
                ))}
              </tbody>
            </table>
          </div>

          <div className="p-4 border-t border-border">
            <Pagination
              currentPage={moviesData.page}
              totalPages={moviesData.totalPages}
              onPageChange={setPage}
            />
          </div>
        </Card>
      )}

      {/* Create / Edit Movie Modal */}
      <Modal
        isOpen={isMovieModalOpen}
        onClose={() => setIsMovieModalOpen(false)}
        title={editingMovie ? 'Chỉnh sửa phim' : 'Thêm phim mới'}
        className="max-w-2xl"
      >
        <form onSubmit={handleMovieSubmit} className="space-y-4 max-h-[75vh] overflow-y-auto px-1">
          <Input
            label="Tên phim (Tiếng Việt)"
            required
            value={title}
            onChange={(e) => setTitle(e.target.value)}
          />

          <Input
            label="Tên gốc (Nguyên bản)"
            value={originalTitle}
            onChange={(e) => setOriginalTitle(e.target.value)}
          />

          <Textarea
            label="Tóm tắt nội dung (Synopsis)"
            rows={4}
            value={synopsis}
            onChange={(e) => setSynopsis(e.target.value)}
          />

          <div className="grid grid-cols-1 sm:grid-cols-3 gap-4">
            <Input
              label="Thời lượng (phút)"
              type="number"
              min="1"
              max="600"
              required
              value={durationMinutes}
              onChange={(e) => setDurationMinutes(e.target.value)}
            />

            <Input
              label="Ngày khởi chiếu"
              type="date"
              value={releaseDate}
              onChange={(e) => setReleaseDate(e.target.value)}
            />

            <div>
              <label className="block text-sm font-medium text-foreground mb-1.5">Phân loại tuổi</label>
              <select
                value={ageRating}
                onChange={(e) => setAgeRating(e.target.value)}
                className="h-10 w-full rounded-lg border border-border bg-background px-3 text-sm"
              >
                <option value="P">P (Mọi lứa tuổi)</option>
                <option value="K">K (Dưới 13 có phụ huynh)</option>
                <option value="T13">T13 (Từ 13 tuổi trở lên)</option>
                <option value="T16">T16 (Từ 16 tuổi trở lên)</option>
                <option value="T18">T18 (Từ 18 tuổi trở lên)</option>
                <option value="C">C (Cấm phổ biến)</option>
              </select>
            </div>
          </div>

          <div>
            <label className="block text-sm font-medium text-foreground mb-1.5">Gán Thể loại</label>
            <div className="flex flex-wrap gap-2 p-3 border border-border rounded-xl bg-background max-h-36 overflow-y-auto">
              {genresData?.items?.map((genre) => {
                const isSelected = selectedGenreIds.includes(genre.id);
                return (
                  <button
                    key={genre.id}
                    type="button"
                    onClick={() => toggleGenre(genre.id)}
                    className={`px-3 py-1 rounded-full text-xs font-semibold transition-colors flex items-center gap-1 ${
                      isSelected
                        ? 'bg-primary text-white'
                        : 'bg-background-subtle border border-border text-foreground hover:bg-border/60'
                    }`}
                  >
                    {isSelected && <Check className="w-3 h-3" />}
                    {genre.name}
                  </button>
                );
              })}
            </div>
          </div>

          <div className="pt-4 flex justify-end gap-2 border-t border-border">
            <Button type="button" variant="outline" onClick={() => setIsMovieModalOpen(false)}>
              Hủy
            </Button>
            <Button type="submit" variant="primary" isLoading={isSubmitting}>
              {editingMovie ? 'Cập nhật phim' : 'Tạo phim'}
            </Button>
          </div>
        </form>
      </Modal>

      {/* Poster Management Modal */}
      <Modal
        isOpen={isPosterModalOpen}
        onClose={() => setIsPosterModalOpen(false)}
        title={`Quản lý Poster: ${posterMovie?.title}`}
        description="Xem trước, tải lên ảnh mới (tối đa 6 MiB) hoặc xóa poster hiện tại."
      >
        <div className="space-y-4">
          {posterError && <Alert variant="error">{posterError}</Alert>}

          <div className="flex flex-col items-center justify-center p-4 border border-dashed border-border rounded-xl bg-background-subtle/40">
            <div className="w-40 shadow-md rounded-xl overflow-hidden mb-3">
              <MoviePoster
                url={posterPreview}
                title={posterMovie?.title || ''}
                aspectRatio="poster"
              />
            </div>

            <label className="cursor-pointer">
              <Button variant="outline" size="sm" type="button" className="pointer-events-none">
                <Upload className="w-4 h-4 mr-1.5" /> Chọn tệp poster từ máy
              </Button>
              <input
                type="file"
                accept="image/*"
                onChange={handleFileChange}
                className="hidden"
              />
            </label>
            <span className="text-[11px] text-foreground-muted mt-2">
              Hỗ trợ PNG, JPG, WebP. Kích thước tối đa: 6 MiB.
            </span>
          </div>

          <div className="flex justify-between items-center pt-3 border-t border-border">
            {posterMovie?.posterUrl ? (
              <Button
                variant="danger"
                size="sm"
                onClick={handleDeletePoster}
                isLoading={isUploadingPoster}
              >
                <Trash2 className="w-4 h-4 mr-1.5" /> Xóa poster
              </Button>
            ) : (
              <div />
            )}

            <div className="flex gap-2">
              <Button
                variant="outline"
                size="sm"
                onClick={() => setIsPosterModalOpen(false)}
              >
                Hủy
              </Button>
              <Button
                variant="primary"
                size="sm"
                disabled={!posterFile}
                onClick={handleUploadPoster}
                isLoading={isUploadingPoster}
              >
                Lưu poster mới
              </Button>
            </div>
          </div>
        </div>
      </Modal>
    </div>
  );
};
