using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace LumiCine.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddMovieSearchDocuments : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "MovieSearchDocuments",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    MovieId = table.Column<Guid>(type: "uuid", nullable: false),
                    DocumentText = table.Column<string>(type: "character varying(4000)", maxLength: 4000, nullable: false),
                    SourceHash = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    EmbeddingModelId = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: true),
                    EmbeddingDimensions = table.Column<int>(type: "integer", nullable: true),
                    Vector = table.Column<float[]>(type: "real[]", nullable: true),
                    Status = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    AttemptCount = table.Column<int>(type: "integer", nullable: false),
                    NextAttemptAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    LeaseUntilUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    LastError = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true),
                    IndexedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    CreatedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    UpdatedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_MovieSearchDocuments", x => x.Id);
                    table.CheckConstraint("CK_MovieSearchDocuments_AttemptCount", "\"AttemptCount\" >= 0 AND \"AttemptCount\" <= 5");
                    table.CheckConstraint("CK_MovieSearchDocuments_DocumentTextNotEmpty", "btrim(\"DocumentText\") <> ''");
                    table.CheckConstraint("CK_MovieSearchDocuments_HashHex", "\"SourceHash\" ~ '^[0-9A-Fa-f]{64}$'");
                    table.CheckConstraint("CK_MovieSearchDocuments_Status", "\"Status\" IN ('Pending', 'Indexed', 'Failed')");
                    table.ForeignKey(
                        name: "FK_MovieSearchDocuments_Movies_MovieId",
                        column: x => x.MovieId,
                        principalTable: "Movies",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "IX_MovieSearchDocuments_MovieId",
                table: "MovieSearchDocuments",
                column: "MovieId",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_MovieSearchDocuments_Status_NextAttemptAtUtc",
                table: "MovieSearchDocuments",
                columns: new[] { "Status", "NextAttemptAtUtc" },
                filter: "\"Status\" = 'Pending'");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "MovieSearchDocuments");
        }
    }
}
