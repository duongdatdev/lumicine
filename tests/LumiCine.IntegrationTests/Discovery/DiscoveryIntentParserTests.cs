using LumiCine.Application.Discovery;
using LumiCine.Infrastructure.Discovery;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

namespace LumiCine.IntegrationTests.Discovery;

public sealed class DiscoveryIntentParserTests
{
    private static DiscoveryIntentParser CreateParser(IChatClient? chatClient) =>
        new(new GeminiAiClients(chatClient, new ScriptedEmbeddingGenerator([1f])),
            Options.Create(new GeminiOptions()), NullLogger<DiscoveryIntentParser>.Instance);

    private static DiscoveryIntentInput SampleInput(string message = "Có phim du hành thời gian dưới 2 tiếng ở Hà Nội tối thứ Bảy dưới 120 nghìn đủ 3 ghế không?") => new(
        message,
        "{\"TopicText\":\"phim hành động\",\"MaxPrice\":90000}",
        [new DiscoveryIntentTurn("user", "Có phim hành động không?"), new DiscoveryIntentTurn("assistant", "Có ba phim hành động.")],
        new DateTime(2026, 11, 8, 13, 0, 0, DateTimeKind.Utc),
        ["khoa-hoc", "hanh-dong", "hai"],
        ["Beta Mỹ Đình", "Lotte Hà Đông"],
        ["Hà Nội", "TP. Hồ Chí Minh"]);

    [Fact]
    public async Task Parses_full_intent_from_vietnamese_message()
    {
        const string json = """
            {"Intent":"adjust","TopicText":"du hành thời gian","MovieNameText":null,"GenreSlugs":["KHOA-HOC"],
             "MaxDurationMinutes":120,"City":"Hà Nội","CinemaName":null,"DateFrom":"2026-11-14","DateTo":"2026-11-14",
             "TimeWindowStartMinutes":1020,"TimeWindowEndMinutes":1439,"MaxPrice":120000,"MinSeats":3,
             "ClearFields":["movieName","ban_tat_ca"]}
            """;
        var chat = new ScriptedChatClient(new ScriptedChatReply(json));

        var result = await CreateParser(chat).ParseAsync(SampleInput(), default);

        Assert.Null(result.Error);
        var intent = result.Intent!;
        Assert.Equal(DiscoveryIntentTypes.Adjust, intent.Intent);
        Assert.Equal("du hành thời gian", intent.TopicText);
        Assert.Equal(120, intent.MaxDurationMinutes);
        Assert.Equal(new DateOnly(2026, 11, 14), intent.DateFrom);
        Assert.Equal(120_000m, intent.MaxPrice);
        Assert.Equal(3, intent.MinSeats);
        Assert.Equal(["movieName"], intent.ClearFields); // unknown clear fields are dropped

        var (messages, options) = Assert.Single(chat.Requests);
        Assert.Equal(2, messages.Count);
        Assert.Equal(ChatRole.System, messages[0].Role);
        Assert.Equal(ChatRole.User, messages[1].Role);
        var prompt = messages[1].Text;
        Assert.Contains("2026-11-08 20:00", prompt);                       // UTC+7 wall clock
        Assert.Contains("\"TopicText\":\"phim hành động\"", prompt);        // current filter state
        Assert.Contains("khoa-hoc", prompt);                                // genre hints
        Assert.Contains("Beta Mỹ Đình", prompt);                            // cinema hints
        Assert.Contains(SampleInput().Message, prompt);                     // the raw message
        Assert.NotNull(options!.ResponseFormat);                            // structured output on
        Assert.Equal(512, options.MaxOutputTokens);
    }

    [Fact]
    public async Task Parser_requires_configured_clients()
    {
        var parser = new DiscoveryIntentParser(GeminiAiClients.Unconfigured, Options.Create(new GeminiOptions()), NullLogger<DiscoveryIntentParser>.Instance);

        var result = await parser.ParseAsync(SampleInput(), default);

        Assert.Null(result.Intent);
        Assert.Contains("not configured", result.Error);
    }

    [Fact]
    public async Task Provider_failures_surface_as_parse_errors()
    {
        var parser = CreateParser(new ScriptedChatClient(new ScriptedChatReply(Failure: new HttpRequestException("503 upstream"))));

        var result = await parser.ParseAsync(SampleInput(), default);

        Assert.Null(result.Intent);
        Assert.Contains("503 upstream", result.Error);
    }

    [Fact]
    public async Task Provider_timeout_is_reported_as_timeout()
    {
        var parser = CreateParser(new ScriptedChatClient(new ScriptedChatReply(Failure: new OperationCanceledException())));

        var result = await parser.ParseAsync(SampleInput(), default);

        Assert.Null(result.Intent);
        Assert.Contains("timed out", result.Error);
    }

    [Theory]
    [InlineData("this is not json")]
    [InlineData("{\"Intent\":\"delete_all_payments\"}")]
    [InlineData("{\"Intent\":\"new_search\",\"MaxPrice\":\"many\"}")]
    [InlineData("")]
    public async Task Schema_violations_fail_without_intent(string text)
    {
        var parser = CreateParser(new ScriptedChatClient(new ScriptedChatReply(text)));

        var result = await parser.ParseAsync(SampleInput(), default);

        Assert.Null(result.Intent);
        Assert.NotNull(result.Error);
    }
}
