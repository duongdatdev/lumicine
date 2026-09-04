using LumiCine.Domain.Cinemas;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace LumiCine.Infrastructure.Persistence.Configurations.Cinemas;

public sealed class RoomConfiguration : IEntityTypeConfiguration<Room>
{
    public void Configure(EntityTypeBuilder<Room> builder)
    {
        builder.ToTable("Rooms", table =>
        {
            table.HasCheckConstraint("CK_Rooms_Code_NotBlank", "btrim(\"Code\") <> ''");
            table.HasCheckConstraint("CK_Rooms_Name_NotBlank", "btrim(\"Name\") <> ''");
        });
        builder.HasKey(x => x.Id);
        builder.Property(x => x.Id).ValueGeneratedNever();
        builder.Property(x => x.Code).HasMaxLength(30).IsRequired();
        builder.Property(x => x.Name).HasMaxLength(100).IsRequired();
        builder.Property(x => x.IsActive).HasDefaultValue(true).HasSentinel(true);
        builder.Property(x => x.CreatedAtUtc).HasColumnType("timestamp with time zone");
        builder.Property(x => x.UpdatedAtUtc).HasColumnType("timestamp with time zone");
        builder.HasOne<Cinema>().WithMany().HasForeignKey(x => x.CinemaId).OnDelete(DeleteBehavior.Restrict);
        builder.HasIndex(x => new { x.CinemaId, x.Code }).IsUnique();
    }
}