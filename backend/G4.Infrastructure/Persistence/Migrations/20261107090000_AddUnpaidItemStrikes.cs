using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Metadata;
using Microsoft.EntityFrameworkCore.Migrations;

namespace G4.Infrastructure.Persistence.Migrations;

[DbContext(typeof(ApplicationDbContext))]
[Migration("20261107090000_AddUnpaidItemStrikes")]
public sealed class AddUnpaidItemStrikes : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.AddColumn<DateTime>(
            name: "BannedAt",
            table: "User",
            type: "datetime2",
            nullable: true
        );
        migrationBuilder.AddColumn<string>(
            name: "BanReason",
            table: "User",
            type: "nvarchar(500)",
            maxLength: 500,
            nullable: true
        );
        migrationBuilder.AddColumn<string>(
            name: "CancelReasonCode",
            table: "OrderTable",
            type: "nvarchar(20)",
            maxLength: 20,
            nullable: true
        );
        migrationBuilder.CreateTable(
            name: "UnpaidItemStrike",
            columns: table => new
            {
                Id = table
                    .Column<int>(type: "int", nullable: false)
                    .Annotation("SqlServer:Identity", "1, 1"),
                BuyerId = table.Column<int>(type: "int", nullable: false),
                SellerId = table.Column<int>(type: "int", nullable: false),
                OrderId = table.Column<int>(type: "int", nullable: false),
                Reason = table.Column<string>(type: "nvarchar(500)", maxLength: 500, nullable: false),
                CreatedAt = table.Column<DateTime>(type: "datetime2", nullable: false),
            },
            constraints: table =>
            {
                table.PrimaryKey("PK_UnpaidItemStrike", x => x.Id);
                table.ForeignKey(
                    name: "FK_UnpaidItemStrike_User_BuyerId",
                    column: x => x.BuyerId,
                    principalTable: "User",
                    principalColumn: "id"
                );
                table.ForeignKey(
                    name: "FK_UnpaidItemStrike_OrderTable_OrderId",
                    column: x => x.OrderId,
                    principalTable: "OrderTable",
                    principalColumn: "id"
                );
            }
        );
        migrationBuilder.CreateIndex(
            name: "IX_UnpaidItemStrike_OrderId",
            table: "UnpaidItemStrike",
            column: "OrderId",
            unique: true
        );
        migrationBuilder.CreateIndex(
            name: "IX_UnpaidItemStrike_BuyerId_CreatedAt",
            table: "UnpaidItemStrike",
            columns: new[] { "BuyerId", "CreatedAt" }
        );
    }

    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.DropTable(name: "UnpaidItemStrike");
        migrationBuilder.DropColumn(name: "CancelReasonCode", table: "OrderTable");
        migrationBuilder.DropColumn(name: "BanReason", table: "User");
        migrationBuilder.DropColumn(name: "BannedAt", table: "User");
    }
}
