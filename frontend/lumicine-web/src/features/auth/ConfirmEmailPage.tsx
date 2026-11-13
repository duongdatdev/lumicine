import React, { useEffect, useState } from 'react';
import { useSearchParams, Link } from 'react-router-dom';
import { authApi } from '../../api/auth';
import { Card } from '../../components/ui/Card';
import { Button } from '../../components/ui/Button';
import { Input } from '../../components/ui/Input';
import { Alert } from '../../components/ui/Alert';
import { ErrorAlert } from '../../components/common/ErrorAlert';
import { CheckCircle2, Mail } from 'lucide-react';
import { Spinner } from '../../components/ui/Spinner';

export const ConfirmEmailPage: React.FC = () => {
  const [searchParams] = useSearchParams();
  const userId = searchParams.get('userId');
  const code = searchParams.get('code');
  const emailParam = searchParams.get('email') || '';

  const [isVerifying, setIsVerifying] = useState(false);
  const [isSuccess, setIsSuccess] = useState(false);
  const [apiError, setApiError] = useState<unknown>(null);

  // Resend state
  const [resendEmail, setResendEmail] = useState(emailParam);
  const [isResending, setIsResending] = useState(false);
  const [resendSuccess, setResendSuccess] = useState(false);
  const [resendError, setResendError] = useState<unknown>(null);

  useEffect(() => {
    if (userId && code) {
      setIsVerifying(true);
      setApiError(null);
      authApi
        .confirmEmail({ userId, code })
        .then(() => {
          setIsSuccess(true);
        })
        .catch((err) => {
          setApiError(err);
        })
        .finally(() => {
          setIsVerifying(false);
        });
    }
  }, [userId, code]);

  const handleResend = async (e: React.FormEvent) => {
    e.preventDefault();
    if (!resendEmail) return;
    setIsResending(true);
    setResendError(null);
    setResendSuccess(false);

    try {
      await authApi.resendConfirmation(resendEmail);
      setResendSuccess(true);
    } catch (err) {
      setResendError(err);
    } finally {
      setIsResending(false);
    }
  };

  return (
    <div className="container mx-auto px-4 py-12 flex items-center justify-center min-h-[calc(100vh-160px)]">
      <Card className="w-full max-w-md p-8 shadow-lg">
        {isVerifying ? (
          <div className="text-center py-6 space-y-3">
            <Spinner className="w-10 h-10 mx-auto text-primary" />
            <h2 className="font-heading text-2xl font-bold">Đang xác nhận email...</h2>
            <p className="text-sm text-foreground-muted">Vui lòng chờ trong giây lát.</p>
          </div>
        ) : isSuccess ? (
          <div className="text-center space-y-4 py-4">
            <div className="inline-flex p-3 rounded-full bg-green-100 text-green-700">
              <CheckCircle2 className="w-12 h-12" />
            </div>
            <h2 className="font-heading text-2xl font-bold text-foreground">Xác nhận email thành công!</h2>
            <p className="text-sm text-foreground-muted">
              Tài khoản của bạn đã được kích hoạt. Bạn hiện có thể đăng nhập và trải nghiệm dịch vụ.
            </p>
            <Link to="/account/login" className="block pt-2">
              <Button variant="primary" className="w-full">
                Đăng nhập ngay
              </Button>
            </Link>
          </div>
        ) : (
          <div className="space-y-6">
            <div className="text-center">
              <div className="inline-flex p-3 rounded-full bg-primary/10 text-primary mb-2">
                <Mail className="w-8 h-8" />
              </div>
              <h1 className="font-heading text-2xl font-bold text-foreground">Kích hoạt tài khoản</h1>
              <p className="text-sm text-foreground-muted mt-1">
                {userId && code
                  ? 'Liên kết xác nhận không hợp lệ hoặc đã hết hạn.'
                  : 'Kiểm tra email của bạn hoặc yêu cầu gửi lại liên kết kích hoạt mới.'}
              </p>
            </div>

            {Boolean(apiError) && <ErrorAlert error={apiError} title="Xác nhận không thành công" />}
            {resendSuccess && (
              <Alert variant="success" title="Đã gửi email kích hoạt">
                Nếu tài khoản tồn tại và chưa kích hoạt, email mới đã được gửi tới hộp thư của bạn.
              </Alert>
            )}
            {Boolean(resendError) && <ErrorAlert error={resendError} title="Không thể gửi lại email" />}

            <form onSubmit={handleResend} className="space-y-4 pt-2">
              <Input
                label="Nhập email của bạn để gửi lại mã"
                type="email"
                placeholder="ten@vidu.com"
                value={resendEmail}
                onChange={(e) => setResendEmail(e.target.value)}
                required
              />

              <Button type="submit" variant="primary" className="w-full" isLoading={isResending}>
                Gửi lại email xác nhận
              </Button>
            </form>

            <div className="text-center pt-2">
              <Link to="/account/login" className="text-sm font-semibold text-primary hover:underline">
                Quay lại đăng nhập
              </Link>
            </div>
          </div>
        )}
      </Card>
    </div>
  );
};
