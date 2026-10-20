using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace G4.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class SnapshotReturnWindowDeadlines : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<DateTime>(
                name: "ReturnWindowClosesAt",
                table: "OrderTable",
                type: "datetime2",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "ReturnWindowPolicyVersion",
                table: "OrderTable",
                type: "int",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<int>(
                name: "ReturnWindowSeconds",
                table: "OrderTable",
                type: "int",
                nullable: true);

            migrationBuilder.AddColumn<DateTime>(
                name: "ReturnWindowStartedAt",
                table: "OrderTable",
                type: "datetime2",
                nullable: true);
            // Every pre-upgrade delivered order retains the historical seven-day entitlement.
            // Do not rewrite ReturnRequest.ReturnDeadline or infer a Development policy for old data.
            migrationBuilder.Sql(@"UPDATE o SET ReturnWindowPolicyVersion = 0, ReturnWindowSeconds = 604800,
                ReturnWindowStartedAt = s.DeliveredAt, ReturnWindowClosesAt = DATEADD(second, 604800, s.DeliveredAt)
                FROM OrderTable o JOIN ShippingInfo s ON s.orderId = o.id AND s.Direction = N'Outbound'
                WHERE s.DeliveredAt IS NOT NULL AND o.ReturnWindowClosesAt IS NULL;");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "ReturnWindowClosesAt",
                table: "OrderTable");

            migrationBuilder.DropColumn(
                name: "ReturnWindowPolicyVersion",
                table: "OrderTable");

            migrationBuilder.DropColumn(
                name: "ReturnWindowSeconds",
                table: "OrderTable");

            migrationBuilder.DropColumn(
                name: "ReturnWindowStartedAt",
                table: "OrderTable");
        }
    }
}
