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
}
