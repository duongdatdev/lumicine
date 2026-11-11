import React from 'react';
import { Film, Heart } from 'lucide-react';
import { Link } from 'react-router-dom';

export const Footer: React.FC = () => {
  return (
    <footer className="border-t border-border bg-background-surface mt-auto py-10">
      <div className="container mx-auto px-4">
        <div className="grid grid-cols-1 md:grid-cols-4 gap-8">
          <div className="space-y-3">
            <div className="flex items-center gap-2 font-heading text-xl font-black text-primary">
              <Film className="w-5 h-5 text-primary" />
              <span>LumiCine</span>
            </div>
            <p className="text-sm text-foreground-muted leading-relaxed">
              Trải nghiệm điện ảnh đỉnh cao với hệ thống đặt vé hiện đại, sơ đồ ghế thời gian thực và trợ lý gợi ý phim thông minh.
            </p>
          </div>

          <div>
            <h4 className="font-heading font-bold text-sm text-foreground uppercase tracking-wider mb-3">
              Khám phá
            </h4>
            <ul className="space-y-2 text-sm text-foreground-muted">
              <li>
                <Link to="/movies" className="hover:text-primary transition-colors">
                  Phim đang chiếu
                </Link>
              </li>
              <li>
                <Link to="/cinemas" className="hover:text-primary transition-colors">
                  Cụm rạp
                </Link>
              </li>
              <li>
                <Link to="/discover" className="hover:text-primary transition-colors">
                  Trợ lý tìm phim AI
                </Link>
              </li>
            </ul>
          </div>

          <div>
            <h4 className="font-heading font-bold text-sm text-foreground uppercase tracking-wider mb-3">
              Tài khoản
            </h4>
            <ul className="space-y-2 text-sm text-foreground-muted">
              <li>
                <Link to="/account/login" className="hover:text-primary transition-colors">
                  Đăng nhập
                </Link>
              </li>
              <li>
                <Link to="/account/register" className="hover:text-primary transition-colors">
                  Đăng ký tài khoản
                </Link>
              </li>
              <li>
                <Link to="/tickets" className="hover:text-primary transition-colors">
                  Tra cứu vé điện tử
                </Link>
              </li>
            </ul>
          </div>

          <div>
            <h4 className="font-heading font-bold text-sm text-foreground uppercase tracking-wider mb-3">
              Thông tin hỗ trợ
            </h4>
            <p className="text-sm text-foreground-muted leading-relaxed">
              Hotline: 1900 6868 (8:00 - 22:00)
              <br />
              Email: support@lumicine.vn
              <br />
              Hệ thống thanh toán qua cổng VNPay Sandbox an toàn.
            </p>
          </div>
        </div>

        <div className="border-t border-border mt-8 pt-6 flex flex-col sm:flex-row items-center justify-between text-xs text-foreground-muted gap-4">
          <p>© {new Date().getFullYear()} LumiCine Cinema. Bản quyền thuộc về LumiCine.</p>
          <p className="flex items-center gap-1">
            Được phát triển với <Heart className="w-3.5 h-3.5 text-red-500 fill-current" /> cho người yêu điện ảnh.
          </p>
        </div>
      </div>
    </footer>
  );
};
