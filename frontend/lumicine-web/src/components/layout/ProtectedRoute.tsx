import React from 'react';
import { Navigate, useLocation } from 'react-router-dom';
import { useAuth } from '../../auth/useAuth';
import { LoadingState } from '../common/LoadingState';
import { Alert } from '../ui/Alert';

export interface ProtectedRouteProps {
  children: React.ReactNode;
  requiredRole?: 'Admin' | 'Staff';
}

export const ProtectedRoute: React.FC<ProtectedRouteProps> = ({ children, requiredRole }) => {
  const { user, isLoading, isAuthenticated, isAdmin, isStaff } = useAuth();
  const location = useLocation();

  if (isLoading) {
    return <LoadingState message="Đang kiểm tra thông tin đăng nhập..." />;
  }

  if (!isAuthenticated || !user) {
    // Only accept internal paths for returnTo
    const returnTo =
      location.pathname.startsWith('/') && !location.pathname.startsWith('//')
        ? location.pathname + location.search
        : '/';

    return <Navigate to={`/account/login?returnTo=${encodeURIComponent(returnTo)}`} replace />;
  }

  if (requiredRole === 'Admin' && !isAdmin) {
    return (
      <div className="container mx-auto p-8 max-w-lg">
        <Alert variant="error" title="Từ chối truy cập">
          Bạn không có quyền quản trị viên (Admin) để truy cập trang này.
        </Alert>
      </div>
    );
  }

  if (requiredRole === 'Staff' && !isStaff) {
    return (
      <div className="container mx-auto p-8 max-w-lg">
        <Alert variant="error" title="Từ chối truy cập">
          Bạn không có quyền nhân viên (Staff) để truy cập trang này.
        </Alert>
      </div>
    );
  }

  return <>{children}</>;
};
