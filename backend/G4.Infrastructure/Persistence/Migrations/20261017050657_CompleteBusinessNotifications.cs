using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace G4.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class CompleteBusinessNotifications : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_NotificationOutbox_OrderId_EventType",
                table: "NotificationOutbox");

            migrationBuilder.AddColumn<int>(
                name: "CancelRequestNumber",
                table: "OrderTable",
                type: "int",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<int>(
                name: "PayoutId",
                table: "NotificationOutbox",
                type: "int",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "RecipientUserId",
                table: "NotificationOutbox",
                type: "int",
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "IX_NotificationOutbox_OrderId_EventType",
                table: "NotificationOutbox",
                columns: new[] { "OrderId", "EventType" },
                unique: true,
                filter: "[EventKey] IS NULL AND [OrderId] IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "IX_NotificationOutbox_PayoutId",
                table: "NotificationOutbox",
                column: "PayoutId");

            migrationBuilder.CreateIndex(
                name: "IX_NotificationOutbox_RecipientUserId_CreatedAt",
                table: "NotificationOutbox",
                columns: new[] { "RecipientUserId", "CreatedAt" });

            migrationBuilder.AddForeignKey(
                name: "FK_NotificationOutbox_SellerPayout_PayoutId",
                table: "NotificationOutbox",
                column: "PayoutId",
                principalTable: "SellerPayout",
                principalColumn: "Id");

            migrationBuilder.AddForeignKey(
                name: "FK_NotificationOutbox_User_RecipientUserId",
                table: "NotificationOutbox",
                column: "RecipientUserId",
                principalTable: "User",
                principalColumn: "id");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_NotificationOutbox_SellerPayout_PayoutId",
                table: "NotificationOutbox");

            migrationBuilder.DropForeignKey(
                name: "FK_NotificationOutbox_User_RecipientUserId",
                table: "NotificationOutbox");

            migrationBuilder.DropIndex(
                name: "IX_NotificationOutbox_OrderId_EventType",
                table: "NotificationOutbox");

            migrationBuilder.DropIndex(
                name: "IX_NotificationOutbox_PayoutId",
                table: "NotificationOutbox");

            migrationBuilder.DropIndex(
                name: "IX_NotificationOutbox_RecipientUserId_CreatedAt",
                table: "NotificationOutbox");

            migrationBuilder.DropColumn(
                name: "CancelRequestNumber",
                table: "OrderTable");

            migrationBuilder.DropColumn(
                name: "PayoutId",
                table: "NotificationOutbox");

            migrationBuilder.DropColumn(
                name: "RecipientUserId",
                table: "NotificationOutbox");

            migrationBuilder.CreateIndex(
                name: "IX_NotificationOutbox_OrderId_EventType",
                table: "NotificationOutbox",
                columns: new[] { "OrderId", "EventType" },
                unique: true,
                filter: "[OrderId] IS NOT NULL");
        }
    }
}
