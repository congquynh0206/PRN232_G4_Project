using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace G4.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddSellerFinance : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "SellerAccount",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    SellerId = table.Column<int>(type: "int", nullable: false),
                    Level = table.Column<int>(type: "int", nullable: false),
                    Status = table.Column<string>(type: "nvarchar(20)", maxLength: 20, nullable: false),
                    MonthlySalesLimit = table.Column<decimal>(type: "decimal(18,2)", nullable: false),
                    HoldDays = table.Column<int>(type: "int", nullable: false),
                    ProcessingBalance = table.Column<decimal>(type: "decimal(18,2)", nullable: false),
                    AvailableBalance = table.Column<decimal>(type: "decimal(18,2)", nullable: false),
                    OnHoldBalance = table.Column<decimal>(type: "decimal(18,2)", nullable: false),
                    NegativeBalance = table.Column<decimal>(type: "decimal(18,2)", nullable: false),
                    MonthlySalesAmount = table.Column<decimal>(type: "decimal(18,2)", nullable: false),
                    SalesMonth = table.Column<DateTime>(type: "datetime2", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "datetime2", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_SellerAccount", x => x.Id);
                    table.ForeignKey(
                        name: "FK_SellerAccount_User_SellerId",
                        column: x => x.SellerId,
                        principalTable: "User",
                        principalColumn: "id");
                });

            migrationBuilder.CreateTable(
                name: "SellerPayout",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    SellerAccountId = table.Column<int>(type: "int", nullable: false),
                    Amount = table.Column<decimal>(type: "decimal(18,2)", nullable: false),
                    Currency = table.Column<string>(type: "nvarchar(3)", maxLength: 3, nullable: false),
                    Status = table.Column<string>(type: "nvarchar(20)", maxLength: 20, nullable: false),
                    IdempotencyKey = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: false),
                    DestinationMasked = table.Column<string>(type: "nvarchar(30)", maxLength: 30, nullable: false),
                    BankReferenceId = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: true),
                    SimulateFailure = table.Column<bool>(type: "bit", nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "datetime2", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "datetime2", nullable: false),
                    CompletedAt = table.Column<DateTime>(type: "datetime2", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_SellerPayout", x => x.Id);
                    table.ForeignKey(
                        name: "FK_SellerPayout_SellerAccount_SellerAccountId",
                        column: x => x.SellerAccountId,
                        principalTable: "SellerAccount",
                        principalColumn: "Id");
                });

            migrationBuilder.CreateTable(
                name: "SellerSettlement",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    SellerAccountId = table.Column<int>(type: "int", nullable: false),
                    SellerId = table.Column<int>(type: "int", nullable: false),
                    OrderId = table.Column<int>(type: "int", nullable: false),
                    PaymentId = table.Column<int>(type: "int", nullable: false),
                    GrossAmount = table.Column<decimal>(type: "decimal(18,2)", nullable: false),
                    PlatformFeeAmount = table.Column<decimal>(type: "decimal(18,2)", nullable: false),
                    FixedFeeAmount = table.Column<decimal>(type: "decimal(18,2)", nullable: false),
                    NetAmount = table.Column<decimal>(type: "decimal(18,2)", nullable: false),
                    ProcessingAmount = table.Column<decimal>(type: "decimal(18,2)", nullable: false),
                    RefundedAmount = table.Column<decimal>(type: "decimal(18,2)", nullable: false),
                    FeeCreditAmount = table.Column<decimal>(type: "decimal(18,2)", nullable: false),
                    Currency = table.Column<string>(type: "nvarchar(3)", maxLength: 3, nullable: false),
                    Status = table.Column<string>(type: "nvarchar(30)", maxLength: 30, nullable: false),
                    ReleaseAt = table.Column<DateTime>(type: "datetime2", nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "datetime2", nullable: false),
                    ReleasedAt = table.Column<DateTime>(type: "datetime2", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_SellerSettlement", x => x.Id);
                    table.ForeignKey(
                        name: "FK_SellerSettlement_OrderTable_OrderId",
                        column: x => x.OrderId,
                        principalTable: "OrderTable",
                        principalColumn: "id");
                    table.ForeignKey(
                        name: "FK_SellerSettlement_Payment_PaymentId",
                        column: x => x.PaymentId,
                        principalTable: "Payment",
                        principalColumn: "id");
                    table.ForeignKey(
                        name: "FK_SellerSettlement_SellerAccount_SellerAccountId",
                        column: x => x.SellerAccountId,
                        principalTable: "SellerAccount",
                        principalColumn: "Id");
                });

            migrationBuilder.CreateTable(
                name: "FinancialTransaction",
                columns: table => new
                {
                    Id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    SellerAccountId = table.Column<int>(type: "int", nullable: false),
                    OrderId = table.Column<int>(type: "int", nullable: true),
                    SettlementId = table.Column<int>(type: "int", nullable: true),
                    PayoutId = table.Column<int>(type: "int", nullable: true),
                    RefundId = table.Column<int>(type: "int", nullable: true),
                    Type = table.Column<string>(type: "nvarchar(30)", maxLength: 30, nullable: false),
                    Bucket = table.Column<string>(type: "nvarchar(20)", maxLength: 20, nullable: false),
                    Amount = table.Column<decimal>(type: "decimal(18,2)", nullable: false),
                    Currency = table.Column<string>(type: "nvarchar(3)", maxLength: 3, nullable: false),
                    EntryKey = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: false),
                    Description = table.Column<string>(type: "nvarchar(500)", maxLength: 500, nullable: true),
                    CreatedAt = table.Column<DateTime>(type: "datetime2", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_FinancialTransaction", x => x.Id);
                    table.ForeignKey(
                        name: "FK_FinancialTransaction_OrderTable_OrderId",
                        column: x => x.OrderId,
                        principalTable: "OrderTable",
                        principalColumn: "id");
                    table.ForeignKey(
                        name: "FK_FinancialTransaction_Refund_RefundId",
                        column: x => x.RefundId,
                        principalTable: "Refund",
                        principalColumn: "Id");
                    table.ForeignKey(
                        name: "FK_FinancialTransaction_SellerAccount_SellerAccountId",
                        column: x => x.SellerAccountId,
                        principalTable: "SellerAccount",
                        principalColumn: "Id");
                    table.ForeignKey(
                        name: "FK_FinancialTransaction_SellerPayout_PayoutId",
                        column: x => x.PayoutId,
                        principalTable: "SellerPayout",
                        principalColumn: "Id");
                    table.ForeignKey(
                        name: "FK_FinancialTransaction_SellerSettlement_SettlementId",
                        column: x => x.SettlementId,
                        principalTable: "SellerSettlement",
                        principalColumn: "Id");
                });

            migrationBuilder.CreateIndex(
                name: "IX_FinancialTransaction_EntryKey",
                table: "FinancialTransaction",
                column: "EntryKey",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_FinancialTransaction_OrderId",
                table: "FinancialTransaction",
                column: "OrderId");

            migrationBuilder.CreateIndex(
                name: "IX_FinancialTransaction_PayoutId",
                table: "FinancialTransaction",
                column: "PayoutId");

            migrationBuilder.CreateIndex(
                name: "IX_FinancialTransaction_RefundId",
                table: "FinancialTransaction",
                column: "RefundId");

            migrationBuilder.CreateIndex(
                name: "IX_FinancialTransaction_SellerAccountId_CreatedAt",
                table: "FinancialTransaction",
                columns: new[] { "SellerAccountId", "CreatedAt" });

            migrationBuilder.CreateIndex(
                name: "IX_FinancialTransaction_SettlementId",
                table: "FinancialTransaction",
                column: "SettlementId");

            migrationBuilder.CreateIndex(
                name: "IX_SellerAccount_SellerId",
                table: "SellerAccount",
                column: "SellerId",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_SellerPayout_IdempotencyKey",
                table: "SellerPayout",
                column: "IdempotencyKey",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_SellerPayout_SellerAccountId",
                table: "SellerPayout",
                column: "SellerAccountId");

            migrationBuilder.CreateIndex(
                name: "IX_SellerSettlement_OrderId",
                table: "SellerSettlement",
                column: "OrderId",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_SellerSettlement_PaymentId",
                table: "SellerSettlement",
                column: "PaymentId",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_SellerSettlement_SellerAccountId",
                table: "SellerSettlement",
                column: "SellerAccountId");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "FinancialTransaction");

            migrationBuilder.DropTable(
                name: "SellerPayout");

            migrationBuilder.DropTable(
                name: "SellerSettlement");

            migrationBuilder.DropTable(
                name: "SellerAccount");
        }
    }
}
