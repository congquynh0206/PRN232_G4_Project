BEGIN TRANSACTION;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261027153709_AddUserNotificationBell'
)
BEGIN
    ALTER TABLE [CommerceChange] ADD [NotificationMessage] nvarchar(500) NULL;
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261027153709_AddUserNotificationBell'
)
BEGIN
    ALTER TABLE [CommerceChange] ADD [NotificationTitle] nvarchar(160) NULL;
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261027153709_AddUserNotificationBell'
)
BEGIN
    CREATE TABLE [UserNotification] (
        [Id] bigint NOT NULL IDENTITY,
        [CommerceChangeId] bigint NOT NULL,
        [UserId] int NOT NULL,
        [Title] nvarchar(160) NOT NULL,
        [Message] nvarchar(500) NOT NULL,
        [TargetUrl] nvarchar(200) NOT NULL,
        [CreatedAt] datetime2 NOT NULL,
        [ReadAt] datetime2 NULL,
        CONSTRAINT [PK_UserNotification] PRIMARY KEY ([Id]),
        CONSTRAINT [FK_UserNotification_CommerceChange_CommerceChangeId] FOREIGN KEY ([CommerceChangeId]) REFERENCES [CommerceChange] ([Id]) ON DELETE NO ACTION,
        CONSTRAINT [FK_UserNotification_User_UserId] FOREIGN KEY ([UserId]) REFERENCES [User] ([id]) ON DELETE NO ACTION
    );
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261027153709_AddUserNotificationBell'
)
BEGIN
    CREATE INDEX [IX_UserNotification_CommerceChangeId] ON [UserNotification] ([CommerceChangeId]);
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261027153709_AddUserNotificationBell'
)
BEGIN
    CREATE UNIQUE INDEX [IX_UserNotification_UserId_CommerceChangeId] ON [UserNotification] ([UserId], [CommerceChangeId]);
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261027153709_AddUserNotificationBell'
)
BEGIN
    CREATE INDEX [IX_UserNotification_UserId_ReadAt_Id] ON [UserNotification] ([UserId], [ReadAt], [Id]);
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261027153709_AddUserNotificationBell'
)
BEGIN
    INSERT INTO [__EFMigrationsHistory] ([MigrationId], [ProductVersion])
    VALUES (N'20261027153709_AddUserNotificationBell', N'8.0.31');
END;
GO

COMMIT;
GO

