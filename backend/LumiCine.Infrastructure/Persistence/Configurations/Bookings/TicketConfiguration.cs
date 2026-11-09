using LumiCine.Domain.Bookings;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace LumiCine.Infrastructure.Persistence.Configurations.Bookings;

public sealed class TicketConfiguration : IEntityTypeConfiguration<Ticket>
{
    public void Configure(EntityTypeBuilder<Ticket> builder)
    {
        builder.ToTable("Tickets", table =>
        {
            table.HasCheckConstraint("CK_Tickets_Code_NotBlank", "btrim(\"Code\") <> ''");
            table.HasCheckConstraint("CK_Tickets_Status", "\"Status\" IN ('Issued', 'Used', 'Voided')");
        });
        builder.HasKey(x => x.Id);
        builder.Property(x => x.Id).ValueGeneratedNever();
        builder.Property(x => x.Code).HasMaxLength(60).IsRequired();
        builder.Property(x => x.Status).HasConversion<string>().HasMaxLength(20).IsRequired();
        builder.Property(x => x.IssuedAtUtc).HasColumnType("timestamp with time zone");
        builder.Property(x => x.UsedAtUtc).HasColumnType("timestamp with time zone");
        builder.Property(x => x.CreatedAtUtc).HasColumnType("timestamp with time zone");
        builder.HasOne<BookingItem>().WithMany().HasForeignKey(x => x.BookingItemId).OnDelete(DeleteBehavior.Restrict);
        builder.HasIndex(x => x.BookingItemId).IsUnique();
        builder.HasIndex(x => x.Code).IsUnique();
        builder.HasIndex(x => x.IssuedAtUtc);
        builder.HasIndex(x => x.UsedAtUtc).HasFilter("\"UsedAtUtc\" IS NOT NULL");
    }
}
