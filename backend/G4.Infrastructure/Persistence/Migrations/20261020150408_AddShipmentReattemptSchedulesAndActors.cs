using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace G4.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddShipmentReattemptSchedulesAndActors : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "ReattemptAt",
                table: "ShippingInfo",
                type: "datetimeoffset",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "ReattemptReason",
                table: "ShippingInfo",
                type: "nvarchar(1000)",
                maxLength: 1000,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "ActorRole",
                table: "ShippingEvent",
                type: "nvarchar(20)",
                maxLength: 20,
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "ActorUserId",
                table: "ShippingEvent",
                type: "int",
                nullable: true);

            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "ReattemptAt",
                table: "ShippingEvent",
                type: "datetimeoffset",
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "IX_ShippingEvent_ActorUserId",
                table: "ShippingEvent",
                column: "ActorUserId");

            migrationBuilder.AddForeignKey(
                name: "FK_ShippingEvent_User_ActorUserId",
                table: "ShippingEvent",
                column: "ActorUserId",
                principalTable: "User",
                principalColumn: "id");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_ShippingEvent_User_ActorUserId",
                table: "ShippingEvent");

            migrationBuilder.DropIndex(
                name: "IX_ShippingEvent_ActorUserId",
                table: "ShippingEvent");

            migrationBuilder.DropColumn(
                name: "ReattemptAt",
                table: "ShippingInfo");

            migrationBuilder.DropColumn(
                name: "ReattemptReason",
                table: "ShippingInfo");

            migrationBuilder.DropColumn(
                name: "ActorRole",
                table: "ShippingEvent");

            migrationBuilder.DropColumn(
                name: "ActorUserId",
                table: "ShippingEvent");

            migrationBuilder.DropColumn(
                name: "ReattemptAt",
                table: "ShippingEvent");
        }
    }
}
