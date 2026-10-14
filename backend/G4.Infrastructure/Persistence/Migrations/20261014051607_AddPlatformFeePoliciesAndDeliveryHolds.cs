using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace G4.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddPlatformFeePoliciesAndDeliveryHolds : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AlterColumn<DateTime>(
                name: "ReleaseAt",
                table: "SellerSettlement",
                type: "datetime2",
                nullable: true,
                oldClrType: typeof(DateTime),
                oldType: "datetime2");

            migrationBuilder.AddColumn<decimal>(
                name: "FeePercentSnapshot",
                table: "SellerSettlement",
                type: "decimal(5,2)",
                precision: 5,
                scale: 2,
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "FeePolicyId",
                table: "SellerSettlement",
                type: "int",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "HoldPolicyVersion",
                table: "SellerSettlement",
                type: "int",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<int>(
                name: "HoldSecondsSnapshot",
                table: "SellerSettlement",
                type: "int",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<DateTime>(
                name: "HoldStartedAt",
                table: "SellerSettlement",
                type: "datetime2",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "SellerLevelSnapshot",
                table: "SellerSettlement",
                type: "int",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.CreateTable(
                name: "PlatformFeePolicy",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    FeePercent = table.Column<decimal>(type: "decimal(5,2)", precision: 5, scale: 2, nullable: false),
                    FixedFee = table.Column<decimal>(type: "decimal(18,2)", precision: 18, scale: 2, nullable: false),
                    PreviousFeePercent = table.Column<decimal>(type: "decimal(5,2)", precision: 5, scale: 2, nullable: false),
                    PreviousFixedFee = table.Column<decimal>(type: "decimal(18,2)", precision: 18, scale: 2, nullable: false),
                    EffectiveAt = table.Column<DateTime>(type: "datetime2", nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "datetime2", nullable: false),
                    ChangedById = table.Column<int>(type: "int", nullable: true),
                    Reason = table.Column<string>(type: "nvarchar(1000)", maxLength: 1000, nullable: false),
                    RequestKey = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_PlatformFeePolicy", x => x.Id);
                    table.ForeignKey(
                        name: "FK_PlatformFeePolicy_User_ChangedById",
                        column: x => x.ChangedById,
                        principalTable: "User",
                        principalColumn: "id");
                });

            migrationBuilder.InsertData(
                table: "PlatformFeePolicy",
                columns: new[] { "Id", "ChangedById", "CreatedAt", "EffectiveAt", "FeePercent", "FixedFee", "PreviousFeePercent", "PreviousFixedFee", "Reason", "RequestKey" },
                values: new object[] { 1, null, new DateTime(2000, 1, 1, 0, 0, 0, 0, DateTimeKind.Utc), new DateTime(2000, 1, 1, 0, 0, 0, 0, DateTimeKind.Utc), 12m, 0.30m, 12m, 0.30m, "Mức phí mặc định ban đầu", "initial-platform-fee" });

            migrationBuilder.CreateIndex(
                name: "IX_SellerSettlement_FeePolicyId",
                table: "SellerSettlement",
                column: "FeePolicyId");

            migrationBuilder.CreateIndex(
                name: "IX_PlatformFeePolicy_ChangedById",
                table: "PlatformFeePolicy",
                column: "ChangedById");

            migrationBuilder.CreateIndex(
                name: "IX_PlatformFeePolicy_EffectiveAt",
                table: "PlatformFeePolicy",
                column: "EffectiveAt",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_PlatformFeePolicy_RequestKey",
                table: "PlatformFeePolicy",
                column: "RequestKey",
                unique: true);

            migrationBuilder.AddForeignKey(
                name: "FK_SellerSettlement_PlatformFeePolicy_FeePolicyId",
                table: "SellerSettlement",
                column: "FeePolicyId",
                principalTable: "PlatformFeePolicy",
                principalColumn: "Id");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_SellerSettlement_PlatformFeePolicy_FeePolicyId",
                table: "SellerSettlement");

            migrationBuilder.DropTable(
                name: "PlatformFeePolicy");

            migrationBuilder.DropIndex(
                name: "IX_SellerSettlement_FeePolicyId",
                table: "SellerSettlement");

            migrationBuilder.DropColumn(
                name: "FeePercentSnapshot",
                table: "SellerSettlement");

            migrationBuilder.DropColumn(
                name: "FeePolicyId",
                table: "SellerSettlement");

            migrationBuilder.DropColumn(
                name: "HoldPolicyVersion",
                table: "SellerSettlement");

            migrationBuilder.DropColumn(
                name: "HoldSecondsSnapshot",
                table: "SellerSettlement");

            migrationBuilder.DropColumn(
                name: "HoldStartedAt",
                table: "SellerSettlement");

            migrationBuilder.DropColumn(
                name: "SellerLevelSnapshot",
                table: "SellerSettlement");

            migrationBuilder.AlterColumn<DateTime>(
                name: "ReleaseAt",
                table: "SellerSettlement",
                type: "datetime2",
                nullable: false,
                defaultValue: new DateTime(1, 1, 1, 0, 0, 0, 0, DateTimeKind.Unspecified),
                oldClrType: typeof(DateTime),
                oldType: "datetime2",
                oldNullable: true);
        }
    }
}
