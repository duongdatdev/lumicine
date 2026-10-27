using LumiCine.Domain.Catalog;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace LumiCine.Infrastructure.Persistence.Configurations.Catalog;

public sealed class MovieSearchDocumentConfiguration : IEntityTypeConfiguration<MovieSearchDocument>
{
    public void Configure(EntityTypeBuilder<MovieSearchDocument> builder)
    {
        builder.ToTable("MovieSearchDocuments", table =>
        {
            table.HasCheckConstraint("CK_MovieSearchDocuments_Status", "\"Status\" IN ('Pending', 'Indexed', 'Failed')");
            table.HasCheckConstraint("CK_MovieSearchDocuments_AttemptCount", "\"AttemptCount\" >= 0 AND \"AttemptCount\" <= 5");
            table.HasCheckConstraint("CK_MovieSearchDocuments_HashHex", "\"SourceHash\" ~ '^[0-9A-Fa-f]{64}$'");
            table.HasCheckConstraint("CK_MovieSearchDocuments_DocumentTextNotEmpty", "btrim(\"DocumentText\") <> ''");
        });
        builder.HasKey(x => x.Id);
        builder.Property(x => x.Id).ValueGeneratedNever();
        builder.Property(x => x.DocumentText).HasMaxLength(4000).IsRequired();
        builder.Property(x => x.SourceHash).HasMaxLength(64).IsRequired();
        builder.Property(x => x.EmbeddingModelId).HasMaxLength(100);
        builder.Property(x => x.LastError).HasMaxLength(500);
        builder.Property(x => x.Vector).HasColumnType("real[]");
        builder.Property(x => x.Status).HasConversion<string>().HasMaxLength(20).IsRequired();
        builder.Property(x => x.NextAttemptAtUtc).HasColumnType("timestamp with time zone");
        builder.Property(x => x.LeaseUntilUtc).HasColumnType("timestamp with time zone");
        builder.Property(x => x.IndexedAtUtc).HasColumnType("timestamp with time zone");
        builder.Property(x => x.CreatedAtUtc).HasColumnType("timestamp with time zone");
        builder.Property(x => x.UpdatedAtUtc).HasColumnType("timestamp with time zone");
        builder.HasOne<Movie>().WithMany().HasForeignKey(x => x.MovieId).OnDelete(DeleteBehavior.Restrict);
        builder.HasIndex(x => x.MovieId).IsUnique();
        builder.HasIndex(x => new { x.Status, x.NextAttemptAtUtc }).HasFilter("\"Status\" = 'Pending'");
    }
}
