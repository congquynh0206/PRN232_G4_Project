BEGIN TRANSACTION;
GO

ALTER TABLE [Payment] ADD [CaptureReservedAt] datetime2 NULL;
GO

ALTER TABLE [Payment] ADD [PayPalCheckoutId] bigint NULL;
GO

ALTER TABLE [Payment] ADD [ReservedSellerGross] decimal(18,2) NULL;
GO

CREATE TABLE [CommerceChange] (
    [Id] bigint NOT NULL IDENTITY,
    [Topic] nvarchar(30) NOT NULL,
    [OrderId] int NULL,
    [SellerId] int NULL,
    [BuyerId] int NULL,
    [SellerAccountId] int NULL,
    [ShippingInfoId] int NULL,
    [ShipperId] int NULL,
    [DisputeId] int NULL,
    [CreatedAt] datetime2 NOT NULL,
    [DispatchedAt] datetime2 NULL,
    CONSTRAINT [PK_CommerceChange] PRIMARY KEY ([Id])
);
GO

CREATE TABLE [PayPalCheckout] (
    [Id] bigint NOT NULL IDENTITY,
    [BuyerId] int NOT NULL,
    [CheckoutKey] nvarchar(80) NOT NULL,
    [RequestKey] nvarchar(100) NOT NULL,
    [Status] nvarchar(30) NOT NULL,
    [ProviderOrderId] nvarchar(100) NULL,
    [ApprovalUrl] nvarchar(2000) NULL,
    [LastError] nvarchar(500) NULL,
    [CreatedAt] datetime2 NOT NULL,
    [UpdatedAt] datetime2 NOT NULL,
    CONSTRAINT [PK_PayPalCheckout] PRIMARY KEY ([Id]),
    CONSTRAINT [FK_PayPalCheckout_CheckoutBatch_CheckoutKey] FOREIGN KEY ([CheckoutKey]) REFERENCES [CheckoutBatch] ([Key]),
    CONSTRAINT [FK_PayPalCheckout_User_BuyerId] FOREIGN KEY ([BuyerId]) REFERENCES [User] ([id])
);
GO

CREATE UNIQUE INDEX [IX_Payment_PayPalCheckoutId_orderId] ON [Payment] ([PayPalCheckoutId], [orderId]) WHERE [PayPalCheckoutId] IS NOT NULL;
GO

CREATE INDEX [IX_CommerceChange_DispatchedAt_Id] ON [CommerceChange] ([DispatchedAt], [Id]);
GO

CREATE UNIQUE INDEX [IX_PayPalCheckout_BuyerId_RequestKey] ON [PayPalCheckout] ([BuyerId], [RequestKey]);
GO

CREATE INDEX [IX_PayPalCheckout_CheckoutKey_Status] ON [PayPalCheckout] ([CheckoutKey], [Status]);
GO

CREATE UNIQUE INDEX [IX_PayPalCheckout_ProviderOrderId] ON [PayPalCheckout] ([ProviderOrderId]) WHERE [ProviderOrderId] IS NOT NULL;
GO

ALTER TABLE [Payment] ADD CONSTRAINT [FK_Payment_PayPalCheckout_PayPalCheckoutId] FOREIGN KEY ([PayPalCheckoutId]) REFERENCES [PayPalCheckout] ([Id]);
GO

INSERT INTO [__EFMigrationsHistory] ([MigrationId], [ProductVersion])
VALUES (N'20261015161703_AddPayPalCartAndRealtime', N'8.0.31');
GO

COMMIT;
GO

