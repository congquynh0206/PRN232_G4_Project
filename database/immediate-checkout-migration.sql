BEGIN TRANSACTION;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261108144118_AddImmediateCheckout'
)
BEGIN
    ALTER TABLE [Product] ADD [ImmediatePaymentRequired] bit NOT NULL DEFAULT CAST(0 AS bit);
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261108144118_AddImmediateCheckout'
)
BEGIN
    ALTER TABLE [Product] ADD [IsListed] bit NOT NULL DEFAULT CAST(1 AS bit);
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261108144118_AddImmediateCheckout'
)
BEGIN
    CREATE TABLE [ImmediateCheckout] (
        [Id] uniqueidentifier NOT NULL,
        [BuyerId] int NOT NULL,
        [RequestJson] nvarchar(max) NOT NULL,
        [QuoteJson] nvarchar(max) NOT NULL,
        [CreatedAt] datetime2 NOT NULL,
        [ExpiresAt] datetime2 NOT NULL,
        [Status] nvarchar(20) NOT NULL,
        [OrderId] int NULL,
        CONSTRAINT [PK_ImmediateCheckout] PRIMARY KEY ([Id]),
        CONSTRAINT [FK_ImmediateCheckout_OrderTable_OrderId] FOREIGN KEY ([OrderId]) REFERENCES [OrderTable] ([id]),
        CONSTRAINT [FK_ImmediateCheckout_User_BuyerId] FOREIGN KEY ([BuyerId]) REFERENCES [User] ([id])
    );
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261108144118_AddImmediateCheckout'
)
BEGIN
    CREATE TABLE [ImmediatePaymentAttempt] (
        [Id] bigint NOT NULL IDENTITY,
        [CheckoutId] uniqueidentifier NOT NULL,
        [Key] nvarchar(100) NOT NULL,
        [Status] nvarchar(20) NOT NULL,
        [OrderId] int NULL,
        [ErrorCode] nvarchar(100) NULL,
        [CreatedAt] datetime2 NOT NULL,
        CONSTRAINT [PK_ImmediatePaymentAttempt] PRIMARY KEY ([Id]),
        CONSTRAINT [FK_ImmediatePaymentAttempt_ImmediateCheckout_CheckoutId] FOREIGN KEY ([CheckoutId]) REFERENCES [ImmediateCheckout] ([Id]),
        CONSTRAINT [FK_ImmediatePaymentAttempt_OrderTable_OrderId] FOREIGN KEY ([OrderId]) REFERENCES [OrderTable] ([id])
    );
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261108144118_AddImmediateCheckout'
)
BEGIN
    CREATE INDEX [IX_ImmediateCheckout_BuyerId_CreatedAt] ON [ImmediateCheckout] ([BuyerId], [CreatedAt]);
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261108144118_AddImmediateCheckout'
)
BEGIN
    EXEC(N'CREATE UNIQUE INDEX [IX_ImmediateCheckout_OrderId] ON [ImmediateCheckout] ([OrderId]) WHERE [OrderId] IS NOT NULL');
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261108144118_AddImmediateCheckout'
)
BEGIN
    CREATE INDEX [IX_ImmediatePaymentAttempt_CheckoutId] ON [ImmediatePaymentAttempt] ([CheckoutId]);
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261108144118_AddImmediateCheckout'
)
BEGIN
    CREATE UNIQUE INDEX [IX_ImmediatePaymentAttempt_Key] ON [ImmediatePaymentAttempt] ([Key]);
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261108144118_AddImmediateCheckout'
)
BEGIN
    CREATE INDEX [IX_ImmediatePaymentAttempt_OrderId] ON [ImmediatePaymentAttempt] ([OrderId]);
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261108144118_AddImmediateCheckout'
)
BEGIN
    INSERT INTO [__EFMigrationsHistory] ([MigrationId], [ProductVersion])
    VALUES (N'20261108144118_AddImmediateCheckout', N'8.0.31');
END;
GO

COMMIT;
GO

