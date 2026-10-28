using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace G4.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddUserNotificationBell : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "NotificationMessage",
                table: "CommerceChange",
                type: "nvarchar(500)",
                maxLength: 500,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "NotificationTitle",
                table: "CommerceChange",
                type: "nvarchar(160)",
                maxLength: 160,
                nullable: true);

            migrationBuilder.CreateTable(
                name: "UserNotification",
                columns: table => new
                {
                    Id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    CommerceChangeId = table.Column<long>(type: "bigint", nullable: false),
                    UserId = table.Column<int>(type: "int", nullable: false),
                    Title = table.Column<string>(type: "nvarchar(160)", maxLength: 160, nullable: false),
                    Message = table.Column<string>(type: "nvarchar(500)", maxLength: 500, nullable: false),
                    TargetUrl = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "datetime2", nullable: false),
                    ReadAt = table.Column<DateTime>(type: "datetime2", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_UserNotification", x => x.Id);
                    table.ForeignKey(
                        name: "FK_UserNotification_CommerceChange_CommerceChangeId",
                        column: x => x.CommerceChangeId,
                        principalTable: "CommerceChange",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_UserNotification_User_UserId",
                        column: x => x.UserId,
                        principalTable: "User",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "IX_UserNotification_CommerceChangeId",
                table: "UserNotification",
                column: "CommerceChangeId");

            migrationBuilder.CreateIndex(
                name: "IX_UserNotification_UserId_CommerceChangeId",
                table: "UserNotification",
                columns: new[] { "UserId", "CommerceChangeId" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_UserNotification_UserId_ReadAt_Id",
                table: "UserNotification",
                columns: new[] { "UserId", "ReadAt", "Id" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "UserNotification");

            migrationBuilder.DropColumn(
                name: "NotificationMessage",
                table: "CommerceChange");

            migrationBuilder.DropColumn(
                name: "NotificationTitle",
                table: "CommerceChange");
        }
    }
}
