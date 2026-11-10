using LumiCine.Application.Common;
using LumiCine.Application.Payments;
using LumiCine.Application.Scheduling;
using LumiCine.Domain.Bookings;
using LumiCine.Domain.Scheduling;
using LumiCine.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Npgsql;

namespace LumiCine.Infrastructure.Payments;

public sealed class PaymentService(
    LumiCineDbContext db,
    TimeProvider clock,
    IVnPayMessageService messages,
    IVnPayTransactionQueryClient queries,
    IShowtimeAvailabilityNotifier notifier,
    ILogger<PaymentService> logger) : IPaymentService
{
    public async Task<BookingPaymentLink> CreateBookingPaymentAsync(
        Guid userId, Guid bookingId, CreateBookingPaymentRequest request, string clientIpAddress, CancellationToken ct)
    {
        ValidateInput(userId, bookingId, request, clientIpAddress);
        await using var tx = await db.Database.BeginTransactionAsync(ct);
        try
        {
            var hint = await db.Bookings.AsNoTracking().Where(x => x.Id == bookingId && x.UserId == userId)
                .Select(x => new { x.ShowtimeId }).SingleOrDefaultAsync(ct);
            if (hint is null)
                throw new ApiException(404, "Booking was not found.");

            // Lock the showtime first so a concurrent expiry or cancellation cannot interleave
            // with the payment attempt on the same booking.
            await db.LockRowAsync("Showtimes", hint.ShowtimeId, ct);
            await db.LockRowAsync("Bookings", bookingId, ct);
            var booking = await db.Bookings.SingleAsync(x => x.Id == bookingId && x.UserId == userId, ct);
            var now = UtcNow;
            if (booking.Status != BookingStatus.Pending)
                throw new ApiException(409, "Only pending bookings can be paid for.");
            if (booking.HoldExpiresAtUtc is not DateTime expiresAt)
                throw new ApiException(409, "This booking has no hold deadline and cannot be paid for online.");
            if (now >= expiresAt)
                throw new ApiException(409, "The booking hold has expired.");
            if (booking.TotalAmount <= 0)
                throw new ApiException(400, "Only bookings with a positive total can be paid online.");

            var seatRows = await (from item in db.BookingItems
                                  join seat in db.ShowtimeSeats on item.ShowtimeSeatId equals seat.Id
                                  where item.BookingId == bookingId
                                  select new { seat.Status, seat.CurrentBookingId }).ToListAsync(ct);
            if (seatRows.Count == 0 || seatRows.Any(x => x.Status != ShowtimeSeatStatus.Held || x.CurrentBookingId != bookingId))
                throw new ApiException(409, "The booking no longer owns all of its seats.");

            var pending = await db.Payments.SingleOrDefaultAsync(x => x.BookingId == bookingId && x.Status == PaymentStatus.Pending, ct);
            if (pending is not null)
            {
                await tx.CommitAsync(ct);
                return new BookingPaymentLink(bookingId, pending.Id, pending.RequestId, pending.PaymentUrl!, expiresAt);
            }

            var previous = await db.Payments.SingleOrDefaultAsync(x => x.BookingId == bookingId && x.RequestId == request.RequestId, ct);
            if (previous is not null && previous.Status == PaymentStatus.Succeeded)
                throw new ApiException(409, "This payment request already succeeded.");
            if (await db.Payments.AnyAsync(x => x.BookingId == bookingId && x.Status == PaymentStatus.Succeeded, ct))
                throw new ApiException(409, "A payment for this booking already succeeded.");

            // The requestId is unique per booking, so a retry after a failed attempt gets a
            // fresh identifier while the caller's requestId stays reserved for the replay check.
            var paymentRequestId = previous is null ? request.RequestId : Guid.NewGuid();
            var paymentId = Guid.NewGuid();
            var merchantReference = $"{booking.Code}.{paymentId.ToString("N")[..12]}";
            var payment = new Payment(paymentId, bookingId, paymentRequestId, merchantReference,
                booking.TotalAmount, "VND", expiresAt, now);
            var url = messages.BuildPaymentUrl(new VnPayPaymentRequest(
                merchantReference, $"Ve xem phim {booking.Code}", booking.TotalAmount, now, expiresAt, clientIpAddress));
            payment.SetPaymentUrl(url, now);
            db.Payments.Add(payment);
            await db.SaveChangesAsync(ct);
            await tx.CommitAsync(ct);
            return new BookingPaymentLink(bookingId, paymentId, paymentRequestId, url, expiresAt);
        }
        catch (DbUpdateException ex) when (FindPostgres(ex)?.SqlState == PostgresErrorCodes.UniqueViolation)
        {
            throw new ApiException(409, "The payment request conflicts with a concurrent payment.");
        }
    }

    public async Task<PaymentView> GetOwnedAsync(Guid userId, Guid paymentId, CancellationToken ct)
    {
        var payment = await (from row in db.Payments.AsNoTracking()
                             join booking in db.Bookings.AsNoTracking() on row.BookingId equals booking.Id
                             where row.Id == paymentId && booking.UserId == userId
                             select row).SingleOrDefaultAsync(ct);
        return payment is null ? throw new ApiException(404, "Payment was not found.") : ToView(payment);
    }

    public async Task<VnPayCallbackOutcome> ApplyVnPayCallbackAsync(
        IReadOnlyDictionary<string, IReadOnlyList<string>> parameters, CancellationToken ct)
    {
        VnPayCallbackData data;
        try
        {
            data = messages.ValidateCallback(parameters);
        }
        catch (VnPayCallbackException ex)
        {
            return new VnPayCallbackOutcome(ex.RspCode, ex.Message, null);
        }

        await using var tx = await db.Database.BeginTransactionAsync(ct);
        try
        {
            var payment = await db.Payments.SingleOrDefaultAsync(x => x.MerchantReference == data.TransactionReference, ct);
            if (payment is null)
                return new VnPayCallbackOutcome(VnPayResponseCodes.TransactionNotFound, "Transaction not found.", null);
            var view = ToView(payment);
            if (data.AmountMinor != VnPayAmount.ToMinorUnits(payment.Amount))
                return new VnPayCallbackOutcome(VnPayResponseCodes.InvalidAmount, "Invalid amount.", view);

            return await ApplyResultAsync(payment, data, ct);
        }
        catch (DbUpdateException ex) when (FindPostgres(ex)?.SqlState == PostgresErrorCodes.UniqueViolation)
        {
            throw new ApiException(409, "The payment result conflicts with a concurrent change.");
        }
    }

    public async Task<VnPayCallbackOutcome> ProcessVnPayReturnAsync(IReadOnlyDictionary<string, IReadOnlyList<string>> parameters, CancellationToken ct)
    {
        VnPayCallbackData data;
        try
        {
            data = messages.ValidateCallback(parameters);
        }
        catch (VnPayCallbackException ex)
        {
            return new VnPayCallbackOutcome(ex.RspCode, ex.Message, null);
        }

        var payment = await db.Payments.AsNoTracking().SingleOrDefaultAsync(x => x.MerchantReference == data.TransactionReference, ct);
        if (payment is null)
            return new VnPayCallbackOutcome(VnPayResponseCodes.TransactionNotFound, "Transaction not found.", null);

        var view = ToView(payment);
        if (data.AmountMinor != VnPayAmount.ToMinorUnits(payment.Amount))
            return new VnPayCallbackOutcome(VnPayResponseCodes.InvalidAmount, "Invalid amount.", view);

        if (payment.Status != PaymentStatus.Pending)
            return new VnPayCallbackOutcome(VnPayResponseCodes.AlreadyProcessed, "Order already processed.", view);

        return new VnPayCallbackOutcome(data.ResponseCode, "Return callback verified.", view);
    }

    private async Task<VnPayCallbackOutcome> ApplyResultAsync(Payment payment, VnPayCallbackData data, CancellationToken ct)
    {
        var succeeded = data.ResponseCode == VnPayResponseCodes.Success && data.TransactionStatus == VnPayResponseCodes.Success;
        if (!succeeded)
        {
            // Failure callbacks arriving after a confirmed success never downgrade the payment.
            if (payment.Status != PaymentStatus.Pending)
                return new VnPayCallbackOutcome(VnPayResponseCodes.AlreadyProcessed, "Order already processed.", ToView(payment));

            payment.MarkFailed(UtcNow, data.ResponseCode, data.TransactionStatus);
            await db.SaveChangesAsync(ct);
            await db.Database.CurrentTransaction!.CommitAsync(ct);
            return new VnPayCallbackOutcome(VnPayResponseCodes.Success, "Confirm Success.", ToView(payment));
        }

        return await ApplySuccessAsync(payment, data, ct);
    }

    private async Task<VnPayCallbackOutcome> ApplySuccessAsync(Payment payment, VnPayCallbackData data, CancellationToken ct)
    {
        await db.Entry(payment).ReloadAsync(ct);
        if (payment.Status == PaymentStatus.Succeeded)
            return new VnPayCallbackOutcome(VnPayResponseCodes.AlreadyProcessed, "Order already processed.", ToView(payment));

        var booking = await db.Bookings.SingleAsync(x => x.Id == payment.BookingId, ct);
        // Lock order Showtime → Booking → Payment mirrors cancellation and expiry so that a
        // callback racing the expiration worker produces exactly one committed outcome.
        await db.LockRowAsync("Showtimes", booking.ShowtimeId, ct);
        await db.LockRowAsync("Bookings", booking.Id, ct);
        await db.LockRowAsync("Payments", payment.Id, ct);
        await db.Entry(payment).ReloadAsync(ct);
        await db.Entry(booking).ReloadAsync(ct);
        if (payment.Status == PaymentStatus.Succeeded)
            return new VnPayCallbackOutcome(VnPayResponseCodes.AlreadyProcessed, "Order already processed.", ToView(payment));

        var now = UtcNow;
        var seats = await db.ShowtimeSeats.Where(x => x.ShowtimeId == booking.ShowtimeId && x.CurrentBookingId == booking.Id)
            .ToListAsync(ct);
        var items = await db.BookingItems.Where(x => x.BookingId == booking.Id).ToListAsync(ct);
        var canFulfill = booking.Status == BookingStatus.Pending &&
            booking.HoldExpiresAtUtc is DateTime expiry && now < expiry &&
            items.Count > 0 && seats.Count == items.Count &&
            seats.All(x => x.Status == ShowtimeSeatStatus.Held);

        if (canFulfill)
        {
            payment.MarkSucceeded(now, data.TransactionId, data.ResponseCode, data.TransactionStatus, PaymentProcessingStatus.Fulfilled);
            booking.Confirm(now);
            foreach (var seat in seats) seat.Sell(booking.Id);
            foreach (var item in items)
                db.Tickets.Add(new Ticket(Guid.NewGuid(), item.Id, TicketCode.New(), now, now));
            db.TicketEmailDeliveries.Add(new TicketEmailDelivery(Guid.NewGuid(), booking.Id, now));
        }
        else
        {
            // The money arrived after the booking could no longer be honoured, so record it for
            // a manual refund review instead of touching the booking.
            payment.MarkSucceeded(now, data.TransactionId, data.ResponseCode, data.TransactionStatus, PaymentProcessingStatus.RequiresRefundReview);
        }
        await db.SaveChangesAsync(ct);
        await db.Database.CurrentTransaction!.CommitAsync(ct);
        if (canFulfill)
            await NotifySeatsAsync(booking.ShowtimeId, ct);
        return new VnPayCallbackOutcome(VnPayResponseCodes.Success, "Confirm Success.", ToView(payment));
    }

    private async Task NotifySeatsAsync(Guid showtimeId, CancellationToken ct)
    {
        try
        {
            await notifier.NotifyAsync(showtimeId, ct);
        }
        catch (Exception ex)
        {
            // Realtime availability is best effort; the payment result is already committed.
            logger.LogWarning(ex, "Could not publish availability for showtime {ShowtimeId} after payment.", showtimeId);
        }
    }

    public async Task<VnPayReconcileResult> ReconcileAsync(Guid paymentId, CancellationToken ct)
    {
        await using var tx = await db.Database.BeginTransactionAsync(ct);
        try
        {
            var payment = await db.Payments.SingleOrDefaultAsync(x => x.Id == paymentId, ct)
                ?? throw new ApiException(404, "Payment was not found.");
            var query = await queries.QueryAsync(payment.MerchantReference, payment.Amount, payment.CreatedAtUtc, ct);
            var view = ToView(payment);
            if (query.AmountMinor is long amountMinor && amountMinor != VnPayAmount.ToMinorUnits(payment.Amount))
                return new VnPayReconcileResult(VnPayResponseCodes.InvalidAmount, "Invalid amount.", view);

            var data = new VnPayCallbackData(payment.MerchantReference,
                query.AmountMinor ?? VnPayAmount.ToMinorUnits(payment.Amount),
                query.ResponseCode, query.TransactionStatus ?? string.Empty, query.TransactionNo ?? string.Empty, string.Empty, null);
            var outcome = await ApplyResultAsync(payment, data, ct);
            return new VnPayReconcileResult(outcome.RspCode, outcome.Message, outcome.Payment ?? view);
        }
        catch (DbUpdateException ex) when (FindPostgres(ex)?.SqlState == PostgresErrorCodes.UniqueViolation)
        {
            throw new ApiException(409, "The payment result conflicts with a concurrent change.");
        }
    }

    public async Task<PagedResult<AdminPaymentView>> ListForAdminAsync(AdminPaymentQuery query, CancellationToken ct)
    {
        var page = query.Paging.Validate();
        var rows = db.Payments.AsNoTracking().AsQueryable();
        if (query.BookingId is Guid bookingId)
            rows = rows.Where(x => x.BookingId == bookingId);
        if (!string.IsNullOrWhiteSpace(query.Status))
        {
            if (!Enum.TryParse<PaymentStatus>(query.Status, true, out var status) ||
                !string.Equals(query.Status.Trim(), status.ToString(), StringComparison.OrdinalIgnoreCase) ||
                !Enum.IsDefined(status))
                throw new ApiException(400, "Status filter is invalid.");
            rows = rows.Where(x => x.Status == status);
        }
        if (!string.IsNullOrWhiteSpace(query.ProcessingStatus))
        {
            if (!Enum.TryParse<PaymentProcessingStatus>(query.ProcessingStatus, true, out var processing) ||
                !string.Equals(query.ProcessingStatus.Trim(), processing.ToString(), StringComparison.OrdinalIgnoreCase) ||
                !Enum.IsDefined(processing))
                throw new ApiException(400, "Processing status filter is invalid.");
            rows = rows.Where(x => x.ProcessingStatus == processing);
        }

        var total = await rows.CountAsync(ct);
        var offset = page.Offset;
        if (offset > int.MaxValue) throw new ApiException(400, "Page is too large.");
        var payments = await rows.OrderByDescending(x => x.CreatedAtUtc).ThenBy(x => x.Id)
            .Skip((int)offset).Take(page.PageSize).ToListAsync(ct);
        var bookings = await DescribeBookingsAsync(payments.Select(x => x.BookingId).Distinct().ToList(), ct);
        var items = payments.Select(payment => ToAdminView(payment, bookings)).ToArray();
        return new(items, total, page.Page, page.PageSize);
    }

    public async Task<AdminPaymentView> GetForAdminAsync(Guid paymentId, CancellationToken ct)
    {
        var payment = await db.Payments.AsNoTracking().SingleOrDefaultAsync(x => x.Id == paymentId, ct)
            ?? throw new ApiException(404, "Payment was not found.");
        var bookings = await DescribeBookingsAsync([payment.BookingId], ct);
        return ToAdminView(payment, bookings);
    }

    public async Task<AdminPaymentView> RecordRefundAsync(Guid adminUserId, Guid paymentId, RecordRefundRequest request, CancellationToken ct)
    {
        if (adminUserId == Guid.Empty)
            throw new ApiException(400, "A valid admin account is required.");
        if (request is null || string.IsNullOrWhiteSpace(request.Reference))
            throw new ApiException(400, "A refund reference is required.");
        if (request.Reference.Length > 200 || (request.Note?.Length ?? 0) > 500)
            throw new ApiException(400, "The refund reference or note is too long.");

        await using var tx = await db.Database.BeginTransactionAsync(ct);
        var payment = await db.Payments.SingleOrDefaultAsync(x => x.Id == paymentId, ct)
            ?? throw new ApiException(404, "Payment was not found.");
        try
        {
            payment.RecordRefund(UtcNow, adminUserId, request.Reference.Trim(), request.Note);
            await db.SaveChangesAsync(ct);
            await tx.CommitAsync(ct);
        }
        catch (InvalidOperationException)
        {
            throw new ApiException(409, "Only payments flagged for refund review can record a refund.");
        }
        catch (DbUpdateException ex) when (FindPostgres(ex)?.SqlState == PostgresErrorCodes.UniqueViolation)
        {
            throw new ApiException(409, "The refund record conflicts with a concurrent change.");
        }

        var bookings = await DescribeBookingsAsync([payment.BookingId], ct);
        return ToAdminView(payment, bookings);
    }

    private async Task<IReadOnlyDictionary<Guid, (string Code, string? Email)>> DescribeBookingsAsync(IReadOnlyList<Guid> bookingIds, CancellationToken ct)
    {
        if (bookingIds.Count == 0)
            return new Dictionary<Guid, (string, string?)>();
        var bookings = await db.Bookings.AsNoTracking()
            .Where(x => bookingIds.Contains(x.Id))
            .Select(x => new { x.Id, x.Code, x.UserId })
            .ToDictionaryAsync(x => x.Id, x => (x.Code, x.UserId));
        var userIds = bookings.Values.Where(x => x.UserId is Guid).Select(x => x.UserId!.Value).Distinct().ToList();
        var emails = userIds.Count == 0
            ? new Dictionary<Guid, string>()
            : await db.Users.AsNoTracking().Where(x => userIds.Contains(x.Id)).ToDictionaryAsync(x => x.Id, x => x.Email);
        return bookings.ToDictionary(
            x => x.Key,
            x => (x.Value.Code, x.Value.UserId is Guid id ? emails.GetValueOrDefault(id) : null));
    }

    private static AdminPaymentView ToAdminView(Payment payment, IReadOnlyDictionary<Guid, (string Code, string? Email)> bookings)
    {
        var booking = bookings.GetValueOrDefault(payment.BookingId);
        return new AdminPaymentView(ToView(payment), booking.Code, booking.Email);
    }

    private static PaymentView ToView(Payment payment) => new(
        payment.Id, payment.BookingId, payment.RequestId, payment.MerchantReference, payment.Amount,
        payment.Currency, payment.Status.ToString(), payment.ProcessingStatus.ToString(), payment.PaymentUrl,
        payment.CreatedAtUtc, payment.ExpiresAtUtc, payment.ProcessedAtUtc, payment.VnPayTransactionId,
        payment.ResponseCode, payment.TransactionStatus);

    private static void ValidateInput(Guid userId, Guid bookingId, CreateBookingPaymentRequest request, string clientIpAddress)
    {
        if (userId == Guid.Empty || bookingId == Guid.Empty || request is null || request.RequestId == Guid.Empty)
            throw new ApiException(400, "Valid account, booking, and request identifiers are required.");
        if (string.IsNullOrWhiteSpace(clientIpAddress))
            throw new ApiException(400, "A client IP address is required.");
    }

    private DateTime UtcNow => clock.GetUtcNow().UtcDateTime;

    private static PostgresException? FindPostgres(Exception ex)
    {
        for (Exception? current = ex; current is not null; current = current.InnerException)
            if (current is PostgresException postgres) return postgres;
        return null;
    }
}
