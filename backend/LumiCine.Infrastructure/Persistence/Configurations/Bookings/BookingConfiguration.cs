using LumiCine.Domain.Bookings;
using LumiCine.Domain.Scheduling;
using LumiCine.Infrastructure.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace LumiCine.Infrastructure.Persistence.Configurations.Bookings;

public sealed class BookingConfiguration : IEntityTypeConfiguration<Booking>
{
    public void Configure(EntityTypeBuilder<Booking> builder)
    {
        builder.ToTable("Bookings", table =>
        {
            table.HasCheckConstraint("CK_Bookings_Code_NotBlank", "btrim(\"Code\") <> ''");
            table.HasCheckConstraint("CK_Bookings_TotalAmount", "\"TotalAmount\" >= 0");
            table.HasCheckConstraint("CK_Bookings_Status", "\"Status\" IN ('Pending', 'Confirmed', 'Cancelled', 'Expired')");
        });
        builder.HasKey(x => x.Id);
        builder.Property(x => x.Id).ValueGeneratedNever();
        builder.Property(x => x.Code).HasMaxLength(40).IsRequired();
        builder.Property(x => x.Status).HasConversion<string>().HasMaxLength(20).IsRequired();
        builder.Property(x => x.HoldExpiresAtUtc).HasColumnType("timestamp with time zone");
        builder.Property(x => x.TotalAmount).HasColumnType("numeric(18,2)");
        builder.Property(x => x.CreatedAtUtc).HasColumnType("timestamp with time zone");
        builder.Property(x => x.UpdatedAtUtc).HasColumnType("timestamp with time zone");
        builder.HasAlternateKey(x => new { x.Id, x.ShowtimeId });
        builder.HasOne<Showtime>().WithMany().HasForeignKey(x => x.ShowtimeId).OnDelete(DeleteBehavior.Restrict);
        builder.HasOne<ApplicationUser>().WithMany().HasForeignKey(x => x.UserId).OnDelete(DeleteBehavior.Restrict);
        builder.HasIndex(x => x.Code).IsUnique();
    }
}
