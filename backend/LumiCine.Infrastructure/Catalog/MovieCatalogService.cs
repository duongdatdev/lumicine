using LumiCine.Application.Catalog;
using LumiCine.Application.Common;
using LumiCine.Domain.Catalog;
using LumiCine.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Npgsql;

namespace LumiCine.Infrastructure.Catalog;

public sealed class MovieCatalogService(LumiCineDbContext db, TimeProvider timeProvider) : IMovieCatalogService
{
    public async Task<PagedResult<MovieSummary>> BrowseAsync(PageQuery page, string? search, Guid? genreId, bool includeInactive, CancellationToken cancellationToken)
    {
        page.Validate();
        var query = db.Movies.AsNoTracking().AsQueryable();
        if (!includeInactive) query = query.Where(m => m.IsActive);
        if (!string.IsNullOrWhiteSpace(search))
        {
            var term = search.Trim().ToLower();
            query = query.Where(m => m.Title.ToLower().Contains(term));
        }
        if (genreId.HasValue) query = query.Where(m => db.MovieGenres.Any(mg => mg.MovieId == m.Id && mg.GenreId == genreId));
        var total = await query.CountAsync(cancellationToken);
        if (page.Offset > int.MaxValue) return new([], total, page.Page, page.PageSize);
        var movies = await query.OrderBy(m => m.Title).ThenBy(m => m.Id).Skip((int)page.Offset).Take(page.PageSize)
            .Select(m => new MovieSummary(m.Id, m.Title, m.OriginalTitle, m.Synopsis, m.DurationMinutes, m.ReleaseDate, m.AgeRating, m.PosterUrl, m.IsActive,
                db.MovieGenres.Where(mg => mg.MovieId == m.Id).Join(db.Genres, mg => mg.GenreId, g => g.Id, (mg, g) => new GenreSummary(g.Id, g.Name, g.Slug)).OrderBy(g => g.Name).ToList()))
            .ToListAsync(cancellationToken);
        return new(movies, total, page.Page, page.PageSize);
    }

    public async Task<MovieSummary?> GetAsync(Guid id, bool includeInactive, CancellationToken cancellationToken) =>
        await BrowseOne(id, includeInactive, cancellationToken);

    public async Task<PagedResult<GenreSummary>> GetGenresAsync(PageQuery page, CancellationToken cancellationToken)
    {
        page.Validate();
        var query = db.Genres.AsNoTracking().OrderBy(g => g.Name).ThenBy(g => g.Id);
        var total = await query.CountAsync(cancellationToken);
        var items = page.Offset > int.MaxValue ? [] : await query.Skip((int)page.Offset).Take(page.PageSize)
            .Select(g => new GenreSummary(g.Id, g.Name, g.Slug)).ToListAsync(cancellationToken);
        return new(items, total, page.Page, page.PageSize);
    }

    public async Task<GenreSummary> CreateGenreAsync(string name, string slug, CancellationToken cancellationToken)
    {
        var genre = new Genre(Guid.NewGuid(), name, slug, timeProvider.GetUtcNow().UtcDateTime);
        db.Genres.Add(genre);
        await SaveGenreChangesAsync(cancellationToken);
        return new(genre.Id, genre.Name, genre.Slug);
    }

    public async Task<GenreSummary?> UpdateGenreAsync(Guid id, string name, string slug, CancellationToken cancellationToken)
    {
        var genre = await db.Genres.SingleOrDefaultAsync(g => g.Id == id, cancellationToken);
        if (genre is null) return null;
        genre.Update(name, slug);
        await SaveGenreChangesAsync(cancellationToken);
        return new(genre.Id, genre.Name, genre.Slug);
    }

    public async Task<bool> DeleteGenreAsync(Guid id, CancellationToken cancellationToken)
    {
        await using var tx = await db.Database.BeginTransactionAsync(cancellationToken);
        await LockGenres([id], cancellationToken);
        var genre = await db.Genres.SingleOrDefaultAsync(g => g.Id == id, cancellationToken);
        if (genre is null) return false;
        if (await db.MovieGenres.AnyAsync(mg => mg.GenreId == id, cancellationToken))
            throw new ApiException(409, "Genre is assigned to one or more movies.");
        db.Genres.Remove(genre);
        await SaveGenreChangesAsync(cancellationToken);
        await tx.CommitAsync(cancellationToken);
        return true;
    }

    public async Task<MovieSummary> CreateAsync(MovieMetadata metadata, CancellationToken cancellationToken)
    {
        ValidateGenreIds(metadata.GenreIds);
        await using var tx = await db.Database.BeginTransactionAsync(cancellationToken);
        await RequireGenres(metadata.GenreIds, cancellationToken);
        var now = timeProvider.GetUtcNow().UtcDateTime;
        var movie = new Movie(Guid.NewGuid(), metadata.Title, metadata.DurationMinutes, now, metadata.OriginalTitle, metadata.Synopsis,
            metadata.ReleaseDate, metadata.AgeRating);
        db.Movies.Add(movie);
        AddGenres(movie.Id, metadata.GenreIds, now);
        await db.SaveChangesAsync(cancellationToken);
        await tx.CommitAsync(cancellationToken);
        return (await BrowseOne(movie.Id, true, cancellationToken))!;
    }

    public async Task<MovieSummary?> UpdateAsync(Guid id, MovieMetadata metadata, CancellationToken cancellationToken)
    {
        ValidateGenreIds(metadata.GenreIds);
        await using var tx = await db.Database.BeginTransactionAsync(cancellationToken);
        var movie = await LockMovie(id, cancellationToken);
        if (movie is null) return null;
        await RequireGenres(metadata.GenreIds, cancellationToken);
        if (movie.DurationMinutes != metadata.DurationMinutes && await HasFutureShowtime(id, cancellationToken))
            throw new ApiException(409, "Movie duration cannot change while scheduled showtimes remain.");
        movie.Update(metadata.Title, metadata.OriginalTitle, metadata.Synopsis, metadata.DurationMinutes, metadata.ReleaseDate, metadata.AgeRating, timeProvider.GetUtcNow().UtcDateTime);
        await ReplaceGenres(id, metadata.GenreIds, cancellationToken);
        await db.SaveChangesAsync(cancellationToken);
        await tx.CommitAsync(cancellationToken);
        return await BrowseOne(id, true, cancellationToken);
    }

    public async Task<bool> SetActiveAsync(Guid id, bool isActive, CancellationToken cancellationToken)
    {
        await using var tx = await db.Database.BeginTransactionAsync(cancellationToken);
        var movie = await LockMovie(id, cancellationToken);
        if (movie is null) return false;
        if (!isActive && await HasFutureShowtime(id, cancellationToken))
            throw new ApiException(409, "Movie cannot be deactivated while scheduled showtimes remain.");
        movie.SetActive(isActive, timeProvider.GetUtcNow().UtcDateTime);
        await db.SaveChangesAsync(cancellationToken);
        await tx.CommitAsync(cancellationToken);
        return true;
    }

    public async Task<bool> AssignGenresAsync(Guid id, IReadOnlyList<Guid> genreIds, CancellationToken cancellationToken)
    {
        ValidateGenreIds(genreIds);
        await using var tx = await db.Database.BeginTransactionAsync(cancellationToken);
        if (await LockMovie(id, cancellationToken) is null) return false;
        await RequireGenres(genreIds, cancellationToken);
        await ReplaceGenres(id, genreIds, cancellationToken);
        await db.SaveChangesAsync(cancellationToken);
        await tx.CommitAsync(cancellationToken);
        return true;
    }

    private async Task<MovieSummary?> BrowseOne(Guid id, bool includeInactive, CancellationToken ct) =>
        await db.Movies.AsNoTracking().Where(m => m.Id == id && (includeInactive || m.IsActive))
            .Select(m => new MovieSummary(m.Id, m.Title, m.OriginalTitle, m.Synopsis, m.DurationMinutes, m.ReleaseDate, m.AgeRating, m.PosterUrl, m.IsActive,
                db.MovieGenres.Where(mg => mg.MovieId == m.Id).Join(db.Genres, mg => mg.GenreId, g => g.Id, (mg, g) => new GenreSummary(g.Id, g.Name, g.Slug)).OrderBy(g => g.Name).ToList()))
            .SingleOrDefaultAsync(ct);

    private async Task<Movie?> LockMovie(Guid id, CancellationToken ct)
    {
        await db.Database.ExecuteSqlInterpolatedAsync($"SELECT 1 FROM \"Movies\" WHERE \"Id\" = {id} FOR UPDATE", ct);
        return await db.Movies.SingleOrDefaultAsync(m => m.Id == id, ct);
    }

    private Task<bool> HasFutureShowtime(Guid movieId, CancellationToken ct)
    {
        var now = timeProvider.GetUtcNow().UtcDateTime;
        return db.Showtimes.AnyAsync(s => s.MovieId == movieId && s.Status == Domain.Scheduling.ShowtimeStatus.Scheduled && s.EndAtUtc.AddMinutes(15) > now, ct);
    }

    private async Task RequireGenres(IReadOnlyList<Guid> ids, CancellationToken ct)
    {
        await LockGenres(ids, ct);
        if (ids.Count != 0 && await db.Genres.CountAsync(g => ids.Contains(g.Id), ct) != ids.Count)
            throw new ApiException(400, "One or more genres do not exist.");
    }

    private static void ValidateGenreIds(IReadOnlyList<Guid> ids)
    {
        if (ids.Distinct().Count() != ids.Count || ids.Any(id => id == Guid.Empty))
            throw new ApiException(400, "Genre IDs must be non-empty and unique.");
    }

    private void AddGenres(Guid movieId, IReadOnlyList<Guid> ids, DateTime now) =>
        db.MovieGenres.AddRange(ids.Select(id => new MovieGenre(movieId, id, now)));

    private async Task ReplaceGenres(Guid movieId, IReadOnlyList<Guid> ids, CancellationToken ct)
    {
        var existing = await db.MovieGenres.Where(mg => mg.MovieId == movieId).ToListAsync(ct);
        var desired = ids.ToHashSet();
        db.MovieGenres.RemoveRange(existing.Where(mg => !desired.Contains(mg.GenreId)));
        var present = existing.Select(mg => mg.GenreId).ToHashSet();
        db.MovieGenres.AddRange(ids.Where(id => !present.Contains(id)).Select(id => new MovieGenre(movieId, id, timeProvider.GetUtcNow().UtcDateTime)));
    }

    private async Task LockGenres(IReadOnlyList<Guid> ids, CancellationToken ct)
    {
        foreach (var id in ids.Order())
            await db.Database.ExecuteSqlInterpolatedAsync($"SELECT 1 FROM \"Genres\" WHERE \"Id\" = {id} FOR UPDATE", ct);
    }

    private async Task SaveGenreChangesAsync(CancellationToken ct)
    {
        try { await db.SaveChangesAsync(ct); }
        catch (DbUpdateException ex) when (ex.InnerException is PostgresException { SqlState: PostgresErrorCodes.UniqueViolation })
        {
            throw new ApiException(409, "A genre with the same name or slug already exists.");
        }
    }
}
