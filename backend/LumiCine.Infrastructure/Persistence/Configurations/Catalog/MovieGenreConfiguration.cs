using LumiCine.Domain.Catalog;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace LumiCine.Infrastructure.Persistence.Configurations.Catalog;

public sealed class MovieGenreConfiguration : IEntityTypeConfiguration<MovieGenre>
{
    public void Configure(EntityTypeBuilder<MovieGenre> builder)
    {
        builder.ToTable("MovieGenres");
        builder.HasKey(x => new { x.MovieId, x.GenreId });
        builder.Property(x => x.CreatedAtUtc).HasColumnType("timestamp with time zone");
        builder.HasOne<Movie>().WithMany().HasForeignKey(x => x.MovieId).OnDelete(DeleteBehavior.Cascade);
        builder.HasOne<Genre>().WithMany().HasForeignKey(x => x.GenreId).OnDelete(DeleteBehavior.Cascade);
    }
}