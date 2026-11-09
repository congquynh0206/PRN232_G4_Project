using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace G4.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddImmediateAuthorizationSaga : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<decimal>(
                name: "Amount",
                table: "ImmediatePaymentAttempt",
                type: "decimal(10,2)",
                precision: 10,
                scale: 2,
                nullable: false,
                defaultValue: 0m);

            migrationBuilder.AddColumn<string>(
                name: "AuthorizationId",
                table: "ImmediatePaymentAttempt",
                type: "nvarchar(100)",
                maxLength: 100,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "AuthorizationOutcome",
                table: "ImmediatePaymentAttempt",
                type: "nvarchar(30)",
                maxLength: 30,
                nullable: false,
                defaultValue: "");

            migrationBuilder.AddColumn<string>(
                name: "CaptureId",
                table: "ImmediatePaymentAttempt",
                type: "nvarchar(100)",
                maxLength: 100,
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "FinalizationAttempts",
                table: "ImmediatePaymentAttempt",
                type: "int",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<bool>(
                name: "InventoryHeld",
                table: "ImmediatePaymentAttempt",
                type: "bit",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<Guid>(
                name: "ProcessingToken",
                table: "ImmediatePaymentAttempt",
                type: "uniqueidentifier",
                nullable: true);

            migrationBuilder.AddColumn<DateTime>(
                name: "ProcessingUntil",
                table: "ImmediatePaymentAttempt",
                type: "datetime2",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "Stage",
                table: "ImmediatePaymentAttempt",
                type: "nvarchar(30)",
                maxLength: 30,
                nullable: false,
                defaultValue: "");

            migrationBuilder.AddColumn<DateTime>(
                name: "UpdatedAt",
                table: "ImmediatePaymentAttempt",
                type: "datetime2",
                nullable: false,
                defaultValue: new DateTime(1, 1, 1, 0, 0, 0, 0, DateTimeKind.Unspecified));

            migrationBuilder.CreateTable(
                name: "SimulatedCardAuthorization",
                columns: table => new
                {
                    Id = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: false),
                    Amount = table.Column<decimal>(type: "decimal(10,2)", precision: 10, scale: 2, nullable: false),
                    Status = table.Column<string>(type: "nvarchar(30)", maxLength: 30, nullable: false),
                    CaptureId = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: true),
                    UpdatedAt = table.Column<DateTime>(type: "datetime2", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_SimulatedCardAuthorization", x => x.Id);
                });

            migrationBuilder.CreateIndex(
                name: "IX_ImmediatePaymentAttempt_Status_ProcessingUntil",
                table: "ImmediatePaymentAttempt",
                columns: new[] { "Status", "ProcessingUntil" });

            // Preserve terminal history created by the earlier atomic-card implementation.
            migrationBuilder.Sql(@"
UPDATE [ImmediatePaymentAttempt]
SET [Stage] = CASE WHEN [Status] = 'Succeeded' THEN 'Completed'
    WHEN [Status] = 'Failed' THEN 'Declined' ELSE 'Authorizing' END,
    [UpdatedAt] = [CreatedAt];
UPDATE a SET a.[Amount] = ISNULL(o.[totalPrice], 0)
FROM [ImmediatePaymentAttempt] a
JOIN [OrderTable] o ON o.[id] = a.[OrderId];
");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "SimulatedCardAuthorization");

            migrationBuilder.DropIndex(
                name: "IX_ImmediatePaymentAttempt_Status_ProcessingUntil",
                table: "ImmediatePaymentAttempt");

            migrationBuilder.DropColumn(
                name: "Amount",
                table: "ImmediatePaymentAttempt");

            migrationBuilder.DropColumn(
                name: "AuthorizationId",
                table: "ImmediatePaymentAttempt");

            migrationBuilder.DropColumn(
                name: "AuthorizationOutcome",
                table: "ImmediatePaymentAttempt");

            migrationBuilder.DropColumn(
                name: "CaptureId",
                table: "ImmediatePaymentAttempt");

            migrationBuilder.DropColumn(
                name: "FinalizationAttempts",
                table: "ImmediatePaymentAttempt");

            migrationBuilder.DropColumn(
                name: "InventoryHeld",
                table: "ImmediatePaymentAttempt");

            migrationBuilder.DropColumn(
                name: "ProcessingToken",
                table: "ImmediatePaymentAttempt");

            migrationBuilder.DropColumn(
                name: "ProcessingUntil",
                table: "ImmediatePaymentAttempt");

            migrationBuilder.DropColumn(
                name: "Stage",
                table: "ImmediatePaymentAttempt");

            migrationBuilder.DropColumn(
                name: "UpdatedAt",
                table: "ImmediatePaymentAttempt");
        }
    }
}
