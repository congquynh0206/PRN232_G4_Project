using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace G4.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddSellerDebtManagement : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_NotificationOutbox_OrderId_EventType",
                table: "NotificationOutbox");

            migrationBuilder.AddColumn<int>(
                name: "DebtCycleNumber",
                table: "SellerAccount",
                type: "int",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<DateTime>(
                name: "DebtDueAt",
                table: "SellerAccount",
                type: "datetime2",
                nullable: true);

            migrationBuilder.AddColumn<DateTime>(
                name: "DebtStartedAt",
                table: "SellerAccount",
                type: "datetime2",
                nullable: true);

            migrationBuilder.AddColumn<bool>(
                name: "DebtSuspended",
                table: "SellerAccount",
                type: "bit",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AlterColumn<int>(
                name: "OrderId",
                table: "NotificationOutbox",
                type: "int",
                nullable: true,
                oldClrType: typeof(int),
                oldType: "int");

            migrationBuilder.AddColumn<string>(
                name: "EventKey",
                table: "NotificationOutbox",
                type: "nvarchar(100)",
                maxLength: 100,
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "SellerId",
                table: "NotificationOutbox",
                type: "int",
                nullable: true);

            migrationBuilder.CreateTable(
                name: "SellerDebtCycle",
                columns: table => new
                {
                    Id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    SellerAccountId = table.Column<int>(type: "int", nullable: false),
                    Number = table.Column<int>(type: "int", nullable: false),
                    OpeningBalance = table.Column<decimal>(type: "decimal(18,2)", precision: 18, scale: 2, nullable: false),
                    StartedAt = table.Column<DateTime>(type: "datetime2", nullable: false),
                    DueAt = table.Column<DateTime>(type: "datetime2", nullable: false),
                    WarningAt = table.Column<DateTime>(type: "datetime2", nullable: false),
                    WarnedAt = table.Column<DateTime>(type: "datetime2", nullable: true),
                    SuspendedAt = table.Column<DateTime>(type: "datetime2", nullable: true),
                    ClosedAt = table.Column<DateTime>(type: "datetime2", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_SellerDebtCycle", x => x.Id);
                    table.ForeignKey(
                        name: "FK_SellerDebtCycle_SellerAccount_SellerAccountId",
                        column: x => x.SellerAccountId,
                        principalTable: "SellerAccount",
                        principalColumn: "Id");
                });

            migrationBuilder.CreateTable(
                name: "SellerDebtEvent",
                columns: table => new
                {
                    Id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    SellerAccountId = table.Column<int>(type: "int", nullable: false),
                    CycleNumber = table.Column<int>(type: "int", nullable: false),
                    EventType = table.Column<string>(type: "nvarchar(40)", maxLength: 40, nullable: false),
                    EntryKey = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: false),
                    Balance = table.Column<decimal>(type: "decimal(18,2)", precision: 18, scale: 2, nullable: false),
                    Description = table.Column<string>(type: "nvarchar(1000)", maxLength: 1000, nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "datetime2", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_SellerDebtEvent", x => x.Id);
                    table.ForeignKey(
                        name: "FK_SellerDebtEvent_SellerAccount_SellerAccountId",
                        column: x => x.SellerAccountId,
                        principalTable: "SellerAccount",
                        principalColumn: "Id");
                });

            migrationBuilder.CreateTable(
                name: "SellerDebtRepayment",
                columns: table => new
                {
                    Id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    SellerAccountId = table.Column<int>(type: "int", nullable: false),
                    CycleNumber = table.Column<int>(type: "int", nullable: false),
                    Amount = table.Column<decimal>(type: "decimal(18,2)", precision: 18, scale: 2, nullable: false),
                    ExpectedDebt = table.Column<decimal>(type: "decimal(18,2)", precision: 18, scale: 2, nullable: false),
                    RemainingDebt = table.Column<decimal>(type: "decimal(18,2)", precision: 18, scale: 2, nullable: false),
                    Currency = table.Column<string>(type: "nvarchar(3)", maxLength: 3, nullable: false),
                    RequestKey = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: false),
                    SimulateFailure = table.Column<bool>(type: "bit", nullable: false),
                    Status = table.Column<string>(type: "nvarchar(20)", maxLength: 20, nullable: false),
                    BankReferenceId = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "datetime2", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_SellerDebtRepayment", x => x.Id);
                    table.ForeignKey(
                        name: "FK_SellerDebtRepayment_SellerAccount_SellerAccountId",
                        column: x => x.SellerAccountId,
                        principalTable: "SellerAccount",
                        principalColumn: "Id");
                });

            migrationBuilder.CreateIndex(
                name: "IX_SellerAccount_NegativeBalance_DebtDueAt",
                table: "SellerAccount",
                columns: new[] { "NegativeBalance", "DebtDueAt" });

            migrationBuilder.CreateIndex(
                name: "IX_NotificationOutbox_EventKey",
                table: "NotificationOutbox",
                column: "EventKey",
                unique: true,
                filter: "[EventKey] IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "IX_NotificationOutbox_OrderId_EventType",
                table: "NotificationOutbox",
                columns: new[] { "OrderId", "EventType" },
                unique: true,
                filter: "[OrderId] IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "IX_NotificationOutbox_SellerId",
                table: "NotificationOutbox",
                column: "SellerId");

            migrationBuilder.CreateIndex(
                name: "IX_SellerDebtCycle_SellerAccountId_Number",
                table: "SellerDebtCycle",
                columns: new[] { "SellerAccountId", "Number" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_SellerDebtEvent_EntryKey",
                table: "SellerDebtEvent",
                column: "EntryKey",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_SellerDebtEvent_SellerAccountId_Id",
                table: "SellerDebtEvent",
                columns: new[] { "SellerAccountId", "Id" });

            migrationBuilder.CreateIndex(
                name: "IX_SellerDebtRepayment_SellerAccountId_RequestKey",
                table: "SellerDebtRepayment",
                columns: new[] { "SellerAccountId", "RequestKey" },
                unique: true);

            migrationBuilder.AddForeignKey(
                name: "FK_NotificationOutbox_User_SellerId",
                table: "NotificationOutbox",
                column: "SellerId",
                principalTable: "User",
                principalColumn: "id");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_NotificationOutbox_User_SellerId",
                table: "NotificationOutbox");

            migrationBuilder.DropTable(
                name: "SellerDebtCycle");

            migrationBuilder.DropTable(
                name: "SellerDebtEvent");

            migrationBuilder.DropTable(
                name: "SellerDebtRepayment");

            migrationBuilder.DropIndex(
                name: "IX_SellerAccount_NegativeBalance_DebtDueAt",
                table: "SellerAccount");

            migrationBuilder.DropIndex(
                name: "IX_NotificationOutbox_EventKey",
                table: "NotificationOutbox");

            migrationBuilder.DropIndex(
                name: "IX_NotificationOutbox_OrderId_EventType",
                table: "NotificationOutbox");

            migrationBuilder.DropIndex(
                name: "IX_NotificationOutbox_SellerId",
                table: "NotificationOutbox");

            migrationBuilder.DropColumn(
                name: "DebtCycleNumber",
                table: "SellerAccount");

            migrationBuilder.DropColumn(
                name: "DebtDueAt",
                table: "SellerAccount");

            migrationBuilder.DropColumn(
                name: "DebtStartedAt",
                table: "SellerAccount");

            migrationBuilder.DropColumn(
                name: "DebtSuspended",
                table: "SellerAccount");

            migrationBuilder.DropColumn(
                name: "EventKey",
                table: "NotificationOutbox");

            migrationBuilder.DropColumn(
                name: "SellerId",
                table: "NotificationOutbox");

            migrationBuilder.AlterColumn<int>(
                name: "OrderId",
                table: "NotificationOutbox",
                type: "int",
                nullable: false,
                defaultValue: 0,
                oldClrType: typeof(int),
                oldType: "int",
                oldNullable: true);

            migrationBuilder.CreateIndex(
                name: "IX_NotificationOutbox_OrderId_EventType",
                table: "NotificationOutbox",
                columns: new[] { "OrderId", "EventType" },
                unique: true);
        }
    }
}
