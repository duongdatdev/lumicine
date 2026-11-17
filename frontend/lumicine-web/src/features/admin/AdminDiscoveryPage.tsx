import React, { useState } from 'react';
import { useQuery } from '@tanstack/react-query';
import { adminApi } from '../../api/admin';
import type { DiscoveryRebuildResult } from '../../api/types';
import { Card } from '../../components/ui/Card';
import { Button } from '../../components/ui/Button';
import { Badge } from '../../components/ui/Badge';
import { Alert } from '../../components/ui/Alert';
import { LoadingState } from '../../components/common/LoadingState';
import { ErrorAlert } from '../../components/common/ErrorAlert';
import { formatDateTimeUtc7 } from '../../lib/utils';
import { Sparkles, RefreshCw, Database, CheckCircle2, AlertTriangle, Cpu } from 'lucide-react';

export const AdminDiscoveryPage: React.FC = () => {
  const [isRebuilding, setIsRebuilding] = useState(false);
  const [rebuildResult, setRebuildResult] = useState<DiscoveryRebuildResult | null>(null);
  const [actionError, setActionError] = useState<unknown>(null);

  const { data: status, isLoading, error, refetch } = useQuery({
    queryKey: ['adminDiscoveryIndex'],
    queryFn: () => adminApi.getDiscoveryIndexStatus(),
  });

  const handleRebuild = async () => {
    if (!window.confirm('Quá trình xây dựng lại chỉ mục AI sẽ mã hóa vector toàn bộ phim đang hoạt động. Tiếp tục?')) return;

    setIsRebuilding(true);
    setActionError(null);
    setRebuildResult(null);

    try {
      const res = await adminApi.rebuildDiscoveryIndex();
      setRebuildResult(res);
      refetch();
    } catch (err) {
      setActionError(err);
    } finally {
      setIsRebuilding(false);
    }
  };

  return (
    <div className="space-y-6 max-w-4xl">
      <div className="flex flex-col sm:flex-row sm:items-center justify-between gap-4">
        <div>
          <h1 className="font-heading text-3xl font-black text-foreground">
            Chỉ mục Tìm kiếm Thông minh (AI Discovery Index)
          </h1>
          <p className="text-sm text-foreground-muted">
            Quản lý chỉ mục vector ngữ nghĩa phục vụ tính năng trợ lý tìm phim AI.
          </p>
        </div>

        <Button
          variant="accent"
          onClick={handleRebuild}
          isLoading={isRebuilding}
          className="shadow-sm"
        >
          <Sparkles className="w-4 h-4 mr-2" /> Xây dựng lại chỉ mục
        </Button>
      </div>

      <ErrorAlert error={actionError} title="Không thể tái tạo chỉ mục" />

      {rebuildResult && (
        <Alert variant="success" title="Xây dựng chỉ mục hoàn tất">
          Đã mã hóa và lập chỉ mục cho <strong>{rebuildResult.indexedCount}</strong> bộ phim thành công lúc{' '}
          {formatDateTimeUtc7(rebuildResult.completedAtUtc)}.
        </Alert>
      )}

      {isLoading ? (
        <LoadingState message="Đang kiểm tra trạng thái chỉ mục AI..." />
      ) : error ? (
        <ErrorAlert error={error} title="Không thể kết nối dịch vụ chỉ mục" />
      ) : status ? (
        <div className="space-y-6">
          <div className="grid grid-cols-1 sm:grid-cols-3 gap-5">
            <Card className="p-5 space-y-2">
              <span className="text-xs font-semibold uppercase tracking-wider text-foreground-muted block">
                Tổng số phim hoạt động
              </span>
              <div className="flex items-center justify-between">
                <span className="font-heading font-black text-3xl text-foreground">
                  {status.movieCount}
                </span>
                <div className="p-2.5 rounded-xl bg-primary/10 text-primary">
                  <Database className="w-5 h-5" />
                </div>
              </div>
            </Card>

            <Card className="p-5 space-y-2">
              <span className="text-xs font-semibold uppercase tracking-wider text-foreground-muted block">
                Phim đã nhúng Vector
              </span>
              <div className="flex items-center justify-between">
                <span className="font-heading font-black text-3xl text-accent">
                  {status.embeddedMovieCount}
                </span>
                <div className="p-2.5 rounded-xl bg-accent/10 text-accent">
                  <Cpu className="w-5 h-5" />
                </div>
              </div>
            </Card>

            <Card className="p-5 space-y-2">
              <span className="text-xs font-semibold uppercase tracking-wider text-foreground-muted block">
                Tình trạng chỉ mục
              </span>
              <div className="flex items-center justify-between">
                <div>
                  <Badge variant={status.isStale ? 'warning' : 'success'} className="text-xs font-bold">
                    {status.isStale ? 'Cần cập nhật (Stale)' : 'Cập nhật mới nhất'}
                  </Badge>
                </div>
                <div className="p-2.5 rounded-xl bg-background-subtle text-foreground-muted">
                  {status.isStale ? (
                    <AlertTriangle className="w-5 h-5 text-amber-600" />
                  ) : (
                    <CheckCircle2 className="w-5 h-5 text-green-600" />
                  )}
                </div>
              </div>
              <p className="text-[11px] text-foreground-muted">
                Lần lập chỉ mục gần nhất: {formatDateTimeUtc7(status.lastBuiltAtUtc)}
              </p>
            </Card>
          </div>

          <Card className="p-6 space-y-4">
            <h3 className="font-heading font-bold text-lg text-foreground">
              Nguyên lý hoạt động của AI Discovery
            </h3>
            <p className="text-sm text-foreground-muted leading-relaxed">
              Hệ thống kết hợp tìm kiếm ngữ nghĩa đa tầng (Hybrid Vector + Keyword Search) dựa trên Gemini Embeddings và PostgreSQL pgvector.
              Khi phim mới được thêm hoặc thay đổi mô tả, chỉ mục sẽ đánh dấu là "Cần cập nhật".
              Bạn có thể chủ động bấm <strong>"Xây dựng lại chỉ mục"</strong> bất kỳ lúc nào để áp dụng các thay đổi mới nhất cho AI Assistant.
            </p>
          </Card>
        </div>
      ) : null}
    </div>
  );
};
