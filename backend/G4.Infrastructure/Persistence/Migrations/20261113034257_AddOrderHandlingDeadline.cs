using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace G4.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddOrderHandlingDeadline : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<DateTime>(
                name: "HandedOverAt",
                table: "OrderTable",
                type: "datetime2",
                nullable: true);

            migrationBuilder.AddColumn<DateTime>(
                name: "HandlingDeadlineAt",
                table: "OrderTable",
                type: "datetime2",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "HandlingSeconds",
                table: "OrderTable",
                type: "int",
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "HandedOverAt",
                table: "OrderTable");

            migrationBuilder.DropColumn(
                name: "HandlingDeadlineAt",
                table: "OrderTable");

            migrationBuilder.DropColumn(
                name: "HandlingSeconds",
                table: "OrderTable");
        }
    }
}
