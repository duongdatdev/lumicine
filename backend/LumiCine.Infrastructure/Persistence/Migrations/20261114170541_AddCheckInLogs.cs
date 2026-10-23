using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace LumiCine.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddCheckInLogs : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "CheckInLogs",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    StaffUserId = table.Column<Guid>(type: "uuid", nullable: false),
                    RequestId = table.Column<Guid>(type: "uuid", nullable: false),
                    TicketId = table.Column<Guid>(type: "uuid", nullable: true),
                    BookingId = table.Column<Guid>(type: "uuid", nullable: true),
                    ShowtimeId = table.Column<Guid>(type: "uuid", nullable: true),
                    AttemptedTicketCode = table.Column<string>(type: "character varying(60)", maxLength: 60, nullable: true),
                    Result = table.Column<string>(type: "character varying(30)", maxLength: 30, nullable: false),
                    LoggedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_CheckInLogs", x => x.Id);
                    table.CheckConstraint("CK_CheckInLogs_Result", "\"Result\" IN ('Success', 'DuplicateScan', 'TicketNotFound', 'BookingNotConfirmed', 'ShowtimeCancelled', 'OutsideWindow', 'TicketVoided')");
                    table.CheckConstraint("CK_CheckInLogs_Success_Has_Ticket", "\"Result\" <> 'Success' OR \"TicketId\" IS NOT NULL");
                    table.ForeignKey(
                        name: "FK_CheckInLogs_Bookings_BookingId",
                        column: x => x.BookingId,
                        principalTable: "Bookings",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_CheckInLogs_Showtimes_ShowtimeId",
                        column: x => x.ShowtimeId,
                        principalTable: "Showtimes",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_CheckInLogs_Tickets_TicketId",
                        column: x => x.TicketId,
                        principalTable: "Tickets",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_CheckInLogs_Users_StaffUserId",
                        column: x => x.StaffUserId,
                        principalTable: "Users",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "IX_CheckInLogs_BookingId",
                table: "CheckInLogs",
                column: "BookingId");

            migrationBuilder.CreateIndex(
                name: "IX_CheckInLogs_Result",
                table: "CheckInLogs",
                column: "Result");

            migrationBuilder.CreateIndex(
                name: "IX_CheckInLogs_ShowtimeId",
                table: "CheckInLogs",
                column: "ShowtimeId");

            migrationBuilder.CreateIndex(
                name: "IX_CheckInLogs_StaffUserId_RequestId",
                table: "CheckInLogs",
                columns: new[] { "StaffUserId", "RequestId" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_CheckInLogs_TicketId",
                table: "CheckInLogs",
                column: "TicketId");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "CheckInLogs");
        }
    }
}
