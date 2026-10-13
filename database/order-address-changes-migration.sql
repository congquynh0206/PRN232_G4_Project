BEGIN TRANSACTION;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261013091245_AddOrderAddressChanges'
)
BEGIN
    ALTER TABLE [ShippingInfo] ADD [AddressChangeAcknowledgedAt] datetime2 NULL;
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261013091245_AddOrderAddressChanges'
)
BEGIN
    ALTER TABLE [ShippingInfo] ADD [AddressChangeRequestId] int NULL;
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261013091245_AddOrderAddressChanges'
)
BEGIN
    ALTER TABLE [SellerSettlement] ADD [ShippingAdjustmentCost] decimal(18,2) NOT NULL DEFAULT 0.0;
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261013091245_AddOrderAddressChanges'
)
BEGIN
    ALTER TABLE [OrderTable] ADD [AddressChangeCount] int NOT NULL DEFAULT 0;
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261013091245_AddOrderAddressChanges'
)
BEGIN
    ALTER TABLE [OrderTable] ADD [DeliveryCountrySnapshot] nvarchar(50) NULL;
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261013091245_AddOrderAddressChanges'
)
BEGIN
    ALTER TABLE [OrderTable] ADD [DeliveryStateSnapshot] nvarchar(50) NULL;
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261013091245_AddOrderAddressChanges'
)
BEGIN
    ALTER TABLE [OrderTable] ADD [PickupCountrySnapshot] nvarchar(50) NULL;
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261013091245_AddOrderAddressChanges'
)
BEGIN
    ALTER TABLE [OrderTable] ADD [PickupStateSnapshot] nvarchar(50) NULL;
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261013091245_AddOrderAddressChanges'
)
BEGIN
    CREATE TABLE [AddressChangeRequest] (
        [Id] int NOT NULL IDENTITY,
        [OrderId] int NOT NULL,
        [RequestKey] nvarchar(100) NOT NULL,
        [Reason] nvarchar(1000) NOT NULL,
        [OldAddress] nvarchar(500) NOT NULL,
        [NewAddress] nvarchar(500) NOT NULL,
        [NewState] nvarchar(50) NOT NULL,
        [NewCountry] nvarchar(50) NOT NULL,
        [OldShippingCost] decimal(18,2) NOT NULL,
        [NewShippingCost] decimal(18,2) NOT NULL,
        [SellerCap] decimal(18,2) NOT NULL,
        [BuyerRefund] decimal(18,2) NOT NULL,
        [RefundId] int NULL,
        [Status] nvarchar(20) NOT NULL,
        [DecisionReason] nvarchar(1000) NULL,
        [CreatedAt] datetime2 NOT NULL,
        [DecidedAt] datetime2 NULL,
        CONSTRAINT [PK_AddressChangeRequest] PRIMARY KEY ([Id]),
        CONSTRAINT [FK_AddressChangeRequest_OrderTable_OrderId] FOREIGN KEY ([OrderId]) REFERENCES [OrderTable] ([id]),
        CONSTRAINT [FK_AddressChangeRequest_Refund_RefundId] FOREIGN KEY ([RefundId]) REFERENCES [Refund] ([Id])
    );
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261013091245_AddOrderAddressChanges'
)
BEGIN
    CREATE TABLE [SellerShippingPreference] (
        [SellerId] int NOT NULL,
        [AddressChangeCap] decimal(18,2) NOT NULL,
        CONSTRAINT [PK_SellerShippingPreference] PRIMARY KEY ([SellerId]),
        CONSTRAINT [FK_SellerShippingPreference_User_SellerId] FOREIGN KEY ([SellerId]) REFERENCES [User] ([id])
    );
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261013091245_AddOrderAddressChanges'
)
BEGIN
    CREATE INDEX [IX_ShippingInfo_AddressChangeRequestId] ON [ShippingInfo] ([AddressChangeRequestId]);
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261013091245_AddOrderAddressChanges'
)
BEGIN
    EXEC(N'CREATE UNIQUE INDEX [IX_AddressChangeRequest_OrderId] ON [AddressChangeRequest] ([OrderId]) WHERE [Status] = ''Pending''');
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261013091245_AddOrderAddressChanges'
)
BEGIN
    CREATE INDEX [IX_AddressChangeRequest_RefundId] ON [AddressChangeRequest] ([RefundId]);
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261013091245_AddOrderAddressChanges'
)
BEGIN
    CREATE UNIQUE INDEX [IX_AddressChangeRequest_RequestKey] ON [AddressChangeRequest] ([RequestKey]);
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261013091245_AddOrderAddressChanges'
)
BEGIN
    ALTER TABLE [ShippingInfo] ADD CONSTRAINT [FK_ShippingInfo_AddressChangeRequest_AddressChangeRequestId] FOREIGN KEY ([AddressChangeRequestId]) REFERENCES [AddressChangeRequest] ([Id]);
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261013091245_AddOrderAddressChanges'
)
BEGIN
    INSERT INTO [__EFMigrationsHistory] ([MigrationId], [ProductVersion])
    VALUES (N'20261013091245_AddOrderAddressChanges', N'8.0.31');
END;
GO

COMMIT;
GO

