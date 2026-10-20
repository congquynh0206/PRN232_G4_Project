BEGIN TRANSACTION;
GO

DROP INDEX [IX_AddressChangeRequest_OrderId] ON [AddressChangeRequest];
GO

ALTER TABLE [SellerShippingPreference] ADD [Revision] int NOT NULL DEFAULT 0;
GO

ALTER TABLE [AddressChangeRequest] ADD [AppliedAt] datetime2 NULL;
GO

ALTER TABLE [AddressChangeRequest] ADD [BuyerConfirmedAt] datetime2 NULL;
GO

ALTER TABLE [AddressChangeRequest] ADD [BuyerUserId] int NULL;
GO

ALTER TABLE [AddressChangeRequest] ADD [CapRevision] int NOT NULL DEFAULT 0;
GO

ALTER TABLE [AddressChangeRequest] ADD [ExpectedShipperId] int NULL;
GO

ALTER TABLE [AddressChangeRequest] ADD [ExpiresAt] datetime2 NULL;
GO

ALTER TABLE [AddressChangeRequest] ADD [IsPending] bit NOT NULL DEFAULT CAST(0 AS bit);
GO

ALTER TABLE [AddressChangeRequest] ADD [QuoteToken] nvarchar(64) NULL;
GO

ALTER TABLE [AddressChangeRequest] ADD [SellerConfirmedAt] datetime2 NULL;
GO

ALTER TABLE [AddressChangeRequest] ADD [SellerUserId] int NULL;
GO

ALTER TABLE [AddressChangeRequest] ADD [ShipperDecidedAt] datetime2 NULL;
GO

ALTER TABLE [AddressChangeRequest] ADD [ShipperUserId] int NULL;
GO

ALTER TABLE [AddressChangeRequest] ADD [WorkflowVersion] int NOT NULL DEFAULT 0;
GO

UPDATE [AddressChangeRequest] SET [IsPending] = 1, [ExpiresAt] = DATEADD(day, 1, [CreatedAt]) WHERE [Status] = N'Pending';
GO

CREATE INDEX [IX_AddressChangeRequest_IsPending_ExpiresAt] ON [AddressChangeRequest] ([IsPending], [ExpiresAt]);
GO

CREATE UNIQUE INDEX [IX_AddressChangeRequest_OrderId] ON [AddressChangeRequest] ([OrderId]) WHERE [IsPending] = 1;
GO

INSERT INTO [__EFMigrationsHistory] ([MigrationId], [ProductVersion])
VALUES (N'20261020153620_AddAddressChangeConfirmations', N'8.0.31');
GO

COMMIT;
GO

