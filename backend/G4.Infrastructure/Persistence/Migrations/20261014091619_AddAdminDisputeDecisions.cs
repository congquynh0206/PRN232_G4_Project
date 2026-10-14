using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace G4.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddAdminDisputeDecisions : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<int>(
                name: "DisputeDecisionId",
                table: "Refund",
                type: "int",
                nullable: true);

            migrationBuilder.CreateTable(
                name: "DisputeDecision",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    DisputeId = table.Column<int>(type: "int", nullable: false),
                    OrderId = table.Column<int>(type: "int", nullable: false),
                    AdminId = table.Column<int>(type: "int", nullable: false),
                    Outcome = table.Column<string>(type: "nvarchar(30)", maxLength: 30, nullable: false),
                    Responsibility = table.Column<string>(type: "nvarchar(20)", maxLength: 20, nullable: false),
                    Reason = table.Column<string>(type: "nvarchar(4000)", maxLength: 4000, nullable: false),
                    EvidenceEntryIdsJson = table.Column<string>(type: "nvarchar(2000)", maxLength: 2000, nullable: false),
                    RequestKey = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: false),
                    PricingFingerprint = table.Column<string>(type: "nvarchar(64)", maxLength: 64, nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "datetime2", nullable: false),
                    RequestedAmount = table.Column<decimal>(type: "decimal(18,2)", precision: 18, scale: 2, nullable: true),
                    MaximumRefund = table.Column<decimal>(type: "decimal(18,2)", precision: 18, scale: 2, nullable: false),
                    BuyerRefund = table.Column<decimal>(type: "decimal(18,2)", precision: 18, scale: 2, nullable: false),
                    OutboundShippingRetained = table.Column<decimal>(type: "decimal(18,2)", precision: 18, scale: 2, nullable: false),
                    ReturnShippingCost = table.Column<decimal>(type: "decimal(18,2)", precision: 18, scale: 2, nullable: false),
                    BuyerShippingRecovery = table.Column<decimal>(type: "decimal(18,2)", precision: 18, scale: 2, nullable: false),
                    SubsidyReversal = table.Column<decimal>(type: "decimal(18,2)", precision: 18, scale: 2, nullable: false),
                    SellerGrossRefund = table.Column<decimal>(type: "decimal(18,2)", precision: 18, scale: 2, nullable: false),
                    FeeCredit = table.Column<decimal>(type: "decimal(18,2)", precision: 18, scale: 2, nullable: false),
                    SellerNetDebit = table.Column<decimal>(type: "decimal(18,2)", precision: 18, scale: 2, nullable: false),
                    ShippingPayer = table.Column<string>(type: "nvarchar(20)", maxLength: 20, nullable: true),
                    Currency = table.Column<string>(type: "nvarchar(3)", maxLength: 3, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_DisputeDecision", x => x.Id);
                    table.ForeignKey(
                        name: "FK_DisputeDecision_Dispute_DisputeId",
                        column: x => x.DisputeId,
                        principalTable: "Dispute",
                        principalColumn: "id");
                    table.ForeignKey(
                        name: "FK_DisputeDecision_OrderTable_OrderId",
                        column: x => x.OrderId,
                        principalTable: "OrderTable",
                        principalColumn: "id");
                    table.ForeignKey(
                        name: "FK_DisputeDecision_User_AdminId",
                        column: x => x.AdminId,
                        principalTable: "User",
                        principalColumn: "id");
                });

            migrationBuilder.CreateIndex(
                name: "IX_Refund_DisputeDecisionId",
                table: "Refund",
                column: "DisputeDecisionId");

            migrationBuilder.CreateIndex(
                name: "IX_DisputeDecision_AdminId",
                table: "DisputeDecision",
                column: "AdminId");

            migrationBuilder.CreateIndex(
                name: "IX_DisputeDecision_DisputeId",
                table: "DisputeDecision",
                column: "DisputeId",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_DisputeDecision_OrderId",
                table: "DisputeDecision",
                column: "OrderId");

            migrationBuilder.CreateIndex(
                name: "IX_DisputeDecision_RequestKey",
                table: "DisputeDecision",
                column: "RequestKey",
                unique: true);

            migrationBuilder.AddForeignKey(
                name: "FK_Refund_DisputeDecision_DisputeDecisionId",
                table: "Refund",
                column: "DisputeDecisionId",
                principalTable: "DisputeDecision",
                principalColumn: "Id");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_Refund_DisputeDecision_DisputeDecisionId",
                table: "Refund");

            migrationBuilder.DropTable(
                name: "DisputeDecision");

            migrationBuilder.DropIndex(
                name: "IX_Refund_DisputeDecisionId",
                table: "Refund");

            migrationBuilder.DropColumn(
                name: "DisputeDecisionId",
                table: "Refund");
        }
    }
}
