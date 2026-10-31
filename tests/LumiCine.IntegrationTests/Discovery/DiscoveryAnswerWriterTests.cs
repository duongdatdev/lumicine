using LumiCine.Application.Discovery;
using LumiCine.Infrastructure.Discovery;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

namespace LumiCine.IntegrationTests.Discovery;

public sealed class DiscoveryAnswerWriterTests
{
    private static readonly Guid MovieId = Guid.Parse("a1000000-0000-0000-0000-000000000001");
    private static readonly Guid ShowtimeId = Guid.Parse("b2000000-0000-0000-0000-000000000002");

    // 2026-11-14 13:30 UTC is 20:30 on 14/11 in UTC+7.
    private static readonly DateTime NowUtc = new(2026, 11, 14, 13, 30, 0, DateTimeKind.Utc);

    private static DiscoveryEvidenceShowtime Showtime() => new(
        ShowtimeId, Guid.NewGuid(), "Beta Mỹ Đình", "Phòng 3",
        new DateTime(2026, 11, 14, 13, 30, 0, DateTimeKind.Utc), 120_000m, 42);

    private static DiscoveryEvidenceMovie Movie() => new(
        MovieId, "Kỳ Quan Trở Về", 120, "T16", ["Khoa học", "Hài"], [Showtime()]);

    private static DiscoveryAnswerWriter CreateWriter(IChatClient? chatClient) =>
        new(new GeminiAiClients(chatClient, new ScriptedEmbeddingGenerator([1f])),
            Options.Create(new GeminiOptions()), NullLogger<DiscoveryAnswerWriter>.Instance);

    private static string ScriptedAnswer() =>
        $"Mình gợi ý [movie:{MovieId}] nhé, suất [showtime:{ShowtimeId}] lúc [showtime:{ShowtimeId}:gio], " +
        $"giá [showtime:{ShowtimeId}:gia], dài [movie:{MovieId}:thoiluong].";

    [Fact]
    public async Task WriteAsync_returns_scripted_answer_and_records_grounded_prompt()
    {
        var chat = new ScriptedChatClient(new ScriptedChatReply(ScriptedAnswer()));
        const string message = "Có phim khoa học dưới 2 tiếng tối thứ Bảy dưới 120 nghìn không?";

        var result = await CreateWriter(chat).WriteAsync(message, [], [Movie()], noResults: false, NowUtc, default);

        Assert.Null(result.Error);
        Assert.Equal(ScriptedAnswer(), result.Answer);

        var (messages, options) = Assert.Single(chat.Requests);
        Assert.Equal(2, messages.Count);
        Assert.Equal(ChatRole.System, messages[0].Role);
        Assert.Equal(ChatRole.User, messages[1].Role);

        var system = messages[0].Text;
        Assert.Contains("[movie:<movieId>]", system);
        Assert.Contains("[showtime:<showtimeId>:gio]", system);
        Assert.Contains("[showtime:<showtimeId>:gia]", system);
        Assert.Contains("rejects unknown references", system);
        Assert.Contains("untrusted DATA", system);
        Assert.Contains("booking/payment", system);
        // The rules must not carry concrete numbers the model could parrot; values come from evidence.
        Assert.DoesNotContain("120.000đ", system);

        var prompt = messages[1].Text;
        Assert.Contains(message, prompt);
        Assert.Contains("Kỳ Quan Trở Về", prompt);
        Assert.Contains(ShowtimeId.ToString(), prompt);
        Assert.Contains("2026-11-14 20:30 (UTC+7)", prompt);   // wall-clock line
        Assert.Contains("start (UTC+7): 20:30 14/11", prompt); // evidence showtime card
        Assert.Contains("120.000đ", prompt);                   // server-provided price
        Assert.Contains("noResults: false", prompt);
        Assert.Null(options!.ResponseFormat);                  // free-text answer
        Assert.Equal(800, options.MaxOutputTokens);
        Assert.Equal(0.4f, options.Temperature);
    }

    [Fact]
    public async Task Provider_failures_surface_as_write_errors()
    {
        var writer = CreateWriter(new ScriptedChatClient(new ScriptedChatReply(Failure: new HttpRequestException("503 upstream"))));

        var result = await writer.WriteAsync("xin chào", [], [], noResults: true, NowUtc, default);

        Assert.Null(result.Answer);
        Assert.Contains("AI answer writing failed", result.Error);
        Assert.Contains("503 upstream", result.Error);
    }

    [Fact]
    public async Task Provider_timeout_is_reported_as_timeout()
    {
        var writer = CreateWriter(new ScriptedChatClient(new ScriptedChatReply(Failure: new OperationCanceledException())));

        var result = await writer.WriteAsync("xin chào", [], [], noResults: true, NowUtc, default);

        Assert.Null(result.Answer);
        Assert.Contains("AI answer writing timed out.", result.Error);
    }

    [Fact]
    public async Task Writer_requires_configured_clients()
    {
        var writer = new DiscoveryAnswerWriter(GeminiAiClients.Unconfigured, Options.Create(new GeminiOptions()), NullLogger<DiscoveryAnswerWriter>.Instance);

        var result = await writer.WriteAsync("xin chào", [], [], noResults: true, NowUtc, default);

        Assert.Null(result.Answer);
        Assert.Contains("AI service is not configured.", result.Error);
    }

    [Fact]
    public void Results_template_lists_movies_with_local_times_and_prices()
    {
        var answer = DiscoveryAnswerTemplates.BuildResultsAnswer([Movie()], NowUtc);

        Assert.Contains("1. Kỳ Quan Trở Về (120 phút, thể loại: Khoa học, Hài)", answer);
        Assert.Contains("20:30 ngày 14/11 tại Beta Mỹ Đình (Phòng 3), giá từ 120.000đ, còn 42 ghế.", answer);
        Assert.DoesNotContain("13:30", answer); // UTC must not leak
    }

    [Fact]
    public void No_results_template_restates_active_conditions()
    {
        var filters = new DiscoveryFiltersView(
            "du hành thời gian", null, ["khoa-hoc"], 120, "Hà Nội", "Beta Mỹ Đình",
            new DateOnly(2026, 11, 14), new DateOnly(2026, 11, 15), 1020, 1439, 120_000m, 3);

        var answer = DiscoveryAnswerTemplates.BuildNoResultsAnswer(filters, NowUtc);

        Assert.Contains("chủ đề \"du hành thời gian\"", answer);
        Assert.Contains("thể loại khoa-hoc", answer);
        Assert.Contains("thời lượng dưới 120 phút", answer);
        Assert.Contains("thành phố Hà Nội", answer);
        Assert.Contains("rạp Beta Mỹ Đình", answer);
        Assert.Contains("ngày 14/11–15/11", answer);
        Assert.Contains("khung giờ 17:00–23:59", answer);
        Assert.Contains("giá tối đa 120.000đ", answer);
        Assert.Contains("ít nhất 3 ghế trống", answer);
        Assert.Contains("Bạn thử nới thời gian, đổi rạp hoặc bỏ một điều kiện nhé.", answer);
    }

    [Fact]
    public void No_results_template_with_no_conditions_suggests_starting_a_search()
    {
        var filters = new DiscoveryFiltersView(
            null, null, [], null, null, null, null, null, null, null, null, null);

        var answer = DiscoveryAnswerTemplates.BuildNoResultsAnswer(filters, NowUtc);

        Assert.Contains("Bạn thử nói thể loại hoặc phim bạn muốn xem nhé.", answer);
        Assert.DoesNotContain("điều kiện", answer);
    }

    [Fact]
    public void Degraded_notice_explains_the_template_fallback()
    {
        Assert.Equal("Dịch vụ AI tạm thời không trả lời được, mình dùng câu trả lời mẫu từ dữ liệu tìm kiếm.",
            DiscoveryAnswerTemplates.BuildDegradedNotice());
    }
}
