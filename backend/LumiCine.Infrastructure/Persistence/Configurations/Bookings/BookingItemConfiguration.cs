using LumiCine.Domain.Bookings;
using LumiCine.Domain.Scheduling;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace LumiCine.Infrastructure.Persistence.Configurations.Bookings;

public sealed class BookingItemConfiguration : IEntityTypeConfiguration<BookingItem>
{
    public void Configure(EntityTypeBuilder<BookingItem> builder)
    {
        builder.ToTable("BookingItems", table =>
        {
            table.HasCheckConstraint("CK_BookingItems_UnitPrice", "\"UnitPrice\" >= 0");
        });
        builder.HasKey(x => x.Id);
        builder.Property(x => x.Id).ValueGeneratedNever();
        builder.Property(x => x.UnitPrice).HasColumnType("numeric(18,2)");
        builder.Property(x => x.CreatedAtUtc).HasColumnType("timestamp with time zone");
        builder.HasOne<Booking>().WithMany()
            .HasForeignKey(x => new { x.BookingId, x.ShowtimeId })
            .HasPrincipalKey(x => new { x.Id, x.ShowtimeId })
            .OnDelete(DeleteBehavior.Restrict);
        builder.HasOne<ShowtimeSeat>().WithMany()
            .HasForeignKey(x => new { x.ShowtimeSeatId, x.ShowtimeId })
            .HasPrincipalKey(x => new { x.Id, x.ShowtimeId })
            .OnDelete(DeleteBehavior.Restrict);
        builder.HasIndex(x => new { x.BookingId, x.ShowtimeSeatId }).IsUnique();
    }
}