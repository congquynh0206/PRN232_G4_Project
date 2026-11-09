using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace G4.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddImmediateCheckout : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<bool>(
                name: "ImmediatePaymentRequired",
                table: "Product",
                type: "bit",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<bool>(
                name: "IsListed",
                table: "Product",
                type: "bit",
                nullable: false,
                defaultValue: true);

            migrationBuilder.CreateTable(
                name: "ImmediateCheckout",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    BuyerId = table.Column<int>(type: "int", nullable: false),
                    RequestJson = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    QuoteJson = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "datetime2", nullable: false),
                    ExpiresAt = table.Column<DateTime>(type: "datetime2", nullable: false),
                    Status = table.Column<string>(type: "nvarchar(20)", maxLength: 20, nullable: false),
                    OrderId = table.Column<int>(type: "int", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ImmediateCheckout", x => x.Id);
                    table.ForeignKey(
                        name: "FK_ImmediateCheckout_OrderTable_OrderId",
                        column: x => x.OrderId,
                        principalTable: "OrderTable",
                        principalColumn: "id");
                    table.ForeignKey(
                        name: "FK_ImmediateCheckout_User_BuyerId",
                        column: x => x.BuyerId,
                        principalTable: "User",
                        principalColumn: "id");
                });

            migrationBuilder.CreateTable(
                name: "ImmediatePaymentAttempt",
                columns: table => new
                {
                    Id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    CheckoutId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Key = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: false),
                    Status = table.Column<string>(type: "nvarchar(20)", maxLength: 20, nullable: false),
                    OrderId = table.Column<int>(type: "int", nullable: true),
                    ErrorCode = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: true),
                    CreatedAt = table.Column<DateTime>(type: "datetime2", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ImmediatePaymentAttempt", x => x.Id);
                    table.ForeignKey(
                        name: "FK_ImmediatePaymentAttempt_ImmediateCheckout_CheckoutId",
                        column: x => x.CheckoutId,
                        principalTable: "ImmediateCheckout",
                        principalColumn: "Id");
                    table.ForeignKey(
                        name: "FK_ImmediatePaymentAttempt_OrderTable_OrderId",
                        column: x => x.OrderId,
                        principalTable: "OrderTable",
                        principalColumn: "id");
                });

            migrationBuilder.CreateIndex(
                name: "IX_ImmediateCheckout_BuyerId_CreatedAt",
                table: "ImmediateCheckout",
                columns: new[] { "BuyerId", "CreatedAt" });

            migrationBuilder.CreateIndex(
                name: "IX_ImmediateCheckout_OrderId",
                table: "ImmediateCheckout",
                column: "OrderId",
                unique: true,
                filter: "[OrderId] IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "IX_ImmediatePaymentAttempt_CheckoutId",
                table: "ImmediatePaymentAttempt",
                column: "CheckoutId");

            migrationBuilder.CreateIndex(
                name: "IX_ImmediatePaymentAttempt_Key",
                table: "ImmediatePaymentAttempt",
                column: "Key",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_ImmediatePaymentAttempt_OrderId",
                table: "ImmediatePaymentAttempt",
                column: "OrderId");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "ImmediatePaymentAttempt");

            migrationBuilder.DropTable(
                name: "ImmediateCheckout");

            migrationBuilder.DropColumn(
                name: "ImmediatePaymentRequired",
                table: "Product");

            migrationBuilder.DropColumn(
                name: "IsListed",
                table: "Product");
        }
    }
}
