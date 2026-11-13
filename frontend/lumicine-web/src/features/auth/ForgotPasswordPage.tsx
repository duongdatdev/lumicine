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
import { KeyRound } from 'lucide-react';

const forgotPasswordSchema = z.object({
  email: z.string().min(1, 'Vui lòng nhập email').email('Email không đúng định dạng'),
});

type ForgotPasswordFormData = z.infer<typeof forgotPasswordSchema>;

export const ForgotPasswordPage: React.FC = () => {
  const [apiError, setApiError] = useState<unknown>(null);
  const [isSuccess, setIsSuccess] = useState(false);

  const {
    register,
    handleSubmit,
    formState: { errors, isSubmitting },
  } = useForm<ForgotPasswordFormData>({
    resolver: zodResolver(forgotPasswordSchema),
  });

  const onSubmit = async (data: ForgotPasswordFormData) => {
    setApiError(null);
    try {
      await authApi.forgotPassword(data.email);
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
            <KeyRound className="w-8 h-8" />
          </div>
          <h1 className="font-heading text-3xl font-bold text-foreground">Quên mật khẩu?</h1>
          <p className="text-sm text-foreground-muted mt-1">
            Nhập email của bạn và chúng tôi sẽ gửi hướng dẫn đặt lại mật khẩu.
          </p>
        </div>

        <ErrorAlert error={apiError} className="mb-6" />

        {isSuccess ? (
          <div className="space-y-4">
            <Alert variant="success" title="Đã gửi email khôi phục">
              Nếu địa chỉ email tồn tại trong hệ thống, bạn sẽ nhận được đường dẫn đặt lại mật khẩu trong ít phút.
            </Alert>
            <Link to="/account/login" className="block pt-2">
              <Button variant="primary" className="w-full">
                Quay lại Đăng nhập
              </Button>
            </Link>
          </div>
        ) : (
          <form onSubmit={handleSubmit(onSubmit)} className="space-y-4">
            <Input
              label="Địa chỉ Email"
              type="email"
              placeholder="ten@vidu.com"
              error={errors.email?.message}
              {...register('email')}
            />

            <Button type="submit" variant="primary" className="w-full mt-2" isLoading={isSubmitting}>
              Gửi liên kết khôi phục
            </Button>

            <div className="text-center pt-2">
              <Link to="/account/login" className="text-sm font-semibold text-primary hover:underline">
                Quay lại đăng nhập
              </Link>
            </div>
          </form>
        )}
      </Card>
    </div>
  );
};
