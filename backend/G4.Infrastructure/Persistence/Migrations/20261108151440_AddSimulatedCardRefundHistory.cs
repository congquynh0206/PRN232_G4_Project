using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace G4.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddSimulatedCardRefundHistory : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<decimal>(
                name: "RefundedAmount",
                table: "SimulatedCardAuthorization",
                type: "decimal(10,2)",
                precision: 10,
                scale: 2,
                nullable: false,
                defaultValue: 0m);

            migrationBuilder.AddColumn<string>(
                name: "RefundsJson",
                table: "SimulatedCardAuthorization",
                type: "nvarchar(max)",
                nullable: false,
                defaultValue: "{}");

            migrationBuilder.Sql(@"
UPDATE [SimulatedCardAuthorization]
SET [RefundedAmount] = [Amount],
    [RefundsJson] = CONCAT('{""legacy-full-refund"":',
        CONVERT(varchar(40), [Amount]), '}')
WHERE [Status] = 'Refunded';
");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "RefundedAmount",
                table: "SimulatedCardAuthorization");

            migrationBuilder.DropColumn(
                name: "RefundsJson",
                table: "SimulatedCardAuthorization");
        }
    }
}
