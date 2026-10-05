-- AddEmailIntegrationDiagnostics: additive, atomic, restart-safe. Select the database explicitly.
SET ANSI_NULLS ON;
SET QUOTED_IDENTIFIER ON;
SET XACT_ABORT ON;
IF NOT EXISTS (SELECT 1 FROM [__EFMigrationsHistory] WHERE [MigrationId] = N'20261004133301_AddPromotions')
    THROW 50001, 'Apply AddPromotions before email diagnostics.', 1;
IF EXISTS (SELECT 1 FROM [__EFMigrationsHistory] WHERE [MigrationId] = N'20261005062401_AddEmailIntegrationDiagnostics') RETURN;
BEGIN TRY
    BEGIN TRANSACTION;
    EXEC(N'
ALTER TABLE [NotificationOutbox] ADD [AttemptsInCycle] int NOT NULL DEFAULT 0;
');
    EXEC(N'
ALTER TABLE [NotificationOutbox] ADD [CapturedAt] datetime2 NULL;
');
    EXEC(N'
ALTER TABLE [NotificationOutbox] ADD [Cycle] int NOT NULL DEFAULT 1;
');
    EXEC(N'
ALTER TABLE [NotificationOutbox] ADD [From] nvarchar(254) NULL;
');
    EXEC(N'
ALTER TABLE [NotificationOutbox] ADD [HtmlBody] nvarchar(max) NULL;
');
    EXEC(N'
ALTER TABLE [NotificationOutbox] ADD [LastAttemptAt] datetime2 NULL;
');
    EXEC(N'
ALTER TABLE [NotificationOutbox] ADD [LastErrorCode] nvarchar(64) NULL;
');
    EXEC(N'
ALTER TABLE [NotificationOutbox] ADD [LastErrorSummary] nvarchar(300) NULL;
');
    EXEC(N'
ALTER TABLE [NotificationOutbox] ADD [NextAttemptAt] datetime2 NULL;
');
    EXEC(N'
ALTER TABLE [NotificationOutbox] ADD [ProcessingToken] uniqueidentifier NULL;
');
    EXEC(N'
ALTER TABLE [NotificationOutbox] ADD [ProcessingUntil] datetime2 NULL;
');
    EXEC(N'
CREATE TABLE [IntegrationLog] (
    [Id] bigint NOT NULL IDENTITY,
    [OrderId] int NULL,
    [CorrelationId] nvarchar(64) NOT NULL,
    [Service] nvarchar(32) NOT NULL,
    [Operation] nvarchar(64) NOT NULL,
    [Mode] nvarchar(20) NOT NULL,
    [EntityId] nvarchar(100) NULL,
    [Attempt] int NOT NULL,
    [Outcome] nvarchar(20) NOT NULL,
    [HttpStatus] int NULL,
    [DurationMs] bigint NOT NULL,
    [ProviderReference] nvarchar(200) NULL,
    [ErrorCode] nvarchar(64) NULL,
    [ErrorSummary] nvarchar(300) NULL,
    [NotificationId] int NULL,
    [Cycle] int NULL,
    [CreatedAt] datetime2 NOT NULL,
    CONSTRAINT [PK_IntegrationLog] PRIMARY KEY ([Id])
);
');
    EXEC(N'
CREATE INDEX [IX_NotificationOutbox_Status_NextAttemptAt] ON [NotificationOutbox] ([Status], [NextAttemptAt]);
');
    EXEC(N'
CREATE INDEX [IX_NotificationOutbox_Status_ProcessingUntil] ON [NotificationOutbox] ([Status], [ProcessingUntil]);
');
    EXEC(N'
CREATE INDEX [IX_IntegrationLog_NotificationId_CreatedAt_Id] ON [IntegrationLog] ([NotificationId], [CreatedAt], [Id]);
');
    EXEC(N'
CREATE INDEX [IX_IntegrationLog_OrderId_CreatedAt_Id] ON [IntegrationLog] ([OrderId], [CreatedAt], [Id]);
');
    EXEC(N'
CREATE INDEX [IX_IntegrationLog_Service_CreatedAt_Id] ON [IntegrationLog] ([Service], [CreatedAt], [Id]);
');
    EXEC(N'
UPDATE [NotificationOutbox] SET [CapturedAt] = COALESCE([SentAt], [CreatedAt]), [SentAt] = NULL WHERE [Status] = N''Captured'';
');
    EXEC(N'
INSERT INTO [__EFMigrationsHistory] ([MigrationId], [ProductVersion])
VALUES (N''20261005062401_AddEmailIntegrationDiagnostics'', N''8.0.31'');
');
    COMMIT TRANSACTION;
END TRY
BEGIN CATCH
    IF @@TRANCOUNT > 0 ROLLBACK TRANSACTION;
    THROW;
END CATCH;