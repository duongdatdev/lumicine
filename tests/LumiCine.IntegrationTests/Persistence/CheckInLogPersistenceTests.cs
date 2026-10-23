using LumiCine.Domain.Bookings;
using LumiCine.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace LumiCine.IntegrationTests.Persistence;

[Collection(PostgresDatabaseCollection.Name)]
public sealed class CheckInLogPersistenceTests(PostgresDatabaseFixture fixture)
{
    [Fact]
    public async Task Staff_request_pairs_are_unique()
    {
        await using var db = fixture.CreateDbContext();
        var staffId = await CreateUserAsync(db);
        var log = NewLog(staffId);
        db.CheckInLogs.Add(log);
        await db.SaveChangesAsync();

        var duplicate = NewLog(staffId, requestId: log.RequestId, result: CheckInLogResult.TicketNotFound);
        db.CheckInLogs.Add(duplicate);
        await Assert.ThrowsAsync<DbUpdateException>(() => db.SaveChangesAsync());
        db.Entry(duplicate).State = EntityState.Detached;

        var sameStaffDifferentRequest = NewLog(staffId);
        db.CheckInLogs.Add(sameStaffDifferentRequest);
        await db.SaveChangesAsync();

        var otherStaffSameRequest = NewLog(await CreateUserAsync(db), requestId: log.RequestId);
        db.CheckInLogs.Add(otherStaffSameRequest);
        await db.SaveChangesAsync();
        Assert.Equal(3, await db.CheckInLogs.CountAsync(x => x.StaffUserId == staffId || x.RequestId == log.RequestId));
    }

    [Fact]
    public async Task Rejected_scans_persist_without_a_ticket()
    {
        await using var db = fixture.CreateDbContext();
        var staffId = await CreateUserAsync(db);
        var log = NewLog(staffId, result: CheckInLogResult.TicketNotFound, attemptedCode: "not-a-real-code");
        db.CheckInLogs.Add(log);
        await db.SaveChangesAsync();

        var stored = await db.CheckInLogs.AsNoTracking().SingleAsync(x => x.Id == log.Id);
        Assert.Equal(CheckInLogResult.TicketNotFound, stored.Result);
        Assert.Equal("not-a-real-code", stored.AttemptedTicketCode);
        Assert.Null(stored.TicketId);
    }

    private static CheckInLog NewLog(
        Guid staffUserId,
        Guid? requestId = null,
        CheckInLogResult result = CheckInLogResult.DuplicateScan,
        string? attemptedCode = null) =>
        new(Guid.NewGuid(), staffUserId, requestId ?? Guid.NewGuid(), DateTime.UtcNow, result,
            attemptedTicketCode: attemptedCode);

    private static async Task<Guid> CreateUserAsync(LumiCineDbContext db)
    {
        var userId = Guid.NewGuid();
        var email = $"{userId:N}@example.com";
        db.Users.Add(new LumiCine.Infrastructure.Identity.ApplicationUser
        {
            Id = userId, Email = email, NormalizedEmail = email.ToUpperInvariant(),
            EmailConfirmed = true, IsActive = true, DisplayName = "Check-in test"
        });
        await db.SaveChangesAsync();
        return userId;
    }
}
