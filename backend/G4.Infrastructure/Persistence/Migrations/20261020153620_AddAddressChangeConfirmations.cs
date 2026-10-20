using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace G4.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddAddressChangeConfirmations : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_AddressChangeRequest_OrderId",
                table: "AddressChangeRequest");

            migrationBuilder.AddColumn<int>(
                name: "Revision",
                table: "SellerShippingPreference",
                type: "int",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<DateTime>(
                name: "AppliedAt",
                table: "AddressChangeRequest",
                type: "datetime2",
                nullable: true);

            migrationBuilder.AddColumn<DateTime>(
                name: "BuyerConfirmedAt",
                table: "AddressChangeRequest",
                type: "datetime2",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "BuyerUserId",
                table: "AddressChangeRequest",
                type: "int",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "CapRevision",
                table: "AddressChangeRequest",
                type: "int",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<int>(
                name: "ExpectedShipperId",
                table: "AddressChangeRequest",
                type: "int",
                nullable: true);

            migrationBuilder.AddColumn<DateTime>(
                name: "ExpiresAt",
                table: "AddressChangeRequest",
                type: "datetime2",
                nullable: true);

            migrationBuilder.AddColumn<bool>(
                name: "IsPending",
                table: "AddressChangeRequest",
                type: "bit",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<string>(
                name: "QuoteToken",
                table: "AddressChangeRequest",
                type: "nvarchar(64)",
                maxLength: 64,
                nullable: true);

            migrationBuilder.AddColumn<DateTime>(
                name: "SellerConfirmedAt",
                table: "AddressChangeRequest",
                type: "datetime2",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "SellerUserId",
                table: "AddressChangeRequest",
                type: "int",
                nullable: true);

            migrationBuilder.AddColumn<DateTime>(
                name: "ShipperDecidedAt",
                table: "AddressChangeRequest",
                type: "datetime2",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "ShipperUserId",
                table: "AddressChangeRequest",
                type: "int",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "WorkflowVersion",
                table: "AddressChangeRequest",
                type: "int",
                nullable: false,
                defaultValue: 0);

            // Preserve all historical outcomes/actors/money. Only legacy Pending gets a bounded wait.
            migrationBuilder.Sql("UPDATE [AddressChangeRequest] SET [IsPending] = 1, [ExpiresAt] = DATEADD(day, 1, [CreatedAt]) WHERE [Status] = N'Pending';");

            migrationBuilder.CreateIndex(
                name: "IX_AddressChangeRequest_IsPending_ExpiresAt",
                table: "AddressChangeRequest",
                columns: new[] { "IsPending", "ExpiresAt" });

            migrationBuilder.CreateIndex(
                name: "IX_AddressChangeRequest_OrderId",
                table: "AddressChangeRequest",
                column: "OrderId",
                unique: true,
                filter: "[IsPending] = 1");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_AddressChangeRequest_IsPending_ExpiresAt",
                table: "AddressChangeRequest");

            migrationBuilder.DropIndex(
                name: "IX_AddressChangeRequest_OrderId",
                table: "AddressChangeRequest");

            migrationBuilder.DropColumn(
                name: "Revision",
                table: "SellerShippingPreference");

            migrationBuilder.DropColumn(
                name: "AppliedAt",
                table: "AddressChangeRequest");

            migrationBuilder.DropColumn(
                name: "BuyerConfirmedAt",
                table: "AddressChangeRequest");

            migrationBuilder.DropColumn(
                name: "BuyerUserId",
                table: "AddressChangeRequest");

            migrationBuilder.DropColumn(
                name: "CapRevision",
                table: "AddressChangeRequest");

            migrationBuilder.DropColumn(
                name: "ExpectedShipperId",
                table: "AddressChangeRequest");

            migrationBuilder.DropColumn(
                name: "ExpiresAt",
                table: "AddressChangeRequest");

            migrationBuilder.DropColumn(
                name: "IsPending",
                table: "AddressChangeRequest");

            migrationBuilder.DropColumn(
                name: "QuoteToken",
                table: "AddressChangeRequest");

            migrationBuilder.DropColumn(
                name: "SellerConfirmedAt",
                table: "AddressChangeRequest");

            migrationBuilder.DropColumn(
                name: "SellerUserId",
                table: "AddressChangeRequest");

            migrationBuilder.DropColumn(
                name: "ShipperDecidedAt",
                table: "AddressChangeRequest");

            migrationBuilder.DropColumn(
                name: "ShipperUserId",
                table: "AddressChangeRequest");

            migrationBuilder.DropColumn(
                name: "WorkflowVersion",
                table: "AddressChangeRequest");

            migrationBuilder.CreateIndex(
                name: "IX_AddressChangeRequest_OrderId",
                table: "AddressChangeRequest",
                column: "OrderId",
                unique: true,
                filter: "[Status] = 'Pending'");
        }
    }
}
