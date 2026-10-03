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
    public async Task Public_filter_rejects_out_of_range_dates_and_page_offsets()
    {
        await using var db = fixture.CreateDbContext();
        var service = new ShowtimeService(db, TimeProvider.System);
        foreach (var date in new[] { DateOnly.MinValue, DateOnly.MaxValue })
        {
            var error = await Assert.ThrowsAsync<ApiException>(() => service.GetPublicAsync(new ShowtimeQuery(null, null, date, new()), default));
            Assert.Equal(400, error.StatusCode);
        }
        var pageError = await Assert.ThrowsAsync<ApiException>(() => service.GetPublicAsync(
            new ShowtimeQuery(null, null, null, new(int.MaxValue, 100)), default));
        Assert.Equal(400, pageError.StatusCode);
    }

[Fact]
    public async Task Utc_plus_seven_day_filter_includes_midnight_and_excludes_it_from_previous_day()
    {
        await using var seed = fixture.CreateDbContext();
        var (movie, _, room) = await DatabaseTestData.CreateBaseAsync(seed);
        await DatabaseTestData.CreateSeatAsync(seed, room.Id);
        var now = new DateTimeOffset(2026, 11, 3, 0, 0, 0, TimeSpan.Zero);
        var clock = new FixedClock(now);
        var date = DateOnly.FromDateTime(now.UtcDateTime.AddHours(7)).AddDays(3);
        var midnightUtc = DateTime.SpecifyKind(date.ToDateTime(TimeOnly.MinValue).AddHours(-7), DateTimeKind.Utc);
        var showtimes = new ShowtimeService(seed, clock);
        var created = await showtimes.CreateAsync(new(movie.Id, room.Id, midnightUtc.ToString("O"), 90), default);

        var onDate = await showtimes.GetPublicAsync(new ShowtimeQuery(movie.Id, null, date, new()), default);
        var previousDate = await showtimes.GetPublicAsync(new ShowtimeQuery(movie.Id, null, date.AddDays(-1), new()), default);

        Assert.Contains(onDate.Items, item => item.Id == created.Id);
        Assert.DoesNotContain(previousDate.Items, item => item.Id == created.Id);
    }
}
