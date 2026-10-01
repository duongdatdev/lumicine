using Microsoft.EntityFrameworkCore.Migrations;

namespace LumiCine.Infrastructure.Persistence.Migrations;

public sealed partial class PreventOverlappingShowtimes : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.Sql("CREATE EXTENSION IF NOT EXISTS btree_gist;");
        migrationBuilder.DropIndex(name: "IX_Showtimes_MovieId", table: "Showtimes");
        migrationBuilder.Sql("CREATE INDEX \"IX_Showtimes_MovieId_StartAtUtc\" ON \"Showtimes\" (\"MovieId\", \"StartAtUtc\");");
        migrationBuilder.Sql("CREATE INDEX \"IX_Showtimes_StartAtUtc_Status\" ON \"Showtimes\" (\"StartAtUtc\", \"Status\");");
        migrationBuilder.Sql("ALTER TABLE \"Showtimes\" ADD CONSTRAINT \"EX_Showtimes_Room_TimeRange\" EXCLUDE USING gist (\"RoomId\" WITH =, tsrange(\"StartAtUtc\" AT TIME ZONE 'UTC', (\"EndAtUtc\" AT TIME ZONE 'UTC') + interval '15 minutes', '[)') WITH &&) WHERE (\"Status\" <> 'Cancelled');");
    }

    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.Sql("ALTER TABLE \"Showtimes\" DROP CONSTRAINT \"EX_Showtimes_Room_TimeRange\";");
        migrationBuilder.DropIndex("IX_Showtimes_MovieId_StartAtUtc", "Showtimes");
        migrationBuilder.DropIndex("IX_Showtimes_StartAtUtc_Status", "Showtimes");
        migrationBuilder.CreateIndex(name: "IX_Showtimes_MovieId", table: "Showtimes", column: "MovieId");
    }
}

