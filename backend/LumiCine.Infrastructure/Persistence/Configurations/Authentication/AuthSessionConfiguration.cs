using LumiCine.Infrastructure.Authentication;
using LumiCine.Infrastructure.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace LumiCine.Infrastructure.Persistence.Configurations.Authentication;

public sealed class AuthSessionConfiguration : IEntityTypeConfiguration<AuthSession>
{
    public void Configure(EntityTypeBuilder<AuthSession> builder)
    {
        builder.ToTable("AuthSessions", table =>
            table.HasCheckConstraint("CK_AuthSessions_ExpiresAfterCreated", "\"ExpiresAtUtc\" > \"CreatedAtUtc\""));
        builder.HasKey(session => session.Id);
        builder.Property(session => session.Id).ValueGeneratedNever();
        builder.Property(session => session.CreatedAtUtc).HasColumnType("timestamp with time zone").IsRequired();
        builder.Property(session => session.ExpiresAtUtc).HasColumnType("timestamp with time zone").IsRequired();
        builder.Property(session => session.RevokedAtUtc).HasColumnType("timestamp with time zone");
        builder.HasOne<ApplicationUser>().WithMany().HasForeignKey(session => session.UserId).OnDelete(DeleteBehavior.Restrict);
        builder.HasIndex(session => session.UserId);
    }
}
