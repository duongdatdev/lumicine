using LumiCine.Domain.Catalog;
using LumiCine.Domain.Cinemas;
using LumiCine.Domain.Scheduling;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace LumiCine.Infrastructure.Persistence.Configurations.Scheduling;

public sealed class ShowtimeConfiguration : IEntityTypeConfiguration<Showtime>
{
    public void Configure(EntityTypeBuilder<Showtime> builder)
    {
        builder.ToTable("Showtimes", table =>
        {
            table.HasCheckConstraint("CK_Showtimes_TimeRange", "\"EndAtUtc\" > \"StartAtUtc\"");
            table.HasCheckConstraint("CK_Showtimes_BasePrice", "\"BasePrice\" >= 0");
            table.HasCheckConstraint("CK_Showtimes_Status", "\"Status\" IN ('Scheduled', 'Cancelled', 'Completed')");
        });
        builder.HasKey(x => x.Id);
        builder.Property(x => x.Id).ValueGeneratedNever();
        builder.Property(x => x.StartAtUtc).HasColumnType("timestamp with time zone");
        builder.Property(x => x.EndAtUtc).HasColumnType("timestamp with time zone");
        builder.Property(x => x.BasePrice).HasColumnType("numeric(18,2)");
        builder.Property(x => x.Status).HasConversion<string>().HasMaxLength(20).IsRequired();
        builder.Property(x => x.CreatedAtUtc).HasColumnType("timestamp with time zone");
        builder.Property(x => x.UpdatedAtUtc).HasColumnType("timestamp with time zone");
        builder.HasAlternateKey(x => new { x.Id, x.RoomId });
        builder.HasIndex(x => new { x.MovieId, x.StartAtUtc });
        builder.HasIndex(x => new { x.StartAtUtc, x.Status });
        builder.HasOne<Movie>().WithMany().HasForeignKey(x => x.MovieId).OnDelete(DeleteBehavior.Restrict);
        builder.HasOne<Room>().WithMany().HasForeignKey(x => x.RoomId).OnDelete(DeleteBehavior.Restrict);
    }
}
