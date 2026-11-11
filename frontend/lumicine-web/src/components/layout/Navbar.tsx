import React, { useState } from 'react';
import { Link, useNavigate, useLocation } from 'react-router-dom';
import { useAuth } from '../../auth/useAuth';
import { Button } from '../ui/Button';
import {
  Film,
  Sparkles,
  Ticket,
  User,
  LogOut,
  Menu,
  X,
  ShieldAlert,
  ClipboardList,
} from 'lucide-react';

export const Navbar: React.FC = () => {
  const { user, isAuthenticated, isAdmin, isStaff, logout } = useAuth();
  const navigate = useNavigate();
  const location = useLocation();
  const [mobileMenuOpen, setMobileMenuOpen] = useState(false);

  const handleLogout = async () => {
    await logout();
    navigate('/');
    setMobileMenuOpen(false);
  };

  const isActive = (path: string) => {
    if (path === '/' && location.pathname === '/') return true;
    if (path !== '/' && location.pathname.startsWith(path)) return true;
    return false;
  };

  return (
    <header className="sticky top-0 z-40 w-full border-b border-border bg-background-surface/95 backdrop-blur">
      <div className="container mx-auto px-4 h-16 flex items-center justify-between">
        {/* Brand */}
        <Link to="/" className="flex items-center gap-2 font-heading text-2xl font-black text-primary tracking-tight">
          <Film className="w-6 h-6 text-primary" />
          <span>LumiCine</span>
        </Link>

        {/* Desktop Nav */}
        <nav className="hidden md:flex items-center gap-6 text-sm font-medium">
          <Link
            to="/movies"
            className={`transition-colors hover:text-primary ${
              isActive('/movies') ? 'text-primary font-semibold' : 'text-foreground'
            }`}
          >
            Phim
          </Link>
          <Link
            to="/cinemas"
            className={`transition-colors hover:text-primary ${
              isActive('/cinemas') ? 'text-primary font-semibold' : 'text-foreground'
            }`}
          >
            Rạp chiếu
          </Link>
          <Link
            to="/discover"
            className={`flex items-center gap-1.5 px-3 py-1.5 rounded-full border border-accent/20 bg-accent/5 text-accent font-semibold transition-all hover:bg-accent hover:text-white ${
              isActive('/discover') ? 'bg-accent text-white' : ''
            }`}
          >
            <Sparkles className="w-4 h-4" />
            <span>Tìm phim AI</span>
          </Link>

          {isAuthenticated && (
            <Link
              to="/tickets"
              className={`flex items-center gap-1 transition-colors hover:text-primary ${
                isActive('/tickets') || isActive('/bookings') ? 'text-primary font-semibold' : 'text-foreground'
              }`}
            >
              <Ticket className="w-4 h-4" />
              <span>Vé của tôi</span>
            </Link>
          )}

          {isStaff && (
            <Link
              to="/staff/schedules"
              className={`flex items-center gap-1 text-amber-700 hover:text-amber-800 ${
                isActive('/staff') ? 'font-bold' : ''
              }`}
            >
              <ClipboardList className="w-4 h-4" />
              <span>Nhân viên</span>
            </Link>
          )}

          {isAdmin && (
            <Link
              to="/admin/dashboard"
              className={`flex items-center gap-1 text-primary hover:text-primary-hover ${
                isActive('/admin') ? 'font-bold' : ''
              }`}
            >
              <ShieldAlert className="w-4 h-4" />
              <span>Quản trị</span>
            </Link>
          )}
        </nav>

        {/* Auth Buttons */}
        <div className="hidden md:flex items-center gap-3">
          {isAuthenticated ? (
            <div className="flex items-center gap-3">
              <Link
                to="/account/profile"
                className="flex items-center gap-2 px-3 py-1.5 rounded-lg border border-border bg-background-subtle hover:bg-border/60 text-sm font-medium text-foreground transition-colors"
              >
                <User className="w-4 h-4 text-foreground-muted" />
                <span className="max-w-[120px] truncate">{user?.displayName || user?.email}</span>
              </Link>
              <Button
                variant="ghost"
                size="sm"
                onClick={handleLogout}
                aria-label="Đăng xuất"
                className="text-foreground-muted hover:text-red-600"
              >
                <LogOut className="w-4 h-4" />
              </Button>
            </div>
          ) : (
            <div className="flex items-center gap-2">
              <Link to="/account/login">
                <Button variant="ghost" size="sm">
                  Đăng nhập
                </Button>
              </Link>
              <Link to="/account/register">
                <Button variant="primary" size="sm">
                  Đăng ký
                </Button>
              </Link>
            </div>
          )}
        </div>

        {/* Mobile menu button */}
        <div className="md:hidden flex items-center">
          <button
            onClick={() => setMobileMenuOpen(!mobileMenuOpen)}
            className="p-2 rounded-lg text-foreground hover:bg-background-subtle"
            aria-label="Mở menu"
          >
            {mobileMenuOpen ? <X className="w-6 h-6" /> : <Menu className="w-6 h-6" />}
          </button>
        </div>
      </div>

      {/* Mobile Drawer */}
      {mobileMenuOpen && (
        <div className="md:hidden border-b border-border bg-background-surface px-4 pt-2 pb-6 space-y-3">
          <Link
            to="/movies"
            onClick={() => setMobileMenuOpen(false)}
            className="block py-2 text-base font-medium text-foreground hover:text-primary"
          >
            Phim đang chiếu
          </Link>
          <Link
            to="/cinemas"
            onClick={() => setMobileMenuOpen(false)}
            className="block py-2 text-base font-medium text-foreground hover:text-primary"
          >
            Rạp chiếu
          </Link>
          <Link
            to="/discover"
            onClick={() => setMobileMenuOpen(false)}
            className="flex items-center gap-2 py-2 text-base font-semibold text-accent"
          >
            <Sparkles className="w-4 h-4" />
            <span>Tìm phim AI</span>
          </Link>

          {isAuthenticated && (
            <Link
              to="/tickets"
              onClick={() => setMobileMenuOpen(false)}
              className="flex items-center gap-2 py-2 text-base font-medium text-foreground hover:text-primary"
            >
              <Ticket className="w-4 h-4" />
              <span>Vé của tôi</span>
            </Link>
          )}

          {isStaff && (
            <Link
              to="/staff/schedules"
              onClick={() => setMobileMenuOpen(false)}
              className="flex items-center gap-2 py-2 text-base font-medium text-amber-700"
            >
              <ClipboardList className="w-4 h-4" />
              <span>Khu vực Nhân viên</span>
            </Link>
          )}

          {isAdmin && (
            <Link
              to="/admin/dashboard"
              onClick={() => setMobileMenuOpen(false)}
              className="flex items-center gap-2 py-2 text-base font-medium text-primary"
            >
              <ShieldAlert className="w-4 h-4" />
              <span>Khu vực Quản trị</span>
            </Link>
          )}

          <div className="border-t border-border pt-4">
            {isAuthenticated ? (
              <div className="space-y-3">
                <Link
                  to="/account/profile"
                  onClick={() => setMobileMenuOpen(false)}
                  className="flex items-center gap-2 text-sm font-medium text-foreground"
                >
                  <User className="w-4 h-4" />
                  <span>Tài khoản ({user?.displayName})</span>
                </Link>
                <Button
                  variant="outline"
                  size="sm"
                  onClick={handleLogout}
                  className="w-full text-red-600 hover:text-red-700"
                >
                  <LogOut className="w-4 h-4 mr-2" /> Đăng xuất
                </Button>
              </div>
            ) : (
              <div className="grid grid-cols-2 gap-2">
                <Link to="/account/login" onClick={() => setMobileMenuOpen(false)}>
                  <Button variant="outline" size="sm" className="w-full">
                    Đăng nhập
                  </Button>
                </Link>
                <Link to="/account/register" onClick={() => setMobileMenuOpen(false)}>
                  <Button variant="primary" size="sm" className="w-full">
                    Đăng ký
                  </Button>
                </Link>
              </div>
            )}
          </div>
        </div>
      )}
    </header>
  );
};
