using LumiCine.Domain.Discovery;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace LumiCine.Infrastructure.Persistence.Configurations.Discovery;

public sealed class DiscoveryMessageConfiguration : IEntityTypeConfiguration<DiscoveryMessage>
{
    public void Configure(EntityTypeBuilder<DiscoveryMessage> builder)
    {
        builder.ToTable("DiscoveryMessages", table =>
        {
            table.HasCheckConstraint("CK_DiscoveryMessages_TurnNumber", "\"TurnNumber\" > 0");
            table.HasCheckConstraint("CK_DiscoveryMessages_Role", "\"Role\" IN ('User', 'Assistant')");
            table.HasCheckConstraint("CK_DiscoveryMessages_RequestId_User", "\"RequestId\" IS NULL OR \"Role\" = 'User'");
            table.HasCheckConstraint("CK_DiscoveryMessages_ResponseJson_Assistant", "\"ResponseJson\" IS NULL OR \"Role\" = 'Assistant'");
            table.HasCheckConstraint("CK_DiscoveryMessages_UserContentLength", "\"Role\" <> 'User' OR Length(\"Content\") <= 1000");
        });
        builder.HasKey(x => x.Id);
        builder.Property(x => x.Id).ValueGeneratedNever();
        builder.Property(x => x.Role).HasConversion<string>().HasMaxLength(20).IsRequired();
        builder.Property(x => x.Content).HasMaxLength(DiscoveryMessage.AssistantContentMaxLength).IsRequired();
        builder.Property(x => x.ResponseJson).HasMaxLength(DiscoveryMessage.ResponseJsonMaxLength);
        builder.Property(x => x.CreatedAtUtc).HasColumnType("timestamp with time zone");
        builder.HasOne<DiscoveryConversation>().WithMany()
            .HasForeignKey(x => x.ConversationId).OnDelete(DeleteBehavior.Restrict);
        builder.HasIndex(x => new { x.ConversationId, x.TurnNumber, x.Role });
        builder.HasIndex(x => new { x.ConversationId, x.RequestId }).IsUnique().HasFilter("\"RequestId\" IS NOT NULL");
    }
}
