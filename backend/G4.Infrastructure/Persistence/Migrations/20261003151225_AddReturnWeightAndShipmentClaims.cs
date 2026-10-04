using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace G4.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddReturnWeightAndShipmentClaims : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<DateTime>(
                name: "ClaimedAt",
                table: "ShippingInfo",
                type: "datetime2",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "DeliveryAddressSnapshot",
                table: "ShippingInfo",
                type: "nvarchar(500)",
                maxLength: 500,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "PickupAddressSnapshot",
                table: "ShippingInfo",
                type: "nvarchar(500)",
                maxLength: 500,
                nullable: true);

            migrationBuilder.AddColumn<byte[]>(
                name: "RowVersion",
                table: "ShippingInfo",
                type: "rowversion",
                rowVersion: true,
                nullable: false,
                defaultValue: new byte[0]);

            migrationBuilder.AddColumn<int>(
                name: "ShipperId",
                table: "ShippingInfo",
                type: "int",
                nullable: true);

            migrationBuilder.AddColumn<DateTime>(
                name: "ConfirmationDueAt",
                table: "ReturnRequest",
                type: "datetime2",
                nullable: true);

            migrationBuilder.AddColumn<decimal>(
                name: "WeightKg",
                table: "Product",
                type: "decimal(10,3)",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "PickupAddressSnapshot",
                table: "OrderTable",
                type: "nvarchar(500)",
                maxLength: 500,
                nullable: true);

            migrationBuilder.AddColumn<decimal>(
                name: "TotalWeightKg",
                table: "OrderTable",
                type: "decimal(18,3)",
                nullable: true);

            migrationBuilder.AddColumn<decimal>(
                name: "UnitWeightKgSnapshot",
                table: "OrderItem",
                type: "decimal(10,3)",
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "IX_ShippingInfo_ShipperId_status",
                table: "ShippingInfo",
                columns: new[] { "ShipperId", "status" });

            migrationBuilder.AddForeignKey(
                name: "FK_ShippingInfo_User_ShipperId",
                table: "ShippingInfo",
                column: "ShipperId",
                principalTable: "User",
                principalColumn: "id");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_ShippingInfo_User_ShipperId",
                table: "ShippingInfo");

            migrationBuilder.DropIndex(
                name: "IX_ShippingInfo_ShipperId_status",
                table: "ShippingInfo");

            migrationBuilder.DropColumn(
                name: "ClaimedAt",
                table: "ShippingInfo");

            migrationBuilder.DropColumn(
                name: "DeliveryAddressSnapshot",
                table: "ShippingInfo");

            migrationBuilder.DropColumn(
                name: "PickupAddressSnapshot",
                table: "ShippingInfo");

            migrationBuilder.DropColumn(
                name: "RowVersion",
                table: "ShippingInfo");

            migrationBuilder.DropColumn(
                name: "ShipperId",
                table: "ShippingInfo");

            migrationBuilder.DropColumn(
                name: "ConfirmationDueAt",
                table: "ReturnRequest");

            migrationBuilder.DropColumn(
                name: "WeightKg",
                table: "Product");

            migrationBuilder.DropColumn(
                name: "PickupAddressSnapshot",
                table: "OrderTable");

            migrationBuilder.DropColumn(
                name: "TotalWeightKg",
                table: "OrderTable");

            migrationBuilder.DropColumn(
                name: "UnitWeightKgSnapshot",
                table: "OrderItem");
        }
    }
}
