using LumiCine.Domain.Bookings;

namespace LumiCine.UnitTests.Bookings;

public sealed class TicketCodeTests
{
    [Fact]
    public void Codes_are_forty_three_url_safe_characters()
    {
        for (var i = 0; i < 20; i++)
        {
            var code = TicketCode.New();
            Assert.Matches("^[A-Za-z0-9_-]{43}$", code);
        }
    }

    [Fact]
    public void Codes_do_not_repeat()
    {
        var codes = Enumerable.Range(0, 1000).Select(_ => TicketCode.New()).ToHashSet();

        Assert.Equal(1000, codes.Count);
    }
}
