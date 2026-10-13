using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace G4.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddOrderAddressChanges : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<DateTime>(
                name: "AddressChangeAcknowledgedAt",
                table: "ShippingInfo",
                type: "datetime2",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "AddressChangeRequestId",
                table: "ShippingInfo",
                type: "int",
                nullable: true);

            migrationBuilder.AddColumn<decimal>(
                name: "ShippingAdjustmentCost",
                table: "SellerSettlement",
                type: "decimal(18,2)",
                nullable: false,
                defaultValue: 0m);

            migrationBuilder.AddColumn<int>(
                name: "AddressChangeCount",
                table: "OrderTable",
                type: "int",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<string>(
                name: "DeliveryCountrySnapshot",
                table: "OrderTable",
                type: "nvarchar(50)",
                maxLength: 50,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "DeliveryStateSnapshot",
                table: "OrderTable",
                type: "nvarchar(50)",
                maxLength: 50,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "PickupCountrySnapshot",
                table: "OrderTable",
                type: "nvarchar(50)",
                maxLength: 50,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "PickupStateSnapshot",
                table: "OrderTable",
                type: "nvarchar(50)",
                maxLength: 50,
                nullable: true);

            migrationBuilder.CreateTable(
                name: "AddressChangeRequest",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    OrderId = table.Column<int>(type: "int", nullable: false),
                    RequestKey = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: false),
                    Reason = table.Column<string>(type: "nvarchar(1000)", maxLength: 1000, nullable: false),
                    OldAddress = table.Column<string>(type: "nvarchar(500)", maxLength: 500, nullable: false),
                    NewAddress = table.Column<string>(type: "nvarchar(500)", maxLength: 500, nullable: false),
                    NewState = table.Column<string>(type: "nvarchar(50)", maxLength: 50, nullable: false),
                    NewCountry = table.Column<string>(type: "nvarchar(50)", maxLength: 50, nullable: false),
                    OldShippingCost = table.Column<decimal>(type: "decimal(18,2)", nullable: false),
                    NewShippingCost = table.Column<decimal>(type: "decimal(18,2)", nullable: false),
                    SellerCap = table.Column<decimal>(type: "decimal(18,2)", nullable: false),
                    BuyerRefund = table.Column<decimal>(type: "decimal(18,2)", nullable: false),
                    RefundId = table.Column<int>(type: "int", nullable: true),
                    Status = table.Column<string>(type: "nvarchar(20)", maxLength: 20, nullable: false),
                    DecisionReason = table.Column<string>(type: "nvarchar(1000)", maxLength: 1000, nullable: true),
                    CreatedAt = table.Column<DateTime>(type: "datetime2", nullable: false),
                    DecidedAt = table.Column<DateTime>(type: "datetime2", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_AddressChangeRequest", x => x.Id);
                    table.ForeignKey(
                        name: "FK_AddressChangeRequest_OrderTable_OrderId",
                        column: x => x.OrderId,
                        principalTable: "OrderTable",
                        principalColumn: "id");
                    table.ForeignKey(
                        name: "FK_AddressChangeRequest_Refund_RefundId",
                        column: x => x.RefundId,
                        principalTable: "Refund",
                        principalColumn: "Id");
                });

            migrationBuilder.CreateTable(
                name: "SellerShippingPreference",
                columns: table => new
                {
                    SellerId = table.Column<int>(type: "int", nullable: false),
                    AddressChangeCap = table.Column<decimal>(type: "decimal(18,2)", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_SellerShippingPreference", x => x.SellerId);
                    table.ForeignKey(
                        name: "FK_SellerShippingPreference_User_SellerId",
                        column: x => x.SellerId,
                        principalTable: "User",
                        principalColumn: "id");
                });

            migrationBuilder.CreateIndex(
                name: "IX_ShippingInfo_AddressChangeRequestId",
                table: "ShippingInfo",
                column: "AddressChangeRequestId");

            migrationBuilder.CreateIndex(
                name: "IX_AddressChangeRequest_OrderId",
                table: "AddressChangeRequest",
                column: "OrderId",
                unique: true,
                filter: "[Status] = 'Pending'");

            migrationBuilder.CreateIndex(
                name: "IX_AddressChangeRequest_RefundId",
                table: "AddressChangeRequest",
                column: "RefundId");

            migrationBuilder.CreateIndex(
                name: "IX_AddressChangeRequest_RequestKey",
                table: "AddressChangeRequest",
                column: "RequestKey",
                unique: true);

            migrationBuilder.AddForeignKey(
                name: "FK_ShippingInfo_AddressChangeRequest_AddressChangeRequestId",
                table: "ShippingInfo",
                column: "AddressChangeRequestId",
                principalTable: "AddressChangeRequest",
                principalColumn: "Id");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_ShippingInfo_AddressChangeRequest_AddressChangeRequestId",
                table: "ShippingInfo");

            migrationBuilder.DropTable(
                name: "AddressChangeRequest");

            migrationBuilder.DropTable(
                name: "SellerShippingPreference");

            migrationBuilder.DropIndex(
                name: "IX_ShippingInfo_AddressChangeRequestId",
                table: "ShippingInfo");

            migrationBuilder.DropColumn(
                name: "AddressChangeAcknowledgedAt",
                table: "ShippingInfo");

            migrationBuilder.DropColumn(
                name: "AddressChangeRequestId",
                table: "ShippingInfo");

            migrationBuilder.DropColumn(
                name: "ShippingAdjustmentCost",
                table: "SellerSettlement");

            migrationBuilder.DropColumn(
                name: "AddressChangeCount",
                table: "OrderTable");

            migrationBuilder.DropColumn(
                name: "DeliveryCountrySnapshot",
                table: "OrderTable");

            migrationBuilder.DropColumn(
                name: "DeliveryStateSnapshot",
                table: "OrderTable");

            migrationBuilder.DropColumn(
                name: "PickupCountrySnapshot",
                table: "OrderTable");

            migrationBuilder.DropColumn(
                name: "PickupStateSnapshot",
                table: "OrderTable");
        }
    }
}
