import React, { useState } from 'react';
import { useForm } from 'react-hook-form';
import { zodResolver } from '@hookform/resolvers/zod';
import { z } from 'zod';
import { Link, useNavigate, useSearchParams } from 'react-router-dom';
import { useAuth } from '../../auth/useAuth';
import { Input } from '../../components/ui/Input';
import { Button } from '../../components/ui/Button';
import { Card } from '../../components/ui/Card';
import { ErrorAlert } from '../../components/common/ErrorAlert';
import { Film } from 'lucide-react';

const loginSchema = z.object({
  email: z.string().min(1, 'Vui lòng nhập email').email('Email không đúng định dạng'),
  password: z.string().min(1, 'Vui lòng nhập mật khẩu'),
});

type LoginFormData = z.infer<typeof loginSchema>;

export const LoginPage: React.FC = () => {
  const { login } = useAuth();
  const navigate = useNavigate();
  const [searchParams] = useSearchParams();
  const [apiError, setApiError] = useState<unknown>(null);

  const rawReturnTo = searchParams.get('returnTo');
  const returnTo =
    rawReturnTo && rawReturnTo.startsWith('/') && !rawReturnTo.startsWith('//')
      ? rawReturnTo
      : '/';

  const {
    register,
    handleSubmit,
    formState: { errors, isSubmitting },
  } = useForm<LoginFormData>({
    resolver: zodResolver(loginSchema),
  });

  const onSubmit = async (data: LoginFormData) => {
    setApiError(null);
    try {
      await login(data.email, data.password);
      navigate(returnTo, { replace: true });
    } catch (err) {
      setApiError(err);
    }
  };

  return (
    <div className="container mx-auto px-4 py-12 flex items-center justify-center min-h-[calc(100vh-160px)]">
      <Card className="w-full max-w-md p-8 shadow-lg border-border">
        <div className="text-center mb-6">
          <div className="inline-flex p-3 rounded-full bg-primary/10 text-primary mb-3">
            <Film className="w-8 h-8" />
          </div>
          <h1 className="font-heading text-3xl font-bold text-foreground">Đăng nhập LumiCine</h1>
          <p className="text-sm text-foreground-muted mt-1">
            Chào mừng bạn quay trở lại! Đăng nhập để tiếp tục.
          </p>
        </div>

        <ErrorAlert error={apiError} className="mb-6" />

        <form onSubmit={handleSubmit(onSubmit)} className="space-y-4">
          <Input
            label="Địa chỉ Email"
            type="email"
            placeholder="ten@vidu.com"
            error={errors.email?.message}
            {...register('email')}
          />

          <div className="space-y-1">
            <div className="flex items-center justify-between">
              <label className="block text-sm font-medium text-foreground">Mật khẩu</label>
              <Link
                to="/account/forgot-password"
                className="text-xs text-primary hover:underline font-medium"
              >
                Quên mật khẩu?
              </Link>
            </div>
            <Input
              type="password"
              placeholder="••••••••"
              error={errors.password?.message}
              {...register('password')}
            />
          </div>

          <Button type="submit" variant="primary" className="w-full mt-2" isLoading={isSubmitting}>
            Đăng nhập
          </Button>
        </form>

        <div className="mt-6 text-center text-sm text-foreground-muted">
          Chưa có tài khoản?{' '}
          <Link to="/account/register" className="font-semibold text-primary hover:underline">
            Đăng ký ngay
          </Link>
        </div>
      </Card>
    </div>
  );
};
