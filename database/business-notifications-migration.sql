BEGIN TRANSACTION;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261017050657_CompleteBusinessNotifications'
)
BEGIN
    DROP INDEX [IX_NotificationOutbox_OrderId_EventType] ON [NotificationOutbox];
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261017050657_CompleteBusinessNotifications'
)
BEGIN
    ALTER TABLE [OrderTable] ADD [CancelRequestNumber] int NOT NULL DEFAULT 0;
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261017050657_CompleteBusinessNotifications'
)
BEGIN
    ALTER TABLE [NotificationOutbox] ADD [PayoutId] int NULL;
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261017050657_CompleteBusinessNotifications'
)
BEGIN
    ALTER TABLE [NotificationOutbox] ADD [RecipientUserId] int NULL;
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261017050657_CompleteBusinessNotifications'
)
BEGIN
    EXEC(N'CREATE UNIQUE INDEX [IX_NotificationOutbox_OrderId_EventType] ON [NotificationOutbox] ([OrderId], [EventType]) WHERE [EventKey] IS NULL AND [OrderId] IS NOT NULL');
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261017050657_CompleteBusinessNotifications'
)
BEGIN
    CREATE INDEX [IX_NotificationOutbox_PayoutId] ON [NotificationOutbox] ([PayoutId]);
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261017050657_CompleteBusinessNotifications'
)
BEGIN
    CREATE INDEX [IX_NotificationOutbox_RecipientUserId_CreatedAt] ON [NotificationOutbox] ([RecipientUserId], [CreatedAt]);
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261017050657_CompleteBusinessNotifications'
)
BEGIN
    ALTER TABLE [NotificationOutbox] ADD CONSTRAINT [FK_NotificationOutbox_SellerPayout_PayoutId] FOREIGN KEY ([PayoutId]) REFERENCES [SellerPayout] ([Id]);
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261017050657_CompleteBusinessNotifications'
)
BEGIN
    ALTER TABLE [NotificationOutbox] ADD CONSTRAINT [FK_NotificationOutbox_User_RecipientUserId] FOREIGN KEY ([RecipientUserId]) REFERENCES [User] ([id]);
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261017050657_CompleteBusinessNotifications'
)
BEGIN
    INSERT INTO [__EFMigrationsHistory] ([MigrationId], [ProductVersion])
    VALUES (N'20261017050657_CompleteBusinessNotifications', N'8.0.31');
END;
GO

COMMIT;
GO

