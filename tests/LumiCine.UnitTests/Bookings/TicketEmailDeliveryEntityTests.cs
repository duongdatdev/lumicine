using LumiCine.Domain.Bookings;

namespace LumiCine.UnitTests.Bookings;

public sealed class TicketEmailDeliveryEntityTests
{
    private static readonly DateTime Now = DateTime.UtcNow;

    [Fact]
    public void New_delivery_is_pending_and_due_immediately()
    {
        var delivery = NewDelivery();

        Assert.Equal(TicketEmailDeliveryStatus.Pending, delivery.Status);
        Assert.Equal(0, delivery.AttemptCount);
        Assert.Equal(Now, delivery.NextAttemptAtUtc);
        Assert.Null(delivery.LeaseUntilUtc);
        Assert.Null(delivery.SentAtUtc);
    }

    [Fact]
    public void Lease_blocks_a_second_worker_until_it_expires()
    {
        var delivery = NewDelivery();

        Assert.True(delivery.TryAcquireLease(Now, TimeSpan.FromMinutes(5)));
        Assert.False(delivery.TryAcquireLease(Now.AddMinutes(4), TimeSpan.FromMinutes(5)));
        Assert.True(delivery.TryAcquireLease(Now.AddMinutes(5), TimeSpan.FromMinutes(5)));
        Assert.Equal(Now.AddMinutes(10), delivery.LeaseUntilUtc);
    }

    [Fact]
    public void Sent_and_failed_deliveries_cannot_be_leased()
    {
        var sent = NewDelivery();
        sent.MarkSent(Now);
        Assert.False(sent.TryAcquireLease(Now, TimeSpan.FromMinutes(5)));

        var failed = NewDelivery();
        for (var attempt = 0; attempt < TicketEmailDelivery.MaxAttempts; attempt++)
            failed.ScheduleRetry(Now, "SMTP unavailable");
        Assert.Equal(TicketEmailDeliveryStatus.Failed, failed.Status);
        Assert.False(failed.TryAcquireLease(Now, TimeSpan.FromMinutes(5)));
    }

    [Fact]
    public void Retries_back_off_one_five_fifteen_and_sixty_minutes_then_fail()
    {
        var delivery = NewDelivery();

        delivery.ScheduleRetry(Now, "first");
        Assert.Equal(1, delivery.AttemptCount);
        Assert.Equal(Now.AddMinutes(1), delivery.NextAttemptAtUtc);
        Assert.Equal(TicketEmailDeliveryStatus.Pending, delivery.Status);

        delivery.ScheduleRetry(Now.AddMinutes(1), "second");
        Assert.Equal(Now.AddMinutes(6), delivery.NextAttemptAtUtc);

        delivery.ScheduleRetry(Now.AddMinutes(6), "third");
        Assert.Equal(Now.AddMinutes(21), delivery.NextAttemptAtUtc);

        delivery.ScheduleRetry(Now.AddMinutes(21), "fourth");
        Assert.Equal(Now.AddMinutes(81), delivery.NextAttemptAtUtc);
        Assert.Equal(TicketEmailDeliveryStatus.Pending, delivery.Status);

        delivery.ScheduleRetry(Now.AddMinutes(81), "fifth");
        Assert.Equal(5, delivery.AttemptCount);
        Assert.Equal(TicketEmailDeliveryStatus.Failed, delivery.Status);
        Assert.Null(delivery.NextAttemptAtUtc);
        Assert.Null(delivery.LeaseUntilUtc);
        Assert.Equal("fifth", delivery.LastError);
    }

    [Fact]
    public void Long_errors_are_rejected_and_retry_needs_pending_status()
    {
        var delivery = NewDelivery();
        Assert.Throws<ArgumentOutOfRangeException>(() => delivery.ScheduleRetry(Now, new string('e', 501)));

        delivery.MarkSent(Now);
        Assert.Throws<InvalidOperationException>(() => delivery.ScheduleRetry(Now, "late failure"));
        Assert.Throws<InvalidOperationException>(() => delivery.MarkSent(Now));
    }

    [Fact]
    public void Resend_resets_a_failed_or_sent_delivery_for_a_fresh_cycle()
    {
        var delivery = NewDelivery();
        delivery.ScheduleRetry(Now, "SMTP unavailable");
        delivery.ScheduleRetry(Now.AddMinutes(1), "SMTP unavailable");
        delivery.ScheduleRetry(Now.AddMinutes(6), "SMTP unavailable");
        delivery.ScheduleRetry(Now.AddMinutes(21), "SMTP unavailable");
        delivery.ScheduleRetry(Now.AddMinutes(81), "SMTP unavailable");

        delivery.ResetForResend(Now.AddHours(2));

        Assert.Equal(TicketEmailDeliveryStatus.Pending, delivery.Status);
        Assert.Equal(0, delivery.AttemptCount);
        Assert.Equal(Now.AddHours(2), delivery.NextAttemptAtUtc);
        Assert.Null(delivery.LastError);
        Assert.Null(delivery.SentAtUtc);
        Assert.Null(delivery.LeaseUntilUtc);
    }

    [Fact]
    public void New_delivery_requires_ids_and_utc_time()
    {
        Assert.Throws<ArgumentException>(() => new TicketEmailDelivery(Guid.Empty, Guid.NewGuid(), Now));
        Assert.Throws<ArgumentException>(() => new TicketEmailDelivery(Guid.NewGuid(), Guid.Empty, Now));
        Assert.Throws<ArgumentException>(() => new TicketEmailDelivery(Guid.NewGuid(), Guid.NewGuid(), DateTime.Now));
    }

    private static TicketEmailDelivery NewDelivery() => new(Guid.NewGuid(), Guid.NewGuid(), Now);
}
