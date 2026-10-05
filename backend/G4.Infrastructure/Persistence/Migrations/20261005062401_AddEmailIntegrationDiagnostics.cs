using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace G4.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddEmailIntegrationDiagnostics : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<int>(
                name: "AttemptsInCycle",
                table: "NotificationOutbox",
                type: "int",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<DateTime>(
                name: "CapturedAt",
                table: "NotificationOutbox",
                type: "datetime2",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "Cycle",
                table: "NotificationOutbox",
                type: "int",
                nullable: false,
                defaultValue: 1);

            migrationBuilder.AddColumn<string>(
                name: "From",
                table: "NotificationOutbox",
                type: "nvarchar(254)",
                maxLength: 254,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "HtmlBody",
                table: "NotificationOutbox",
                type: "nvarchar(max)",
                nullable: true);

            migrationBuilder.AddColumn<DateTime>(
                name: "LastAttemptAt",
                table: "NotificationOutbox",
                type: "datetime2",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "LastErrorCode",
                table: "NotificationOutbox",
                type: "nvarchar(64)",
                maxLength: 64,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "LastErrorSummary",
                table: "NotificationOutbox",
                type: "nvarchar(300)",
                maxLength: 300,
                nullable: true);

            migrationBuilder.AddColumn<DateTime>(
                name: "NextAttemptAt",
                table: "NotificationOutbox",
                type: "datetime2",
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "ProcessingToken",
                table: "NotificationOutbox",
                type: "uniqueidentifier",
                nullable: true);

            migrationBuilder.AddColumn<DateTime>(
                name: "ProcessingUntil",
                table: "NotificationOutbox",
                type: "datetime2",
                nullable: true);

            migrationBuilder.CreateTable(
                name: "IntegrationLog",
                columns: table => new
                {
                    Id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    OrderId = table.Column<int>(type: "int", nullable: true),
                    CorrelationId = table.Column<string>(type: "nvarchar(64)", maxLength: 64, nullable: false),
                    Service = table.Column<string>(type: "nvarchar(32)", maxLength: 32, nullable: false),
                    Operation = table.Column<string>(type: "nvarchar(64)", maxLength: 64, nullable: false),
                    Mode = table.Column<string>(type: "nvarchar(20)", maxLength: 20, nullable: false),
                    EntityId = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: true),
                    Attempt = table.Column<int>(type: "int", nullable: false),
                    Outcome = table.Column<string>(type: "nvarchar(20)", maxLength: 20, nullable: false),
                    HttpStatus = table.Column<int>(type: "int", nullable: true),
                    DurationMs = table.Column<long>(type: "bigint", nullable: false),
                    ProviderReference = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: true),
                    ErrorCode = table.Column<string>(type: "nvarchar(64)", maxLength: 64, nullable: true),
                    ErrorSummary = table.Column<string>(type: "nvarchar(300)", maxLength: 300, nullable: true),
                    NotificationId = table.Column<int>(type: "int", nullable: true),
                    Cycle = table.Column<int>(type: "int", nullable: true),
                    CreatedAt = table.Column<DateTime>(type: "datetime2", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_IntegrationLog", x => x.Id);
                });

            migrationBuilder.CreateIndex(
                name: "IX_NotificationOutbox_Status_NextAttemptAt",
                table: "NotificationOutbox",
                columns: new[] { "Status", "NextAttemptAt" });

            migrationBuilder.CreateIndex(
                name: "IX_NotificationOutbox_Status_ProcessingUntil",
                table: "NotificationOutbox",
                columns: new[] { "Status", "ProcessingUntil" });

            migrationBuilder.CreateIndex(
                name: "IX_IntegrationLog_NotificationId_CreatedAt_Id",
                table: "IntegrationLog",
                columns: new[] { "NotificationId", "CreatedAt", "Id" });

            migrationBuilder.CreateIndex(
                name: "IX_IntegrationLog_OrderId_CreatedAt_Id",
                table: "IntegrationLog",
                columns: new[] { "OrderId", "CreatedAt", "Id" });

            migrationBuilder.CreateIndex(
                name: "IX_IntegrationLog_Service_CreatedAt_Id",
                table: "IntegrationLog",
                columns: new[] { "Service", "CreatedAt", "Id" });
            migrationBuilder.Sql("UPDATE [NotificationOutbox] SET [CapturedAt] = COALESCE([SentAt], [CreatedAt]), [SentAt] = NULL WHERE [Status] = N'Captured';");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "IntegrationLog");

            migrationBuilder.DropIndex(
                name: "IX_NotificationOutbox_Status_NextAttemptAt",
                table: "NotificationOutbox");

            migrationBuilder.DropIndex(
                name: "IX_NotificationOutbox_Status_ProcessingUntil",
                table: "NotificationOutbox");

            migrationBuilder.DropColumn(
                name: "AttemptsInCycle",
                table: "NotificationOutbox");

            migrationBuilder.DropColumn(
                name: "CapturedAt",
                table: "NotificationOutbox");

            migrationBuilder.DropColumn(
                name: "Cycle",
                table: "NotificationOutbox");

            migrationBuilder.DropColumn(
                name: "From",
                table: "NotificationOutbox");

            migrationBuilder.DropColumn(
                name: "HtmlBody",
                table: "NotificationOutbox");

            migrationBuilder.DropColumn(
                name: "LastAttemptAt",
                table: "NotificationOutbox");

            migrationBuilder.DropColumn(
                name: "LastErrorCode",
                table: "NotificationOutbox");

            migrationBuilder.DropColumn(
                name: "LastErrorSummary",
                table: "NotificationOutbox");

            migrationBuilder.DropColumn(
                name: "NextAttemptAt",
                table: "NotificationOutbox");

            migrationBuilder.DropColumn(
                name: "ProcessingToken",
                table: "NotificationOutbox");

            migrationBuilder.DropColumn(
                name: "ProcessingUntil",
                table: "NotificationOutbox");
        }
    }
}
