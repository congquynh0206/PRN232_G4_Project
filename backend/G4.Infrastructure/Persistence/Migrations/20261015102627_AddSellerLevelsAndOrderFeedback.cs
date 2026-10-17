using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace G4.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddSellerLevelsAndOrderFeedback : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<DateTime>(
                name: "LastLevelEvaluatedAt",
                table: "SellerAccount",
                type: "datetime2",
                nullable: true);

            migrationBuilder.AddColumn<DateTime>(
                name: "LevelActivityStartedAt",
                table: "SellerAccount",
                type: "datetime2",
                nullable: true);

            migrationBuilder.CreateTable(
                name: "SellerLevelEvaluation",
                columns: table => new
                {
                    Id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    SellerId = table.Column<int>(type: "int", nullable: false),
                    PreviousLevel = table.Column<int>(type: "int", nullable: false),
                    ResultLevel = table.Column<int>(type: "int", nullable: false),
                    Source = table.Column<string>(type: "nvarchar(30)", maxLength: 30, nullable: false),
                    Reason = table.Column<string>(type: "nvarchar(1000)", maxLength: 1000, nullable: false),
                    SnapshotJson = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "datetime2", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_SellerLevelEvaluation", x => x.Id);
                    table.ForeignKey(
                        name: "FK_SellerLevelEvaluation_User_SellerId",
                        column: x => x.SellerId,
                        principalTable: "User",
                        principalColumn: "id");
                });

            migrationBuilder.CreateTable(
                name: "SellerOrderFeedback",
                columns: table => new
                {
                    Id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    OrderId = table.Column<int>(type: "int", nullable: false),
                    BuyerId = table.Column<int>(type: "int", nullable: false),
                    SellerId = table.Column<int>(type: "int", nullable: false),
                    Rating = table.Column<int>(type: "int", nullable: false),
                    Comment = table.Column<string>(type: "nvarchar(2000)", maxLength: 2000, nullable: false),
                    RequestKey = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "datetime2", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_SellerOrderFeedback", x => x.Id);
                    table.CheckConstraint("CK_SellerOrderFeedback_Rating", "[Rating] >= 1 AND [Rating] <= 5");
                    table.ForeignKey(
                        name: "FK_SellerOrderFeedback_OrderTable_OrderId",
                        column: x => x.OrderId,
                        principalTable: "OrderTable",
                        principalColumn: "id");
                    table.ForeignKey(
                        name: "FK_SellerOrderFeedback_User_BuyerId",
                        column: x => x.BuyerId,
                        principalTable: "User",
                        principalColumn: "id");
                    table.ForeignKey(
                        name: "FK_SellerOrderFeedback_User_SellerId",
                        column: x => x.SellerId,
                        principalTable: "User",
                        principalColumn: "id");
                });

            migrationBuilder.CreateIndex(
                name: "IX_SellerAccount_LastLevelEvaluatedAt",
                table: "SellerAccount",
                column: "LastLevelEvaluatedAt");

            migrationBuilder.CreateIndex(
                name: "IX_SellerLevelEvaluation_SellerId_Id",
                table: "SellerLevelEvaluation",
                columns: new[] { "SellerId", "Id" });

            migrationBuilder.CreateIndex(
                name: "IX_SellerOrderFeedback_BuyerId_RequestKey",
                table: "SellerOrderFeedback",
                columns: new[] { "BuyerId", "RequestKey" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_SellerOrderFeedback_OrderId",
                table: "SellerOrderFeedback",
                column: "OrderId",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_SellerOrderFeedback_SellerId_CreatedAt",
                table: "SellerOrderFeedback",
                columns: new[] { "SellerId", "CreatedAt" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "SellerLevelEvaluation");

            migrationBuilder.DropTable(
                name: "SellerOrderFeedback");

            migrationBuilder.DropIndex(
                name: "IX_SellerAccount_LastLevelEvaluatedAt",
                table: "SellerAccount");

            migrationBuilder.DropColumn(
                name: "LastLevelEvaluatedAt",
                table: "SellerAccount");

            migrationBuilder.DropColumn(
                name: "LevelActivityStartedAt",
                table: "SellerAccount");
        }
    }
}
