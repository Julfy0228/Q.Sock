using Microsoft.EntityFrameworkCore.Migrations;

namespace Q.Sock.Server.Plugins.Moodle.Migrations;

public partial class InitialMoodleIntegration : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.Sql(
            """
            CREATE TABLE IF NOT EXISTS "CourseLinks" (
                "Id" TEXT NOT NULL CONSTRAINT "PK_CourseLinks" PRIMARY KEY,
                "ConnectionId" TEXT NOT NULL,
                "ExternalCourseId" INTEGER NOT NULL,
                "GuildId" TEXT NOT NULL,
                "ChatId" TEXT NOT NULL,
                "RoomId" TEXT NOT NULL,
                "SyncedAtUtc" TEXT NOT NULL
            );
            CREATE INDEX IF NOT EXISTS "IX_CourseLinks_ChatId" ON "CourseLinks" ("ChatId");
            CREATE UNIQUE INDEX IF NOT EXISTS "IX_CourseLinks_ConnectionId_ExternalCourseId" ON "CourseLinks" ("ConnectionId", "ExternalCourseId");
            CREATE INDEX IF NOT EXISTS "IX_CourseLinks_GuildId" ON "CourseLinks" ("GuildId");
            """
        );
    }

    protected override void Down(MigrationBuilder migrationBuilder) { }
}
