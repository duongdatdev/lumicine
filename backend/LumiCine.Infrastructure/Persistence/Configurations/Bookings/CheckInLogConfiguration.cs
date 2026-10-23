using LumiCine.Domain.Bookings;
using LumiCine.Domain.Catalog;
using LumiCine.Domain.Cinemas;
using LumiCine.Domain.Scheduling;
using LumiCine.Infrastructure.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace LumiCine.Infrastructure.Persistence.Configurations.Bookings;

public sealed class CheckInLogConfiguration : IEntityTypeConfiguration<CheckInLog>
{
    public void Configure(EntityTypeBuilder<CheckInLog> builder)
    {
        builder.ToTable("CheckInLogs", table =>
        {
            table.HasCheckConstraint("CK_CheckInLogs_Result", "\"Result\" IN ('Success', 'DuplicateScan', 'TicketNotFound', 'BookingNotConfirmed', 'ShowtimeCancelled', 'OutsideWindow', 'TicketVoided')");
            table.HasCheckConstraint("CK_CheckInLogs_Success_Has_Ticket", "\"Result\" <> 'Success' OR \"TicketId\" IS NOT NULL");
        });
        builder.HasKey(x => x.Id);
        builder.Property(x => x.Id).ValueGeneratedNever();
        builder.Property(x => x.Result).HasConversion<string>().HasMaxLength(30).IsRequired();
        builder.Property(x => x.AttemptedTicketCode).HasMaxLength(60);
        builder.Property(x => x.LoggedAtUtc).HasColumnType("timestamp with time zone");
        builder.HasOne<ApplicationUser>().WithMany().HasForeignKey(x => x.StaffUserId).OnDelete(DeleteBehavior.Restrict);
        builder.HasOne<Ticket>().WithMany().HasForeignKey(x => x.TicketId).OnDelete(DeleteBehavior.Restrict);
        builder.HasOne<Booking>().WithMany().HasForeignKey(x => x.BookingId).OnDelete(DeleteBehavior.Restrict);
        builder.HasOne<Showtime>().WithMany().HasForeignKey(x => x.ShowtimeId).OnDelete(DeleteBehavior.Restrict);
        builder.HasIndex(x => new { x.StaffUserId, x.RequestId }).IsUnique();
        builder.HasIndex(x => x.ShowtimeId);
        builder.HasIndex(x => x.Result);
    }
}
