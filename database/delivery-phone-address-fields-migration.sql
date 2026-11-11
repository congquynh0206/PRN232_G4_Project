BEGIN TRANSACTION;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261111034557_AddDeliveryPhoneAndAddressChangeFields'
)
BEGIN
    ALTER TABLE [OrderTable] ADD [DeliveryPhoneSnapshot] nvarchar(20) NULL;
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261111034557_AddDeliveryPhoneAndAddressChangeFields'
)
BEGIN
    ALTER TABLE [AddressChangeRequest] ADD [NewCity] nvarchar(50) NULL;
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261111034557_AddDeliveryPhoneAndAddressChangeFields'
)
BEGIN
    ALTER TABLE [AddressChangeRequest] ADD [NewFullName] nvarchar(100) NULL;
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261111034557_AddDeliveryPhoneAndAddressChangeFields'
)
BEGIN
    ALTER TABLE [AddressChangeRequest] ADD [NewPhone] nvarchar(20) NULL;
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261111034557_AddDeliveryPhoneAndAddressChangeFields'
)
BEGIN
    ALTER TABLE [AddressChangeRequest] ADD [NewPostalCode] nvarchar(20) NULL;
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261111034557_AddDeliveryPhoneAndAddressChangeFields'
)
BEGIN
    ALTER TABLE [AddressChangeRequest] ADD [NewStreet] nvarchar(100) NULL;
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261111034557_AddDeliveryPhoneAndAddressChangeFields'
)
BEGIN
    ALTER TABLE [AddressChangeRequest] ADD [OldPhone] nvarchar(20) NULL;
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261111034557_AddDeliveryPhoneAndAddressChangeFields'
)
BEGIN
    ALTER TABLE [AddressChangeRequest] ADD [OldPostalCode] nvarchar(20) NULL;
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261111034557_AddDeliveryPhoneAndAddressChangeFields'
)
BEGIN
    INSERT INTO [__EFMigrationsHistory] ([MigrationId], [ProductVersion])
    VALUES (N'20261111034557_AddDeliveryPhoneAndAddressChangeFields', N'8.0.31');
END;
GO

COMMIT;
GO

