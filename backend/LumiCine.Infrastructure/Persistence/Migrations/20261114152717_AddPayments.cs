using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace LumiCine.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddPayments : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "Payments",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    BookingId = table.Column<Guid>(type: "uuid", nullable: false),
                    RequestId = table.Column<Guid>(type: "uuid", nullable: false),
                    MerchantReference = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    Amount = table.Column<decimal>(type: "numeric(18,2)", nullable: false),
                    Currency = table.Column<string>(type: "character varying(10)", maxLength: 10, nullable: false),
                    ExpiresAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    Status = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    ProcessingStatus = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    VnPayTransactionId = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: true),
                    ResponseCode = table.Column<string>(type: "character varying(16)", maxLength: 16, nullable: true),
                    TransactionStatus = table.Column<string>(type: "character varying(16)", maxLength: 16, nullable: true),
                    PaymentUrl = table.Column<string>(type: "character varying(1000)", maxLength: 1000, nullable: true),
                    CreatedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    ProcessedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    UpdatedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Payments", x => x.Id);
                    table.CheckConstraint("CK_Payments_Amount", "\"Amount\" >= 0");
                    table.CheckConstraint("CK_Payments_Currency_NotBlank", "btrim(\"Currency\") <> ''");
                    table.CheckConstraint("CK_Payments_ProcessingStatus", "\"ProcessingStatus\" IN ('None', 'Fulfilled', 'RequiresRefundReview', 'RefundRecorded')");
                    table.CheckConstraint("CK_Payments_RefundRecorded_Requires_Success", "\"ProcessingStatus\" <> 'RefundRecorded' OR \"Status\" = 'Succeeded'");
                    table.CheckConstraint("CK_Payments_Status", "\"Status\" IN ('Pending', 'Succeeded', 'Failed')");
                    table.CheckConstraint("CK_Payments_Succeeded_Records_Use", "\"Status\" <> 'Succeeded' OR \"ProcessingStatus\" <> 'None'");
                    table.ForeignKey(
                        name: "FK_Payments_Bookings_BookingId",
                        column: x => x.BookingId,
                        principalTable: "Bookings",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "TicketEmailDeliveries",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    BookingId = table.Column<Guid>(type: "uuid", nullable: false),
                    Status = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    AttemptCount = table.Column<int>(type: "integer", nullable: false),
                    NextAttemptAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    LeaseUntilUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    SentAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    LastError = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true),
                    CreatedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    UpdatedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_TicketEmailDeliveries", x => x.Id);
                    table.CheckConstraint("CK_TicketEmailDeliveries_AttemptCount", "\"AttemptCount\" >= 0 AND \"AttemptCount\" <= 5");
                    table.CheckConstraint("CK_TicketEmailDeliveries_Status", "\"Status\" IN ('Pending', 'Sent', 'Failed')");
                    table.ForeignKey(
                        name: "FK_TicketEmailDeliveries_Bookings_BookingId",
                        column: x => x.BookingId,
                        principalTable: "Bookings",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "IX_Payments_BookingId_Fulfilled",
                table: "Payments",
                column: "BookingId",
                unique: true,
                filter: "\"ProcessingStatus\" = 'Fulfilled'");

            migrationBuilder.CreateIndex(
                name: "IX_Payments_BookingId_Pending",
                table: "Payments",
                column: "BookingId",
                unique: true,
                filter: "\"Status\" = 'Pending'");

            migrationBuilder.CreateIndex(
                name: "IX_Payments_BookingId_RequestId",
                table: "Payments",
                columns: new[] { "BookingId", "RequestId" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_Payments_MerchantReference",
                table: "Payments",
                column: "MerchantReference",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_Payments_Status_ExpiresAtUtc",
                table: "Payments",
                columns: new[] { "Status", "ExpiresAtUtc" });

            migrationBuilder.CreateIndex(
                name: "IX_Payments_VnPayTransactionId",
                table: "Payments",
                column: "VnPayTransactionId",
                unique: true,
                filter: "\"VnPayTransactionId\" IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "IX_TicketEmailDeliveries_BookingId",
                table: "TicketEmailDeliveries",
                column: "BookingId",
                unique: true,
                filter: "\"Status\" = 'Pending'");

            migrationBuilder.CreateIndex(
                name: "IX_TicketEmailDeliveries_Status_NextAttemptAtUtc",
                table: "TicketEmailDeliveries",
                columns: new[] { "Status", "NextAttemptAtUtc" },
                filter: "\"Status\" = 'Pending'");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "Payments");

            migrationBuilder.DropTable(
                name: "TicketEmailDeliveries");
        }
    }
}
