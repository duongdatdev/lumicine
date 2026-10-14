using LumiCine.Domain.Bookings;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace LumiCine.Infrastructure.Persistence.Configurations.Bookings;

public sealed class PaymentConfiguration : IEntityTypeConfiguration<Payment>
{
    public void Configure(EntityTypeBuilder<Payment> builder)
    {
        builder.ToTable("Payments", table =>
        {
            table.HasCheckConstraint("CK_Payments_Amount", "\"Amount\" >= 0");
            table.HasCheckConstraint("CK_Payments_Currency_NotBlank", "btrim(\"Currency\") <> ''");
            table.HasCheckConstraint("CK_Payments_Status", "\"Status\" IN ('Pending', 'Succeeded', 'Failed')");
            table.HasCheckConstraint("CK_Payments_ProcessingStatus", "\"ProcessingStatus\" IN ('None', 'Fulfilled', 'RequiresRefundReview', 'RefundRecorded')");
            table.HasCheckConstraint("CK_Payments_Succeeded_Records_Use", "\"Status\" <> 'Succeeded' OR \"ProcessingStatus\" <> 'None'");
            table.HasCheckConstraint("CK_Payments_RefundRecorded_Requires_Success", "\"ProcessingStatus\" <> 'RefundRecorded' OR \"Status\" = 'Succeeded'");
        });
        builder.HasKey(x => x.Id);
        builder.Property(x => x.Id).ValueGeneratedNever();
        builder.Property(x => x.MerchantReference).HasMaxLength(100).IsRequired();
        builder.Property(x => x.Amount).HasColumnType("numeric(18,2)");
        builder.Property(x => x.Currency).HasMaxLength(10).IsRequired();
        builder.Property(x => x.Status).HasConversion<string>().HasMaxLength(20).IsRequired();
        builder.Property(x => x.ProcessingStatus).HasConversion<string>().HasMaxLength(20).IsRequired();
        builder.Property(x => x.VnPayTransactionId).HasMaxLength(64);
        builder.Property(x => x.ResponseCode).HasMaxLength(16);
        builder.Property(x => x.TransactionStatus).HasMaxLength(16);
        builder.Property(x => x.PaymentUrl).HasMaxLength(1000);
        builder.Property(x => x.ExpiresAtUtc).HasColumnType("timestamp with time zone");
        builder.Property(x => x.CreatedAtUtc).HasColumnType("timestamp with time zone");
        builder.Property(x => x.ProcessedAtUtc).HasColumnType("timestamp with time zone");
        builder.Property(x => x.UpdatedAtUtc).HasColumnType("timestamp with time zone");
        builder.HasOne<Booking>().WithMany().HasForeignKey(x => x.BookingId).OnDelete(DeleteBehavior.Restrict);
        builder.HasIndex(x => x.MerchantReference).IsUnique();
        builder.HasIndex(x => new { x.BookingId, x.RequestId }).IsUnique();
        builder.HasIndex(x => x.VnPayTransactionId).IsUnique().HasFilter("\"VnPayTransactionId\" IS NOT NULL");
        builder.HasIndex(x => x.BookingId, "IX_Payments_BookingId_Pending").IsUnique().HasFilter("\"Status\" = 'Pending'");
        builder.HasIndex(x => x.BookingId, "IX_Payments_BookingId_Fulfilled").IsUnique().HasFilter("\"ProcessingStatus\" = 'Fulfilled'");
        builder.HasIndex(x => new { x.Status, x.ExpiresAtUtc });
    }
}
