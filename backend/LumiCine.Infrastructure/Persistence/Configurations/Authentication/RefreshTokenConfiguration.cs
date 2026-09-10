using LumiCine.Infrastructure.Authentication;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace LumiCine.Infrastructure.Persistence.Configurations.Authentication;

public sealed class RefreshTokenConfiguration : IEntityTypeConfiguration<RefreshToken>
{
    public void Configure(EntityTypeBuilder<RefreshToken> builder)
    {
        builder.ToTable("RefreshTokens", table =>
        {
            table.HasCheckConstraint("CK_RefreshTokens_HashHex", "\"Hash\" ~ '^[0-9A-Fa-f]{64}$'");
            table.HasCheckConstraint("CK_RefreshTokens_ExpiresAfterCreated", "\"ExpiresAtUtc\" > \"CreatedAtUtc\"");
        });
        builder.HasKey(token => token.Id);
        builder.Property(token => token.Id).ValueGeneratedNever();
        builder.Property(token => token.Hash).HasMaxLength(64).IsRequired();
        builder.Property(token => token.CreatedAtUtc).HasColumnType("timestamp with time zone").IsRequired();
        builder.Property(token => token.ExpiresAtUtc).HasColumnType("timestamp with time zone").IsRequired();
        builder.Property(token => token.ConsumedAtUtc).HasColumnType("timestamp with time zone");
        builder.HasOne<AuthSession>().WithMany().HasForeignKey(token => token.SessionId).OnDelete(DeleteBehavior.Restrict);
        builder.HasIndex(token => token.Hash).IsUnique();
    }
}
