using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace LumiCine.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddBookingReservations : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_Bookings_UserId",
                table: "Bookings");

            migrationBuilder.AddColumn<Guid>(
                name: "CurrentBookingId",
                table: "ShowtimeSeats",
                type: "uuid",
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "RequestId",
                table: "Bookings",
                type: "uuid",
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "IX_ShowtimeSeats_CurrentBookingId_ShowtimeId",
                table: "ShowtimeSeats",
                columns: new[] { "CurrentBookingId", "ShowtimeId" });

            migrationBuilder.CreateIndex(
                name: "IX_Bookings_Status_HoldExpiresAtUtc",
                table: "Bookings",
                columns: new[] { "Status", "HoldExpiresAtUtc" });

            migrationBuilder.CreateIndex(
                name: "IX_Bookings_UserId",
                table: "Bookings",
                column: "UserId",
                unique: true,
                filter: "\"Status\" = 'Pending' AND \"RequestId\" IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "IX_Bookings_UserId_RequestId",
                table: "Bookings",
                columns: new[] { "UserId", "RequestId" },
                unique: true,
                filter: "\"RequestId\" IS NOT NULL");

            migrationBuilder.AddCheckConstraint(
                name: "CK_Bookings_RequestId_User",
                table: "Bookings",
                sql: "\"RequestId\" IS NULL OR \"UserId\" IS NOT NULL");

            migrationBuilder.AddForeignKey(
                name: "FK_ShowtimeSeats_Bookings_CurrentBookingId_ShowtimeId",
                table: "ShowtimeSeats",
                columns: new[] { "CurrentBookingId", "ShowtimeId" },
                principalTable: "Bookings",
                principalColumns: new[] { "Id", "ShowtimeId" },
                onDelete: ReferentialAction.Restrict);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_ShowtimeSeats_Bookings_CurrentBookingId_ShowtimeId",
                table: "ShowtimeSeats");

            migrationBuilder.DropIndex(
                name: "IX_ShowtimeSeats_CurrentBookingId_ShowtimeId",
                table: "ShowtimeSeats");

            migrationBuilder.DropIndex(
                name: "IX_Bookings_Status_HoldExpiresAtUtc",
                table: "Bookings");

            migrationBuilder.DropIndex(
                name: "IX_Bookings_UserId",
                table: "Bookings");

            migrationBuilder.DropIndex(
                name: "IX_Bookings_UserId_RequestId",
                table: "Bookings");

            migrationBuilder.DropCheckConstraint(
                name: "CK_Bookings_RequestId_User",
                table: "Bookings");

            migrationBuilder.DropColumn(
                name: "CurrentBookingId",
                table: "ShowtimeSeats");

            migrationBuilder.DropColumn(
                name: "RequestId",
                table: "Bookings");

            migrationBuilder.CreateIndex(
                name: "IX_Bookings_UserId",
                table: "Bookings",
                column: "UserId");
        }
    }
}
