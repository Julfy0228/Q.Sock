using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Q.Sock.Server.Data.Migrations
{
    /// <inheritdoc />
    public partial class MessengerFeatures : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(name: "IX_Messages_SenderId", table: "Messages");

            migrationBuilder.AddColumn<Guid>(
                name: "ChatId",
                table: "Messages",
                type: "TEXT",
                nullable: true
            );

            migrationBuilder.AddColumn<Guid>(
                name: "ClientOperationId",
                table: "Messages",
                type: "TEXT",
                nullable: true
            );

            migrationBuilder.AddColumn<DateTime>(
                name: "DeletedAtUtc",
                table: "Messages",
                type: "TEXT",
                nullable: true
            );

            migrationBuilder.AddColumn<DateTime>(
                name: "EditedAtUtc",
                table: "Messages",
                type: "TEXT",
                nullable: true
            );

            migrationBuilder.AddColumn<string>(
                name: "DisplayName",
                table: "AspNetUsers",
                type: "TEXT",
                nullable: false,
                defaultValue: ""
            );

            migrationBuilder.AddColumn<bool>(
                name: "IsAdmin",
                table: "AspNetUsers",
                type: "INTEGER",
                nullable: false,
                defaultValue: false
            );

            migrationBuilder.AddColumn<bool>(
                name: "IsStudent",
                table: "AspNetUsers",
                type: "INTEGER",
                nullable: false,
                defaultValue: false
            );

            migrationBuilder.AddColumn<bool>(
                name: "IsSuspended",
                table: "AspNetUsers",
                type: "INTEGER",
                nullable: false,
                defaultValue: false
            );

            migrationBuilder.AddColumn<string>(
                name: "UniversityLogin",
                table: "AspNetUsers",
                type: "TEXT",
                nullable: false,
                defaultValue: ""
            );

            migrationBuilder.CreateTable(
                name: "AuditLog",
                columns: table => new
                {
                    Id = table
                        .Column<long>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    ActorId = table.Column<Guid>(type: "TEXT", nullable: true),
                    Action = table.Column<string>(type: "TEXT", nullable: false),
                    Target = table.Column<string>(type: "TEXT", nullable: false),
                    AtUtc = table.Column<DateTime>(type: "TEXT", nullable: false),
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_AuditLog", x => x.Id);
                }
            );

            migrationBuilder.CreateTable(
                name: "ExternalIdentities",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "TEXT", nullable: false),
                    Provider = table.Column<string>(type: "TEXT", nullable: false),
                    Subject = table.Column<string>(type: "TEXT", nullable: false),
                    UserId = table.Column<Guid>(type: "TEXT", nullable: false),
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ExternalIdentities", x => x.Id);
                    table.ForeignKey(
                        name: "FK_ExternalIdentities_AspNetUsers_UserId",
                        column: x => x.UserId,
                        principalTable: "AspNetUsers",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade
                    );
                }
            );

            migrationBuilder.CreateTable(
                name: "Friendships",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "TEXT", nullable: false),
                    FromId = table.Column<Guid>(type: "TEXT", nullable: false),
                    ToId = table.Column<Guid>(type: "TEXT", nullable: false),
                    PairKey = table.Column<string>(type: "TEXT", nullable: false),
                    Status = table.Column<string>(type: "TEXT", nullable: false),
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Friendships", x => x.Id);
                    table.ForeignKey(
                        name: "FK_Friendships_AspNetUsers_FromId",
                        column: x => x.FromId,
                        principalTable: "AspNetUsers",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade
                    );
                    table.ForeignKey(
                        name: "FK_Friendships_AspNetUsers_ToId",
                        column: x => x.ToId,
                        principalTable: "AspNetUsers",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade
                    );
                }
            );

            migrationBuilder.CreateTable(
                name: "Guilds",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "TEXT", nullable: false),
                    OwnerId = table.Column<Guid>(type: "TEXT", nullable: false),
                    Name = table.Column<string>(type: "TEXT", nullable: false),
                    Managed = table.Column<bool>(type: "INTEGER", nullable: false),
                    DeletionRequested = table.Column<bool>(type: "INTEGER", nullable: false),
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Guilds", x => x.Id);
                    table.ForeignKey(
                        name: "FK_Guilds_AspNetUsers_OwnerId",
                        column: x => x.OwnerId,
                        principalTable: "AspNetUsers",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict
                    );
                }
            );

            migrationBuilder.CreateTable(
                name: "LaunchNonces",
                columns: table => new
                {
                    Id = table.Column<string>(type: "TEXT", maxLength: 128, nullable: false),
                    ExpiresAtUtc = table.Column<DateTime>(type: "TEXT", nullable: false),
                    Payload = table.Column<string>(type: "TEXT", nullable: false),
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_LaunchNonces", x => x.Id);
                }
            );

            migrationBuilder.CreateTable(
                name: "MessageReceipts",
                columns: table => new
                {
                    MessageId = table.Column<Guid>(type: "TEXT", nullable: false),
                    UserId = table.Column<Guid>(type: "TEXT", nullable: false),
                    ReadAtUtc = table.Column<DateTime>(type: "TEXT", nullable: false),
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_MessageReceipts", x => new { x.MessageId, x.UserId });
                    table.ForeignKey(
                        name: "FK_MessageReceipts_AspNetUsers_UserId",
                        column: x => x.UserId,
                        principalTable: "AspNetUsers",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade
                    );
                    table.ForeignKey(
                        name: "FK_MessageReceipts_Messages_MessageId",
                        column: x => x.MessageId,
                        principalTable: "Messages",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade
                    );
                }
            );

            migrationBuilder.CreateTable(
                name: "Settings",
                columns: table => new
                {
                    Id = table.Column<string>(type: "TEXT", nullable: false),
                    Json = table.Column<string>(type: "TEXT", nullable: false),
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Settings", x => x.Id);
                }
            );

            migrationBuilder.CreateTable(
                name: "Chats",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "TEXT", nullable: false),
                    GuildId = table.Column<Guid>(type: "TEXT", nullable: true),
                    OwnerId = table.Column<Guid>(type: "TEXT", nullable: false),
                    Name = table.Column<string>(type: "TEXT", nullable: false),
                    Type = table.Column<string>(type: "TEXT", nullable: false),
                    DirectKey = table.Column<string>(type: "TEXT", nullable: true),
                    Managed = table.Column<bool>(type: "INTEGER", nullable: false),
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Chats", x => x.Id);
                    table.ForeignKey(
                        name: "FK_Chats_AspNetUsers_OwnerId",
                        column: x => x.OwnerId,
                        principalTable: "AspNetUsers",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict
                    );
                    table.ForeignKey(
                        name: "FK_Chats_Guilds_GuildId",
                        column: x => x.GuildId,
                        principalTable: "Guilds",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade
                    );
                }
            );

            migrationBuilder.CreateTable(
                name: "GuildMembers",
                columns: table => new
                {
                    GuildId = table.Column<Guid>(type: "TEXT", nullable: false),
                    UserId = table.Column<Guid>(type: "TEXT", nullable: false),
                    Role = table.Column<string>(type: "TEXT", nullable: false),
                    GroupsJson = table.Column<string>(type: "TEXT", nullable: false),
                    Active = table.Column<bool>(type: "INTEGER", nullable: false),
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_GuildMembers", x => new { x.GuildId, x.UserId });
                    table.ForeignKey(
                        name: "FK_GuildMembers_AspNetUsers_UserId",
                        column: x => x.UserId,
                        principalTable: "AspNetUsers",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade
                    );
                    table.ForeignKey(
                        name: "FK_GuildMembers_Guilds_GuildId",
                        column: x => x.GuildId,
                        principalTable: "Guilds",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade
                    );
                }
            );

            migrationBuilder.CreateTable(
                name: "Attachments",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "TEXT", nullable: false),
                    ChatId = table.Column<Guid>(type: "TEXT", nullable: false),
                    UploadedBy = table.Column<Guid>(type: "TEXT", nullable: false),
                    MessageId = table.Column<Guid>(type: "TEXT", nullable: true),
                    Name = table.Column<string>(type: "TEXT", nullable: false),
                    ContentType = table.Column<string>(type: "TEXT", nullable: false),
                    StorageKey = table.Column<string>(type: "TEXT", nullable: false),
                    Size = table.Column<long>(type: "INTEGER", nullable: false),
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Attachments", x => x.Id);
                    table.ForeignKey(
                        name: "FK_Attachments_AspNetUsers_UploadedBy",
                        column: x => x.UploadedBy,
                        principalTable: "AspNetUsers",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict
                    );
                    table.ForeignKey(
                        name: "FK_Attachments_Chats_ChatId",
                        column: x => x.ChatId,
                        principalTable: "Chats",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade
                    );
                    table.ForeignKey(
                        name: "FK_Attachments_Messages_MessageId",
                        column: x => x.MessageId,
                        principalTable: "Messages",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade
                    );
                }
            );

            migrationBuilder.CreateTable(
                name: "Calls",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "TEXT", nullable: false),
                    ChatId = table.Column<Guid>(type: "TEXT", nullable: false),
                    OrganizerId = table.Column<Guid>(type: "TEXT", nullable: false),
                    Title = table.Column<string>(type: "TEXT", nullable: false),
                    Status = table.Column<string>(type: "TEXT", nullable: false),
                    CreatedAtUtc = table.Column<DateTime>(type: "TEXT", nullable: false),
                    StartsAtUtc = table.Column<DateTime>(type: "TEXT", nullable: true),
                    DurationMinutes = table.Column<int>(type: "INTEGER", nullable: false),
                    Reminder = table.Column<bool>(type: "INTEGER", nullable: false),
                    ReminderSent = table.Column<bool>(type: "INTEGER", nullable: false),
                    ContactEmail = table.Column<string>(type: "TEXT", nullable: true),
                    MaterialsJson = table.Column<string>(type: "TEXT", nullable: false),
                    CalendarPublished = table.Column<bool>(type: "INTEGER", nullable: false),
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Calls", x => x.Id);
                    table.ForeignKey(
                        name: "FK_Calls_AspNetUsers_OrganizerId",
                        column: x => x.OrganizerId,
                        principalTable: "AspNetUsers",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict
                    );
                    table.ForeignKey(
                        name: "FK_Calls_Chats_ChatId",
                        column: x => x.ChatId,
                        principalTable: "Chats",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade
                    );
                }
            );

            migrationBuilder.CreateTable(
                name: "ChatParticipants",
                columns: table => new
                {
                    ChatId = table.Column<Guid>(type: "TEXT", nullable: false),
                    UserId = table.Column<Guid>(type: "TEXT", nullable: false),
                    Role = table.Column<string>(type: "TEXT", nullable: false),
                    CanWrite = table.Column<bool>(type: "INTEGER", nullable: false),
                    CanSpeak = table.Column<bool>(type: "INTEGER", nullable: false),
                    CanConnect = table.Column<bool>(type: "INTEGER", nullable: false),
                    CanShare = table.Column<bool>(type: "INTEGER", nullable: false),
                    Active = table.Column<bool>(type: "INTEGER", nullable: false),
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ChatParticipants", x => new { x.ChatId, x.UserId });
                    table.ForeignKey(
                        name: "FK_ChatParticipants_AspNetUsers_UserId",
                        column: x => x.UserId,
                        principalTable: "AspNetUsers",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade
                    );
                    table.ForeignKey(
                        name: "FK_ChatParticipants_Chats_ChatId",
                        column: x => x.ChatId,
                        principalTable: "Chats",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade
                    );
                }
            );

            migrationBuilder.CreateTable(
                name: "CourseLinks",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "TEXT", nullable: false),
                    ConnectionId = table.Column<string>(type: "TEXT", nullable: false),
                    ExternalCourseId = table.Column<long>(type: "INTEGER", nullable: false),
                    GuildId = table.Column<Guid>(type: "TEXT", nullable: false),
                    ChatId = table.Column<Guid>(type: "TEXT", nullable: false),
                    RoomId = table.Column<Guid>(type: "TEXT", nullable: false),
                    SyncedAtUtc = table.Column<DateTime>(type: "TEXT", nullable: false),
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_CourseLinks", x => x.Id);
                    table.ForeignKey(
                        name: "FK_CourseLinks_Chats_ChatId",
                        column: x => x.ChatId,
                        principalTable: "Chats",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict
                    );
                    table.ForeignKey(
                        name: "FK_CourseLinks_Guilds_GuildId",
                        column: x => x.GuildId,
                        principalTable: "Guilds",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade
                    );
                }
            );

            migrationBuilder.CreateTable(
                name: "CallParticipants",
                columns: table => new
                {
                    CallId = table.Column<Guid>(type: "TEXT", nullable: false),
                    UserId = table.Column<Guid>(type: "TEXT", nullable: false),
                    Removed = table.Column<bool>(type: "INTEGER", nullable: false),
                    Muted = table.Column<bool>(type: "INTEGER", nullable: false),
                    CanShare = table.Column<bool>(type: "INTEGER", nullable: false),
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_CallParticipants", x => new { x.CallId, x.UserId });
                    table.ForeignKey(
                        name: "FK_CallParticipants_AspNetUsers_UserId",
                        column: x => x.UserId,
                        principalTable: "AspNetUsers",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade
                    );
                    table.ForeignKey(
                        name: "FK_CallParticipants_Calls_CallId",
                        column: x => x.CallId,
                        principalTable: "Calls",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade
                    );
                }
            );

            migrationBuilder.CreateIndex(
                name: "IX_Messages_ChatId_SentAt",
                table: "Messages",
                columns: new[] { "ChatId", "SentAt" }
            );

            migrationBuilder.CreateIndex(
                name: "IX_Messages_SenderId_ClientOperationId",
                table: "Messages",
                columns: new[] { "SenderId", "ClientOperationId" },
                unique: true
            );

            migrationBuilder.CreateIndex(
                name: "IX_Attachments_ChatId",
                table: "Attachments",
                column: "ChatId"
            );

            migrationBuilder.CreateIndex(
                name: "IX_Attachments_MessageId",
                table: "Attachments",
                column: "MessageId"
            );

            migrationBuilder.CreateIndex(
                name: "IX_Attachments_UploadedBy",
                table: "Attachments",
                column: "UploadedBy"
            );

            migrationBuilder.CreateIndex(
                name: "IX_AuditLog_AtUtc",
                table: "AuditLog",
                column: "AtUtc"
            );

            migrationBuilder.CreateIndex(
                name: "IX_CallParticipants_UserId",
                table: "CallParticipants",
                column: "UserId"
            );

            migrationBuilder.CreateIndex(
                name: "IX_Calls_ChatId",
                table: "Calls",
                column: "ChatId",
                unique: true,
                filter: "\"Status\" = 'live'"
            );

            migrationBuilder.CreateIndex(
                name: "IX_Calls_OrganizerId",
                table: "Calls",
                column: "OrganizerId"
            );

            migrationBuilder.CreateIndex(
                name: "IX_ChatParticipants_UserId",
                table: "ChatParticipants",
                column: "UserId"
            );

            migrationBuilder.CreateIndex(
                name: "IX_Chats_DirectKey",
                table: "Chats",
                column: "DirectKey",
                unique: true
            );

            migrationBuilder.CreateIndex(
                name: "IX_Chats_GuildId",
                table: "Chats",
                column: "GuildId"
            );

            migrationBuilder.CreateIndex(
                name: "IX_Chats_OwnerId",
                table: "Chats",
                column: "OwnerId"
            );

            migrationBuilder.CreateIndex(
                name: "IX_CourseLinks_ChatId",
                table: "CourseLinks",
                column: "ChatId"
            );

            migrationBuilder.CreateIndex(
                name: "IX_CourseLinks_ConnectionId_ExternalCourseId",
                table: "CourseLinks",
                columns: new[] { "ConnectionId", "ExternalCourseId" },
                unique: true
            );

            migrationBuilder.CreateIndex(
                name: "IX_CourseLinks_GuildId",
                table: "CourseLinks",
                column: "GuildId"
            );

            migrationBuilder.CreateIndex(
                name: "IX_ExternalIdentities_Provider_Subject",
                table: "ExternalIdentities",
                columns: new[] { "Provider", "Subject" },
                unique: true
            );

            migrationBuilder.CreateIndex(
                name: "IX_ExternalIdentities_UserId",
                table: "ExternalIdentities",
                column: "UserId"
            );

            migrationBuilder.CreateIndex(
                name: "IX_Friendships_FromId",
                table: "Friendships",
                column: "FromId"
            );

            migrationBuilder.CreateIndex(
                name: "IX_Friendships_PairKey",
                table: "Friendships",
                column: "PairKey",
                unique: true
            );

            migrationBuilder.CreateIndex(
                name: "IX_Friendships_ToId",
                table: "Friendships",
                column: "ToId"
            );

            migrationBuilder.CreateIndex(
                name: "IX_GuildMembers_UserId",
                table: "GuildMembers",
                column: "UserId"
            );

            migrationBuilder.CreateIndex(
                name: "IX_Guilds_OwnerId",
                table: "Guilds",
                column: "OwnerId"
            );

            migrationBuilder.CreateIndex(
                name: "IX_MessageReceipts_UserId",
                table: "MessageReceipts",
                column: "UserId"
            );

            migrationBuilder.AddForeignKey(
                name: "FK_Messages_Chats_ChatId",
                table: "Messages",
                column: "ChatId",
                principalTable: "Chats",
                principalColumn: "Id",
                onDelete: ReferentialAction.Cascade
            );
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(name: "FK_Messages_Chats_ChatId", table: "Messages");

            migrationBuilder.DropTable(name: "Attachments");

            migrationBuilder.DropTable(name: "AuditLog");

            migrationBuilder.DropTable(name: "CallParticipants");

            migrationBuilder.DropTable(name: "ChatParticipants");

            migrationBuilder.DropTable(name: "CourseLinks");

            migrationBuilder.DropTable(name: "ExternalIdentities");

            migrationBuilder.DropTable(name: "Friendships");

            migrationBuilder.DropTable(name: "GuildMembers");

            migrationBuilder.DropTable(name: "LaunchNonces");

            migrationBuilder.DropTable(name: "MessageReceipts");

            migrationBuilder.DropTable(name: "Settings");

            migrationBuilder.DropTable(name: "Calls");

            migrationBuilder.DropTable(name: "Chats");

            migrationBuilder.DropTable(name: "Guilds");

            migrationBuilder.DropIndex(name: "IX_Messages_ChatId_SentAt", table: "Messages");

            migrationBuilder.DropIndex(
                name: "IX_Messages_SenderId_ClientOperationId",
                table: "Messages"
            );

            migrationBuilder.DropColumn(name: "ChatId", table: "Messages");

            migrationBuilder.DropColumn(name: "ClientOperationId", table: "Messages");

            migrationBuilder.DropColumn(name: "DeletedAtUtc", table: "Messages");

            migrationBuilder.DropColumn(name: "EditedAtUtc", table: "Messages");

            migrationBuilder.DropColumn(name: "DisplayName", table: "AspNetUsers");

            migrationBuilder.DropColumn(name: "IsAdmin", table: "AspNetUsers");

            migrationBuilder.DropColumn(name: "IsStudent", table: "AspNetUsers");

            migrationBuilder.DropColumn(name: "IsSuspended", table: "AspNetUsers");

            migrationBuilder.DropColumn(name: "UniversityLogin", table: "AspNetUsers");

            migrationBuilder.CreateIndex(
                name: "IX_Messages_SenderId",
                table: "Messages",
                column: "SenderId"
            );
        }
    }
}
