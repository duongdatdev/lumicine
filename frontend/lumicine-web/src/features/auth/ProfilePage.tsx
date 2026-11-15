import React, { useState } from 'react';
import { useForm } from 'react-hook-form';
import { zodResolver } from '@hookform/resolvers/zod';
import { z } from 'zod';
import { useAuth } from '../../auth/useAuth';
import { authApi } from '../../api/auth';
import { Card } from '../../components/ui/Card';
import { Button } from '../../components/ui/Button';
import { Input } from '../../components/ui/Input';
import { Alert } from '../../components/ui/Alert';
import { Badge } from '../../components/ui/Badge';
import { ErrorAlert } from '../../components/common/ErrorAlert';
import { sessionCoordinator } from '../../auth/sessionCoordinator';
import { User, Shield, KeyRound, Check } from 'lucide-react';

const profileSchema = z.object({
  displayName: z.string().min(2, 'Họ tên tối thiểu 2 ký tự').max(100, 'Tối đa 100 ký tự'),
});

const passwordSchema = z
  .object({
    currentPassword: z.string().min(1, 'Vui lòng nhập mật khẩu hiện tại'),
    newPassword: z.string().min(8, 'Mật khẩu mới tối thiểu 8 ký tự'),
    confirmNewPassword: z.string().min(1, 'Vui lòng xác nhận mật khẩu mới'),
  })
  .refine((data) => data.newPassword === data.confirmNewPassword, {
    message: 'Mật khẩu xác nhận không khớp',
    path: ['confirmNewPassword'],
  });

type ProfileFormData = z.infer<typeof profileSchema>;
type PasswordFormData = z.infer<typeof passwordSchema>;

export const ProfilePage: React.FC = () => {
  const { user, updateUser } = useAuth();

  const [profileSuccess, setProfileSuccess] = useState(false);
  const [profileError, setProfileError] = useState<unknown>(null);

  const [passwordSuccess, setPasswordSuccess] = useState(false);
  const [passwordError, setPasswordError] = useState<unknown>(null);

  const {
    register: registerProfile,
    handleSubmit: handleProfileSubmit,
    formState: { errors: profileErrors, isSubmitting: isProfileSubmitting },
  } = useForm<ProfileFormData>({
    resolver: zodResolver(profileSchema),
    defaultValues: {
      displayName: user?.displayName || '',
    },
  });

  const {
    register: registerPassword,
    handleSubmit: handlePasswordSubmit,
    reset: resetPasswordForm,
    formState: { errors: passwordErrors, isSubmitting: isPasswordSubmitting },
  } = useForm<PasswordFormData>({
    resolver: zodResolver(passwordSchema),
  });

  const onUpdateProfile = async (data: ProfileFormData) => {
    setProfileError(null);
    setProfileSuccess(false);
    try {
      const updated = await authApi.updateProfile(data.displayName);
      updateUser({ displayName: updated.displayName });
      setProfileSuccess(true);
    } catch (err) {
      setProfileError(err);
    }
  };

  const onChangePassword = async (data: PasswordFormData) => {
    setPasswordError(null);
    setPasswordSuccess(false);
    try {
      await authApi.changePassword({
        currentPassword: data.currentPassword,
        newPassword: data.newPassword,
      });
      setPasswordSuccess(true);
      resetPasswordForm();
      // Synchronize across tabs
      sessionCoordinator.broadcast({ type: 'PASSWORD_CHANGED' });
    } catch (err) {
      setPasswordError(err);
    }
  };

  return (
    <div className="container mx-auto px-4 py-8 max-w-4xl space-y-8">
      <div>
        <h1 className="font-heading text-3xl font-bold text-foreground">Hồ sơ cá nhân</h1>
        <p className="text-sm text-foreground-muted mt-1">
          Quản lý thông tin tài khoản và thiết lập bảo mật.
        </p>
      </div>

      <div className="grid grid-cols-1 md:grid-cols-3 gap-6">
        {/* Left: User Overview */}
        <Card className="p-6 h-fit space-y-4">
          <div className="flex items-center gap-3">
            <div className="w-12 h-12 rounded-full bg-primary/10 text-primary flex items-center justify-center font-bold text-lg">
              {user?.displayName?.charAt(0).toUpperCase() || 'U'}
            </div>
            <div className="truncate">
              <h3 className="font-semibold text-foreground truncate">{user?.displayName}</h3>
              <p className="text-xs text-foreground-muted truncate">{user?.email}</p>
            </div>
          </div>

          <div className="pt-2 border-t border-border space-y-2">
            <p className="text-xs font-semibold uppercase text-foreground-muted tracking-wider">
              Vai trò tài khoản
            </p>
            <div className="flex flex-wrap gap-1.5">
              {user?.roles?.map((role) => (
                <Badge key={role} variant={role === 'Admin' ? 'default' : role === 'Staff' ? 'warning' : 'neutral'}>
                  <Shield className="w-3 h-3 mr-1" />
                  {role}
                </Badge>
              ))}
            </div>
          </div>
        </Card>

        {/* Right: Update Profile & Change Password */}
        <div className="md:col-span-2 space-y-6">
          {/* Edit Display Name */}
          <Card className="p-6 space-y-4">
            <div className="flex items-center gap-2 border-b border-border pb-3">
              <User className="w-5 h-5 text-primary" />
              <h2 className="font-heading text-xl font-bold">Thông tin tài khoản</h2>
            </div>

            {profileSuccess && (
              <Alert variant="success" title="Thành công">
                Họ tên hiển thị đã được cập nhật.
              </Alert>
            )}
            <ErrorAlert error={profileError} />

            <form onSubmit={handleProfileSubmit(onUpdateProfile)} className="space-y-4">
              <Input label="Email đăng nhập" value={user?.email || ''} disabled readOnly />

              <Input
                label="Họ và tên hiển thị"
                error={profileErrors.displayName?.message}
                {...registerProfile('displayName')}
              />

              <Button type="submit" variant="primary" isLoading={isProfileSubmitting}>
                Lưu thay đổi
              </Button>
            </form>
          </Card>

          {/* Change Password */}
          <Card className="p-6 space-y-4">
            <div className="flex items-center gap-2 border-b border-border pb-3">
              <KeyRound className="w-5 h-5 text-primary" />
              <h2 className="font-heading text-xl font-bold">Đổi mật khẩu</h2>
            </div>

            {passwordSuccess && (
              <Alert variant="success" title="Đã đổi mật khẩu">
                Mật khẩu của bạn đã được thay đổi an toàn. Các phiên đăng nhập trên các tab khác đã được đồng bộ.
              </Alert>
            )}
            <ErrorAlert error={passwordError} />

            <form onSubmit={handlePasswordSubmit(onChangePassword)} className="space-y-4">
              <Input
                label="Mật khẩu hiện tại"
                type="password"
                error={passwordErrors.currentPassword?.message}
                {...registerPassword('currentPassword')}
              />

              <Input
                label="Mật khẩu mới (tối thiểu 8 ký tự)"
                type="password"
                error={passwordErrors.newPassword?.message}
                {...registerPassword('newPassword')}
              />

              <Input
                label="Xác nhận mật khẩu mới"
                type="password"
                error={passwordErrors.confirmNewPassword?.message}
                {...registerPassword('confirmNewPassword')}
              />

              <Button type="submit" variant="primary" isLoading={isPasswordSubmitting}>
                Đổi mật khẩu
              </Button>
            </form>
          </Card>
        </div>
      </div>
    </div>
  );
};
