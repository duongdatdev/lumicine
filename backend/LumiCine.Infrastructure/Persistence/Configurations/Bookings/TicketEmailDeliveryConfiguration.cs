using LumiCine.Domain.Bookings;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace LumiCine.Infrastructure.Persistence.Configurations.Bookings;

public sealed class TicketEmailDeliveryConfiguration : IEntityTypeConfiguration<TicketEmailDelivery>
{
    public void Configure(EntityTypeBuilder<TicketEmailDelivery> builder)
    {
        builder.ToTable("TicketEmailDeliveries", table =>
        {
            table.HasCheckConstraint("CK_TicketEmailDeliveries_Status", "\"Status\" IN ('Pending', 'Sent', 'Failed')");
            table.HasCheckConstraint("CK_TicketEmailDeliveries_AttemptCount", "\"AttemptCount\" >= 0 AND \"AttemptCount\" <= 5");
        });
        builder.HasKey(x => x.Id);
        builder.Property(x => x.Id).ValueGeneratedNever();
        builder.Property(x => x.Status).HasConversion<string>().HasMaxLength(20).IsRequired();
        builder.Property(x => x.LastError).HasMaxLength(500);
        builder.Property(x => x.NextAttemptAtUtc).HasColumnType("timestamp with time zone");
        builder.Property(x => x.LeaseUntilUtc).HasColumnType("timestamp with time zone");
        builder.Property(x => x.SentAtUtc).HasColumnType("timestamp with time zone");
        builder.Property(x => x.CreatedAtUtc).HasColumnType("timestamp with time zone");
        builder.Property(x => x.UpdatedAtUtc).HasColumnType("timestamp with time zone");
        builder.HasOne<Booking>().WithMany().HasForeignKey(x => x.BookingId).OnDelete(DeleteBehavior.Restrict);
        builder.HasIndex(x => new { x.Status, x.NextAttemptAtUtc }).HasFilter("\"Status\" = 'Pending'");
        builder.HasIndex(x => x.BookingId).IsUnique().HasFilter("\"Status\" = 'Pending'");
        builder.HasIndex(x => x.BookingId);
    }
}
