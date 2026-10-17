using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace G4.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddSellerRegistrationAndAutomaticWriteOff : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<decimal>(
                name: "LastObservedBalance",
                table: "SellerDebtCycle",
                type: "decimal(18,2)",
                precision: 18,
                scale: 2,
                nullable: false,
                defaultValue: 0m);

            migrationBuilder.AddColumn<DateTime>(
                name: "LastRecoveryAt",
                table: "SellerDebtCycle",
                type: "datetime2",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "RecoveryGraceSeconds",
                table: "SellerDebtCycle",
                type: "int",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<DateTime>(
                name: "WriteOffEligibleAt",
                table: "SellerDebtCycle",
                type: "datetime2",
                nullable: true);

            migrationBuilder.AddColumn<long>(
                name: "CurrentIdentityId",
                table: "SellerAccount",
                type: "bigint",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "IdentityVersion",
                table: "SellerAccount",
                type: "int",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<DateTime>(
                name: "PermanentSalesBlockedAt",
                table: "SellerAccount",
                type: "datetime2",
                nullable: true);

            migrationBuilder.CreateTable(
                name: "SellerDebtWriteOff",
                columns: table => new
                {
                    Id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    SellerAccountId = table.Column<int>(type: "int", nullable: false),
                    CycleNumber = table.Column<int>(type: "int", nullable: false),
                    OriginalAmount = table.Column<decimal>(type: "decimal(18,2)", precision: 18, scale: 2, nullable: false),
                    DebtStartedAt = table.Column<DateTime>(type: "datetime2", nullable: false),
                    DebtDueAt = table.Column<DateTime>(type: "datetime2", nullable: false),
                    EligibleAt = table.Column<DateTime>(type: "datetime2", nullable: false),
                    LastRecoveryAt = table.Column<DateTime>(type: "datetime2", nullable: false),
                    RecoveryGraceSeconds = table.Column<int>(type: "int", nullable: false),
                    BlacklistedIdentities = table.Column<int>(type: "int", nullable: false),
                    Actor = table.Column<string>(type: "nvarchar(40)", maxLength: 40, nullable: false),
                    Reason = table.Column<string>(type: "nvarchar(1000)", maxLength: 1000, nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "datetime2", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_SellerDebtWriteOff", x => x.Id);
                    table.ForeignKey(
                        name: "FK_SellerDebtWriteOff_SellerAccount_SellerAccountId",
                        column: x => x.SellerAccountId,
                        principalTable: "SellerAccount",
                        principalColumn: "Id");
                });

            migrationBuilder.CreateTable(
                name: "SellerIdentity",
                columns: table => new
                {
                    Id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    SellerId = table.Column<int>(type: "int", nullable: false),
                    Kind = table.Column<string>(type: "nvarchar(20)", maxLength: 20, nullable: false),
                    MaskedValue = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: false),
                    Fingerprint = table.Column<string>(type: "nvarchar(64)", maxLength: 64, nullable: false),
                    KeyId = table.Column<string>(type: "nvarchar(40)", maxLength: 40, nullable: false),
                    RequestKey = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: false),
                    RequestFingerprint = table.Column<string>(type: "nvarchar(64)", maxLength: 64, nullable: false),
                    VerifiedAt = table.Column<DateTime>(type: "datetime2", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_SellerIdentity", x => x.Id);
                    table.ForeignKey(
                        name: "FK_SellerIdentity_User_SellerId",
                        column: x => x.SellerId,
                        principalTable: "User",
                        principalColumn: "id");
                });

            migrationBuilder.CreateTable(
                name: "SellerIdentityKey",
                columns: table => new
                {
                    Id = table.Column<string>(type: "nvarchar(40)", maxLength: 40, nullable: false),
                    Verifier = table.Column<string>(type: "nvarchar(64)", maxLength: 64, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_SellerIdentityKey", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "SellerRegistration",
                columns: table => new
                {
                    Id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    SellerId = table.Column<int>(type: "int", nullable: false),
                    RequestKey = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: false),
                    RequestFingerprint = table.Column<string>(type: "nvarchar(64)", maxLength: 64, nullable: false),
                    Email = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: false),
                    StoreName = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "datetime2", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_SellerRegistration", x => x.Id);
                    table.ForeignKey(
                        name: "FK_SellerRegistration_User_SellerId",
                        column: x => x.SellerId,
                        principalTable: "User",
                        principalColumn: "id");
                });

            migrationBuilder.CreateTable(
                name: "SellerBlacklist",
                columns: table => new
                {
                    Id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    Kind = table.Column<string>(type: "nvarchar(20)", maxLength: 20, nullable: false),
                    MaskedValue = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: false),
                    Fingerprint = table.Column<string>(type: "nvarchar(64)", maxLength: 64, nullable: false),
                    KeyId = table.Column<string>(type: "nvarchar(40)", maxLength: 40, nullable: false),
                    SourceSellerId = table.Column<int>(type: "int", nullable: true),
                    WriteOffId = table.Column<long>(type: "bigint", nullable: true),
                    IsDemo = table.Column<bool>(type: "bit", nullable: false),
                    Reason = table.Column<string>(type: "nvarchar(1000)", maxLength: 1000, nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "datetime2", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_SellerBlacklist", x => x.Id);
                    table.ForeignKey(
                        name: "FK_SellerBlacklist_SellerDebtWriteOff_WriteOffId",
                        column: x => x.WriteOffId,
                        principalTable: "SellerDebtWriteOff",
                        principalColumn: "Id");
                    table.ForeignKey(
                        name: "FK_SellerBlacklist_User_SourceSellerId",
                        column: x => x.SourceSellerId,
                        principalTable: "User",
                        principalColumn: "id");
                });

            migrationBuilder.CreateTable(
                name: "SellerWriteOffRecovery",
                columns: table => new
                {
                    Id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    WriteOffId = table.Column<long>(type: "bigint", nullable: false),
                    Amount = table.Column<decimal>(type: "decimal(18,2)", precision: 18, scale: 2, nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "datetime2", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_SellerWriteOffRecovery", x => x.Id);
                    table.ForeignKey(
                        name: "FK_SellerWriteOffRecovery_SellerDebtWriteOff_WriteOffId",
                        column: x => x.WriteOffId,
                        principalTable: "SellerDebtWriteOff",
                        principalColumn: "Id");
                });

            migrationBuilder.CreateIndex(
                name: "IX_SellerAccount_CurrentIdentityId",
                table: "SellerAccount",
                column: "CurrentIdentityId");

            migrationBuilder.CreateIndex(
                name: "IX_SellerBlacklist_KeyId_Fingerprint",
                table: "SellerBlacklist",
                columns: new[] { "KeyId", "Fingerprint" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_SellerBlacklist_SourceSellerId",
                table: "SellerBlacklist",
                column: "SourceSellerId");

            migrationBuilder.CreateIndex(
                name: "IX_SellerBlacklist_WriteOffId",
                table: "SellerBlacklist",
                column: "WriteOffId");

            migrationBuilder.CreateIndex(
                name: "IX_SellerDebtWriteOff_SellerAccountId_CycleNumber",
                table: "SellerDebtWriteOff",
                columns: new[] { "SellerAccountId", "CycleNumber" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_SellerIdentity_KeyId_Fingerprint",
                table: "SellerIdentity",
                columns: new[] { "KeyId", "Fingerprint" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_SellerIdentity_SellerId_RequestKey",
                table: "SellerIdentity",
                columns: new[] { "SellerId", "RequestKey" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_SellerRegistration_RequestKey",
                table: "SellerRegistration",
                column: "RequestKey",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_SellerRegistration_SellerId",
                table: "SellerRegistration",
                column: "SellerId",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_SellerWriteOffRecovery_WriteOffId",
                table: "SellerWriteOffRecovery",
                column: "WriteOffId");

            migrationBuilder.AddForeignKey(
                name: "FK_SellerAccount_SellerIdentity_CurrentIdentityId",
                table: "SellerAccount",
                column: "CurrentIdentityId",
                principalTable: "SellerIdentity",
                principalColumn: "Id");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_SellerAccount_SellerIdentity_CurrentIdentityId",
                table: "SellerAccount");

            migrationBuilder.DropTable(
                name: "SellerBlacklist");

            migrationBuilder.DropTable(
                name: "SellerIdentity");

            migrationBuilder.DropTable(
                name: "SellerIdentityKey");

            migrationBuilder.DropTable(
                name: "SellerRegistration");

            migrationBuilder.DropTable(
                name: "SellerWriteOffRecovery");

            migrationBuilder.DropTable(
                name: "SellerDebtWriteOff");

            migrationBuilder.DropIndex(
                name: "IX_SellerAccount_CurrentIdentityId",
                table: "SellerAccount");

            migrationBuilder.DropColumn(
                name: "LastObservedBalance",
                table: "SellerDebtCycle");

            migrationBuilder.DropColumn(
                name: "LastRecoveryAt",
                table: "SellerDebtCycle");

            migrationBuilder.DropColumn(
                name: "RecoveryGraceSeconds",
                table: "SellerDebtCycle");

            migrationBuilder.DropColumn(
                name: "WriteOffEligibleAt",
                table: "SellerDebtCycle");

            migrationBuilder.DropColumn(
                name: "CurrentIdentityId",
                table: "SellerAccount");

            migrationBuilder.DropColumn(
                name: "IdentityVersion",
                table: "SellerAccount");

            migrationBuilder.DropColumn(
                name: "PermanentSalesBlockedAt",
                table: "SellerAccount");
        }
    }
}
