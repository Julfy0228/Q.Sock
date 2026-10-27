using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Q.Sock.Server.Data.Migrations
{
    /// <inheritdoc />
    public partial class AddDeviceSessions : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "DeviceSessions",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "TEXT", nullable: false),
                    UserId = table.Column<Guid>(type: "TEXT", nullable: false),
                    DeviceId = table.Column<string>(type: "TEXT", maxLength: 128, nullable: false),
                    DeviceName = table.Column<string>(
                        type: "TEXT",
                        maxLength: 128,
                        nullable: false
                    ),
                    RefreshTokenHash = table.Column<string>(
                        type: "TEXT",
                        maxLength: 64,
                        nullable: false
                    ),
                    CreatedAtUtc = table.Column<DateTime>(type: "TEXT", nullable: false),
                    LastActivityAtUtc = table.Column<DateTime>(type: "TEXT", nullable: false),
                    ExpiresAtUtc = table.Column<DateTime>(type: "TEXT", nullable: false),
                    RevokedAtUtc = table.Column<DateTime>(type: "TEXT", nullable: true),
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_DeviceSessions", x => x.Id);
                    table.ForeignKey(
                        name: "FK_DeviceSessions_AspNetUsers_UserId",
                        column: x => x.UserId,
                        principalTable: "AspNetUsers",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade
                    );
                }
            );

            migrationBuilder.CreateIndex(
                name: "IX_DeviceSessions_RefreshTokenHash",
                table: "DeviceSessions",
                column: "RefreshTokenHash",
                unique: true
            );

            migrationBuilder.CreateIndex(
                name: "IX_DeviceSessions_UserId_RevokedAtUtc",
                table: "DeviceSessions",
                columns: new[] { "UserId", "RevokedAtUtc" }
            );
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(name: "DeviceSessions");
        }
    }
}
