using LumiCine.Domain.Bookings;

namespace LumiCine.UnitTests.Bookings;

public sealed class CheckInLogEntityTests
{
    private static readonly DateTime Now = DateTime.UtcNow;

    [Fact]
    public void Successful_check_ins_must_reference_their_ticket()
    {
        Assert.Throws<ArgumentException>(() =>
            new CheckInLog(Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), Now, CheckInLogResult.Success));
    }

    [Fact]
    public void Rejected_scans_may_log_the_attempted_code_without_a_ticket()
    {
        var log = new CheckInLog(Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), Now,
            CheckInLogResult.TicketNotFound, attemptedTicketCode: "some-scanned-code");

        Assert.Equal(CheckInLogResult.TicketNotFound, log.Result);
        Assert.Equal("some-scanned-code", log.AttemptedTicketCode);
        Assert.Null(log.TicketId);
    }

    [Fact]
    public void Logs_require_ids_result_and_utc_time()
    {
        Assert.Throws<ArgumentException>(() => NewLog(id: Guid.Empty));
        Assert.Throws<ArgumentException>(() => NewLog(staffUserId: Guid.Empty));
        Assert.Throws<ArgumentException>(() => NewLog(requestId: Guid.Empty));
        Assert.Throws<ArgumentException>(() => NewLog(loggedAtUtc: DateTime.Now));
        Assert.Throws<ArgumentOutOfRangeException>(() => NewLog(attemptedCode: new string('x', 61)));
    }

    private static CheckInLog NewLog(
        Guid? id = null,
        Guid? staffUserId = null,
        Guid? requestId = null,
        DateTime? loggedAtUtc = null,
        string? attemptedCode = null) =>
        new(id ?? Guid.NewGuid(), staffUserId ?? Guid.NewGuid(), requestId ?? Guid.NewGuid(),
            loggedAtUtc ?? Now, CheckInLogResult.DuplicateScan, ticketId: Guid.NewGuid(),
            bookingId: Guid.NewGuid(), showtimeId: Guid.NewGuid(), attemptedTicketCode: attemptedCode);
}
