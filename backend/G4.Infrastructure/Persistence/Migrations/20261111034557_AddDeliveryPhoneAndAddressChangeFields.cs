using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace G4.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddDeliveryPhoneAndAddressChangeFields : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "DeliveryPhoneSnapshot",
                table: "OrderTable",
                type: "nvarchar(20)",
                maxLength: 20,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "NewCity",
                table: "AddressChangeRequest",
                type: "nvarchar(50)",
                maxLength: 50,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "NewFullName",
                table: "AddressChangeRequest",
                type: "nvarchar(100)",
                maxLength: 100,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "NewPhone",
                table: "AddressChangeRequest",
                type: "nvarchar(20)",
                maxLength: 20,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "NewPostalCode",
                table: "AddressChangeRequest",
                type: "nvarchar(20)",
                maxLength: 20,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "NewStreet",
                table: "AddressChangeRequest",
                type: "nvarchar(100)",
                maxLength: 100,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "OldPhone",
                table: "AddressChangeRequest",
                type: "nvarchar(20)",
                maxLength: 20,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "OldPostalCode",
                table: "AddressChangeRequest",
                type: "nvarchar(20)",
                maxLength: 20,
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "DeliveryPhoneSnapshot",
                table: "OrderTable");

            migrationBuilder.DropColumn(
                name: "NewCity",
                table: "AddressChangeRequest");

            migrationBuilder.DropColumn(
                name: "NewFullName",
                table: "AddressChangeRequest");

            migrationBuilder.DropColumn(
                name: "NewPhone",
                table: "AddressChangeRequest");

            migrationBuilder.DropColumn(
                name: "NewPostalCode",
                table: "AddressChangeRequest");

            migrationBuilder.DropColumn(
                name: "NewStreet",
                table: "AddressChangeRequest");

            migrationBuilder.DropColumn(
                name: "OldPhone",
                table: "AddressChangeRequest");

            migrationBuilder.DropColumn(
                name: "OldPostalCode",
                table: "AddressChangeRequest");
        }
    }
}
