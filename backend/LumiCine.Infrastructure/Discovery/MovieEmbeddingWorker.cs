using LumiCine.Application.Discovery;
using LumiCine.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace LumiCine.Infrastructure.Discovery;

public sealed class MovieEmbeddingWorker(
    IServiceScopeFactory scopeFactory,
    TimeProvider clock,
    GeminiAiClients clients,
    IOptions<GeminiOptions> gemini,
    ILogger<MovieEmbeddingWorker> logger) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                await ProcessDueDocumentsAsync(20, stoppingToken);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "Movie embedding pass failed.");
            }

            try { await Task.Delay(TimeSpan.FromSeconds(2), clock, stoppingToken); }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested) { break; }
        }
    }

    // One embedding pass over the due, leased queue rows; exposed for deterministic tests.
    public async Task ProcessDueDocumentsAsync(int batchSize, CancellationToken stoppingToken)
    {
        await using var scope = scopeFactory.CreateAsyncScope();
        var documents = scope.ServiceProvider.GetRequiredService<IMovieSearchDocumentService>();
        var claimed = await documents.ClaimDueAsync(batchSize, stoppingToken);
        if (!clients.IsConfigured)
        {
            logger.LogWarning("Gemini is not configured; skipping embedding batch.");
            return;
        }
        var builder = scope.ServiceProvider.GetRequiredService<IMovieSearchDocumentBuilder>();
        var db = scope.ServiceProvider.GetRequiredService<LumiCineDbContext>();
        foreach (var work in claimed)
        {
            var movie = await db.Movies.AsNoTracking().SingleOrDefaultAsync(m => m.Id == work.MovieId, stoppingToken);
            if (movie is null)
            {
                logger.LogWarning("Skipping embedding for document {DocumentId}; movie {MovieId} no longer exists.", work.DocumentId, work.MovieId);
                continue;
            }
            var genreNames = await db.MovieGenres.Where(mg => mg.MovieId == work.MovieId)
                .Join(db.Genres, mg => mg.GenreId, g => g.Id, (mg, g) => g)
                .OrderBy(g => g.Name).ThenBy(g => g.Id)
                .Select(g => g.Name)
                .ToListAsync(stoppingToken);
            var text = builder.BuildDocumentText(movie.Title, movie.OriginalTitle, movie.Synopsis, genreNames);
            if (builder.ComputeSourceHash(text) != work.SourceHash)
            {
                logger.LogWarning("Skipping embedding for document {DocumentId}; it was invalidated after the claim and its lease will expire.", work.DocumentId);
                continue;
            }
            try
            {
                var embedding = await clients.EmbeddingGenerator!.GenerateAsync([work.DocumentText],
                    new EmbeddingGenerationOptions { Dimensions = gemini.Value.EmbeddingDimensions }, stoppingToken);
                var vector = embedding.Single().Vector.ToArray();
                if (vector.Length != gemini.Value.EmbeddingDimensions)
                {
                    await documents.RetryAsync(work.DocumentId,
                        $"Embedding vector has {vector.Length} dimensions, expected {gemini.Value.EmbeddingDimensions}.", stoppingToken);
                    continue;
                }
                await documents.CompleteAsync(work.DocumentId, gemini.Value.EmbeddingModel, gemini.Value.EmbeddingDimensions, vector, work.SourceHash, stoppingToken);
            }
            catch (Exception ex)
            {
                logger.LogWarning(ex, "Could not embed the search document {DocumentId}.", work.DocumentId);
                await documents.RetryAsync(work.DocumentId, ex.Message, stoppingToken);
            }
        }
    }
}
