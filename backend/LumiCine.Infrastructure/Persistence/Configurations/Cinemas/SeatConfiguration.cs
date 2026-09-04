using LumiCine.Domain.Cinemas;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace LumiCine.Infrastructure.Persistence.Configurations.Cinemas;

public sealed class SeatConfiguration : IEntityTypeConfiguration<Seat>
{
    public void Configure(EntityTypeBuilder<Seat> builder)
    {
        builder.ToTable("Seats", table =>
        {
            table.HasCheckConstraint("CK_Seats_Number", "\"Number\" > 0");
            table.HasCheckConstraint("CK_Seats_RowLabel_NotBlank", "btrim(\"RowLabel\") <> ''");
            table.HasCheckConstraint("CK_Seats_SeatType_NotBlank", "btrim(\"SeatType\") <> ''");
        });
        builder.HasKey(x => x.Id);
        builder.Property(x => x.Id).ValueGeneratedNever();
        builder.Property(x => x.RowLabel).HasMaxLength(10).IsRequired();
        builder.Property(x => x.SeatType).HasMaxLength(30).IsRequired();
        builder.Property(x => x.IsActive).HasDefaultValue(true).HasSentinel(true);
        builder.Property(x => x.CreatedAtUtc).HasColumnType("timestamp with time zone");
        builder.HasAlternateKey(x => new { x.Id, x.RoomId });
        builder.HasOne<Room>().WithMany().HasForeignKey(x => x.RoomId).OnDelete(DeleteBehavior.Restrict);
        builder.HasIndex(x => new { x.RoomId, x.RowLabel, x.Number }).IsUnique();
    }
}