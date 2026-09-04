using LumiCine.Domain.Cinemas;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace LumiCine.Infrastructure.Persistence.Configurations.Cinemas;

public sealed class CinemaConfiguration : IEntityTypeConfiguration<Cinema>
{
    public void Configure(EntityTypeBuilder<Cinema> builder)
    {
        builder.ToTable("Cinemas", table =>
        {
            table.HasCheckConstraint("CK_Cinemas_Code_NotBlank", "btrim(\"Code\") <> ''");
            table.HasCheckConstraint("CK_Cinemas_Name_NotBlank", "btrim(\"Name\") <> ''");
            table.HasCheckConstraint("CK_Cinemas_Address_NotBlank", "btrim(\"Address\") <> ''");
            table.HasCheckConstraint("CK_Cinemas_City_NotBlank", "btrim(\"City\") <> ''");
        });
        builder.HasKey(x => x.Id);
        builder.Property(x => x.Id).ValueGeneratedNever();
        builder.Property(x => x.Code).HasMaxLength(30).IsRequired();
        builder.Property(x => x.Name).HasMaxLength(160).IsRequired();
        builder.Property(x => x.Address).HasMaxLength(300).IsRequired();
        builder.Property(x => x.City).HasMaxLength(100).IsRequired();
        builder.Property(x => x.IsActive).HasDefaultValue(true).HasSentinel(true);
        builder.Property(x => x.CreatedAtUtc).HasColumnType("timestamp with time zone");
        builder.Property(x => x.UpdatedAtUtc).HasColumnType("timestamp with time zone");
        builder.HasIndex(x => x.Code).IsUnique();
    }
}