using LumiCine.Application.Common;
using LumiCine.Application.Scheduling;
using LumiCine.Domain.Scheduling;
using LumiCine.Infrastructure.Scheduling;
using LumiCine.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
namespace LumiCine.IntegrationTests.Persistence;

public sealed partial class ShowtimeServiceTests
{
    [Fact]
    public async Task Reschedule_updates_price_and_preserves_snapshotted_runtime()
    {
        await using var db = fixture.CreateDbContext();
        var (movie, _, room) = await DatabaseTestData.CreateBaseAsync(db);
        await DatabaseTestData.CreateSeatAsync(db, room.Id);
        var service = new ShowtimeService(db, TimeProvider.System);
        var start = DateTimeOffset.UtcNow.AddDays(4);
        var created = await service.CreateAsync(new(movie.Id, room.Id, start.ToString("O"), 125), default);
        var newStart = start.AddDays(1);
        var updated = await service.RescheduleAsync(created.Id, new(newStart.ToString("O"), 150), default);
        Assert.NotNull(updated);
        Assert.InRange(Math.Abs((newStart.UtcDateTime - updated.StartAtUtc).Ticks), 0, 10);
        Assert.Equal(movie.DurationMinutes, (updated.EndAtUtc - updated.StartAtUtc).TotalMinutes);
        Assert.All(await db.ShowtimeSeats.Where(x => x.ShowtimeId == created.Id).ToListAsync(), x => Assert.Equal(150, x.Price));
    }

[Fact]
    public async Task Booking_without_items_blocks_reschedule_and_cancellation()
    {
        await using var db = fixture.CreateDbContext();
        var (movie, _, room) = await DatabaseTestData.CreateBaseAsync(db);
        await DatabaseTestData.CreateSeatAsync(db, room.Id);
        var service = new ShowtimeService(db, TimeProvider.System);
        var start = DateTimeOffset.UtcNow.AddDays(7);
        var created = await service.CreateAsync(new(movie.Id, room.Id, start.ToString("O"), 100), default);
        await DatabaseTestData.CreateBookingAsync(db, created.Id);

        var reschedule = await Assert.ThrowsAsync<ApiException>(() => service.RescheduleAsync(created.Id,
            new(start.AddDays(1).ToString("O"), 100), default));
        var cancel = await Assert.ThrowsAsync<ApiException>(() => service.CancelAsync(created.Id, default));

        Assert.Equal(409, reschedule.StatusCode);
        Assert.Equal(409, cancel.StatusCode);
    }
}
