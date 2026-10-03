using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace G4.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddDisputeWorkflow : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // The baseline SQL creates the table/FK without EF's conventional FK index.
            // Other databases may already have that index, so replace it only if present.
            migrationBuilder.Sql(@"
IF EXISTS (
    SELECT 1 FROM sys.indexes
    WHERE [name] = N'IX_Dispute_orderId'
      AND [object_id] = OBJECT_ID(N'[Dispute]')
)
    DROP INDEX [IX_Dispute_orderId] ON [Dispute];");

            migrationBuilder.AddColumn<DateTime>(
                name: "BuyerResponseDueAt",
                table: "Dispute",
                type: "datetime2",
                nullable: true);

            migrationBuilder.AddColumn<DateTime>(
                name: "ClosedAt",
                table: "Dispute",
                type: "datetime2",
                nullable: true);

            migrationBuilder.AddColumn<DateTime>(
                name: "CreatedAt",
                table: "Dispute",
                type: "datetime2",
                nullable: false,
                defaultValue: new DateTime(1, 1, 1, 0, 0, 0, 0, DateTimeKind.Unspecified));

            migrationBuilder.AddColumn<DateTime>(
                name: "EscalatedAt",
                table: "Dispute",
                type: "datetime2",
                nullable: true);

            migrationBuilder.AddColumn<bool>(
                name: "IsOpen",
                table: "Dispute",
                type: "bit",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<string>(
                name: "Outcome",
                table: "Dispute",
                type: "nvarchar(30)",
                maxLength: 30,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "Proposal",
                table: "Dispute",
                type: "nvarchar(30)",
                maxLength: 30,
                nullable: true);

            migrationBuilder.AddColumn<byte[]>(
                name: "RowVersion",
                table: "Dispute",
                type: "rowversion",
                rowVersion: true,
                nullable: false,
                defaultValue: new byte[0]);

            migrationBuilder.AddColumn<DateTime>(
                name: "SellerRespondedAt",
                table: "Dispute",
                type: "datetime2",
                nullable: true);

            migrationBuilder.AddColumn<DateTime>(
                name: "SellerResponseDueAt",
                table: "Dispute",
                type: "datetime2",
                nullable: false,
                defaultValue: new DateTime(1, 1, 1, 0, 0, 0, 0, DateTimeKind.Unspecified));

            migrationBuilder.AddColumn<DateTime>(
                name: "UpdatedAt",
                table: "Dispute",
                type: "datetime2",
                nullable: false,
                defaultValue: new DateTime(1, 1, 1, 0, 0, 0, 0, DateTimeKind.Unspecified));

            migrationBuilder.AddColumn<bool>(
                name: "WorkflowEnabled",
                table: "Dispute",
                type: "bit",
                nullable: false,
                defaultValue: false);

            migrationBuilder.CreateTable(
                name: "DisputeEntry",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    DisputeId = table.Column<int>(type: "int", nullable: false),
                    ActorId = table.Column<int>(type: "int", nullable: true),
                    ActorRole = table.Column<string>(type: "nvarchar(20)", maxLength: 20, nullable: false),
                    Kind = table.Column<string>(type: "nvarchar(30)", maxLength: 30, nullable: false),
                    Description = table.Column<string>(type: "nvarchar(4000)", maxLength: 4000, nullable: false),
                    EvidenceLinksJson = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "datetime2", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_DisputeEntry", x => x.Id);
                    table.ForeignKey(
                        name: "FK_DisputeEntry_Dispute_DisputeId",
                        column: x => x.DisputeId,
                        principalTable: "Dispute",
                        principalColumn: "id");
                    table.ForeignKey(
                        name: "FK_DisputeEntry_User_ActorId",
                        column: x => x.ActorId,
                        principalTable: "User",
                        principalColumn: "id");
                });

            migrationBuilder.CreateIndex(
                name: "IX_Dispute_orderId",
                table: "Dispute",
                column: "orderId",
                unique: true,
                filter: "[IsOpen] = 1 AND [WorkflowEnabled] = 1 AND [orderId] IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "IX_Dispute_WorkflowEnabled_status_SellerResponseDueAt",
                table: "Dispute",
                columns: new[] { "WorkflowEnabled", "status", "SellerResponseDueAt" });

            migrationBuilder.CreateIndex(
                name: "IX_DisputeEntry_ActorId",
                table: "DisputeEntry",
                column: "ActorId");

            migrationBuilder.CreateIndex(
                name: "IX_DisputeEntry_DisputeId_Id",
                table: "DisputeEntry",
                columns: new[] { "DisputeId", "Id" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "DisputeEntry");

            migrationBuilder.DropIndex(
                name: "IX_Dispute_orderId",
                table: "Dispute");

            migrationBuilder.DropIndex(
                name: "IX_Dispute_WorkflowEnabled_status_SellerResponseDueAt",
                table: "Dispute");

            migrationBuilder.DropColumn(
                name: "BuyerResponseDueAt",
                table: "Dispute");

            migrationBuilder.DropColumn(
                name: "ClosedAt",
                table: "Dispute");

            migrationBuilder.DropColumn(
                name: "CreatedAt",
                table: "Dispute");

            migrationBuilder.DropColumn(
                name: "EscalatedAt",
                table: "Dispute");

            migrationBuilder.DropColumn(
                name: "IsOpen",
                table: "Dispute");

            migrationBuilder.DropColumn(
                name: "Outcome",
                table: "Dispute");

            migrationBuilder.DropColumn(
                name: "Proposal",
                table: "Dispute");

            migrationBuilder.DropColumn(
                name: "RowVersion",
                table: "Dispute");

            migrationBuilder.DropColumn(
                name: "SellerRespondedAt",
                table: "Dispute");

            migrationBuilder.DropColumn(
                name: "SellerResponseDueAt",
                table: "Dispute");

            migrationBuilder.DropColumn(
                name: "UpdatedAt",
                table: "Dispute");

            migrationBuilder.DropColumn(
                name: "WorkflowEnabled",
                table: "Dispute");

            migrationBuilder.CreateIndex(
                name: "IX_Dispute_orderId",
                table: "Dispute",
                column: "orderId");
        }
    }
}
