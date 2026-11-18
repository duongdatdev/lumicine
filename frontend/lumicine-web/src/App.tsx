import React from 'react';
import { BrowserRouter, Routes, Route, Navigate } from 'react-router-dom';
import { QueryClientProvider } from '@tanstack/react-query';
import { queryClient } from './lib/queryClient';
import { AuthProvider } from './auth/AuthContext';

// Layouts
import { CustomerLayout } from './components/layout/CustomerLayout';
import { StaffLayout } from './components/layout/StaffLayout';
import { AdminLayout } from './components/layout/AdminLayout';
import { ProtectedRoute } from './components/layout/ProtectedRoute';

// Auth Features
import { LoginPage } from './features/auth/LoginPage';
import { RegisterPage } from './features/auth/RegisterPage';
import { ConfirmEmailPage } from './features/auth/ConfirmEmailPage';
import { ForgotPasswordPage } from './features/auth/ForgotPasswordPage';
import { ResetPasswordPage } from './features/auth/ResetPasswordPage';
import { ProfilePage } from './features/auth/ProfilePage';

// Catalog Features
import { HomePage } from './features/catalog/HomePage';
import { MovieDetailPage } from './features/catalog/MovieDetailPage';
import { CinemasPage } from './features/catalog/CinemasPage';

// Booking & Tickets Features
import { ShowtimeSeatSelectionPage } from './features/booking/ShowtimeSeatSelectionPage';
import { BookingCheckoutPage } from './features/booking/BookingCheckoutPage';
import { PaymentReturnPage } from './features/payments/PaymentReturnPage';
import { TicketsListPage } from './features/tickets/TicketsListPage';
import { TicketDetailPage } from './features/tickets/TicketDetailPage';

// Discovery AI Features
import { DiscoveryChatPage } from './features/discovery/DiscoveryChatPage';

// Staff Features
import { StaffSchedulePage } from './features/staff/StaffSchedulePage';
import { StaffCheckInPage } from './features/staff/StaffCheckInPage';
import { StaffCatalogPage } from './features/staff/StaffCatalogPage';

// Admin Features
import { AdminDashboardPage } from './features/admin/AdminDashboardPage';
import { AdminGenresPage } from './features/admin/AdminGenresPage';
import { AdminMoviesPage } from './features/admin/AdminMoviesPage';
import { AdminCinemasPage } from './features/admin/AdminCinemasPage';
import { AdminRoomSeatsPage } from './features/admin/AdminRoomSeatsPage';
import { AdminShowtimesPage } from './features/admin/AdminShowtimesPage';
import { AdminUsersPage } from './features/admin/AdminUsersPage';
import { AdminPaymentsPage } from './features/admin/AdminPaymentsPage';
import { AdminDiscoveryPage } from './features/admin/AdminDiscoveryPage';

export function App() {
  return (
    <QueryClientProvider client={queryClient}>
      <AuthProvider>
        <BrowserRouter>
          <Routes>
            {/* Customer Routes */}
            <Route path="/" element={<CustomerLayout />}>
              <Route index element={<HomePage />} />
              <Route path="movies" element={<HomePage />} />
              <Route path="movies/:id" element={<MovieDetailPage />} />
              <Route path="cinemas" element={<CinemasPage />} />
              <Route path="showtimes/:id/seats" element={<ShowtimeSeatSelectionPage />} />

              {/* Protected Customer Routes */}
              <Route
                path="bookings/:id"
                element={
                  <ProtectedRoute>
                    <BookingCheckoutPage />
                  </ProtectedRoute>
                }
              />
              <Route
                path="tickets"
                element={
                  <ProtectedRoute>
                    <TicketsListPage />
                  </ProtectedRoute>
                }
              />
              <Route
                path="tickets/:id"
                element={
                  <ProtectedRoute>
                    <TicketDetailPage />
                  </ProtectedRoute>
                }
              />
              <Route
                path="account/profile"
                element={
                  <ProtectedRoute>
                    <ProfilePage />
                  </ProtectedRoute>
                }
              />

              {/* Payment Return Handlers */}
              <Route path="payments/vnpay/return" element={<PaymentReturnPage />} />
              <Route path="payments/return" element={<PaymentReturnPage />} />

              {/* Discovery AI */}
              <Route path="discover" element={<DiscoveryChatPage />} />
              <Route path="discover/:id" element={<DiscoveryChatPage />} />

              {/* Auth Routes */}
              <Route path="account/login" element={<LoginPage />} />
              <Route path="account/register" element={<RegisterPage />} />
              <Route path="account/confirm-email" element={<ConfirmEmailPage />} />
              <Route path="account/forgot-password" element={<ForgotPasswordPage />} />
              <Route path="account/reset-password" element={<ResetPasswordPage />} />
            </Route>

            {/* Staff Routes */}
            <Route
              path="/staff"
              element={
                <ProtectedRoute requiredRole="Staff">
                  <StaffLayout />
                </ProtectedRoute>
              }
            >
              <Route index element={<Navigate to="/staff/schedules" replace />} />
              <Route path="schedules" element={<StaffSchedulePage />} />
              <Route path="checkin" element={<StaffCheckInPage />} />
              <Route path="catalog" element={<StaffCatalogPage />} />
            </Route>

            {/* Admin Routes */}
            <Route
              path="/admin"
              element={
                <ProtectedRoute requiredRole="Admin">
                  <AdminLayout />
                </ProtectedRoute>
              }
            >
              <Route index element={<Navigate to="/admin/dashboard" replace />} />
              <Route path="dashboard" element={<AdminDashboardPage />} />
              <Route path="genres" element={<AdminGenresPage />} />
              <Route path="movies" element={<AdminMoviesPage />} />
              <Route path="cinemas" element={<AdminCinemasPage />} />
              <Route path="rooms/:id/seats" element={<AdminRoomSeatsPage />} />
              <Route path="showtimes" element={<AdminShowtimesPage />} />
              <Route path="users" element={<AdminUsersPage />} />
              <Route path="payments" element={<AdminPaymentsPage />} />
              <Route path="discovery" element={<AdminDiscoveryPage />} />
            </Route>

            {/* Catch-all 404 */}
            <Route path="*" element={<Navigate to="/" replace />} />
          </Routes>
        </BrowserRouter>
      </AuthProvider>
    </QueryClientProvider>
  );
}

export default App;
