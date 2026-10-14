using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace G4.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddReasonBasedReturnPricing : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<decimal>(
                name: "ReturnShippingCost",
                table: "SellerSettlement",
                type: "decimal(18,2)",
                precision: 18,
                scale: 2,
                nullable: false,
                defaultValue: 0m);

            migrationBuilder.AddColumn<decimal>(
                name: "ReturnShippingRecovery",
                table: "SellerSettlement",
                type: "decimal(18,2)",
                precision: 18,
                scale: 2,
                nullable: false,
                defaultValue: 0m);

            migrationBuilder.AddColumn<decimal>(
                name: "BuyerRefundAmount",
                table: "ReturnRequest",
                type: "decimal(18,2)",
                precision: 18,
                scale: 2,
                nullable: false,
                defaultValue: 0m);

            migrationBuilder.AddColumn<decimal>(
                name: "BuyerReturnShippingFee",
                table: "ReturnRequest",
                type: "decimal(18,2)",
                precision: 18,
                scale: 2,
                nullable: false,
                defaultValue: 0m);

            migrationBuilder.AddColumn<string>(
                name: "DeliveryAddressSnapshot",
                table: "ReturnRequest",
                type: "nvarchar(500)",
                maxLength: 500,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "Description",
                table: "ReturnRequest",
                type: "nvarchar(2000)",
                maxLength: 2000,
                nullable: true);

            migrationBuilder.AddColumn<decimal>(
                name: "GoodsPaidSnapshot",
                table: "ReturnRequest",
                type: "decimal(18,2)",
                precision: 18,
                scale: 2,
                nullable: false,
                defaultValue: 0m);

            migrationBuilder.AddColumn<decimal>(
                name: "OutboundShippingRetained",
                table: "ReturnRequest",
                type: "decimal(18,2)",
                precision: 18,
                scale: 2,
                nullable: false,
                defaultValue: 0m);

            migrationBuilder.AddColumn<string>(
                name: "PickupAddressSnapshot",
                table: "ReturnRequest",
                type: "nvarchar(500)",
                maxLength: 500,
                nullable: true);

            migrationBuilder.AddColumn<decimal>(
                name: "PlatformSubsidyReversal",
                table: "ReturnRequest",
                type: "decimal(18,2)",
                precision: 18,
                scale: 2,
                nullable: false,
                defaultValue: 0m);

            migrationBuilder.AddColumn<string>(
                name: "PricingFingerprint",
                table: "ReturnRequest",
                type: "nvarchar(64)",
                maxLength: 64,
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "PricingPolicyVersion",
                table: "ReturnRequest",
                type: "int",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<string>(
                name: "ReasonCode",
                table: "ReturnRequest",
                type: "nvarchar(30)",
                maxLength: 30,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "RequestKey",
                table: "ReturnRequest",
                type: "nvarchar(100)",
                maxLength: 100,
                nullable: true);

            migrationBuilder.AddColumn<decimal>(
                name: "ReturnShippingFee",
                table: "ReturnRequest",
                type: "decimal(18,2)",
                precision: 18,
                scale: 2,
                nullable: false,
                defaultValue: 0m);

            migrationBuilder.AddColumn<decimal>(
                name: "SellerGrossRefund",
                table: "ReturnRequest",
                type: "decimal(18,2)",
                precision: 18,
                scale: 2,
                nullable: false,
                defaultValue: 0m);

            migrationBuilder.AddColumn<DateTime>(
                name: "ShippingCostChargedAt",
                table: "ReturnRequest",
                type: "datetime2",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "ShippingPayer",
                table: "ReturnRequest",
                type: "nvarchar(20)",
                maxLength: 20,
                nullable: true);

            migrationBuilder.AddColumn<decimal>(
                name: "WeightKgSnapshot",
                table: "ReturnRequest",
                type: "decimal(18,3)",
                precision: 18,
                scale: 3,
                nullable: true);

            migrationBuilder.AddColumn<decimal>(
                name: "BuyerReturnShippingRecovery",
                table: "Refund",
                type: "decimal(18,2)",
                precision: 18,
                scale: 2,
                nullable: false,
                defaultValue: 0m);

            migrationBuilder.AddColumn<decimal>(
                name: "PlatformSubsidyReversalAmount",
                table: "Refund",
                type: "decimal(18,2)",
                precision: 18,
                scale: 2,
                nullable: false,
                defaultValue: 0m);

            migrationBuilder.AddColumn<int>(
                name: "ReturnPolicyVersion",
                table: "Refund",
                type: "int",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<decimal>(
                name: "SellerGrossRefundAmount",
                table: "Refund",
                type: "decimal(18,2)",
                precision: 18,
                scale: 2,
                nullable: false,
                defaultValue: 0m);

            migrationBuilder.CreateIndex(
                name: "IX_ReturnRequest_RequestKey",
                table: "ReturnRequest",
                column: "RequestKey",
                unique: true,
                filter: "[RequestKey] IS NOT NULL");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_ReturnRequest_RequestKey",
                table: "ReturnRequest");

            migrationBuilder.DropColumn(
                name: "ReturnShippingCost",
                table: "SellerSettlement");

            migrationBuilder.DropColumn(
                name: "ReturnShippingRecovery",
                table: "SellerSettlement");

            migrationBuilder.DropColumn(
                name: "BuyerRefundAmount",
                table: "ReturnRequest");

            migrationBuilder.DropColumn(
                name: "BuyerReturnShippingFee",
                table: "ReturnRequest");

            migrationBuilder.DropColumn(
                name: "DeliveryAddressSnapshot",
                table: "ReturnRequest");

            migrationBuilder.DropColumn(
                name: "Description",
                table: "ReturnRequest");

            migrationBuilder.DropColumn(
                name: "GoodsPaidSnapshot",
                table: "ReturnRequest");

            migrationBuilder.DropColumn(
                name: "OutboundShippingRetained",
                table: "ReturnRequest");

            migrationBuilder.DropColumn(
                name: "PickupAddressSnapshot",
                table: "ReturnRequest");

            migrationBuilder.DropColumn(
                name: "PlatformSubsidyReversal",
                table: "ReturnRequest");

            migrationBuilder.DropColumn(
                name: "PricingFingerprint",
                table: "ReturnRequest");

            migrationBuilder.DropColumn(
                name: "PricingPolicyVersion",
                table: "ReturnRequest");

            migrationBuilder.DropColumn(
                name: "ReasonCode",
                table: "ReturnRequest");

            migrationBuilder.DropColumn(
                name: "RequestKey",
                table: "ReturnRequest");

            migrationBuilder.DropColumn(
                name: "ReturnShippingFee",
                table: "ReturnRequest");

            migrationBuilder.DropColumn(
                name: "SellerGrossRefund",
                table: "ReturnRequest");

            migrationBuilder.DropColumn(
                name: "ShippingCostChargedAt",
                table: "ReturnRequest");

            migrationBuilder.DropColumn(
                name: "ShippingPayer",
                table: "ReturnRequest");

            migrationBuilder.DropColumn(
                name: "WeightKgSnapshot",
                table: "ReturnRequest");

            migrationBuilder.DropColumn(
                name: "BuyerReturnShippingRecovery",
                table: "Refund");

            migrationBuilder.DropColumn(
                name: "PlatformSubsidyReversalAmount",
                table: "Refund");

            migrationBuilder.DropColumn(
                name: "ReturnPolicyVersion",
                table: "Refund");

            migrationBuilder.DropColumn(
                name: "SellerGrossRefundAmount",
                table: "Refund");
        }
    }
}
