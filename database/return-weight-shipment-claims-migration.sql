BEGIN TRANSACTION;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261003151225_AddReturnWeightAndShipmentClaims'
)
BEGIN
    ALTER TABLE [ShippingInfo] ADD [ClaimedAt] datetime2 NULL;
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261003151225_AddReturnWeightAndShipmentClaims'
)
BEGIN
    ALTER TABLE [ShippingInfo] ADD [DeliveryAddressSnapshot] nvarchar(500) NULL;
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261003151225_AddReturnWeightAndShipmentClaims'
)
BEGIN
    ALTER TABLE [ShippingInfo] ADD [PickupAddressSnapshot] nvarchar(500) NULL;
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261003151225_AddReturnWeightAndShipmentClaims'
)
BEGIN
    ALTER TABLE [ShippingInfo] ADD [RowVersion] rowversion NOT NULL;
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261003151225_AddReturnWeightAndShipmentClaims'
)
BEGIN
    ALTER TABLE [ShippingInfo] ADD [ShipperId] int NULL;
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261003151225_AddReturnWeightAndShipmentClaims'
)
BEGIN
    ALTER TABLE [ReturnRequest] ADD [ConfirmationDueAt] datetime2 NULL;
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261003151225_AddReturnWeightAndShipmentClaims'
)
BEGIN
    ALTER TABLE [Product] ADD [WeightKg] decimal(10,3) NULL;
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261003151225_AddReturnWeightAndShipmentClaims'
)
BEGIN
    ALTER TABLE [OrderTable] ADD [PickupAddressSnapshot] nvarchar(500) NULL;
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261003151225_AddReturnWeightAndShipmentClaims'
)
BEGIN
    ALTER TABLE [OrderTable] ADD [TotalWeightKg] decimal(18,3) NULL;
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261003151225_AddReturnWeightAndShipmentClaims'
)
BEGIN
    ALTER TABLE [OrderItem] ADD [UnitWeightKgSnapshot] decimal(10,3) NULL;
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261003151225_AddReturnWeightAndShipmentClaims'
)
BEGIN
    CREATE INDEX [IX_ShippingInfo_ShipperId_status] ON [ShippingInfo] ([ShipperId], [status]);
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261003151225_AddReturnWeightAndShipmentClaims'
)
BEGIN
    ALTER TABLE [ShippingInfo] ADD CONSTRAINT [FK_ShippingInfo_User_ShipperId] FOREIGN KEY ([ShipperId]) REFERENCES [User] ([id]);
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261003151225_AddReturnWeightAndShipmentClaims'
)
BEGIN
    INSERT INTO [__EFMigrationsHistory] ([MigrationId], [ProductVersion])
    VALUES (N'20261003151225_AddReturnWeightAndShipmentClaims', N'8.0.31');
END;
GO

COMMIT;
GO

