using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace G4.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddBestOffers : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<bool>(
                name: "AllowOffers",
                table: "Product",
                type: "bit",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<decimal>(
                name: "AutoAcceptOfferPrice",
                table: "Product",
                type: "decimal(10,2)",
                nullable: true);

            migrationBuilder.AddColumn<decimal>(
                name: "AutoDeclineOfferBelow",
                table: "Product",
                type: "decimal(10,2)",
                nullable: true);

            migrationBuilder.CreateTable(
                name: "BestOffer",
                columns: table => new
                {
                    Id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    ProductId = table.Column<int>(type: "int", nullable: false),
                    BuyerId = table.Column<int>(type: "int", nullable: false),
                    SellerId = table.Column<int>(type: "int", nullable: false),
                    AddressId = table.Column<int>(type: "int", nullable: false),
                    CurrentAmount = table.Column<decimal>(type: "decimal(10,2)", nullable: false),
                    CurrentActorId = table.Column<int>(type: "int", nullable: false),
                    Status = table.Column<string>(type: "nvarchar(24)", maxLength: 24, nullable: false),
                    BuyerOfferCount = table.Column<int>(type: "int", nullable: false),
                    PaymentMethod = table.Column<string>(type: "nvarchar(20)", maxLength: 20, nullable: false),
                    CardProfile = table.Column<string>(type: "nvarchar(20)", maxLength: 20, nullable: true),
                    CardExpiry = table.Column<string>(type: "nvarchar(5)", maxLength: 5, nullable: true),
                    OrderId = table.Column<int>(type: "int", nullable: true),
                    CreationKey = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "datetime2", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "datetime2", nullable: false),
                    ExpiresAt = table.Column<DateTime>(type: "datetime2", nullable: false),
                    RowVersion = table.Column<byte[]>(type: "rowversion", rowVersion: true, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_BestOffer", x => x.Id);
                    table.ForeignKey(
                        name: "FK_BestOffer_Address_AddressId",
                        column: x => x.AddressId,
                        principalTable: "Address",
                        principalColumn: "id");
                    table.ForeignKey(
                        name: "FK_BestOffer_OrderTable_OrderId",
                        column: x => x.OrderId,
                        principalTable: "OrderTable",
                        principalColumn: "id");
                    table.ForeignKey(
                        name: "FK_BestOffer_Product_ProductId",
                        column: x => x.ProductId,
                        principalTable: "Product",
                        principalColumn: "id");
                    table.ForeignKey(
                        name: "FK_BestOffer_User_BuyerId",
                        column: x => x.BuyerId,
                        principalTable: "User",
                        principalColumn: "id");
                    table.ForeignKey(
                        name: "FK_BestOffer_User_CurrentActorId",
                        column: x => x.CurrentActorId,
                        principalTable: "User",
                        principalColumn: "id");
                    table.ForeignKey(
                        name: "FK_BestOffer_User_SellerId",
                        column: x => x.SellerId,
                        principalTable: "User",
                        principalColumn: "id");
                });

            migrationBuilder.CreateTable(
                name: "BestOfferTurn",
                columns: table => new
                {
                    Id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    BestOfferId = table.Column<long>(type: "bigint", nullable: false),
                    ActorId = table.Column<int>(type: "int", nullable: false),
                    Amount = table.Column<decimal>(type: "decimal(10,2)", nullable: false),
                    Action = table.Column<string>(type: "nvarchar(20)", maxLength: 20, nullable: false),
                    Result = table.Column<string>(type: "nvarchar(24)", maxLength: 24, nullable: true),
                    RequestKey = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: true),
                    CreatedAt = table.Column<DateTime>(type: "datetime2", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_BestOfferTurn", x => x.Id);
                    table.ForeignKey(
                        name: "FK_BestOfferTurn_BestOffer_BestOfferId",
                        column: x => x.BestOfferId,
                        principalTable: "BestOffer",
                        principalColumn: "Id");
                    table.ForeignKey(
                        name: "FK_BestOfferTurn_User_ActorId",
                        column: x => x.ActorId,
                        principalTable: "User",
                        principalColumn: "id");
                });

            migrationBuilder.CreateIndex(
                name: "IX_BestOffer_AddressId",
                table: "BestOffer",
                column: "AddressId");

            migrationBuilder.CreateIndex(
                name: "IX_BestOffer_BuyerId_UpdatedAt",
                table: "BestOffer",
                columns: new[] { "BuyerId", "UpdatedAt" });

            migrationBuilder.CreateIndex(
                name: "IX_BestOffer_CreationKey",
                table: "BestOffer",
                column: "CreationKey",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_BestOffer_CurrentActorId",
                table: "BestOffer",
                column: "CurrentActorId");

            migrationBuilder.CreateIndex(
                name: "IX_BestOffer_OrderId",
                table: "BestOffer",
                column: "OrderId");

            migrationBuilder.CreateIndex(
                name: "IX_BestOffer_ProductId_BuyerId_Status",
                table: "BestOffer",
                columns: new[] { "ProductId", "BuyerId", "Status" });

            migrationBuilder.CreateIndex(
                name: "IX_BestOffer_SellerId_Status_UpdatedAt",
                table: "BestOffer",
                columns: new[] { "SellerId", "Status", "UpdatedAt" });

            migrationBuilder.CreateIndex(
                name: "IX_BestOfferTurn_ActorId",
                table: "BestOfferTurn",
                column: "ActorId");

            migrationBuilder.CreateIndex(
                name: "IX_BestOfferTurn_BestOfferId_Id",
                table: "BestOfferTurn",
                columns: new[] { "BestOfferId", "Id" });

            migrationBuilder.CreateIndex(
                name: "IX_BestOfferTurn_RequestKey",
                table: "BestOfferTurn",
                column: "RequestKey",
                unique: true,
                filter: "[RequestKey] IS NOT NULL");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "BestOfferTurn");

            migrationBuilder.DropTable(
                name: "BestOffer");

            migrationBuilder.DropColumn(
                name: "AllowOffers",
                table: "Product");

            migrationBuilder.DropColumn(
                name: "AutoAcceptOfferPrice",
                table: "Product");

            migrationBuilder.DropColumn(
                name: "AutoDeclineOfferBelow",
                table: "Product");
        }
    }
}
