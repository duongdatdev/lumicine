using LumiCine.Domain.Scheduling;

namespace LumiCine.UnitTests.Scheduling;

public sealed class ShowtimeLifecycleTests
{
    [Fact]
    public void Reschedule_preserves_snapshotted_runtime_and_updates_time_and_price()
    {
        var now = DateTime.UtcNow;
        var showtime = new Showtime(Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), now.AddDays(1), now.AddDays(1).AddMinutes(125), 100, now);
        var newStart = now.AddDays(2);

        showtime.Reschedule(newStart, 150, now);

        Assert.Equal(newStart, showtime.StartAtUtc);
        Assert.Equal(newStart.AddMinutes(125), showtime.EndAtUtc);
        Assert.Equal(150, showtime.BasePrice);
        Assert.Equal(ShowtimeStatus.Scheduled, showtime.Status);
    }

    [Fact]
    public void Cancel_changes_status_without_rewriting_time()
    {
        var now = DateTime.UtcNow;
        var start = now.AddDays(1);
        var showtime = new Showtime(Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), start, start.AddHours(2), 100, now);

        showtime.Cancel(now);

        Assert.Equal(ShowtimeStatus.Cancelled, showtime.Status);
        Assert.Equal(start, showtime.StartAtUtc);
        Assert.Equal(start.AddHours(2), showtime.EndAtUtc);
    }

    [Fact]
    public void Lifecycle_guards_use_the_supplied_utc_clock()
    {
        var fixedNow = new DateTime(2030, 5, 1, 12, 0, 0, DateTimeKind.Utc);
        var upcomingStart = fixedNow.AddHours(1);
        var upcoming = new Showtime(Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), upcomingStart,
            upcomingStart.AddHours(2), 100, fixedNow.AddDays(-1));
        upcoming.Reschedule(fixedNow.AddHours(3), 120, fixedNow);
        Assert.Equal(fixedNow.AddHours(3), upcoming.StartAtUtc);

        var startedStart = fixedNow.AddMinutes(-1);
        var started = new Showtime(Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), startedStart,
            startedStart.AddHours(2), 100, fixedNow.AddDays(-1));
        Assert.Throws<InvalidOperationException>(() => started.Reschedule(fixedNow.AddHours(1), 120, fixedNow));
        Assert.Throws<InvalidOperationException>(() => started.Cancel(fixedNow));

        var pastTarget = new Showtime(Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), upcomingStart,
            upcomingStart.AddHours(2), 100, fixedNow.AddDays(-1));
        Assert.Throws<InvalidOperationException>(() => pastTarget.Reschedule(fixedNow.AddMinutes(-1), 120, fixedNow));
    }
}
