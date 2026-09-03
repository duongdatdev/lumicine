using LumiCine.Domain.Common;

namespace LumiCine.Domain.Catalog;

public sealed class MovieGenre
{
    private MovieGenre() { }

    public MovieGenre(Guid movieId, Guid genreId, DateTime createdAtUtc)
    {
        MovieId = EntityGuard.Id(movieId, nameof(movieId));
        GenreId = EntityGuard.Id(genreId, nameof(genreId));
        CreatedAtUtc = EntityGuard.Utc(createdAtUtc, nameof(createdAtUtc));
    }

    public Guid MovieId { get; private set; }
    public Guid GenreId { get; private set; }
    public DateTime CreatedAtUtc { get; private set; }
}