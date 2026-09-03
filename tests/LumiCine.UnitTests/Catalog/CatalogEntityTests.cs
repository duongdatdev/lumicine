using LumiCine.Domain.Catalog;

namespace LumiCine.UnitTests.Catalog;

public sealed class CatalogEntityTests
{
    [Fact]
    public void Movie_requires_positive_duration_and_utc_creation_time()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => new Movie(Guid.NewGuid(), "Film", 0, DateTime.UtcNow));
        Assert.Throws<ArgumentException>(() => new Movie(Guid.NewGuid(), "Film", 90, DateTime.Now));
    }

    [Fact]
    public void Movie_rejects_a_title_over_the_database_limit()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => new Movie(Guid.NewGuid(), new string('x', 201), 90, DateTime.UtcNow));
    }

    [Fact]
    public void Genre_requires_both_name_and_slug()
    {
        Assert.Throws<ArgumentException>(() => new Genre(Guid.NewGuid(), " ", "drama", DateTime.UtcNow));
        Assert.Throws<ArgumentException>(() => new Genre(Guid.NewGuid(), "Drama", "", DateTime.UtcNow));
    }

    [Fact]
    public void Movie_genre_requires_both_ids()
    {
        Assert.Throws<ArgumentException>(() => new MovieGenre(Guid.Empty, Guid.NewGuid(), DateTime.UtcNow));
        Assert.Throws<ArgumentException>(() => new MovieGenre(Guid.NewGuid(), Guid.Empty, DateTime.UtcNow));
    }

    [Fact]
    public void Movie_and_genre_accept_valid_values()
    {
        var now = DateTime.UtcNow;
        var movie = new Movie(Guid.NewGuid(), "Film", 90, now);
        var genre = new Genre(Guid.NewGuid(), "Drama", "drama", now);

        Assert.Equal("Film", movie.Title);
        Assert.Equal(90, movie.DurationMinutes);
        Assert.Equal("drama", genre.Slug);
    }
}