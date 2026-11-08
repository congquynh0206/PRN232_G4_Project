using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace G4.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddKlarnaHostedPayment : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "KlarnaRedirectUrl",
                table: "Payment",
                type: "nvarchar(500)",
                maxLength: 500,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "KlarnaSessionId",
                table: "Payment",
                type: "nvarchar(100)",
                maxLength: 100,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "KlarnaSessionUrl",
                table: "Payment",
                type: "nvarchar(500)",
                maxLength: 500,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "DeliveryCitySnapshot",
                table: "OrderTable",
                type: "nvarchar(50)",
                maxLength: 50,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "DeliveryNameSnapshot",
                table: "OrderTable",
                type: "nvarchar(100)",
                maxLength: 100,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "DeliveryPostalCodeSnapshot",
                table: "OrderTable",
                type: "nvarchar(20)",
                maxLength: 20,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "DeliveryStreetSnapshot",
                table: "OrderTable",
                type: "nvarchar(100)",
                maxLength: 100,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "PostalCode",
                table: "Address",
                type: "nvarchar(20)",
                maxLength: 20,
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "IX_Payment_KlarnaSessionId",
                table: "Payment",
                column: "KlarnaSessionId",
                unique: true,
                filter: "[KlarnaSessionId] IS NOT NULL");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_Payment_KlarnaSessionId",
                table: "Payment");

            migrationBuilder.DropColumn(
                name: "KlarnaRedirectUrl",
                table: "Payment");

            migrationBuilder.DropColumn(
                name: "KlarnaSessionId",
                table: "Payment");

            migrationBuilder.DropColumn(
                name: "KlarnaSessionUrl",
                table: "Payment");

            migrationBuilder.DropColumn(
                name: "DeliveryCitySnapshot",
                table: "OrderTable");

            migrationBuilder.DropColumn(
                name: "DeliveryNameSnapshot",
                table: "OrderTable");

            migrationBuilder.DropColumn(
                name: "DeliveryPostalCodeSnapshot",
                table: "OrderTable");

            migrationBuilder.DropColumn(
                name: "DeliveryStreetSnapshot",
                table: "OrderTable");

            migrationBuilder.DropColumn(
                name: "PostalCode",
                table: "Address");
        }
    }
}
