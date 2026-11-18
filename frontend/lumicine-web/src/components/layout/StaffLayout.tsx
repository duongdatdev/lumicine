import React from 'react';
import { Outlet, Link, useLocation } from 'react-router-dom';
import { useAuth } from '../../auth/useAuth';
import { Button } from '../ui/Button';
import { QrCode, Calendar, ArrowLeft, LogOut, ShieldCheck, LibraryBig } from 'lucide-react';

export const StaffLayout: React.FC = () => {
  const { user, logout } = useAuth();
  const location = useLocation();

  const isActive = (path: string) => location.pathname.startsWith(path);

  return (
    <div className="min-h-screen flex flex-col bg-background text-foreground">
      {/* Staff Header */}
      <header className="sticky top-0 z-40 w-full border-b border-amber-200 bg-amber-50/90 backdrop-blur">
        <div className="container mx-auto flex min-h-16 flex-wrap items-center justify-between gap-2 px-4 py-2">
          <div className="flex min-w-0 items-center gap-3 sm:gap-6">
            <Link to="/staff/schedules" className="flex items-center gap-2 font-heading text-xl font-bold text-amber-900">
              <ShieldCheck className="w-5 h-5 text-amber-700" />
              <span>LumiCine Staff</span>
            </Link>

            <nav className="flex items-center gap-1 overflow-x-auto sm:gap-2">
              <Link
                to="/staff/schedules"
                className={`flex items-center gap-1.5 px-3 py-1.5 rounded-lg text-sm font-medium transition-colors ${
                  isActive('/staff/schedules')
                    ? 'bg-amber-200/70 text-amber-900 font-semibold'
                    : 'text-amber-800 hover:bg-amber-100'
                }`}
              >
                <Calendar className="w-4 h-4" />
                <span>Lịch chiếu</span>
              </Link>
              <Link
                to="/staff/catalog"
                className={`flex shrink-0 items-center gap-1.5 rounded-lg px-3 py-1.5 text-sm font-medium transition-colors ${
                  isActive('/staff/catalog') ? 'bg-amber-200/70 text-amber-900 font-semibold' : 'text-amber-800 hover:bg-amber-100'
                }`}
              >
                <LibraryBig className="h-4 w-4" />
                <span>Danh mục</span>
              </Link>
              <Link
                to="/staff/checkin"
                className={`flex items-center gap-1.5 px-3 py-1.5 rounded-lg text-sm font-medium transition-colors ${
                  isActive('/staff/checkin')
                    ? 'bg-amber-200/70 text-amber-900 font-semibold'
                    : 'text-amber-800 hover:bg-amber-100'
                }`}
              >
                <QrCode className="w-4 h-4" />
                <span>Quét vé Check-in</span>
              </Link>
            </nav>
          </div>

          <div className="flex shrink-0 items-center gap-2 sm:gap-3">
            <Link to="/">
              <Button variant="ghost" size="sm" className="text-amber-900 hover:bg-amber-100">
                <ArrowLeft className="w-4 h-4 mr-1" /> Về trang chủ
              </Button>
            </Link>
            <span className="text-xs font-semibold px-2.5 py-1 rounded-full bg-amber-200 text-amber-900">
              {user?.displayName}
            </span>
            <Button
              variant="ghost"
              size="sm"
              onClick={logout}
              aria-label="Đăng xuất"
              className="text-amber-900 hover:text-red-700"
            >
              <LogOut className="w-4 h-4" />
            </Button>
          </div>
        </div>
      </header>

      <main className="flex-1 container mx-auto px-4 py-6">
        <Outlet />
      </main>
    </div>
  );
};
