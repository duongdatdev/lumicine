using LumiCine.Domain.Cinemas;
using LumiCine.Domain.Scheduling;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace LumiCine.Infrastructure.Persistence.Configurations.Scheduling;

public sealed class ShowtimeSeatConfiguration : IEntityTypeConfiguration<ShowtimeSeat>
{
    public void Configure(EntityTypeBuilder<ShowtimeSeat> builder)
    {
        builder.ToTable("ShowtimeSeats", table =>
        {
            table.HasCheckConstraint("CK_ShowtimeSeats_Price", "\"Price\" >= 0");
            table.HasCheckConstraint("CK_ShowtimeSeats_Status", "\"Status\" IN ('Available', 'Held', 'Sold')");
        });
        builder.HasKey(x => x.Id);
        builder.Property(x => x.Id).ValueGeneratedNever();
        builder.Property(x => x.Status).HasConversion<string>().HasMaxLength(20).IsRequired();
        builder.Property(x => x.Price).HasColumnType("numeric(18,2)");
        builder.Property(x => x.CreatedAtUtc).HasColumnType("timestamp with time zone");
        builder.HasAlternateKey(x => new { x.Id, x.ShowtimeId });
        builder.HasOne<Showtime>().WithMany()
            .HasForeignKey(x => new { x.ShowtimeId, x.RoomId })
            .HasPrincipalKey(x => new { x.Id, x.RoomId })
            .OnDelete(DeleteBehavior.Restrict);
        builder.HasOne<Seat>().WithMany()
            .HasForeignKey(x => new { x.SeatId, x.RoomId })
            .HasPrincipalKey(x => new { x.Id, x.RoomId })
            .OnDelete(DeleteBehavior.Restrict);
        builder.HasIndex(x => new { x.ShowtimeId, x.SeatId }).IsUnique();
    }
}