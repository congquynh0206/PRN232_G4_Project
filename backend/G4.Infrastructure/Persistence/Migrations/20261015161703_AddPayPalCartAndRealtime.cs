using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace G4.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddPayPalCartAndRealtime : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<DateTime>(
                name: "CaptureReservedAt",
                table: "Payment",
                type: "datetime2",
                nullable: true);

            migrationBuilder.AddColumn<long>(
                name: "PayPalCheckoutId",
                table: "Payment",
                type: "bigint",
                nullable: true);

            migrationBuilder.AddColumn<decimal>(
                name: "ReservedSellerGross",
                table: "Payment",
                type: "decimal(18,2)",
                precision: 18,
                scale: 2,
                nullable: true);

            migrationBuilder.CreateTable(
                name: "CommerceChange",
                columns: table => new
                {
                    Id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    Topic = table.Column<string>(type: "nvarchar(30)", maxLength: 30, nullable: false),
                    OrderId = table.Column<int>(type: "int", nullable: true),
                    SellerId = table.Column<int>(type: "int", nullable: true),
                    BuyerId = table.Column<int>(type: "int", nullable: true),
                    SellerAccountId = table.Column<int>(type: "int", nullable: true),
                    ShippingInfoId = table.Column<int>(type: "int", nullable: true),
                    ShipperId = table.Column<int>(type: "int", nullable: true),
                    DisputeId = table.Column<int>(type: "int", nullable: true),
                    CreatedAt = table.Column<DateTime>(type: "datetime2", nullable: false),
                    DispatchedAt = table.Column<DateTime>(type: "datetime2", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_CommerceChange", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "PayPalCheckout",
                columns: table => new
                {
                    Id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    BuyerId = table.Column<int>(type: "int", nullable: false),
                    CheckoutKey = table.Column<string>(type: "nvarchar(80)", maxLength: 80, nullable: false),
                    RequestKey = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: false),
                    Status = table.Column<string>(type: "nvarchar(30)", maxLength: 30, nullable: false),
                    ProviderOrderId = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: true),
                    ApprovalUrl = table.Column<string>(type: "nvarchar(2000)", maxLength: 2000, nullable: true),
                    LastError = table.Column<string>(type: "nvarchar(500)", maxLength: 500, nullable: true),
                    CreatedAt = table.Column<DateTime>(type: "datetime2", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "datetime2", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_PayPalCheckout", x => x.Id);
                    table.ForeignKey(
                        name: "FK_PayPalCheckout_CheckoutBatch_CheckoutKey",
                        column: x => x.CheckoutKey,
                        principalTable: "CheckoutBatch",
                        principalColumn: "Key");
                    table.ForeignKey(
                        name: "FK_PayPalCheckout_User_BuyerId",
                        column: x => x.BuyerId,
                        principalTable: "User",
                        principalColumn: "id");
                });

            migrationBuilder.CreateIndex(
                name: "IX_Payment_PayPalCheckoutId_orderId",
                table: "Payment",
                columns: new[] { "PayPalCheckoutId", "orderId" },
                unique: true,
                filter: "[PayPalCheckoutId] IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "IX_CommerceChange_DispatchedAt_Id",
                table: "CommerceChange",
                columns: new[] { "DispatchedAt", "Id" });

            migrationBuilder.CreateIndex(
                name: "IX_PayPalCheckout_BuyerId_RequestKey",
                table: "PayPalCheckout",
                columns: new[] { "BuyerId", "RequestKey" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_PayPalCheckout_CheckoutKey_Status",
                table: "PayPalCheckout",
                columns: new[] { "CheckoutKey", "Status" });

            migrationBuilder.CreateIndex(
                name: "IX_PayPalCheckout_ProviderOrderId",
                table: "PayPalCheckout",
                column: "ProviderOrderId",
                unique: true,
                filter: "[ProviderOrderId] IS NOT NULL");

            migrationBuilder.AddForeignKey(
                name: "FK_Payment_PayPalCheckout_PayPalCheckoutId",
                table: "Payment",
                column: "PayPalCheckoutId",
                principalTable: "PayPalCheckout",
                principalColumn: "Id");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_Payment_PayPalCheckout_PayPalCheckoutId",
                table: "Payment");

            migrationBuilder.DropTable(
                name: "CommerceChange");

            migrationBuilder.DropTable(
                name: "PayPalCheckout");

            migrationBuilder.DropIndex(
                name: "IX_Payment_PayPalCheckoutId_orderId",
                table: "Payment");

            migrationBuilder.DropColumn(
                name: "CaptureReservedAt",
                table: "Payment");

            migrationBuilder.DropColumn(
                name: "PayPalCheckoutId",
                table: "Payment");

            migrationBuilder.DropColumn(
                name: "ReservedSellerGross",
                table: "Payment");
        }
    }
}
