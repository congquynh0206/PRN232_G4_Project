BEGIN TRANSACTION;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260930061058_AddSellerFinance'
)
BEGIN
    CREATE TABLE [SellerAccount] (
        [Id] int NOT NULL IDENTITY,
        [SellerId] int NOT NULL,
        [Level] int NOT NULL,
        [Status] nvarchar(20) NOT NULL,
        [MonthlySalesLimit] decimal(18,2) NOT NULL,
        [HoldDays] int NOT NULL,
        [ProcessingBalance] decimal(18,2) NOT NULL,
        [AvailableBalance] decimal(18,2) NOT NULL,
        [OnHoldBalance] decimal(18,2) NOT NULL,
        [NegativeBalance] decimal(18,2) NOT NULL,
        [MonthlySalesAmount] decimal(18,2) NOT NULL,
        [SalesMonth] datetime2 NOT NULL,
        [UpdatedAt] datetime2 NOT NULL,
        CONSTRAINT [PK_SellerAccount] PRIMARY KEY ([Id]),
        CONSTRAINT [FK_SellerAccount_User_SellerId] FOREIGN KEY ([SellerId]) REFERENCES [User] ([id])
    );
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260930061058_AddSellerFinance'
)
BEGIN
    CREATE TABLE [SellerPayout] (
        [Id] int NOT NULL IDENTITY,
        [SellerAccountId] int NOT NULL,
        [Amount] decimal(18,2) NOT NULL,
        [Currency] nvarchar(3) NOT NULL,
        [Status] nvarchar(20) NOT NULL,
        [IdempotencyKey] nvarchar(100) NOT NULL,
        [DestinationMasked] nvarchar(30) NOT NULL,
        [BankReferenceId] nvarchar(100) NULL,
        [SimulateFailure] bit NOT NULL,
        [CreatedAt] datetime2 NOT NULL,
        [UpdatedAt] datetime2 NOT NULL,
        [CompletedAt] datetime2 NULL,
        CONSTRAINT [PK_SellerPayout] PRIMARY KEY ([Id]),
        CONSTRAINT [FK_SellerPayout_SellerAccount_SellerAccountId] FOREIGN KEY ([SellerAccountId]) REFERENCES [SellerAccount] ([Id])
    );
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260930061058_AddSellerFinance'
)
BEGIN
    CREATE TABLE [SellerSettlement] (
        [Id] int NOT NULL IDENTITY,
        [SellerAccountId] int NOT NULL,
        [SellerId] int NOT NULL,
        [OrderId] int NOT NULL,
        [PaymentId] int NOT NULL,
        [GrossAmount] decimal(18,2) NOT NULL,
        [PlatformFeeAmount] decimal(18,2) NOT NULL,
        [FixedFeeAmount] decimal(18,2) NOT NULL,
        [NetAmount] decimal(18,2) NOT NULL,
        [ProcessingAmount] decimal(18,2) NOT NULL,
        [RefundedAmount] decimal(18,2) NOT NULL,
        [FeeCreditAmount] decimal(18,2) NOT NULL,
        [Currency] nvarchar(3) NOT NULL,
        [Status] nvarchar(30) NOT NULL,
        [ReleaseAt] datetime2 NOT NULL,
        [CreatedAt] datetime2 NOT NULL,
        [ReleasedAt] datetime2 NULL,
        CONSTRAINT [PK_SellerSettlement] PRIMARY KEY ([Id]),
        CONSTRAINT [FK_SellerSettlement_OrderTable_OrderId] FOREIGN KEY ([OrderId]) REFERENCES [OrderTable] ([id]),
        CONSTRAINT [FK_SellerSettlement_Payment_PaymentId] FOREIGN KEY ([PaymentId]) REFERENCES [Payment] ([id]),
        CONSTRAINT [FK_SellerSettlement_SellerAccount_SellerAccountId] FOREIGN KEY ([SellerAccountId]) REFERENCES [SellerAccount] ([Id])
    );
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260930061058_AddSellerFinance'
)
BEGIN
    CREATE TABLE [FinancialTransaction] (
        [Id] bigint NOT NULL IDENTITY,
        [SellerAccountId] int NOT NULL,
        [OrderId] int NULL,
        [SettlementId] int NULL,
        [PayoutId] int NULL,
        [RefundId] int NULL,
        [Type] nvarchar(30) NOT NULL,
        [Bucket] nvarchar(20) NOT NULL,
        [Amount] decimal(18,2) NOT NULL,
        [Currency] nvarchar(3) NOT NULL,
        [EntryKey] nvarchar(100) NOT NULL,
        [Description] nvarchar(500) NULL,
        [CreatedAt] datetime2 NOT NULL,
        CONSTRAINT [PK_FinancialTransaction] PRIMARY KEY ([Id]),
        CONSTRAINT [FK_FinancialTransaction_OrderTable_OrderId] FOREIGN KEY ([OrderId]) REFERENCES [OrderTable] ([id]),
        CONSTRAINT [FK_FinancialTransaction_Refund_RefundId] FOREIGN KEY ([RefundId]) REFERENCES [Refund] ([Id]),
        CONSTRAINT [FK_FinancialTransaction_SellerAccount_SellerAccountId] FOREIGN KEY ([SellerAccountId]) REFERENCES [SellerAccount] ([Id]),
        CONSTRAINT [FK_FinancialTransaction_SellerPayout_PayoutId] FOREIGN KEY ([PayoutId]) REFERENCES [SellerPayout] ([Id]),
        CONSTRAINT [FK_FinancialTransaction_SellerSettlement_SettlementId] FOREIGN KEY ([SettlementId]) REFERENCES [SellerSettlement] ([Id])
    );
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260930061058_AddSellerFinance'
)
BEGIN
    CREATE UNIQUE INDEX [IX_FinancialTransaction_EntryKey] ON [FinancialTransaction] ([EntryKey]);
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260930061058_AddSellerFinance'
)
BEGIN
    CREATE INDEX [IX_FinancialTransaction_OrderId] ON [FinancialTransaction] ([OrderId]);
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260930061058_AddSellerFinance'
)
BEGIN
    CREATE INDEX [IX_FinancialTransaction_PayoutId] ON [FinancialTransaction] ([PayoutId]);
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260930061058_AddSellerFinance'
)
BEGIN
    CREATE INDEX [IX_FinancialTransaction_RefundId] ON [FinancialTransaction] ([RefundId]);
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260930061058_AddSellerFinance'
)
BEGIN
    CREATE INDEX [IX_FinancialTransaction_SellerAccountId_CreatedAt] ON [FinancialTransaction] ([SellerAccountId], [CreatedAt]);
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260930061058_AddSellerFinance'
)
BEGIN
    CREATE INDEX [IX_FinancialTransaction_SettlementId] ON [FinancialTransaction] ([SettlementId]);
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260930061058_AddSellerFinance'
)
BEGIN
    CREATE UNIQUE INDEX [IX_SellerAccount_SellerId] ON [SellerAccount] ([SellerId]);
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260930061058_AddSellerFinance'
)
BEGIN
    CREATE UNIQUE INDEX [IX_SellerPayout_IdempotencyKey] ON [SellerPayout] ([IdempotencyKey]);
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260930061058_AddSellerFinance'
)
BEGIN
    CREATE INDEX [IX_SellerPayout_SellerAccountId] ON [SellerPayout] ([SellerAccountId]);
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260930061058_AddSellerFinance'
)
BEGIN
    CREATE UNIQUE INDEX [IX_SellerSettlement_OrderId] ON [SellerSettlement] ([OrderId]);
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260930061058_AddSellerFinance'
)
BEGIN
    CREATE UNIQUE INDEX [IX_SellerSettlement_PaymentId] ON [SellerSettlement] ([PaymentId]);
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260930061058_AddSellerFinance'
)
BEGIN
    CREATE INDEX [IX_SellerSettlement_SellerAccountId] ON [SellerSettlement] ([SellerAccountId]);
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260930061058_AddSellerFinance'
)
BEGIN
    INSERT INTO [__EFMigrationsHistory] ([MigrationId], [ProductVersion])
    VALUES (N'20260930061058_AddSellerFinance', N'8.0.31');
END;
GO

COMMIT;
GO

