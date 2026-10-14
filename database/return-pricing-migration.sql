BEGIN TRANSACTION;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261014061325_AddReasonBasedReturnPricing'
)
BEGIN
    ALTER TABLE [SellerSettlement] ADD [ReturnShippingCost] decimal(18,2) NOT NULL DEFAULT 0.0;
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261014061325_AddReasonBasedReturnPricing'
)
BEGIN
    ALTER TABLE [SellerSettlement] ADD [ReturnShippingRecovery] decimal(18,2) NOT NULL DEFAULT 0.0;
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261014061325_AddReasonBasedReturnPricing'
)
BEGIN
    ALTER TABLE [ReturnRequest] ADD [BuyerRefundAmount] decimal(18,2) NOT NULL DEFAULT 0.0;
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261014061325_AddReasonBasedReturnPricing'
)
BEGIN
    ALTER TABLE [ReturnRequest] ADD [BuyerReturnShippingFee] decimal(18,2) NOT NULL DEFAULT 0.0;
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261014061325_AddReasonBasedReturnPricing'
)
BEGIN
    ALTER TABLE [ReturnRequest] ADD [DeliveryAddressSnapshot] nvarchar(500) NULL;
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261014061325_AddReasonBasedReturnPricing'
)
BEGIN
    ALTER TABLE [ReturnRequest] ADD [Description] nvarchar(2000) NULL;
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261014061325_AddReasonBasedReturnPricing'
)
BEGIN
    ALTER TABLE [ReturnRequest] ADD [GoodsPaidSnapshot] decimal(18,2) NOT NULL DEFAULT 0.0;
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261014061325_AddReasonBasedReturnPricing'
)
BEGIN
    ALTER TABLE [ReturnRequest] ADD [OutboundShippingRetained] decimal(18,2) NOT NULL DEFAULT 0.0;
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261014061325_AddReasonBasedReturnPricing'
)
BEGIN
    ALTER TABLE [ReturnRequest] ADD [PickupAddressSnapshot] nvarchar(500) NULL;
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261014061325_AddReasonBasedReturnPricing'
)
BEGIN
    ALTER TABLE [ReturnRequest] ADD [PlatformSubsidyReversal] decimal(18,2) NOT NULL DEFAULT 0.0;
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261014061325_AddReasonBasedReturnPricing'
)
BEGIN
    ALTER TABLE [ReturnRequest] ADD [PricingFingerprint] nvarchar(64) NULL;
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261014061325_AddReasonBasedReturnPricing'
)
BEGIN
    ALTER TABLE [ReturnRequest] ADD [PricingPolicyVersion] int NOT NULL DEFAULT 0;
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261014061325_AddReasonBasedReturnPricing'
)
BEGIN
    ALTER TABLE [ReturnRequest] ADD [ReasonCode] nvarchar(30) NULL;
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261014061325_AddReasonBasedReturnPricing'
)
BEGIN
    ALTER TABLE [ReturnRequest] ADD [RequestKey] nvarchar(100) NULL;
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261014061325_AddReasonBasedReturnPricing'
)
BEGIN
    ALTER TABLE [ReturnRequest] ADD [ReturnShippingFee] decimal(18,2) NOT NULL DEFAULT 0.0;
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261014061325_AddReasonBasedReturnPricing'
)
BEGIN
    ALTER TABLE [ReturnRequest] ADD [SellerGrossRefund] decimal(18,2) NOT NULL DEFAULT 0.0;
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261014061325_AddReasonBasedReturnPricing'
)
BEGIN
    ALTER TABLE [ReturnRequest] ADD [ShippingCostChargedAt] datetime2 NULL;
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261014061325_AddReasonBasedReturnPricing'
)
BEGIN
    ALTER TABLE [ReturnRequest] ADD [ShippingPayer] nvarchar(20) NULL;
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261014061325_AddReasonBasedReturnPricing'
)
BEGIN
    ALTER TABLE [ReturnRequest] ADD [WeightKgSnapshot] decimal(18,3) NULL;
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261014061325_AddReasonBasedReturnPricing'
)
BEGIN
    ALTER TABLE [Refund] ADD [BuyerReturnShippingRecovery] decimal(18,2) NOT NULL DEFAULT 0.0;
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261014061325_AddReasonBasedReturnPricing'
)
BEGIN
    ALTER TABLE [Refund] ADD [PlatformSubsidyReversalAmount] decimal(18,2) NOT NULL DEFAULT 0.0;
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261014061325_AddReasonBasedReturnPricing'
)
BEGIN
    ALTER TABLE [Refund] ADD [ReturnPolicyVersion] int NOT NULL DEFAULT 0;
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261014061325_AddReasonBasedReturnPricing'
)
BEGIN
    ALTER TABLE [Refund] ADD [SellerGrossRefundAmount] decimal(18,2) NOT NULL DEFAULT 0.0;
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261014061325_AddReasonBasedReturnPricing'
)
BEGIN
    EXEC(N'CREATE UNIQUE INDEX [IX_ReturnRequest_RequestKey] ON [ReturnRequest] ([RequestKey]) WHERE [RequestKey] IS NOT NULL');
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261014061325_AddReasonBasedReturnPricing'
)
BEGIN
    INSERT INTO [__EFMigrationsHistory] ([MigrationId], [ProductVersion])
    VALUES (N'20261014061325_AddReasonBasedReturnPricing', N'8.0.31');
END;
GO

COMMIT;
GO

