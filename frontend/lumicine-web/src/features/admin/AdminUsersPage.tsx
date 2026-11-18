import React, { useState } from 'react';
import { useSearchParams } from 'react-router-dom';
import { useQuery } from '@tanstack/react-query';
import { adminApi } from '../../api/admin';
import type { AdminUserSummary } from '../../api/types';
import { Card } from '../../components/ui/Card';
import { Button } from '../../components/ui/Button';
import { Badge } from '../../components/ui/Badge';
import { Pagination } from '../../components/ui/Pagination';
import { LoadingState } from '../../components/common/LoadingState';
import { EmptyState } from '../../components/common/EmptyState';
import { ErrorAlert } from '../../components/common/ErrorAlert';
import { formatDateUtc7 } from '../../lib/utils';
import { Users, Shield, UserCheck, UserX, RefreshCw } from 'lucide-react';

export const AdminUsersPage: React.FC = () => {
  const [searchParams, setSearchParams] = useSearchParams();
  const page = Math.max(1, Number.parseInt(searchParams.get('page') || '1', 10) || 1);
  const [actionError, setActionError] = useState<unknown>(null);
  const setPage = (nextPage: number) => {
    const params = new URLSearchParams(searchParams);
    if (nextPage > 1) params.set('page', String(nextPage));
    else params.delete('page');
    setSearchParams(params);
  };

  const { data, isLoading, error, refetch } = useQuery({
    queryKey: ['adminUsers', page],
    queryFn: () => adminApi.getUsers(page, 20),
  });

  const handleToggleStatus = async (user: AdminUserSummary) => {
    setActionError(null);
    try {
      await adminApi.setUserStatus(user.id, !user.isActive);
      refetch();
    } catch (err) {
      setActionError(err);
    }
  };

  const handleToggleRole = async (user: AdminUserSummary) => {
    const isStaff = user.roles.includes('Staff');
    const newRole: 'Customer' | 'Staff' = isStaff ? 'Customer' : 'Staff';

    if (
      !window.confirm(
        `Bạn có chắc muốn đổi vai trò của người dùng này sang "${newRole}"?`
      )
    )
      return;

    setActionError(null);
    try {
      await adminApi.setUserRole(user.id, newRole);
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
            Quản lý Người dùng & Phân quyền
          </h1>
          <p className="text-sm text-foreground-muted">
            Xem danh sách tài khoản, khóa/mở hoạt động và phân quyền Customer hoặc Staff.
          </p>
        </div>

        <Button variant="outline" size="sm" onClick={() => refetch()}>
          <RefreshCw className="w-4 h-4 mr-1.5" /> Làm mới
        </Button>
      </div>

      <ErrorAlert error={actionError} title="Không thể cập nhật người dùng" />

      {isLoading ? (
        <LoadingState message="Đang tải danh sách tài khoản..." />
      ) : error ? (
        <ErrorAlert error={error} title="Không thể tải người dùng" />
      ) : !data?.items || data.items.length === 0 ? (
        <EmptyState title="Chưa có người dùng nào" />
      ) : (
        <Card className="p-0 overflow-hidden border-border">
          <div className="overflow-x-auto">
            <table className="w-full text-left text-sm">
              <thead className="bg-background-subtle border-b border-border text-xs font-semibold text-foreground-muted uppercase">
                <tr>
                  <th className="px-6 py-3.5">Họ tên & Email</th>
                  <th className="px-6 py-3.5">Vai trò</th>
                  <th className="px-6 py-3.5">Xác thực Email</th>
                  <th className="px-6 py-3.5">Trạng thái</th>
                  <th className="px-6 py-3.5">Ngày tạo</th>
                  <th className="px-6 py-3.5 text-right">Thao tác</th>
                </tr>
              </thead>
              <tbody className="divide-y divide-border">
                {data.items.map((user) => {
                  const isAdmin = user.roles.includes('Admin');
                  const isStaff = user.roles.includes('Staff');

                  return (
                    <tr key={user.id} className="hover:bg-background-subtle/50 transition-colors">
                      <td className="px-6 py-4">
                        <p className="font-bold text-foreground">{user.displayName}</p>
                        <p className="text-xs text-foreground-muted">{user.email}</p>
                      </td>
                      <td className="px-6 py-4">
                        <div className="flex flex-wrap gap-1">
                          {user.roles.map((r) => (
                            <Badge
                              key={r}
                              variant={
                                r === 'Admin'
                                  ? 'default'
                                  : r === 'Staff'
                                  ? 'warning'
                                  : 'neutral'
                              }
                            >
                              {r}
                            </Badge>
                          ))}
                        </div>
                      </td>
                      <td className="px-6 py-4">
                        <Badge variant={user.emailConfirmed ? 'success' : 'neutral'} className="text-[10px]">
                          {user.emailConfirmed ? 'Đã kích hoạt' : 'Chưa kích hoạt'}
                        </Badge>
                      </td>
                      <td className="px-6 py-4">
                        <span
                          className={`inline-flex items-center px-2 py-0.5 rounded-full text-xs font-bold ${
                            user.isActive
                              ? 'bg-green-100 text-green-800'
                              : 'bg-red-100 text-red-800'
                          }`}
                        >
                          {user.isActive ? 'Đang hoạt động' : 'Bị khóa'}
                        </span>
                      </td>
                      <td className="px-6 py-4 text-xs text-foreground-muted">
                        {formatDateUtc7(user.createdAtUtc)}
                      </td>
                      <td className="px-6 py-4 text-right space-x-2">
                        {!isAdmin && (
                          <>
                            <Button
                              variant="outline"
                              size="sm"
                              className="text-xs"
                              onClick={() => handleToggleRole(user)}
                            >
                              Đổi sang {isStaff ? 'Customer' : 'Staff'}
                            </Button>
                            <Button
                              variant="ghost"
                              size="sm"
                              onClick={() => handleToggleStatus(user)}
                              className={`text-xs ${
                                user.isActive ? 'text-red-600' : 'text-green-600'
                              }`}
                            >
                              {user.isActive ? 'Khóa' : 'Mở khóa'}
                            </Button>
                          </>
                        )}
                        {isAdmin && (
                          <span className="text-xs text-foreground-muted italic">
                            Admin được bảo vệ
                          </span>
                        )}
                      </td>
                    </tr>
                  );
                })}
              </tbody>
            </table>
          </div>

          <div className="p-4 border-t border-border">
            <Pagination
              currentPage={data.page}
              totalPages={Math.ceil(data.totalCount / data.pageSize) || 1}
              onPageChange={setPage}
            />
          </div>
        </Card>
      )}
    </div>
  );
};
