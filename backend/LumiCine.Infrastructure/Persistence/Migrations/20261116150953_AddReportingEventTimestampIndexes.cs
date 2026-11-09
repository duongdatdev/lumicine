using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace LumiCine.Infrastructure.Persistence.Migrations
{
    public partial class AddReportingEventTimestampIndexes : Migration
    {
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateIndex(name: "IX_Tickets_IssuedAtUtc", table: "Tickets", column: "IssuedAtUtc");
            migrationBuilder.CreateIndex(name: "IX_Tickets_UsedAtUtc", table: "Tickets", column: "UsedAtUtc", filter: "\"UsedAtUtc\" IS NOT NULL");
            migrationBuilder.CreateIndex(name: "IX_Payments_ProcessedAtUtc", table: "Payments", column: "ProcessedAtUtc");
            migrationBuilder.CreateIndex(name: "IX_Payments_RefundRecordedAtUtc", table: "Payments", column: "RefundRecordedAtUtc", filter: "\"RefundRecordedAtUtc\" IS NOT NULL");
            migrationBuilder.CreateIndex(name: "IX_CheckInLogs_LoggedAtUtc", table: "CheckInLogs", column: "LoggedAtUtc");
            migrationBuilder.CreateIndex(name: "IX_Bookings_CreatedAtUtc", table: "Bookings", column: "CreatedAtUtc");
        }

        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(name: "IX_Tickets_IssuedAtUtc", table: "Tickets");
            migrationBuilder.DropIndex(name: "IX_Tickets_UsedAtUtc", table: "Tickets");
            migrationBuilder.DropIndex(name: "IX_Payments_ProcessedAtUtc", table: "Payments");
            migrationBuilder.DropIndex(name: "IX_Payments_RefundRecordedAtUtc", table: "Payments");
            migrationBuilder.DropIndex(name: "IX_CheckInLogs_LoggedAtUtc", table: "CheckInLogs");
            migrationBuilder.DropIndex(name: "IX_Bookings_CreatedAtUtc", table: "Bookings");
        }
    }
}
