using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace LumiCine.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddDiscoveryConversations : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "DiscoveryConversations",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    UserId = table.Column<Guid>(type: "uuid", nullable: false),
                    Version = table.Column<int>(type: "integer", nullable: false),
                    TurnCount = table.Column<int>(type: "integer", nullable: false),
                    FiltersJson = table.Column<string>(type: "character varying(4000)", maxLength: 4000, nullable: false),
                    CreatedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    LastActiveAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    UpdatedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_DiscoveryConversations", x => x.Id);
                    table.CheckConstraint("CK_DiscoveryConversations_Filters_NotBlank", "btrim(\"FiltersJson\") <> ''");
                    table.CheckConstraint("CK_DiscoveryConversations_TurnCount", "\"TurnCount\" >= 0");
                    table.CheckConstraint("CK_DiscoveryConversations_Version", "\"Version\" >= 1");
                    table.ForeignKey(
                        name: "FK_DiscoveryConversations_Users_UserId",
                        column: x => x.UserId,
                        principalTable: "Users",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "DiscoveryMessages",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    ConversationId = table.Column<Guid>(type: "uuid", nullable: false),
                    TurnNumber = table.Column<int>(type: "integer", nullable: false),
                    Role = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    Content = table.Column<string>(type: "character varying(4000)", maxLength: 4000, nullable: false),
                    RequestId = table.Column<Guid>(type: "uuid", nullable: true),
                    ResponseJson = table.Column<string>(type: "character varying(16000)", maxLength: 16000, nullable: true),
                    CreatedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_DiscoveryMessages", x => x.Id);
                    table.CheckConstraint("CK_DiscoveryMessages_RequestId_User", "\"RequestId\" IS NULL OR \"Role\" = 'User'");
                    table.CheckConstraint("CK_DiscoveryMessages_ResponseJson_Assistant", "\"ResponseJson\" IS NULL OR \"Role\" = 'Assistant'");
                    table.CheckConstraint("CK_DiscoveryMessages_Role", "\"Role\" IN ('User', 'Assistant')");
                    table.CheckConstraint("CK_DiscoveryMessages_TurnNumber", "\"TurnNumber\" > 0");
                    table.CheckConstraint("CK_DiscoveryMessages_UserContentLength", "\"Role\" <> 'User' OR Length(\"Content\") <= 1000");
                    table.ForeignKey(
                        name: "FK_DiscoveryMessages_DiscoveryConversations_ConversationId",
                        column: x => x.ConversationId,
                        principalTable: "DiscoveryConversations",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "IX_DiscoveryConversations_UserId_LastActiveAtUtc",
                table: "DiscoveryConversations",
                columns: new[] { "UserId", "LastActiveAtUtc" });

            migrationBuilder.CreateIndex(
                name: "IX_DiscoveryMessages_ConversationId_RequestId",
                table: "DiscoveryMessages",
                columns: new[] { "ConversationId", "RequestId" },
                unique: true,
                filter: "\"RequestId\" IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "IX_DiscoveryMessages_ConversationId_TurnNumber_Role",
                table: "DiscoveryMessages",
                columns: new[] { "ConversationId", "TurnNumber", "Role" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "DiscoveryMessages");

            migrationBuilder.DropTable(
                name: "DiscoveryConversations");
        }
    }
}
