import React, { useState } from 'react';
import { useForm } from 'react-hook-form';
import { zodResolver } from '@hookform/resolvers/zod';
import { z } from 'zod';
import { Link } from 'react-router-dom';
import { authApi } from '../../api/auth';
import { Input } from '../../components/ui/Input';
import { Button } from '../../components/ui/Button';
import { Card } from '../../components/ui/Card';
import { Alert } from '../../components/ui/Alert';
import { ErrorAlert } from '../../components/common/ErrorAlert';
import { Film, MailCheck } from 'lucide-react';

const registerSchema = z
  .object({
    email: z.string().min(1, 'Vui lòng nhập email').email('Email không đúng định dạng'),
    displayName: z.string().min(2, 'Họ tên tối thiểu 2 ký tự').max(100, 'Tối đa 100 ký tự'),
    password: z.string().min(8, 'Mật khẩu phải có ít nhất 8 ký tự'),
    confirmPassword: z.string().min(1, 'Vui lòng xác nhận mật khẩu'),
  })
  .refine((data) => data.password === data.confirmPassword, {
    message: 'Mật khẩu xác nhận không khớp',
    path: ['confirmPassword'],
  });

type RegisterFormData = z.infer<typeof registerSchema>;

export const RegisterPage: React.FC = () => {
  const [apiError, setApiError] = useState<unknown>(null);
  const [isSuccess, setIsSuccess] = useState(false);
  const [registeredEmail, setRegisteredEmail] = useState('');

  const {
    register,
    handleSubmit,
    formState: { errors, isSubmitting },
  } = useForm<RegisterFormData>({
    resolver: zodResolver(registerSchema),
  });

  const onSubmit = async (data: RegisterFormData) => {
    setApiError(null);
    try {
      await authApi.register({
        email: data.email,
        displayName: data.displayName,
        password: data.password,
      });
      setRegisteredEmail(data.email);
      setIsSuccess(true);
    } catch (err) {
      setApiError(err);
    }
  };

  if (isSuccess) {
    return (
      <div className="container mx-auto px-4 py-12 flex items-center justify-center min-h-[calc(100vh-160px)]">
        <Card className="w-full max-w-md p-8 text-center shadow-lg">
          <div className="inline-flex p-4 rounded-full bg-green-100 text-green-700 mb-4">
            <MailCheck className="w-10 h-10" />
          </div>
          <h2 className="font-heading text-2xl font-bold text-foreground mb-2">Đăng ký thành công!</h2>
          <p className="text-sm text-foreground-muted mb-6 leading-relaxed">
            Chúng tôi đã gửi email xác nhận đến địa chỉ{' '}
            <strong className="text-foreground">{registeredEmail}</strong>. Vui lòng kiểm tra hộp thư
            và nhấp vào liên kết để kích hoạt tài khoản.
          </p>
          <div className="space-y-3">
            <Link to="/account/login" className="block">
              <Button variant="primary" className="w-full">
                Đi đến Đăng nhập
              </Button>
            </Link>
            <Link to={`/account/confirm-email?email=${encodeURIComponent(registeredEmail)}`} className="block">
              <Button variant="outline" className="w-full text-xs">
                Nhập mã xác nhận thủ công
              </Button>
            </Link>
          </div>
        </Card>
      </div>
    );
  }

  return (
    <div className="container mx-auto px-4 py-12 flex items-center justify-center min-h-[calc(100vh-160px)]">
      <Card className="w-full max-w-md p-8 shadow-lg border-border">
        <div className="text-center mb-6">
          <div className="inline-flex p-3 rounded-full bg-primary/10 text-primary mb-3">
            <Film className="w-8 h-8" />
          </div>
          <h1 className="font-heading text-3xl font-bold text-foreground">Tạo tài khoản</h1>
          <p className="text-sm text-foreground-muted mt-1">
            Đăng ký để nhận ưu đãi và trải nghiệm đặt vé nhanh chóng
          </p>
        </div>

        <ErrorAlert error={apiError} className="mb-6" />

        <form onSubmit={handleSubmit(onSubmit)} className="space-y-4">
          <Input
            label="Họ và tên"
            placeholder="Nguyễn Văn A"
            error={errors.displayName?.message}
            {...register('displayName')}
          />

          <Input
            label="Địa chỉ Email"
            type="email"
            placeholder="ten@vidu.com"
            error={errors.email?.message}
            {...register('email')}
          />

          <Input
            label="Mật khẩu"
            type="password"
            placeholder="Tối thiểu 8 ký tự"
            error={errors.password?.message}
            {...register('password')}
          />

          <Input
            label="Xác nhận mật khẩu"
            type="password"
            placeholder="Nhập lại mật khẩu"
            error={errors.confirmPassword?.message}
            {...register('confirmPassword')}
          />

          <Button type="submit" variant="primary" className="w-full mt-2" isLoading={isSubmitting}>
            Đăng ký tài khoản
          </Button>
        </form>

        <div className="mt-6 text-center text-sm text-foreground-muted">
          Đã có tài khoản?{' '}
          <Link to="/account/login" className="font-semibold text-primary hover:underline">
            Đăng nhập ngay
          </Link>
        </div>
      </Card>
    </div>
  );
};
