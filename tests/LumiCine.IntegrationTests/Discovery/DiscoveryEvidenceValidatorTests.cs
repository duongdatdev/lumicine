using LumiCine.Application.Discovery;
using LumiCine.Infrastructure.Discovery;

namespace LumiCine.IntegrationTests.Discovery;

public sealed class DiscoveryEvidenceValidatorTests
{
    private static readonly Guid MovieId = Guid.Parse("a1000000-0000-0000-0000-000000000001");
    private static readonly Guid ShowtimeId = Guid.Parse("b2000000-0000-0000-0000-000000000002");
    private static readonly Guid StrangerId = Guid.Parse("c3000000-0000-0000-0000-000000000003");

    // 2026-11-14 13:30 UTC is 20:30 on 14/11 in UTC+7.
    private static readonly DateTime StartUtc = new(2026, 11, 14, 13, 30, 0, DateTimeKind.Utc);

    private static DiscoveryEvidenceMovie Evidence() => new(
        MovieId, "Kỳ Quan Trở Về", 120, "T16", ["Khoa học"],
        [new DiscoveryEvidenceShowtime(ShowtimeId, Guid.NewGuid(), "Beta Mỹ Đình", "Phòng 3", StartUtc, 120_000m, 42)]);

    [Fact]
    public void Expands_every_token_form_from_evidence()
    {
        var answer = $"Xem [movie:{MovieId}] lúc [showtime:{ShowtimeId}] (tức [showtime:{ShowtimeId}:gio]), " +
                     $"giá [showtime:{ShowtimeId}:gia], dài [movie:{MovieId}:thoiluong].";

        var result = DiscoveryEvidenceValidator.Validate(answer, [Evidence()]);

        Assert.True(result.IsValid);
        Assert.Equal("Xem Kỳ Quan Trở Về lúc 20:30 ngày 14/11 (tức 20:30 ngày 14/11), giá 120.000đ, dài 120 phút.", result.Text);
    }

    [Fact]
    public void Unknown_movie_id_makes_the_answer_invalid()
    {
        var result = DiscoveryEvidenceValidator.Validate($"Xem [movie:{StrangerId}] nhé.", [Evidence()]);

        Assert.False(result.IsValid);
        Assert.Equal(string.Empty, result.Text);
    }

    [Fact]
    public void Unknown_showtime_id_makes_the_answer_invalid()
    {
        var result = DiscoveryEvidenceValidator.Validate($"Suất [showtime:{StrangerId}] lúc 20:30.", [Evidence()]);

        Assert.False(result.IsValid);
    }

    [Fact]
    public void Unknown_suffix_makes_the_answer_invalid()
    {
        var result = DiscoveryEvidenceValidator.Validate($"Xem [movie:{MovieId}:ten] nhé.", [Evidence()]);

        Assert.False(result.IsValid);
    }

    [Fact]
    public void Suffix_that_does_not_fit_the_kind_makes_the_answer_invalid()
    {
        var result = DiscoveryEvidenceValidator.Validate($"Suất [showtime:{ShowtimeId}:thoiluong] nhé.", [Evidence()]);

        Assert.False(result.IsValid);
    }

    [Fact]
    public void Empty_answer_is_invalid()
    {
        Assert.False(DiscoveryEvidenceValidator.Validate(null, [Evidence()]).IsValid);
        Assert.False(DiscoveryEvidenceValidator.Validate("   ", [Evidence()]).IsValid);
    }

    [Fact]
    public void Token_free_text_passes_through_unchanged()
    {
        const string answer = "Mình tìm được một phim hay cho bạn, mở lịch chiếu xem nhé.";

        var result = DiscoveryEvidenceValidator.Validate(answer, [Evidence()]);

        Assert.True(result.IsValid);
        Assert.Equal(answer, result.Text);
    }

    [Fact]
    public void Duplicate_tokens_are_all_expanded()
    {
        var answer = $"[movie:{MovieId}] hay ghê, nhắc lại: [movie:{MovieId}] và giờ lại [showtime:{ShowtimeId}:gio].";

        var result = DiscoveryEvidenceValidator.Validate(answer, [Evidence()]);

        Assert.True(result.IsValid);
        Assert.Equal("Kỳ Quan Trở Về hay ghê, nhắc lại: Kỳ Quan Trở Về và giờ lại 20:30 ngày 14/11.", result.Text);
    }
}
