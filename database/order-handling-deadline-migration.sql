BEGIN TRANSACTION;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261113034257_AddOrderHandlingDeadline'
)
BEGIN
    ALTER TABLE [OrderTable] ADD [HandedOverAt] datetime2 NULL;
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261113034257_AddOrderHandlingDeadline'
)
BEGIN
    ALTER TABLE [OrderTable] ADD [HandlingDeadlineAt] datetime2 NULL;
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261113034257_AddOrderHandlingDeadline'
)
BEGIN
    ALTER TABLE [OrderTable] ADD [HandlingSeconds] int NULL;
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261113034257_AddOrderHandlingDeadline'
)
BEGIN
    INSERT INTO [__EFMigrationsHistory] ([MigrationId], [ProductVersion])
    VALUES (N'20261113034257_AddOrderHandlingDeadline', N'8.0.31');
END;
GO

COMMIT;
GO

