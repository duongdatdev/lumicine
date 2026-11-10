using System.Text.Json;
using LumiCine.Application.Common;
using LumiCine.Application.Discovery;
using LumiCine.Domain.Discovery;
using LumiCine.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace LumiCine.Infrastructure.Discovery;

public sealed class DiscoveryConversationService(
    LumiCineDbContext db,
    TimeProvider clock,
    IOptions<DiscoveryConversationOptions> options,
    IOptions<GeminiOptions> gemini,
    IDiscoveryConversationTurnStore turnStore,
    IDiscoveryIntentParser intentParser,
    IDiscoveryFilterResolver filterResolver,
    IDiscoverySearchService searchService,
    IDiscoveryAnswerWriter answerWriter) : IDiscoveryConversationService
{
    private TimeSpan InactivityWindow => TimeSpan.FromDays(options.Value.InactivityDays);
    private DateTime UtcNow => clock.GetUtcNow().UtcDateTime;

    public async Task<DiscoveryConversationDetails> CreateAsync(Guid userId, CancellationToken cancellationToken)
    {
        var conversation = new DiscoveryConversation(Guid.NewGuid(), userId, DiscoveryFilters.Empty().Serialize(), UtcNow);
        db.DiscoveryConversations.Add(conversation);
        await db.SaveChangesAsync(cancellationToken);
        return await LoadDetailsAsync(userId, conversation.Id, cancellationToken)
            ?? throw new InvalidOperationException("Conversation disappeared right after creation.");
    }

    public async Task<PagedResult<DiscoveryConversationSummary>> ListAsync(Guid userId, PageQuery page, CancellationToken cancellationToken)
    {
        page.Validate();
        var now = UtcNow;
        var expiryThreshold = now - InactivityWindow;
        var query = db.DiscoveryConversations.AsNoTracking().Where(c => c.UserId == userId)
            .OrderByDescending(c => c.LastActiveAtUtc).ThenByDescending(c => c.Id);
        var total = await query.CountAsync(cancellationToken);
        var items = page.Offset > int.MaxValue ? [] : await query.Skip((int)page.Offset).Take(page.PageSize)
            .Select(c => new DiscoveryConversationSummary(
                c.Id, c.Version, c.TurnCount, c.LastActiveAtUtc <= expiryThreshold,
                c.CreatedAtUtc, c.LastActiveAtUtc, c.LastActiveAtUtc.AddDays(options.Value.InactivityDays)))
            .ToListAsync(cancellationToken);
        return new(items, total, page.Page, page.PageSize);
    }

    public async Task<DiscoveryConversationDetails?> GetAsync(Guid userId, Guid conversationId, CancellationToken cancellationToken) =>
        await LoadDetailsAsync(userId, conversationId, cancellationToken);

    public async Task<DiscoveryChatResponse> SendMessageAsync(Guid userId, Guid conversationId, SendDiscoveryMessageRequest request, CancellationToken cancellationToken)
    {
        var now = UtcNow;
        var turnStartedAt = clock.GetUtcNow().UtcDateTime;
        // The 30 second turn budget gates further AI calls before each step; every step already
        // enforces its own timeout, so exceeding the budget just skips the remaining AI work.
        bool BudgetExceeded() =>
            (clock.GetUtcNow().UtcDateTime - turnStartedAt).TotalSeconds >= gemini.Value.TotalTimeoutSeconds;

        var begin = await turnStore.BeginAsync(userId, conversationId,
            new DiscoveryBeginTurnRequest(request.RequestId, request.ExpectedVersion, request.Message), cancellationToken);
        if (begin.ReplayedResponse is not null)
            return begin.ReplayedResponse;
        var context = begin.Context!;

        var existingFilters = DiscoveryFilters.Parse(context.FiltersJson);
        var warnings = new List<string>();
        var isDegraded = false;
        IReadOnlyList<DiscoveryMovieResult> movies = [];
        var availabilityAsOfUtc = now;
        string mode;
        string answer;
        DiscoveryFilters appliedFilters;

        ParsedDiscoveryIntent? intent = null;
        if (BudgetExceeded())
        {
            warnings.Add(WithoutAiIntentWarning);
        }
        else
        {
            var catalogHints = await LoadCatalogHintsAsync(cancellationToken);
            var parse = await intentParser.ParseAsync(new DiscoveryIntentInput(
                request.Message, context.FiltersJson, context.RecentTurns, now,
                catalogHints.GenreSlugs, catalogHints.CinemaNames, catalogHints.CityNames), cancellationToken);
            intent = parse.Intent;
            if (intent is null) warnings.Add(WithoutAiIntentWarning);
        }

        if (intent is null)
        {
            // Parse failed or the budget ran out first: keep the previously confirmed filters
            // and fall back to a keyword search over the raw message.
            isDegraded = true;
            mode = DiscoveryModes.Keyword;
            appliedFilters = existingFilters;
            var search = await searchService.SearchAsync(new DiscoverySearchInput(userId, appliedFilters, request.Message, now, PreferMessageKeywords: true), cancellationToken);
            movies = search.Movies;
            availabilityAsOfUtc = search.AvailabilityAsOfUtc;
            if (search.Mode != DiscoveryModes.Hybrid) mode = search.Mode;
            warnings.AddRange(search.Warnings);
            (answer, isDegraded) = await ComposeAnswerAsync(request.Message, appliedFilters, movies, now, warnings, isDegraded, BudgetExceeded(), cancellationToken);
        }
        else
        {
            var resolution = await filterResolver.ResolveAsync(context.FiltersJson, intent, cancellationToken);
            warnings.AddRange(resolution.Warnings);
            if (resolution.ClarificationText is not null)
            {
                // Ambiguous location: ask which one; the conversation state stays untouched.
                mode = DiscoveryModes.Clarification;
                appliedFilters = existingFilters;
                movies = [];
                answer = resolution.ClarificationText;
            }
            else
            {
                appliedFilters = resolution.Filters;
                var search = await searchService.SearchAsync(new DiscoverySearchInput(userId, appliedFilters, request.Message, now), cancellationToken);
                movies = search.Movies;
                availabilityAsOfUtc = search.AvailabilityAsOfUtc;
                mode = search.Mode;
                isDegraded = search.IsDegraded;
                warnings.AddRange(search.Warnings);
                (answer, isDegraded) = await ComposeAnswerAsync(request.Message, appliedFilters, movies, now, warnings, isDegraded, BudgetExceeded(), cancellationToken);
            }
        }

        var response = new DiscoveryChatResponse(
            conversationId, context.Version, request.RequestId, context.TurnNumber, answer,
            appliedFilters.ToView(), movies, mode, isDegraded,
            warnings.Distinct().ToList(), now, availabilityAsOfUtc);
        await turnStore.CompleteAsync(userId, conversationId, context.TurnNumber,
            appliedFilters.Serialize(), response, answer, cancellationToken);
        return response;
    }

    private const string WithoutAiIntentWarning = "Không phân tích được yêu cầu bằng AI, đã dùng tìm kiếm từ khóa.";

    private async Task<(string Answer, bool IsDegraded)> ComposeAnswerAsync(string message, DiscoveryFilters appliedFilters,
        IReadOnlyList<DiscoveryMovieResult> movies, DateTime now, List<string> warnings, bool isDegraded, bool budgetExceeded, CancellationToken cancellationToken)
    {
        var appliedView = appliedFilters.ToView();
        var evidence = movies.Select(MapEvidence).ToList();
        string TemplateAnswer() => evidence.Count > 0
            ? DiscoveryAnswerTemplates.BuildResultsAnswer(evidence, now)
            : DiscoveryAnswerTemplates.BuildNoResultsAnswer(appliedView, now);

        if (budgetExceeded)
        {
            warnings.Add("Hết thời gian xử lý AI nên đã dùng câu trả lời mẫu.");
            return (TemplateAnswer(), true);
        }

        var write = await answerWriter.WriteAsync(message, [appliedView], evidence, evidence.Count == 0, now, cancellationToken);
        if (write.Answer is null)
        {
            warnings.Add("Không viết được câu trả lời AI nên đã dùng câu mẫu.");
            return (TemplateAnswer(), true);
        }

        var validated = DiscoveryEvidenceValidator.Validate(write.Answer, evidence);
        if (!validated.IsValid)
        {
            warnings.Add("Câu trả lời AI có tham chiếu ngoài kết quả nên đã dùng câu mẫu.");
            return (TemplateAnswer(), true);
        }
        return (validated.Text, isDegraded);
    }

    private static DiscoveryEvidenceMovie MapEvidence(DiscoveryMovieResult movie) => new(
        movie.MovieId, movie.Title, movie.DurationMinutes, movie.AgeRating, movie.Genres,
        movie.Showtimes.Select(s => new DiscoveryEvidenceShowtime(
            s.ShowtimeId, s.CinemaId, s.CinemaName, s.RoomName, s.StartAtUtc, s.Price, s.AvailableSeats)).ToList());

    private async Task<DiscoveryCatalogHints> LoadCatalogHintsAsync(CancellationToken cancellationToken)
    {
        var genreSlugs = await db.Genres.AsNoTracking()
            .OrderBy(g => g.Slug).Select(g => g.Slug).ToListAsync(cancellationToken);
        var cinemas = await db.Cinemas.AsNoTracking()
            .Where(c => c.IsActive).OrderBy(c => c.Name).ThenBy(c => c.Id)
            .Select(c => new { c.Name, c.City }).ToListAsync(cancellationToken);
        return new DiscoveryCatalogHints(
            genreSlugs,
            cinemas.Select(c => c.Name).Distinct(OrdinalComparer.Instance).ToList(),
            cinemas.Select(c => c.City).Distinct(OrdinalComparer.Instance).ToList());
    }

    private sealed record DiscoveryCatalogHints(
        IReadOnlyList<string> GenreSlugs, IReadOnlyList<string> CinemaNames, IReadOnlyList<string> CityNames);

    private sealed class OrdinalComparer : IEqualityComparer<string>
    {
        public static readonly OrdinalComparer Instance = new();
        public bool Equals(string? x, string? y) => string.Equals(x, y, StringComparison.Ordinal);
        public int GetHashCode(string obj) => obj.GetHashCode(StringComparison.Ordinal);
    }

    public async Task<bool> DeleteAsync(Guid userId, Guid conversationId, CancellationToken cancellationToken)
    {
        await using var tx = await db.Database.BeginTransactionAsync(cancellationToken);
        await db.LockRowAsync("DiscoveryConversations", conversationId, cancellationToken);
        var conversation = await db.DiscoveryConversations.SingleOrDefaultAsync(c => c.Id == conversationId, cancellationToken);
        if (conversation is null || conversation.UserId != userId) return false;

        // Messages must go first: the FK from DiscoveryMessages is Restrict.
        await db.DiscoveryMessages.Where(m => m.ConversationId == conversationId).ExecuteDeleteAsync(cancellationToken);
        db.DiscoveryConversations.Remove(conversation);
        await db.SaveChangesAsync(cancellationToken);
        await tx.CommitAsync(cancellationToken);
        return true;
    }

    private async Task<DiscoveryConversationDetails?> LoadDetailsAsync(Guid userId, Guid conversationId, CancellationToken cancellationToken)
    {
        var now = UtcNow;
        var conversation = await db.DiscoveryConversations.AsNoTracking()
            .SingleOrDefaultAsync(c => c.Id == conversationId && c.UserId == userId, cancellationToken);
        if (conversation is null) return null;

        var rows = await db.DiscoveryMessages.AsNoTracking()
            .Where(m => m.ConversationId == conversationId)
            .OrderBy(m => m.TurnNumber).ThenBy(m => m.Role)
            .ToListAsync(cancellationToken);

        var messages = rows.Select(m =>
        {
            DiscoveryChatResponse? response = null;
            if (m.Role == DiscoveryMessageRole.Assistant && !string.IsNullOrWhiteSpace(m.ResponseJson))
            {
                try
                {
                    response = JsonSerializer.Deserialize<DiscoveryChatResponse>(m.ResponseJson);
                }
                catch
                {
                    // Degraded / malformed json fallback
                }
            }
            return new DiscoveryMessageView(m.TurnNumber, m.Role.ToString(), m.Content, m.CreatedAtUtc, response);
        }).ToList();

        var filters = DiscoveryFilters.Parse(conversation.FiltersJson);
        return new(conversation.Id, conversation.Version, conversation.TurnCount,
            conversation.IsExpired(now, InactivityWindow), filters.ToView(), messages,
            conversation.CreatedAtUtc, conversation.LastActiveAtUtc, conversation.ExpiresAtUtc(InactivityWindow));
    }
}
