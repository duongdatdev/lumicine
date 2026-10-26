using LumiCine.Domain.Discovery;
using LumiCine.Infrastructure.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace LumiCine.Infrastructure.Persistence.Configurations.Discovery;

public sealed class DiscoveryConversationConfiguration : IEntityTypeConfiguration<DiscoveryConversation>
{
    public void Configure(EntityTypeBuilder<DiscoveryConversation> builder)
    {
        builder.ToTable("DiscoveryConversations", table =>
        {
            table.HasCheckConstraint("CK_DiscoveryConversations_Version", "\"Version\" >= 1");
            table.HasCheckConstraint("CK_DiscoveryConversations_TurnCount", "\"TurnCount\" >= 0");
            table.HasCheckConstraint("CK_DiscoveryConversations_Filters_NotBlank", "btrim(\"FiltersJson\") <> ''");
        });
        builder.HasKey(x => x.Id);
        builder.Property(x => x.Id).ValueGeneratedNever();
        builder.Property(x => x.FiltersJson).HasMaxLength(4000).IsRequired();
        builder.Property(x => x.CreatedAtUtc).HasColumnType("timestamp with time zone");
        builder.Property(x => x.LastActiveAtUtc).HasColumnType("timestamp with time zone");
        builder.Property(x => x.UpdatedAtUtc).HasColumnType("timestamp with time zone");
        builder.HasOne<ApplicationUser>().WithMany().HasForeignKey(x => x.UserId).OnDelete(DeleteBehavior.Restrict);
        builder.HasIndex(x => new { x.UserId, x.LastActiveAtUtc });
    }
}
