import React, { useState } from 'react';
import { useForm } from 'react-hook-form';
import { zodResolver } from '@hookform/resolvers/zod';
import { z } from 'zod';
import { useSearchParams, Link } from 'react-router-dom';
import { authApi } from '../../api/auth';
import { Input } from '../../components/ui/Input';
import { Button } from '../../components/ui/Button';
import { Card } from '../../components/ui/Card';
import { Alert } from '../../components/ui/Alert';
import { ErrorAlert } from '../../components/common/ErrorAlert';
import { Lock, CheckCircle2 } from 'lucide-react';

const resetPasswordSchema = z
  .object({
    email: z.string().min(1, 'Vui lòng nhập email').email('Email không đúng định dạng'),
    code: z.string().min(1, 'Vui lòng nhập mã đặt lại mật khẩu'),
    newPassword: z.string().min(8, 'Mật khẩu mới tối thiểu 8 ký tự'),
    confirmPassword: z.string().min(1, 'Vui lòng xác nhận mật khẩu'),
  })
  .refine((data) => data.newPassword === data.confirmPassword, {
    message: 'Mật khẩu xác nhận không khớp',
    path: ['confirmPassword'],
  });

type ResetPasswordFormData = z.infer<typeof resetPasswordSchema>;

export const ResetPasswordPage: React.FC = () => {
  const [searchParams] = useSearchParams();
  const emailParam = searchParams.get('email') || '';
  const codeParam = searchParams.get('code') || '';

  const [apiError, setApiError] = useState<unknown>(null);
  const [isSuccess, setIsSuccess] = useState(false);

  const {
    register,
    handleSubmit,
    formState: { errors, isSubmitting },
  } = useForm<ResetPasswordFormData>({
    resolver: zodResolver(resetPasswordSchema),
    defaultValues: {
      email: emailParam,
      code: codeParam,
    },
  });

  const onSubmit = async (data: ResetPasswordFormData) => {
    setApiError(null);
    try {
      await authApi.resetPassword({
        email: data.email,
        code: data.code,
        newPassword: data.newPassword,
      });
      setIsSuccess(true);
    } catch (err) {
      setApiError(err);
    }
  };

  return (
    <div className="container mx-auto px-4 py-12 flex items-center justify-center min-h-[calc(100vh-160px)]">
      <Card className="w-full max-w-md p-8 shadow-lg">
        <div className="text-center mb-6">
          <div className="inline-flex p-3 rounded-full bg-primary/10 text-primary mb-3">
            <Lock className="w-8 h-8" />
          </div>
          <h1 className="font-heading text-3xl font-bold text-foreground">Đặt lại mật khẩu</h1>
          <p className="text-sm text-foreground-muted mt-1">
            Thiết lập mật khẩu mới an toàn cho tài khoản của bạn.
          </p>
        </div>

        <ErrorAlert error={apiError} className="mb-6" />

        {isSuccess ? (
          <div className="text-center space-y-4 py-4">
            <div className="inline-flex p-3 rounded-full bg-green-100 text-green-700">
              <CheckCircle2 className="w-12 h-12" />
            </div>
            <h2 className="font-heading text-2xl font-bold text-foreground">
              Đổi mật khẩu thành công!
            </h2>
            <p className="text-sm text-foreground-muted">
              Mật khẩu mới của bạn đã có hiệu lực. Bạn có thể sử dụng mật khẩu này để đăng nhập ngay bây giờ.
            </p>
            <Link to="/account/login" className="block pt-2">
              <Button variant="primary" className="w-full">
                Đăng nhập ngay
              </Button>
            </Link>
          </div>
        ) : (
          <form onSubmit={handleSubmit(onSubmit)} className="space-y-4">
            <Input
              label="Địa chỉ Email"
              type="email"
              error={errors.email?.message}
              {...register('email')}
            />

            <Input
              label="Mã đặt lại mật khẩu"
              placeholder="Nhập mã từ email"
              error={errors.code?.message}
              {...register('code')}
            />

            <Input
              label="Mật khẩu mới"
              type="password"
              placeholder="Tối thiểu 8 ký tự"
              error={errors.newPassword?.message}
              {...register('newPassword')}
            />

            <Input
              label="Xác nhận mật khẩu mới"
              type="password"
              placeholder="Nhập lại mật khẩu mới"
              error={errors.confirmPassword?.message}
              {...register('confirmPassword')}
            />

            <Button type="submit" variant="primary" className="w-full mt-2" isLoading={isSubmitting}>
              Cập nhật mật khẩu
            </Button>

            <div className="text-center pt-2">
              <Link to="/account/login" className="text-sm font-semibold text-primary hover:underline">
                Hủy và quay lại đăng nhập
              </Link>
            </div>
          </form>
        )}
      </Card>
    </div>
  );
};
