using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace G4.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class UnifyPayoutWorkflowAndHistory : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<int>(
                name: "WorkflowVersion",
                table: "SellerPayout",
                type: "int",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.CreateTable(
                name: "SellerPayoutEvent",
                columns: table => new
                {
                    Id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    PayoutId = table.Column<int>(type: "int", nullable: false),
                    FromStatus = table.Column<string>(type: "nvarchar(20)", maxLength: 20, nullable: true),
                    ToStatus = table.Column<string>(type: "nvarchar(20)", maxLength: 20, nullable: false),
                    ActorUserId = table.Column<int>(type: "int", nullable: true),
                    ActorRole = table.Column<string>(type: "nvarchar(20)", maxLength: 20, nullable: false),
                    OccurredAt = table.Column<DateTime>(type: "datetime2", nullable: false),
                    BankReferenceId = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_SellerPayoutEvent", x => x.Id);
                    table.ForeignKey(
                        name: "FK_SellerPayoutEvent_SellerPayout_PayoutId",
                        column: x => x.PayoutId,
                        principalTable: "SellerPayout",
                        principalColumn: "Id");
                    table.ForeignKey(
                        name: "FK_SellerPayoutEvent_User_ActorUserId",
                        column: x => x.ActorUserId,
                        principalTable: "User",
                        principalColumn: "id");
                });

            migrationBuilder.CreateIndex(
                name: "IX_SellerPayoutEvent_ActorUserId",
                table: "SellerPayoutEvent",
                column: "ActorUserId");

            migrationBuilder.CreateIndex(
                name: "IX_SellerPayoutEvent_PayoutId_ToStatus",
                table: "SellerPayoutEvent",
                columns: new[] { "PayoutId", "ToStatus" },
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "SellerPayoutEvent");

            migrationBuilder.DropColumn(
                name: "WorkflowVersion",
                table: "SellerPayout");
        }
    }
}
