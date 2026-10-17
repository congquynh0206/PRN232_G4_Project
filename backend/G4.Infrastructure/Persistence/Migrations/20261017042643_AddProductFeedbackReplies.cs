using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace G4.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddProductFeedbackReplies : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // Older databases initialized from baseline.sql may not have the
            // convention indexes recorded in the EF snapshot.
            migrationBuilder.Sql(@"IF EXISTS (SELECT 1 FROM sys.indexes WHERE object_id = OBJECT_ID(N'[Review]') AND name = N'IX_Review_productId')
                DROP INDEX [IX_Review_productId] ON [Review];
                IF EXISTS (SELECT 1 FROM sys.indexes WHERE object_id = OBJECT_ID(N'[Review]') AND name = N'IX_Review_reviewerId')
                DROP INDEX [IX_Review_reviewerId] ON [Review];");

            migrationBuilder.AddColumn<int>(
                name: "OrderId",
                table: "Review",
                type: "int",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "RequestKey",
                table: "Review",
                type: "nvarchar(100)",
                maxLength: 100,
                nullable: true);

            migrationBuilder.CreateTable(
                name: "ProductReviewReply",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    ReviewId = table.Column<int>(type: "int", nullable: false),
                    SellerId = table.Column<int>(type: "int", nullable: false),
                    Comment = table.Column<string>(type: "nvarchar(2000)", maxLength: 2000, nullable: false),
                    RequestKey = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "datetime2", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ProductReviewReply", x => x.Id);
                    table.ForeignKey(
                        name: "FK_ProductReviewReply_Review_ReviewId",
                        column: x => x.ReviewId,
                        principalTable: "Review",
                        principalColumn: "id");
                    table.ForeignKey(
                        name: "FK_ProductReviewReply_User_SellerId",
                        column: x => x.SellerId,
                        principalTable: "User",
                        principalColumn: "id");
                });

            migrationBuilder.CreateIndex(
                name: "IX_Review_OrderId_productId",
                table: "Review",
                columns: new[] { "OrderId", "productId" },
                unique: true,
                filter: "[OrderId] IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "IX_Review_productId_createdAt",
                table: "Review",
                columns: new[] { "productId", "createdAt" });

            migrationBuilder.CreateIndex(
                name: "IX_Review_reviewerId_RequestKey",
                table: "Review",
                columns: new[] { "reviewerId", "RequestKey" },
                unique: true,
                filter: "[RequestKey] IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "IX_ProductReviewReply_ReviewId",
                table: "ProductReviewReply",
                column: "ReviewId",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_ProductReviewReply_SellerId_RequestKey",
                table: "ProductReviewReply",
                columns: new[] { "SellerId", "RequestKey" },
                unique: true);

            migrationBuilder.AddForeignKey(
                name: "FK_Review_OrderTable_OrderId",
                table: "Review",
                column: "OrderId",
                principalTable: "OrderTable",
                principalColumn: "id");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_Review_OrderTable_OrderId",
                table: "Review");

            migrationBuilder.DropTable(
                name: "ProductReviewReply");

            migrationBuilder.DropIndex(
                name: "IX_Review_OrderId_productId",
                table: "Review");

            migrationBuilder.DropIndex(
                name: "IX_Review_productId_createdAt",
                table: "Review");

            migrationBuilder.DropIndex(
                name: "IX_Review_reviewerId_RequestKey",
                table: "Review");

            migrationBuilder.DropColumn(
                name: "OrderId",
                table: "Review");

            migrationBuilder.DropColumn(
                name: "RequestKey",
                table: "Review");

            migrationBuilder.CreateIndex(
                name: "IX_Review_productId",
                table: "Review",
                column: "productId");

            migrationBuilder.CreateIndex(
                name: "IX_Review_reviewerId",
                table: "Review",
                column: "reviewerId");
        }
    }
}
