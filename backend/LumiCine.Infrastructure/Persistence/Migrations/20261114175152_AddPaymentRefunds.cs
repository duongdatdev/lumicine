using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace LumiCine.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddPaymentRefunds : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "RefundNote",
                table: "Payments",
                type: "character varying(500)",
                maxLength: 500,
                nullable: true);

            migrationBuilder.AddColumn<DateTime>(
                name: "RefundRecordedAtUtc",
                table: "Payments",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "RefundRecordedByUserId",
                table: "Payments",
                type: "uuid",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "RefundReference",
                table: "Payments",
                type: "character varying(200)",
                maxLength: 200,
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "RefundNote",
                table: "Payments");

            migrationBuilder.DropColumn(
                name: "RefundRecordedAtUtc",
                table: "Payments");

            migrationBuilder.DropColumn(
                name: "RefundRecordedByUserId",
                table: "Payments");

            migrationBuilder.DropColumn(
                name: "RefundReference",
                table: "Payments");
        }
    }
}
