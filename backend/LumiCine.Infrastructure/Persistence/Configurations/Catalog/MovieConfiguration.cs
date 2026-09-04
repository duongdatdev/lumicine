using LumiCine.Domain.Catalog;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace LumiCine.Infrastructure.Persistence.Configurations.Catalog;

public sealed class MovieConfiguration : IEntityTypeConfiguration<Movie>
{
    public void Configure(EntityTypeBuilder<Movie> builder)
    {
        builder.ToTable("Movies", table =>
        {
            table.HasCheckConstraint("CK_Movies_DurationMinutes", "\"DurationMinutes\" > 0");
            table.HasCheckConstraint("CK_Movies_Title_NotBlank", "btrim(\"Title\") <> ''");
        });
        builder.HasKey(x => x.Id);
        builder.Property(x => x.Id).ValueGeneratedNever();
        builder.Property(x => x.Title).HasMaxLength(200).IsRequired();
        builder.Property(x => x.OriginalTitle).HasMaxLength(200);
        builder.Property(x => x.AgeRating).HasMaxLength(20);
        builder.Property(x => x.PosterUrl).HasMaxLength(2048);
        builder.Property(x => x.ReleaseDate).HasColumnType("date");
        builder.Property(x => x.CreatedAtUtc).HasColumnType("timestamp with time zone");
        builder.Property(x => x.UpdatedAtUtc).HasColumnType("timestamp with time zone");
        builder.Property(x => x.IsActive).HasDefaultValue(true).HasSentinel(true);
    }
}