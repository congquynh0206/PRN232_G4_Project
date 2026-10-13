BEGIN TRANSACTION;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261008083121_AddBuyerCartCheckoutBatch'
)
BEGIN
    CREATE TABLE [BuyerCart] (
        [BuyerId] int NOT NULL,
        [ItemsJson] nvarchar(max) NOT NULL,
        [Version] uniqueidentifier NOT NULL,
        CONSTRAINT [PK_BuyerCart] PRIMARY KEY ([BuyerId]),
        CONSTRAINT [FK_BuyerCart_User_BuyerId] FOREIGN KEY ([BuyerId]) REFERENCES [User] ([id])
    );
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261008083121_AddBuyerCartCheckoutBatch'
)
BEGIN
    CREATE TABLE [CheckoutBatch] (
        [Key] nvarchar(80) NOT NULL,
        [BuyerId] int NOT NULL,
        [RequestJson] nvarchar(max) NOT NULL,
        [OrderIdsJson] nvarchar(max) NOT NULL,
        CONSTRAINT [PK_CheckoutBatch] PRIMARY KEY ([Key]),
        CONSTRAINT [FK_CheckoutBatch_User_BuyerId] FOREIGN KEY ([BuyerId]) REFERENCES [User] ([id])
    );
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261008083121_AddBuyerCartCheckoutBatch'
)
BEGIN
    CREATE INDEX [IX_CheckoutBatch_BuyerId] ON [CheckoutBatch] ([BuyerId]);
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261008083121_AddBuyerCartCheckoutBatch'
)
BEGIN
    INSERT INTO [__EFMigrationsHistory] ([MigrationId], [ProductVersion])
    VALUES (N'20261008083121_AddBuyerCartCheckoutBatch', N'8.0.31');
END;
GO

COMMIT;
GO

