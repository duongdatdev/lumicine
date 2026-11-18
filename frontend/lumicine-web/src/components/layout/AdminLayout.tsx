import React, { useState } from 'react';
import { Outlet, Link, useLocation } from 'react-router-dom';
import { useAuth } from '../../auth/useAuth';
import { Button } from '../ui/Button';
import {
  LayoutDashboard,
  Film,
  Tags,
  Building2,
  Calendar,
  Users,
  CreditCard,
  Sparkles,
  ArrowLeft,
  LogOut,
  Menu,
  X,
  Shield,
} from 'lucide-react';

export const AdminLayout: React.FC = () => {
  const { user, logout } = useAuth();
  const location = useLocation();
  const [sidebarOpen, setSidebarOpen] = useState(false);

  const navItems = [
    { label: 'Tổng quan & Báo cáo', path: '/admin/dashboard', icon: LayoutDashboard },
    { label: 'Quản lý Thể loại', path: '/admin/genres', icon: Tags },
    { label: 'Quản lý Phim & Poster', path: '/admin/movies', icon: Film },
    { label: 'Rạp & Phòng chiếu', path: '/admin/cinemas', icon: Building2 },
    { label: 'Lịch chiếu & Suất', path: '/admin/showtimes', icon: Calendar },
    { label: 'Người dùng & Phân quyền', path: '/admin/users', icon: Users },
    { label: 'Giao dịch & Hoàn tiền', path: '/admin/payments', icon: CreditCard },
    { label: 'Chỉ mục AI Discovery', path: '/admin/discovery', icon: Sparkles },
  ];

  const isActive = (path: string) => {
    return location.pathname.startsWith(path);
  };

  return (
    <div className="min-h-screen flex bg-background text-foreground">
      {/* Mobile sidebar toggle */}
      <div className="lg:hidden fixed top-3 left-3 z-50">
        <button
          onClick={() => setSidebarOpen(!sidebarOpen)}
          className="p-2 rounded-lg bg-background-surface border border-border shadow-md"
          aria-label="Menu quản trị"
        >
          {sidebarOpen ? <X className="w-5 h-5" /> : <Menu className="w-5 h-5" />}
        </button>
      </div>

      {/* Sidebar */}
      <aside
        className={`fixed inset-y-0 left-0 z-40 w-64 border-r border-border bg-background-surface flex flex-col transition-transform lg:translate-x-0 ${
          sidebarOpen ? 'translate-x-0' : '-translate-x-full'
        }`}
      >
        <div className="p-6 border-b border-border flex items-center justify-between">
          <Link to="/admin/dashboard" className="flex items-center gap-2 font-heading text-xl font-black text-primary">
            <Shield className="w-6 h-6 text-primary" />
            <span>LumiCine Quản trị</span>
          </Link>
        </div>

        <nav className="flex-1 p-4 space-y-1.5 overflow-y-auto">
          {navItems.map((item) => {
            const Icon = item.icon;
            const active = isActive(item.path);
            return (
              <Link
                key={item.path}
                to={item.path}
                onClick={() => setSidebarOpen(false)}
                className={`flex items-center gap-3 px-3 py-2.5 rounded-xl text-sm font-medium transition-all ${
                  active
                    ? 'bg-primary text-white font-semibold shadow-sm'
                    : 'text-foreground hover:bg-background-subtle'
                }`}
              >
                <Icon className={`w-4 h-4 ${active ? 'text-white' : 'text-foreground-muted'}`} />
                <span>{item.label}</span>
              </Link>
            );
          })}
        </nav>

        <div className="p-4 border-t border-border space-y-3 bg-background-subtle/30">
          <Link to="/" className="w-full block">
            <Button variant="outline" size="sm" className="w-full justify-start text-xs">
              <ArrowLeft className="w-3.5 h-3.5 mr-2" /> Về trang khách hàng
            </Button>
          </Link>
          <div className="flex items-center justify-between pt-1">
            <div className="text-xs">
              <p className="font-semibold text-foreground truncate max-w-[130px]">{user?.displayName}</p>
              <p className="text-foreground-muted text-[11px]">Administrator</p>
            </div>
            <Button
              variant="ghost"
              size="sm"
              onClick={logout}
              aria-label="Đăng xuất"
              className="text-foreground-muted hover:text-red-600 p-1.5"
            >
              <LogOut className="w-4 h-4" />
            </Button>
          </div>
        </div>
      </aside>

      {/* Main Content Area */}
      <div className="flex-1 min-w-0 lg:pl-64 flex flex-col min-h-screen">
        <main className="flex-1 min-w-0 p-6 pt-16 md:p-8 md:pt-20 lg:pt-8 max-w-7xl w-full mx-auto">
          <Outlet />
        </main>
      </div>
    </div>
  );
};
