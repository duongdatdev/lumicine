using LumiCine.Application.Discovery;
using LumiCine.Domain.Catalog;
using LumiCine.Domain.Cinemas;
using LumiCine.Infrastructure.Discovery;
using LumiCine.IntegrationTests.Persistence;
using Microsoft.EntityFrameworkCore;

namespace LumiCine.IntegrationTests.Discovery;

[Collection(PostgresDatabaseCollection.Name)]
public sealed class DiscoveryFilterResolverTests(PostgresDatabaseFixture database)
{
    private static readonly DateTime SeedTime = new(2026, 11, 1, 0, 0, 0, DateTimeKind.Utc);

    private static ParsedDiscoveryIntent Intent(
        string intent = DiscoveryIntentTypes.Adjust,
        string? topicText = null,
        string[]? genreSlugs = null,
        string? cinemaName = null,
        string? city = null,
        string[]? clearFields = null) =>
        new(intent, topicText, null, genreSlugs, null, city, cinemaName, null, null, null, null, null, null, clearFields);

    private static Cinema Cinema(string code, string name, string city) =>
        new(Guid.NewGuid(), code, name, "12 Đường example", city, SeedTime);

    private sealed class FixedTimeProvider(DateTimeOffset now) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => now;
    }

    [Fact]
    public async Task Adjust_inherits_unresolved_fields_and_resolves_cinema_identity()
    {
        await using var db = database.CreateDbContext();
        var suffix = Guid.NewGuid().ToString("N")[..8];
        db.Genres.Add(new Genre(Guid.NewGuid(), $"Hành động {suffix}", $"hanh-dong-{suffix}", SeedTime));
        var cinema = db.Cinemas.Add(Cinema($"LC{suffix.ToUpperInvariant()}", $"Rạp Nam {suffix}", $"Hà Nội {suffix}")).Entity;
        await db.SaveChangesAsync();

        var filtersJson = "{\"TopicText\":\"phim hay\",\"MaxPrice\":90000}";
        var intent = Intent(genreSlugs: [$"hanh-dong-{suffix}", $"khong-ton-tai-{suffix}"], cinemaName: $"RẠP NAM {suffix}");
        var resolver = new DiscoveryFilterResolver(db, new FixedTimeProvider(new DateTimeOffset(2026, 11, 13, 10, 0, 0, TimeSpan.Zero)));

        var resolution = await resolver.ResolveAsync(filtersJson, intent, default);

        Assert.Null(resolution.ClarificationText);
        var filters = resolution.Filters;
        Assert.Equal(cinema.Id, filters.CinemaId);
        Assert.Equal($"Rạp Nam {suffix}", filters.CinemaName); // canonical casing from the catalog
        Assert.Equal("phim hay", filters.TopicText);           // inherited from the conversation state
        Assert.Equal(90_000m, filters.MaxPrice);
        Assert.Equal([$"hanh-dong-{suffix}"], filters.GenreSlugs);
        var warning = Assert.Single(resolution.Warnings);
        Assert.Contains($"Thể loại \"khong-ton-tai-{suffix}\" không có trong danh mục nên đã bỏ qua.", warning);
    }

    [Fact]
    public async Task Ambiguous_cinema_name_asks_which_cinema()
    {
        await using var db = database.CreateDbContext();
        var suffix = Guid.NewGuid().ToString("N")[..8];
        db.Cinemas.Add(Cinema($"LA{suffix.ToUpperInvariant()}", $"Beta Kép {suffix}", $"Hà Nội {suffix}"));
        db.Cinemas.Add(Cinema($"LB{suffix.ToUpperInvariant()}", $"Beta Kép {suffix}", $"TP. Hồ Chí Minh {suffix}"));
        await db.SaveChangesAsync();

        var resolver = new DiscoveryFilterResolver(db, new FixedTimeProvider(new DateTimeOffset(2026, 11, 13, 10, 0, 0, TimeSpan.Zero)));
        var resolution = await resolver.ResolveAsync("{}", Intent(cinemaName: $"beta kép {suffix}"), default);

        Assert.NotNull(resolution.ClarificationText);
        Assert.Contains($"Rạp \"beta kép {suffix}\" khớp với nhiều rạp:", resolution.ClarificationText);
        Assert.Contains($"Beta Kép {suffix} (Hà Nội {suffix})", resolution.ClarificationText);
        Assert.Contains($"Beta Kép {suffix} (TP. Hồ Chí Minh {suffix})", resolution.ClarificationText);
        Assert.Contains("Bạn muốn xem ở rạp nào?", resolution.ClarificationText);
        Assert.Null(resolution.Filters.CinemaId);
        Assert.Empty(resolution.Warnings);
    }

    [Fact]
    public async Task Unknown_cinema_name_matching_a_city_becomes_city_filter()
    {
        await using var db = database.CreateDbContext();
        var suffix = Guid.NewGuid().ToString("N")[..8];
        db.Cinemas.Add(Cinema($"LC{suffix.ToUpperInvariant()}", $"Rạp Đà {suffix}", $"Đà Nẵng {suffix}"));
        await db.SaveChangesAsync();

        var resolver = new DiscoveryFilterResolver(db, new FixedTimeProvider(new DateTimeOffset(2026, 11, 13, 10, 0, 0, TimeSpan.Zero)));
        var resolution = await resolver.ResolveAsync("{}", Intent(cinemaName: $"ĐÀ NẴNG {suffix}"), default);

        Assert.Null(resolution.ClarificationText);
        Assert.Empty(resolution.Warnings);
        Assert.Equal($"Đà Nẵng {suffix}", resolution.Filters.City);
        Assert.Null(resolution.Filters.CinemaName);
        Assert.Null(resolution.Filters.CinemaId);
    }

    [Fact]
    public async Task Unknown_cinema_name_asks_for_clarification()
    {
        await using var db = database.CreateDbContext();
        var suffix = Guid.NewGuid().ToString("N")[..8];
        // Digit-leading name keeps this cinema inside the first six listed while the shared
        // test catalog fills with cinemas seeded by other classes.
        db.Cinemas.Add(Cinema($"LD{suffix.ToUpperInvariant()}", $"0 Rạp Có {suffix}", $"Hà Nội {suffix}"));
        await db.SaveChangesAsync();

        var resolver = new DiscoveryFilterResolver(db, new FixedTimeProvider(new DateTimeOffset(2026, 11, 13, 10, 0, 0, TimeSpan.Zero)));
        var resolution = await resolver.ResolveAsync("{}", Intent(cinemaName: $"Rạp Không Có {suffix}"), default);

        Assert.NotNull(resolution.ClarificationText);
        Assert.Contains($"Mình chưa tìm thấy rạp nào khớp \"Rạp Không Có {suffix}\"", resolution.ClarificationText);
        Assert.Contains("Các rạp đang có:", resolution.ClarificationText);
        Assert.Contains($"0 Rạp Có {suffix}", resolution.ClarificationText);
        Assert.Null(resolution.Filters.CinemaId);
    }

    [Fact]
    public async Task Unknown_genre_slug_is_dropped_with_warning()
    {
        await using var db = database.CreateDbContext();
        var suffix = Guid.NewGuid().ToString("N")[..8];
        db.Genres.Add(new Genre(Guid.NewGuid(), $"Khoa học {suffix}", $"khoa-hoc-{suffix}", SeedTime));
        await db.SaveChangesAsync();

        var resolver = new DiscoveryFilterResolver(db, new FixedTimeProvider(new DateTimeOffset(2026, 11, 13, 10, 0, 0, TimeSpan.Zero)));
        var resolution = await resolver.ResolveAsync("{}", Intent(genreSlugs: [$"KHOA-HOC-{suffix}", $"khong-co-{suffix}"]), default);

        Assert.Equal([$"khoa-hoc-{suffix}"], resolution.Filters.GenreSlugs);
        var warning = Assert.Single(resolution.Warnings);
        Assert.Contains($"Thể loại \"khong-co-{suffix}\" không có trong danh mục nên đã bỏ qua.", warning);
    }

    [Fact]
    public async Task New_search_resets_carried_state()
    {
        await using var db = database.CreateDbContext();
        var suffix = Guid.NewGuid().ToString("N")[..8];
        db.Genres.Add(new Genre(Guid.NewGuid(), $"Hài {suffix}", $"hai-{suffix}", SeedTime));
        await db.SaveChangesAsync();

        var filtersJson = "{\"TopicText\":\"cũ\",\"MaxPrice\":90000,\"City\":\"Hà Nội\",\"MinSeats\":3}";
        var resolver = new DiscoveryFilterResolver(db, new FixedTimeProvider(new DateTimeOffset(2026, 11, 13, 10, 0, 0, TimeSpan.Zero)));

        var resolution = await resolver.ResolveAsync(filtersJson, Intent(DiscoveryIntentTypes.NewSearch, genreSlugs: [$"hai-{suffix}"]), default);

        var filters = resolution.Filters;
        Assert.Null(filters.TopicText);
        Assert.Null(filters.MaxPrice);
        Assert.Null(filters.City);
        Assert.Null(filters.MinSeats);
        Assert.Equal([$"hai-{suffix}"], filters.GenreSlugs);
        Assert.Null(resolution.ClarificationText);
        Assert.Empty(resolution.Warnings);
    }

    [Fact]
    public async Task Clear_fields_drop_conditions_and_keep_the_rest()
    {
        await using var db = database.CreateDbContext();
        var filtersJson = """
            {"TopicText":"hành động","MaxPrice":90000,"MinSeats":3,
             "DateFrom":"2026-11-14","DateTo":"2026-11-15"}
            """;
        var resolver = new DiscoveryFilterResolver(db, new FixedTimeProvider(new DateTimeOffset(2026, 11, 13, 10, 0, 0, TimeSpan.Zero)));

        var resolution = await resolver.ResolveAsync(filtersJson, Intent(clearFields: ["topic", "maxPrice"]), default);

        var filters = resolution.Filters;
        Assert.Null(filters.TopicText);
        Assert.Null(filters.MaxPrice);
        Assert.Equal(3, filters.MinSeats);
        Assert.Equal(new DateOnly(2026, 11, 14), filters.DateFrom);
        Assert.Equal(new DateOnly(2026, 11, 15), filters.DateTo);
        Assert.Null(resolution.ClarificationText);
        Assert.Empty(resolution.Warnings);
    }

    [Fact]
    public async Task Repeat_keeps_filters_untouched_without_entity_warnings()
    {
        await using var db = database.CreateDbContext();
        var suffix = Guid.NewGuid().ToString("N")[..8];
        var filtersJson = $"{{\"GenreSlugs\":[\"khong-xac-thuc-{suffix}\"],\"MaxPrice\":90000}}";
        var resolver = new DiscoveryFilterResolver(db, new FixedTimeProvider(new DateTimeOffset(2026, 11, 13, 10, 0, 0, TimeSpan.Zero)));

        var resolution = await resolver.ResolveAsync(filtersJson, Intent(DiscoveryIntentTypes.Repeat), default);

        var filters = resolution.Filters;
        Assert.Equal([$"khong-xac-thuc-{suffix}"], filters.GenreSlugs);
        Assert.Equal(90_000m, filters.MaxPrice);
        Assert.Null(resolution.ClarificationText);
        Assert.Empty(resolution.Warnings);
    }
}
