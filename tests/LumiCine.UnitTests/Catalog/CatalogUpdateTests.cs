using LumiCine.Domain.Catalog;

namespace LumiCine.UnitTests.Catalog;

public sealed class CatalogUpdateTests
{
    [Fact]
    public void Movie_update_trims_metadata_and_enforces_duration_and_synopsis_limits()
    {
        var movie = new Movie(Guid.NewGuid(), " Old ", 90, DateTime.UtcNow);
        var now = DateTime.UtcNow;

        movie.Update(" New ", " Original ", " Plot ", 120, new DateOnly(2026, 1, 1), " PG-13 ", now);

        Assert.Equal("New", movie.Title);
        Assert.Equal("Original", movie.OriginalTitle);
        Assert.Equal("Plot", movie.Synopsis);
        Assert.Equal(120, movie.DurationMinutes);
        Assert.Equal(now, movie.UpdatedAtUtc);
        Assert.Throws<ArgumentOutOfRangeException>(() => movie.Update("Film", null, null, 601, null, null, now));
        Assert.Throws<ArgumentOutOfRangeException>(() => movie.Update("Film", null, new string('x', 10001), 90, null, null, now));
    }

    [Fact]
    public void Genre_update_trims_values_and_normalizes_slug()
    {
        var genre = new Genre(Guid.NewGuid(), " Drama ", "DRAMA", DateTime.UtcNow);

        genre.Update(" Sci-Fi ", " Sci-Fi ");

        Assert.Equal("Sci-Fi", genre.Name);
        Assert.Equal("sci-fi", genre.Slug);
    }
}
