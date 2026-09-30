using LumiCine.Application.Cinemas;
using LumiCine.Application.Common;
using LumiCine.Domain.Scheduling;
using LumiCine.Infrastructure.Cinemas;
using LumiCine.IntegrationTests.Persistence;
using Microsoft.EntityFrameworkCore;

namespace LumiCine.IntegrationTests.Cinemas;

public sealed partial class CinemaServiceTests
{
    [Fact]
    public async Task Layout_can_be_replaced_before_first_snapshot_and_is_pageable()
    {
        await using var db = Fixture.CreateDbContext();
        var (_, _, room) = await DatabaseTestData.CreateBaseAsync(db);
        var service = new CinemaService(db, TimeProvider.System);
        await service.ReplaceSeatLayoutAsync(room.Id, [new("A", 2)], default);
        await service.ReplaceSeatLayoutAsync(room.Id, [new("A", 1), new("B", 2, 1)], default);

        var firstPage = await service.GetAdminSeatsAsync(room.Id, new PageQuery(1, 2), default);
        Assert.NotNull(firstPage);
        Assert.Equal(4, firstPage.TotalCount);
        Assert.Equal(2, firstPage.Items.Count);
        Assert.Empty((await service.GetAdminSeatsAsync(room.Id, new PageQuery(int.MaxValue, 100), default))!.Items);
    }

    [Fact]
    public async Task Layout_replacement_is_blocked_after_any_historical_snapshot()
    {
        await using var db = Fixture.CreateDbContext();
        var (movie, _, room) = await DatabaseTestData.CreateBaseAsync(db);
        var service = new CinemaService(db, TimeProvider.System);
        await service.ReplaceSeatLayoutAsync(room.Id, [new("A", 1)], default);
        var seat = await db.Seats.SingleAsync(x => x.RoomId == room.Id);
        var showtime = await DatabaseTestData.CreateShowtimeAsync(db, movie.Id, room.Id);
        await DatabaseTestData.CreateShowtimeSeatAsync(db, showtime, seat);

        var error = await Assert.ThrowsAsync<ApiException>(() => service.ReplaceSeatLayoutAsync(room.Id, [new("B", 1)], default));
        Assert.Equal(409, error.StatusCode);
    }

    [Fact]
    public async Task Layout_validation_rejects_null_rows_and_normalized_duplicates()
    {
        await using var db = Fixture.CreateDbContext();
        var service = new CinemaService(db, TimeProvider.System);

        Assert.Equal(400, (await Assert.ThrowsAsync<ApiException>(() => service.ReplaceSeatLayoutAsync(Guid.NewGuid(), null!, default))).StatusCode);
        Assert.Equal(400, (await Assert.ThrowsAsync<ApiException>(() => service.ReplaceSeatLayoutAsync(Guid.NewGuid(), [new("a", 1), new(" A ", 1)], default))).StatusCode);
    }
}
