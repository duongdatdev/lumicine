using System.Diagnostics;
using System.Globalization;
using System.Text.Json;
using Google.GenAI;
using LumiCine.Application.Common;
using LumiCine.Application.Discovery;
using LumiCine.Domain.Catalog;
using LumiCine.Domain.Cinemas;
using LumiCine.Domain.Scheduling;
using LumiCine.Infrastructure.Discovery;
using LumiCine.Infrastructure.Identity;
using LumiCine.Infrastructure.Persistence;
using LumiCine.IntegrationTests.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Xunit.Abstractions;
using ApiException = LumiCine.Application.Common.ApiException;

namespace LumiCine.IntegrationTests.Discovery;

/// <summary>
/// Runs the 40-scenario Phase 6 eval set (<see cref="DiscoveryEvalSet"/>) against the real
/// runtime. The 20 deterministic filter scenarios replay scripted parser replies built straight
/// from each expectation and assert the applied filter state, so they pass offline. The 20
/// semantic scenarios need a real Gemini key and network and silently skip without one.
/// </summary>
[Collection(PostgresDatabaseCollection.Name)]
public sealed class DiscoveryEvalHarnessTests(PostgresDatabaseFixture fixture, ITestOutputHelper output)
{
    private static readonly DateTime FixedNow =
        DateTime.Parse(DiscoveryEvalSet.FixedNowUtcIso, CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind);

    private static readonly IReadOnlyDictionary<string, string> EvalGenreNames = new Dictionary<string, string>
    {
        ["khoa-hoc-truyen-vien"] = "Khoa học viễn tưởng",
        ["chinh-kich"] = "Chính kịch",
        ["gia-dinh"] = "Gia đình",
        ["hoat-hinh"] = "Hoạt hình",
        ["hanh-dong"] = "Hành động",
        ["kinh-di"] = "Kinh dị",
        ["hai"] = "Hài",
        ["lang-man"] = "Lãng mạn",
        ["tai-lieu"] = "Tài liệu",
        ["chien-tranh"] = "Chiến tranh",
        ["am-nhac"] = "Âm nhạc",
        ["the-thao"] = "Thể thao"
    };

    private static readonly SemaphoreSlim CatalogSeedGate = new(1, 1);
    private static Guid[]? _evalMovieIds;
    private static bool _evalEmbeddingsSeeded;

    [Fact]
    public async Task Deterministic_filter_scenarios_pass_all_twenty_expectations()
    {
        await EnsureEvalCatalogSeededAsync();
        var scenarios = DiscoveryEvalSet.Scenarios.Where(s => s.Kind == "filter").OrderBy(s => s.Id).ToList();
        Assert.Equal(20, scenarios.Count);

        // One scripted parser reply per turn (built from the scenario expectation) plus one
        // scripted writer reply; 20 turns fill exactly the conversation turn limit.
        var replies = new List<ScriptedChatReply>(scenarios.Count * 2);
        foreach (var scenario in scenarios)
        {
            replies.Add(new ScriptedChatReply(BuildIntentJson(scenario)));
            replies.Add(new ScriptedChatReply("Đây là các phim phù hợp."));
        }

        await using var db = fixture.CreateDbContext();
        var userId = await CreateUserAsync(db, $"discovery-eval-filter-{Guid.NewGuid():N}@lumicine.test");
        var service = DiscoveryTestServices.CreateConversationService(
            db, new ScriptedChatClient([.. replies]), new FixedClock(FixedNow));
        var conversationId = (await service.CreateAsync(userId, default)).Id;

        var version = 1;
        foreach (var scenario in scenarios)
        {
            var response = await service.SendMessageAsync(userId, conversationId,
                new SendDiscoveryMessageRequest(Guid.NewGuid(), version, scenario.Prompt), default);
            Assert.False(response.IsDegraded,
                $"Scenario {scenario.Id} degraded unexpectedly: {string.Join("; ", response.Warnings)}");
            AssertAppliedFiltersMatchExpectation(response.AppliedFilters, scenario.Expect, scenario.Id);
            version = response.Version;
        }

        Assert.Equal(21, version);
        await using var verify = fixture.CreateDbContext();
        var conversation = await verify.DiscoveryConversations.SingleAsync(c => c.Id == conversationId);
        Assert.Equal(20, conversation.TurnCount);
        Assert.Equal(40, await verify.DiscoveryMessages.CountAsync(m => m.ConversationId == conversationId));

        // Twenty turns fill the conversation exactly; the 21st request hits the turn limit.
        var rejected = await Assert.ThrowsAsync<ApiException>(() =>
            service.SendMessageAsync(userId, conversationId,
                new SendDiscoveryMessageRequest(Guid.NewGuid(), version, "Lượt thứ 21."), default));
        Assert.Equal(409, rejected.StatusCode);
    }

    /// <summary>
    /// Real-Gemini semantic retrieval eval: needs the LUMICINE_GEMINI_API_KEY environment
    /// variable plus network access; without the variable the scenario silently skips so
    /// ordinary offline CI runs stay green. Real parser, real embeddings and real writer feed
    /// the graded prompts; at least 16 of the 20 semantic scenarios must surface their expected
    /// movie inside the top five results. Token and realtime cost numbers are never asserted.
    /// </summary>
    [Fact]
    public async Task Semantic_scenarios_rank_expected_movies_in_top_five()
    {
        var apiKey = Environment.GetEnvironmentVariable("LUMICINE_GEMINI_API_KEY");
        if (string.IsNullOrWhiteSpace(apiKey))
        {
            output.WriteLine("LUMICINE_GEMINI_API_KEY is not set; skipping the real-Gemini semantic eval (needs a real key plus network).");
            return;
        }

        var movieIds = await EnsureEvalCatalogSeededAsync();
        await SeedRealEmbeddingsAsync(apiKey, movieIds);
        var scenarios = DiscoveryEvalSet.Scenarios.Where(s => s.Kind == "semantic").OrderBy(s => s.Id).ToList();
        Assert.Equal(20, scenarios.Count);

        await using var db = fixture.CreateDbContext();
        var userId = await CreateUserAsync(db, $"discovery-eval-semantic-{Guid.NewGuid():N}@lumicine.test");
        var service = CreateRealGeminiService(db, apiKey, new FixedClock(FixedNow));
        var conversationId = (await service.CreateAsync(userId, default)).Id;

        var hits = 0;
        foreach (var scenario in scenarios)
        {
            DiscoveryChatResponse? response = null;
            try
            {
                var version = await db.DiscoveryConversations.AsNoTracking()
                    .Where(c => c.Id == conversationId).Select(c => c.Version).SingleAsync();
                response = await service.SendMessageAsync(userId, conversationId,
                    new SendDiscoveryMessageRequest(Guid.NewGuid(), version, scenario.Prompt), default);
            }
            catch (Exception ex)
            {
                output.WriteLine($"scenario {scenario.Id}: ERROR {ex.GetType().Name}: {ex.Message}");
                continue;
            }

            var keywords = scenario.Expect.MovieTitleKeywords;
            var hit = response.Movies.Take(5).Any(m => keywords.Any(k => m.Title.Contains(k, StringComparison.Ordinal)));
            if (hit) hits++;
            output.WriteLine($"scenario {scenario.Id}: {(hit ? "HIT" : "MISS")} degraded={response.IsDegraded} " +
                $"top5=[{string.Join(" | ", response.Movies.Take(5).Select(m => m.Title))}] " +
                $"warnings=[{string.Join("; ", response.Warnings)}]");
        }

        output.WriteLine($"semantic eval: {hits}/20 scenarios surfaced the expected movie in the top five.");
        Assert.True(hits >= 16, $"Expected at least 16 of 20 semantic scenarios to hit the expected movie in the top five, got {hits}.");
    }

    /// <summary>Turns the scenario expectation into the scripted parser reply for that turn.</summary>
    private static string BuildIntentJson(DiscoveryEvalScenario scenario)
    {
        var expect = scenario.Expect;
        // Only the two documented full-reset turns classify as new searches; every other turn
        // refines the ongoing conversation and inherits the fields it does not mention.
        var intent = scenario.Id is 1 or 19 ? DiscoveryIntentTypes.NewSearch : DiscoveryIntentTypes.Adjust;
        return JsonSerializer.Serialize(new Dictionary<string, object?>
        {
            ["Intent"] = intent,
            ["TopicText"] = null,
            ["MovieNameText"] = expect.MovieTitleKeywords.Length > 0 ? expect.MovieTitleKeywords[0] : null,
            ["GenreSlugs"] = expect.GenreSlugs.Length > 0 ? expect.GenreSlugs : null,
            ["MaxDurationMinutes"] = expect.MaxDurationMinutes,
            ["City"] = expect.City,
            ["CinemaName"] = expect.CinemaName,
            ["DateFrom"] = expect.DateFrom,
            ["DateTo"] = expect.DateTo,
            ["TimeWindowStartMinutes"] = null,
            ["TimeWindowEndMinutes"] = null,
            ["MaxPrice"] = expect.MaxPrice,
            ["MinSeats"] = expect.MinSeats,
            ["ClearFields"] = expect.ClearedFields.Length > 0 ? expect.ClearedFields : null
        });
    }

    private void AssertAppliedFiltersMatchExpectation(
        DiscoveryFiltersView view, DiscoveryEvalExpectation expect, int scenarioId)
    {
        output.WriteLine($"scenario {scenarioId}: dates={view.DateFrom}..{view.DateTo} city={view.City} " +
            $"cinema={view.CinemaName} maxPrice={view.MaxPrice} minSeats={view.MinSeats} " +
            $"maxDuration={view.MaxDurationMinutes} genres=[{string.Join(", ", view.GenreSlugs)}]");

        if (expect.DateFrom is { } dateFrom)
            Assert.True(view.DateFrom == dateFrom, $"Scenario {scenarioId}: DateFrom was {view.DateFrom}, expected {dateFrom}.");
        if (expect.DateTo is { } dateTo)
            Assert.True(view.DateTo == dateTo, $"Scenario {scenarioId}: DateTo was {view.DateTo}, expected {dateTo}.");
        if (expect.City is not null)
            Assert.Equal(expect.City, view.City);
        if (expect.CinemaName is not null)
            Assert.Equal(expect.CinemaName, view.CinemaName);
        if (expect.MaxPrice is { } maxPrice)
            Assert.Equal(maxPrice, view.MaxPrice);
        if (expect.MinSeats is { } minSeats)
            Assert.Equal(minSeats, view.MinSeats);
        if (expect.MaxDurationMinutes is { } maxDuration)
            Assert.Equal(maxDuration, view.MaxDurationMinutes);
        foreach (var slug in expect.GenreSlugs)
            Assert.Contains(slug, view.GenreSlugs);
        foreach (var cleared in expect.ClearedFields)
            AssertClearedField(view, cleared, scenarioId);

        // An expectation with every field unset asserts a deliberate full reset (turns 1 and 19).
        if (IsFullReset(expect))
        {
            Assert.Null(view.TopicText);
            Assert.Null(view.MovieNameText);
            Assert.Empty(view.GenreSlugs);
            Assert.Null(view.MaxDurationMinutes);
            Assert.Null(view.City);
            Assert.Null(view.CinemaName);
            Assert.Null(view.DateFrom);
            Assert.Null(view.DateTo);
            Assert.Null(view.TimeWindowStartMinutes);
            Assert.Null(view.TimeWindowEndMinutes);
            Assert.Null(view.MaxPrice);
            Assert.Null(view.MinSeats);
        }
    }

    private static void AssertClearedField(DiscoveryFiltersView view, string field, int scenarioId)
    {
        switch (field)
        {
            case DiscoveryFilterFields.Topic:
                Assert.Null(view.TopicText);
                break;
            case DiscoveryFilterFields.MovieName:
                Assert.Null(view.MovieNameText);
                break;
            case DiscoveryFilterFields.Genres:
                Assert.Empty(view.GenreSlugs);
                break;
            case DiscoveryFilterFields.Duration:
                Assert.Null(view.MaxDurationMinutes);
                break;
            case DiscoveryFilterFields.Cinema:
                Assert.Null(view.CinemaName);
                break;
            case DiscoveryFilterFields.City:
                Assert.Null(view.City);
                break;
            case DiscoveryFilterFields.DateRange:
                Assert.Null(view.DateFrom);
                Assert.Null(view.DateTo);
                break;
            case DiscoveryFilterFields.TimeWindow:
                Assert.Null(view.TimeWindowStartMinutes);
                Assert.Null(view.TimeWindowEndMinutes);
                break;
            case DiscoveryFilterFields.MaxPrice:
                Assert.Null(view.MaxPrice);
                break;
            case DiscoveryFilterFields.MinSeats:
                Assert.Null(view.MinSeats);
                break;
            default:
                Assert.Fail($"Scenario {scenarioId}: unknown cleared field \"{field}\".");
                break;
        }
    }

    private static bool IsFullReset(DiscoveryEvalExpectation expect) =>
        expect.DateFrom is null && expect.DateTo is null && expect.City is null && expect.CinemaName is null &&
        expect.MaxPrice is null && expect.MinSeats is null && expect.MaxDurationMinutes is null &&
        expect.GenreSlugs.Length == 0 && expect.ClearedFields.Length == 0 && expect.MovieTitleKeywords.Length == 0;

    /// <summary>
    /// Seeds the eval catalog once per run: 12 movies with their genres, the 4 named cinemas
    /// across Hà Nội and TP. Hồ Chí Minh, and staggered showtimes (several days per movie so
    /// every movie also stays inside the default 7-day window) with 3 seats each inside the
    /// typical max-price expectations. Deterministic per run; reruns reuse the existing rows.
    /// </summary>
    private async Task<Guid[]> EnsureEvalCatalogSeededAsync()
    {
        if (_evalMovieIds is { } cached) return cached;
        await CatalogSeedGate.WaitAsync();
        try
        {
            if (_evalMovieIds is { } again) return again;
            return await SeedEvalCatalogAsync();
        }
        finally
        {
            CatalogSeedGate.Release();
        }
    }

    private async Task<Guid[]> SeedEvalCatalogAsync()
    {
        await using var db = fixture.CreateDbContext();
        var now = DateTime.UtcNow;
        var titles = DiscoveryEvalCatalog.Movies.Select(m => m.Title).ToList();
        var existingMovies = await db.Movies.AsNoTracking()
            .Where(m => titles.Contains(m.Title))
            .ToDictionaryAsync(m => m.Title, m => m.Id);

        if (existingMovies.Count < DiscoveryEvalCatalog.Movies.Length)
        {
            foreach (var slug in EvalGenreNames.Keys)
            {
                var exists = await db.Genres.AnyAsync(g => g.Slug == slug);
                if (!exists) db.Genres.Add(new Genre(Guid.NewGuid(), EvalGenreNames[slug], slug, now));
            }

            var cinemas = new Dictionary<string, (Guid CinemaId, Guid RoomId)>();
            for (var index = 0; index < DiscoveryEvalCatalog.Cinemas.Length; index++)
            {
                var data = DiscoveryEvalCatalog.Cinemas[index];
                var cinemaId = Guid.NewGuid();
                var roomId = Guid.NewGuid();
                var code = $"EVAL{index}LUMI";
                var cinema = new Cinema(cinemaId, code, data.Name, $"Địa chỉ rạp {index + 1}", data.City, now);
                var room = new Room(roomId, cinemaId, $"EV{index}R1", $"Phòng {index + 1}", now);
                var seats = new List<Seat>();
                for (var seatNumber = 1; seatNumber <= 3; seatNumber++)
                    seats.Add(new Seat(Guid.NewGuid(), roomId, "A", seatNumber, "Standard", now));
                db.AddRangeAsync(cinema, room);
                db.Seats.AddRange(seats);
                cinemas[data.Name] = (cinemaId, roomId);
                _evalRoomSeats[data.Name] = seats;
            }
            await db.SaveChangesAsync();

            var movieIds = new Guid[DiscoveryEvalCatalog.Movies.Length];
            for (var index = 0; index < DiscoveryEvalCatalog.Movies.Length; index++)
            {
                var movie = DiscoveryEvalCatalog.Movies[index];
                var movieId = Guid.NewGuid();
                movieIds[index] = movieId;
                db.Movies.Add(new Movie(movieId, movie.Title, movie.DurationMinutes, now,
                    movie.OriginalTitle, movie.Synopsis, null, movie.AgeRating));
                foreach (var slug in movie.GenreSlugs)
                {
                    var genreId = await db.Genres.Where(g => g.Slug == slug).Select(g => g.Id).SingleAsync();
                    db.MovieGenres.Add(new MovieGenre(movieId, genreId, now));
                }

                var (_, roomId) = cinemas[DiscoveryEvalCatalog.Cinemas[index % DiscoveryEvalCatalog.Cinemas.Length].Name];
                foreach (var start in ShowtimeStarts(index))
                {
                    var showtime = new Showtime(Guid.NewGuid(), movieId, roomId, start,
                        start.AddMinutes(movie.DurationMinutes), 120_000m, now);
                    db.Showtimes.Add(showtime);
                    var prices = new[] { 90_000m, 110_000m, 140_000m };
                    for (var seatIndex = 0; seatIndex < 3; seatIndex++)
                        db.ShowtimeSeats.Add(new ShowtimeSeat(Guid.NewGuid(), showtime.Id, roomId,
                            _evalRoomSeats[DiscoveryEvalCatalog.Cinemas[index % DiscoveryEvalCatalog.Cinemas.Length].Name][seatIndex].Id,
                            prices[seatIndex], now));
                }
            }
            await db.SaveChangesAsync();
            _evalMovieIds = movieIds;
            return movieIds;
        }

        var mapped = DiscoveryEvalCatalog.Movies
            .Select(m => existingMovies[m.Title])
            .ToArray();
        _evalMovieIds = mapped;
        return mapped;
    }

    private static readonly Dictionary<string, List<Seat>> _evalRoomSeats = [];

    /// <summary>
    /// Showtime starts for the movie at the given catalog index. The movie lives in one room
    /// (cinema = index modulo the cinema count), and within a room every showtime gets its own
    /// day through disjoint day blocks (movie block = index / cinema count, three slots each),
    /// so the EX_Showtimes_Room_TimeRange exclusion constraint is never violated. Start hours
    /// stay staggered inside the 2..14 range, and every movie keeps at least one showtime on
    /// day 0, 3 or 6 — always inside the default 7-day window semantic turns query.
    /// </summary>
    private static IEnumerable<DateTime> ShowtimeStarts(int index)
    {
        var firstDay = 3 * (index / DiscoveryEvalCatalog.Cinemas.Length);
        for (var slot = 0; slot < 3; slot++)
            yield return FixedNow.AddDays(firstDay + slot).AddHours(2 + (index % 13));
    }

    /// <summary>
    /// Embeds every eval movie with the real Gemini embedding model once per run and stores the
    /// documents as Indexed so the hybrid search can rank them semantically.
    /// </summary>
    private async Task SeedRealEmbeddingsAsync(string apiKey, Guid[] movieIds)
    {
        if (_evalEmbeddingsSeeded) return;
        var options = new GeminiOptions();
        var builder = new MovieSearchDocumentBuilder(Options.Create(options));
        await using var db = fixture.CreateDbContext();

        var pending = new List<(Guid MovieId, string Text)>();
        for (var index = 0; index < DiscoveryEvalCatalog.Movies.Length; index++)
        {
            var movie = DiscoveryEvalCatalog.Movies[index];
            var alreadyIndexed = await db.MovieSearchDocuments.AsNoTracking().AnyAsync(d =>
                d.MovieId == movieIds[index] && d.Status == MovieSearchDocumentStatus.Indexed &&
                d.EmbeddingModelId == options.EmbeddingModel && d.EmbeddingDimensions == options.EmbeddingDimensions);
            if (!alreadyIndexed)
            {
                var genreNames = movie.GenreSlugs.Select(slug => EvalGenreNames[slug]).ToList();
                var text = builder.BuildDocumentText(movie.Title, movie.OriginalTitle, movie.Synopsis, genreNames);
                pending.Add((movieIds[index], text));
            }
        }

        if (pending.Count > 0)
        {
            var client = new Client(apiKey: apiKey);
            var generator = client.AsIEmbeddingGenerator(options.EmbeddingModel, options.EmbeddingDimensions);
            var embeddings = await generator.GenerateAsync(pending.Select(p => p.Text),
                new EmbeddingGenerationOptions { Dimensions = options.EmbeddingDimensions }, default);
            var vectors = embeddings.Select(e => e.Vector.ToArray()).ToList();
            for (var index = 0; index < pending.Count; index++)
            {
                var (movieId, text) = pending[index];
                var vector = vectors[index];
                var document = new MovieSearchDocument(Guid.NewGuid(), movieId, text,
                    builder.ComputeSourceHash(text), DateTime.UtcNow);
                document.MarkIndexed(options.EmbeddingModel, vector.Length, vector, DateTime.UtcNow);
                db.MovieSearchDocuments.Add(document);
            }
            await db.SaveChangesAsync();
        }
        _evalEmbeddingsSeeded = true;
    }

    /// <summary>Same service graph as <see cref="DiscoveryTestServices"/> but with real Gemini clients.</summary>
    private static DiscoveryConversationService CreateRealGeminiService(LumiCineDbContext db, string apiKey, TimeProvider clock)
    {
        var options = new GeminiOptions();
        var geminiOptions = Options.Create(options);
        var conversationOptions = Options.Create(new DiscoveryConversationOptions());
        var client = new Client(apiKey: apiKey);
        var aiClients = new GeminiAiClients(
            client.AsIChatClient(options.ChatModel),
            client.AsIEmbeddingGenerator(options.EmbeddingModel, options.EmbeddingDimensions));
        return new DiscoveryConversationService(
            db, clock, conversationOptions, geminiOptions,
            new DiscoveryConversationTurnStore(db, clock, conversationOptions),
            new DiscoveryIntentParser(aiClients, geminiOptions, NullLogger<DiscoveryIntentParser>.Instance),
            new DiscoveryFilterResolver(db, clock),
            new DiscoverySearchService(db, clock, aiClients, geminiOptions, conversationOptions),
            new DiscoveryAnswerWriter(aiClients, geminiOptions, NullLogger<DiscoveryAnswerWriter>.Instance));
    }

    private static async Task<Guid> CreateUserAsync(LumiCineDbContext db, string email)
    {
        var userId = Guid.NewGuid();
        db.Users.Add(new ApplicationUser
        {
            Id = userId,
            Email = email,
            NormalizedEmail = email.ToUpperInvariant(),
            EmailConfirmed = true,
            IsActive = true,
            DisplayName = "Eval harness"
        });
        await db.SaveChangesAsync();
        return userId;
    }

    private sealed class FixedClock(DateTimeOffset now) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => now;
    }
}

/// <summary>
/// Offline throughput guard for the discovery ranking math: scanning 5000 fake 768-dimension
/// vectors with <see cref="DiscoveryVectorMath.CosineSimilarity"/> plus ordering must keep the
/// p95 per-query time under 300 ms on a single thread. Needs no database and no collection.
/// </summary>
public sealed class DiscoveryEvalBenchmarkTests(ITestOutputHelper output)
{
    [Fact]
    public void Benchmark_cosine_over_5000_fake_vectors_under_300ms_p95()
    {
        const int Dimensions = 768;
        const int CorpusSize = 5000;
        const int QueryCount = 200;

        // Deterministic normalized corpus and query vectors; arrays are reused across queries
        // to keep allocations out of the measurement.
        var rng = new Random(42);
        var corpus = new float[CorpusSize][];
        for (var index = 0; index < CorpusSize; index++)
            corpus[index] = Normalize(FillRandom(new float[Dimensions], rng));
        var queries = new float[QueryCount][];
        for (var index = 0; index < QueryCount; index++)
            queries[index] = Normalize(FillRandom(new float[Dimensions], rng));

        var scores = new double[CorpusSize];
        var order = new int[CorpusSize];

        void RankOnce(float[] query)
        {
            for (var index = 0; index < CorpusSize; index++)
                scores[index] = DiscoveryVectorMath.CosineSimilarity(query, corpus[index]);
            for (var index = 0; index < CorpusSize; index++)
                order[index] = index;
            Array.Sort(scores, order);
            Array.Reverse(order);
        }

        // Warmup pass so JIT and cache effects stay out of the measurement.
        for (var index = 0; index < 10; index++)
            RankOnce(queries[index]);

        var elapsed = new List<TimeSpan>(QueryCount);
        for (var index = 0; index < QueryCount; index++)
        {
            var stopwatch = Stopwatch.StartNew();
            RankOnce(queries[index]);
            stopwatch.Stop();
            elapsed.Add(stopwatch.Elapsed);
        }

        var sorted = elapsed.OrderBy(t => t.Ticks).ToList();
        var p95 = sorted[(int)Math.Ceiling(0.95 * sorted.Count) - 1];
        var mean = TimeSpan.FromTicks((long)elapsed.Average(t => t.Ticks));
        output.WriteLine($"cosine benchmark over {CorpusSize} vectors x {Dimensions} dims, {QueryCount} queries: " +
            $"mean={mean.TotalMilliseconds:F2}ms p95={p95.TotalMilliseconds:F2}ms max={sorted[^1].TotalMilliseconds:F2}ms");
        Assert.True(p95.TotalMilliseconds < 300, $"p95={p95.TotalMilliseconds:F2}ms exceeded the 300ms budget.");
    }

    private static float[] FillRandom(float[] vector, Random rng)
    {
        for (var index = 0; index < vector.Length; index++)
            vector[index] = (float)(rng.NextDouble() * 2.0 - 1.0);
        return vector;
    }

    private static float[] Normalize(float[] vector)
    {
        double sum = 0;
        for (var index = 0; index < vector.Length; index++)
            sum += (double)vector[index] * vector[index];
        var norm = (float)Math.Sqrt(sum);
        if (norm == 0) norm = 1;
        for (var index = 0; index < vector.Length; index++)
            vector[index] /= norm;
        return vector;
    }
}
