using LumiCine.Application.Common;
using LumiCine.Application.Media;
using LumiCine.Domain.Catalog;
using LumiCine.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Configuration;

namespace LumiCine.Infrastructure.Media;

public sealed class LocalPosterService(
    LumiCineDbContext db,
    TimeProvider timeProvider,
    IConfiguration configuration,
    ILogger<LocalPosterService> logger) : IPosterService
{
    private const int MaxFileBytes = 5 * 1024 * 1024;
    private const long MaxPixels = 20_000_000;
    private const int MaxEdge = 1600;
    private readonly string _directory = Path.GetFullPath(string.IsNullOrWhiteSpace(configuration["Storage:PostersPath"])
        ? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "LumiCine", "uploads", "posters")
        : configuration["Storage:PostersPath"]!);

    public async Task<string> UploadAsync(Guid movieId, Stream content, string contentType, long length, CancellationToken cancellationToken)
    {
        if (length > MaxFileBytes) throw new ApiException(413, "Poster files must be 5 MiB or smaller.");
        if (!IsSupportedContentType(contentType)) throw new ApiException(415, "Only JPEG and PNG posters are supported.");
        var bytes = await ReadBoundedAsync(content, cancellationToken);
        var normalized = PosterImageNormalizer.Normalize(bytes, contentType);
        Directory.CreateDirectory(_directory);
        var newId = Guid.NewGuid();
        var newPath = GetPath(newId);
        string? oldUrl = null;
        try
        {
            await File.WriteAllBytesAsync(newPath, normalized, cancellationToken);
            await using var transaction = await db.Database.BeginTransactionAsync(cancellationToken);
            await db.Database.ExecuteSqlInterpolatedAsync($"SELECT 1 FROM \"Movies\" WHERE \"Id\" = {movieId} FOR UPDATE", cancellationToken);
            var movie = await db.Movies.SingleOrDefaultAsync(m => m.Id == movieId, cancellationToken);
            if (movie is null) throw new ApiException(404, "Movie was not found.");
            oldUrl = movie.PosterUrl;
            movie.SetPosterUrl($"/api/media/posters/{newId:D}", timeProvider.GetUtcNow().UtcDateTime);
            await db.SaveChangesAsync(cancellationToken);
            await transaction.CommitAsync(cancellationToken);
        }
        catch
        {
            await DeleteFileSafelyAsync(newId);
            throw;
        }

        if (oldUrl is not null && Guid.TryParse(Path.GetFileNameWithoutExtension(oldUrl), out var oldId) && oldId != newId)
            await DeleteFileSafelyAsync(oldId);
        return $"/api/media/posters/{newId:D}";
    }

    public async Task<bool> DeleteAsync(Guid movieId, CancellationToken cancellationToken)
    {
        string? oldUrl;
        await using (var transaction = await db.Database.BeginTransactionAsync(cancellationToken))
        {
            await db.Database.ExecuteSqlInterpolatedAsync($"SELECT 1 FROM \"Movies\" WHERE \"Id\" = {movieId} FOR UPDATE", cancellationToken);
            var movie = await db.Movies.SingleOrDefaultAsync(m => m.Id == movieId, cancellationToken);
            if (movie is null) return false;
            oldUrl = movie.PosterUrl;
            movie.SetPosterUrl(null, timeProvider.GetUtcNow().UtcDateTime);
            await db.SaveChangesAsync(cancellationToken);
            await transaction.CommitAsync(cancellationToken);
        }

        if (oldUrl is not null && Guid.TryParse(Path.GetFileNameWithoutExtension(oldUrl), out var oldId))
            await DeleteFileSafelyAsync(oldId);
        return true;
    }

    public Task<PosterContent?> OpenAsync(Guid posterId, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var path = GetPath(posterId);
        if (!File.Exists(path)) return Task.FromResult<PosterContent?>(null);
        try
        {
            Stream stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read);
            return Task.FromResult<PosterContent?>(new(stream, "image/jpeg"));
        }
        catch (FileNotFoundException) { return Task.FromResult<PosterContent?>(null); }
        catch (DirectoryNotFoundException) { return Task.FromResult<PosterContent?>(null); }
    }

    private string GetPath(Guid id) => Path.Combine(_directory, $"{id:N}.jpg");

    private static bool IsSupportedContentType(string contentType) =>
        string.Equals(contentType, "image/jpeg", StringComparison.OrdinalIgnoreCase) ||
        string.Equals(contentType, "image/png", StringComparison.OrdinalIgnoreCase);

    private static async Task<byte[]> ReadBoundedAsync(Stream content, CancellationToken cancellationToken)
    {
        await using var buffer = new MemoryStream();
        var chunk = new byte[81920];
        while (true)
        {
            var read = await content.ReadAsync(chunk, cancellationToken);
            if (read == 0) break;
            if (buffer.Length + read > MaxFileBytes) throw new ApiException(413, "Poster files must be 5 MiB or smaller.");
            await buffer.WriteAsync(chunk.AsMemory(0, read), cancellationToken);
        }
        return buffer.ToArray();
    }

    private Task DeleteFileSafelyAsync(Guid id)
    {
        try { File.Delete(GetPath(id)); }
        catch (Exception exception) { logger.LogWarning(exception, "Failed to remove poster file {PosterId}.", id); }
        return Task.CompletedTask;
    }
}
