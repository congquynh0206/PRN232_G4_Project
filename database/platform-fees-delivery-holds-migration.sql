BEGIN TRANSACTION;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261014051607_AddPlatformFeePoliciesAndDeliveryHolds'
)
BEGIN
    DECLARE @var0 sysname;
    SELECT @var0 = [d].[name]
    FROM [sys].[default_constraints] [d]
    INNER JOIN [sys].[columns] [c] ON [d].[parent_column_id] = [c].[column_id] AND [d].[parent_object_id] = [c].[object_id]
    WHERE ([d].[parent_object_id] = OBJECT_ID(N'[SellerSettlement]') AND [c].[name] = N'ReleaseAt');
    IF @var0 IS NOT NULL EXEC(N'ALTER TABLE [SellerSettlement] DROP CONSTRAINT [' + @var0 + '];');
    ALTER TABLE [SellerSettlement] ALTER COLUMN [ReleaseAt] datetime2 NULL;
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261014051607_AddPlatformFeePoliciesAndDeliveryHolds'
)
BEGIN
    ALTER TABLE [SellerSettlement] ADD [FeePercentSnapshot] decimal(5,2) NULL;
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261014051607_AddPlatformFeePoliciesAndDeliveryHolds'
)
BEGIN
    ALTER TABLE [SellerSettlement] ADD [FeePolicyId] int NULL;
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261014051607_AddPlatformFeePoliciesAndDeliveryHolds'
)
BEGIN
    ALTER TABLE [SellerSettlement] ADD [HoldPolicyVersion] int NOT NULL DEFAULT 0;
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261014051607_AddPlatformFeePoliciesAndDeliveryHolds'
)
BEGIN
    ALTER TABLE [SellerSettlement] ADD [HoldSecondsSnapshot] int NOT NULL DEFAULT 0;
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261014051607_AddPlatformFeePoliciesAndDeliveryHolds'
)
BEGIN
    ALTER TABLE [SellerSettlement] ADD [HoldStartedAt] datetime2 NULL;
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261014051607_AddPlatformFeePoliciesAndDeliveryHolds'
)
BEGIN
    ALTER TABLE [SellerSettlement] ADD [SellerLevelSnapshot] int NOT NULL DEFAULT 0;
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261014051607_AddPlatformFeePoliciesAndDeliveryHolds'
)
BEGIN
    CREATE TABLE [PlatformFeePolicy] (
        [Id] int NOT NULL IDENTITY,
        [FeePercent] decimal(5,2) NOT NULL,
        [FixedFee] decimal(18,2) NOT NULL,
        [PreviousFeePercent] decimal(5,2) NOT NULL,
        [PreviousFixedFee] decimal(18,2) NOT NULL,
        [EffectiveAt] datetime2 NOT NULL,
        [CreatedAt] datetime2 NOT NULL,
        [ChangedById] int NULL,
        [Reason] nvarchar(1000) NOT NULL,
        [RequestKey] nvarchar(100) NOT NULL,
        CONSTRAINT [PK_PlatformFeePolicy] PRIMARY KEY ([Id]),
        CONSTRAINT [FK_PlatformFeePolicy_User_ChangedById] FOREIGN KEY ([ChangedById]) REFERENCES [User] ([id])
    );
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261014051607_AddPlatformFeePoliciesAndDeliveryHolds'
)
BEGIN
    IF EXISTS (SELECT * FROM [sys].[identity_columns] WHERE [name] IN (N'Id', N'ChangedById', N'CreatedAt', N'EffectiveAt', N'FeePercent', N'FixedFee', N'PreviousFeePercent', N'PreviousFixedFee', N'Reason', N'RequestKey') AND [object_id] = OBJECT_ID(N'[PlatformFeePolicy]'))
        SET IDENTITY_INSERT [PlatformFeePolicy] ON;
    EXEC(N'INSERT INTO [PlatformFeePolicy] ([Id], [ChangedById], [CreatedAt], [EffectiveAt], [FeePercent], [FixedFee], [PreviousFeePercent], [PreviousFixedFee], [Reason], [RequestKey])
    VALUES (1, NULL, ''2000-01-01T00:00:00.0000000Z'', ''2000-01-01T00:00:00.0000000Z'', 12.0, 0.3, 12.0, 0.3, N''Mức phí mặc định ban đầu'', N''initial-platform-fee'')');
    IF EXISTS (SELECT * FROM [sys].[identity_columns] WHERE [name] IN (N'Id', N'ChangedById', N'CreatedAt', N'EffectiveAt', N'FeePercent', N'FixedFee', N'PreviousFeePercent', N'PreviousFixedFee', N'Reason', N'RequestKey') AND [object_id] = OBJECT_ID(N'[PlatformFeePolicy]'))
        SET IDENTITY_INSERT [PlatformFeePolicy] OFF;
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261014051607_AddPlatformFeePoliciesAndDeliveryHolds'
)
BEGIN
    CREATE INDEX [IX_SellerSettlement_FeePolicyId] ON [SellerSettlement] ([FeePolicyId]);
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261014051607_AddPlatformFeePoliciesAndDeliveryHolds'
)
BEGIN
    CREATE INDEX [IX_PlatformFeePolicy_ChangedById] ON [PlatformFeePolicy] ([ChangedById]);
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261014051607_AddPlatformFeePoliciesAndDeliveryHolds'
)
BEGIN
    CREATE UNIQUE INDEX [IX_PlatformFeePolicy_EffectiveAt] ON [PlatformFeePolicy] ([EffectiveAt]);
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261014051607_AddPlatformFeePoliciesAndDeliveryHolds'
)
BEGIN
    CREATE UNIQUE INDEX [IX_PlatformFeePolicy_RequestKey] ON [PlatformFeePolicy] ([RequestKey]);
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261014051607_AddPlatformFeePoliciesAndDeliveryHolds'
)
BEGIN
    ALTER TABLE [SellerSettlement] ADD CONSTRAINT [FK_SellerSettlement_PlatformFeePolicy_FeePolicyId] FOREIGN KEY ([FeePolicyId]) REFERENCES [PlatformFeePolicy] ([Id]);
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261014051607_AddPlatformFeePoliciesAndDeliveryHolds'
)
BEGIN
    INSERT INTO [__EFMigrationsHistory] ([MigrationId], [ProductVersion])
    VALUES (N'20261014051607_AddPlatformFeePoliciesAndDeliveryHolds', N'8.0.31');
END;
GO

COMMIT;
GO

