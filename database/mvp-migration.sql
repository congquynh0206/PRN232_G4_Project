IF OBJECT_ID(N'[__EFMigrationsHistory]') IS NULL
BEGIN
    CREATE TABLE [__EFMigrationsHistory] (
        [MigrationId] nvarchar(150) NOT NULL,
        [ProductVersion] nvarchar(32) NOT NULL,
        CONSTRAINT [PK___EFMigrationsHistory] PRIMARY KEY ([MigrationId])
    );
END;
GO

BEGIN TRANSACTION;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260916032502_InitialCreate'
)
BEGIN
    INSERT INTO [__EFMigrationsHistory] ([MigrationId], [ProductVersion])
    VALUES (N'20260916032502_InitialCreate', N'8.0.31');
END;
GO

COMMIT;
GO

BEGIN TRANSACTION;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260924112602_AddCheckoutShippingMvp'
)
BEGIN
    ALTER TABLE [ShippingInfo] ADD [CreatedAt] datetime2 NOT NULL DEFAULT '0001-01-01T00:00:00.0000000';
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260924112602_AddCheckoutShippingMvp'
)
BEGIN
    ALTER TABLE [ShippingInfo] ADD [DeliveredAt] datetime2 NULL;
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260924112602_AddCheckoutShippingMvp'
)
BEGIN
    ALTER TABLE [ShippingInfo] ADD [DeliveryAttempts] int NOT NULL DEFAULT 0;
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260924112602_AddCheckoutShippingMvp'
)
BEGIN
    ALTER TABLE [ShippingInfo] ADD [Direction] nvarchar(20) NOT NULL DEFAULT N'';
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260924112602_AddCheckoutShippingMvp'
)
BEGIN
    ALTER TABLE [ShippingInfo] ADD [FailureReason] nvarchar(max) NULL;
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260924112602_AddCheckoutShippingMvp'
)
BEGIN
    ALTER TABLE [ShippingInfo] ADD [IdempotencyKey] nvarchar(100) NULL;
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260924112602_AddCheckoutShippingMvp'
)
BEGIN
    ALTER TABLE [ReturnRequest] ADD [DecidedAt] datetime2 NULL;
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260924112602_AddCheckoutShippingMvp'
)
BEGIN
    ALTER TABLE [ReturnRequest] ADD [DecisionReason] nvarchar(max) NULL;
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260924112602_AddCheckoutShippingMvp'
)
BEGIN
    ALTER TABLE [ReturnRequest] ADD [ReceivedAt] datetime2 NULL;
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260924112602_AddCheckoutShippingMvp'
)
BEGIN
    ALTER TABLE [ReturnRequest] ADD [RefundId] int NULL;
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260924112602_AddCheckoutShippingMvp'
)
BEGIN
    ALTER TABLE [ReturnRequest] ADD [ReturnDeadline] datetime2 NULL;
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260924112602_AddCheckoutShippingMvp'
)
BEGIN
    ALTER TABLE [Payment] ADD [CreatedAt] datetime2 NOT NULL DEFAULT '0001-01-01T00:00:00.0000000';
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260924112602_AddCheckoutShippingMvp'
)
BEGIN
    ALTER TABLE [Payment] ADD [ErrorCode] nvarchar(100) NULL;
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260924112602_AddCheckoutShippingMvp'
)
BEGIN
    ALTER TABLE [Payment] ADD [IdempotencyKey] nvarchar(100) NULL;
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260924112602_AddCheckoutShippingMvp'
)
BEGIN
    ALTER TABLE [Payment] ADD [ProviderOrderId] nvarchar(100) NULL;
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260924112602_AddCheckoutShippingMvp'
)
BEGIN
    ALTER TABLE [Payment] ADD [ProviderTransactionId] nvarchar(100) NULL;
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260924112602_AddCheckoutShippingMvp'
)
BEGIN
    ALTER TABLE [Payment] ADD [UpdatedAt] datetime2 NULL;
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260924112602_AddCheckoutShippingMvp'
)
BEGIN
    ALTER TABLE [OrderTable] ADD [AddressSnapshot] nvarchar(max) NULL;
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260924112602_AddCheckoutShippingMvp'
)
BEGIN
    ALTER TABLE [OrderTable] ADD [CheckoutKey] nvarchar(100) NULL;
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260924112602_AddCheckoutShippingMvp'
)
BEGIN
    ALTER TABLE [OrderTable] ADD [CouponCode] nvarchar(50) NULL;
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260924112602_AddCheckoutShippingMvp'
)
BEGIN
    ALTER TABLE [OrderTable] ADD [Currency] nvarchar(3) NOT NULL DEFAULT N'';
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260924112602_AddCheckoutShippingMvp'
)
BEGIN
    ALTER TABLE [OrderTable] ADD [DiscountAmount] decimal(10,2) NOT NULL DEFAULT 0.0;
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260924112602_AddCheckoutShippingMvp'
)
BEGIN
    ALTER TABLE [OrderTable] ADD [PaymentExpiresAt] datetime2 NULL;
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260924112602_AddCheckoutShippingMvp'
)
BEGIN
    ALTER TABLE [OrderTable] ADD [SellerId] int NULL;
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260924112602_AddCheckoutShippingMvp'
)
BEGIN
    ALTER TABLE [OrderTable] ADD [ShippingFee] decimal(10,2) NOT NULL DEFAULT 0.0;
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260924112602_AddCheckoutShippingMvp'
)
BEGIN
    ALTER TABLE [OrderTable] ADD [Subtotal] decimal(10,2) NOT NULL DEFAULT 0.0;
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260924112602_AddCheckoutShippingMvp'
)
BEGIN
    ALTER TABLE [OrderItem] ADD [ProductTitleSnapshot] nvarchar(255) NULL;
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260924112602_AddCheckoutShippingMvp'
)
BEGIN
    ALTER TABLE [OrderItem] ADD [SellerIdSnapshot] int NULL;
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260924112602_AddCheckoutShippingMvp'
)
BEGIN
    CREATE TABLE [NotificationOutbox] (
        [Id] int NOT NULL IDENTITY,
        [OrderId] int NOT NULL,
        [EventType] nvarchar(50) NOT NULL,
        [Recipient] nvarchar(100) NOT NULL,
        [Subject] nvarchar(max) NOT NULL,
        [Body] nvarchar(max) NOT NULL,
        [Status] nvarchar(20) NOT NULL,
        [Attempts] int NOT NULL,
        [CreatedAt] datetime2 NOT NULL,
        [SentAt] datetime2 NULL,
        CONSTRAINT [PK_NotificationOutbox] PRIMARY KEY ([Id])
    );
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260924112602_AddCheckoutShippingMvp'
)
BEGIN
    CREATE TABLE [Refund] (
        [Id] int NOT NULL IDENTITY,
        [OrderId] int NOT NULL,
        [PaymentId] int NOT NULL,
        [ReturnRequestId] int NULL,
        [Amount] decimal(10,2) NOT NULL,
        [Currency] nvarchar(3) NOT NULL,
        [Reason] nvarchar(100) NOT NULL,
        [ProviderRefundId] nvarchar(max) NULL,
        [IdempotencyKey] nvarchar(450) NOT NULL,
        [Status] nvarchar(20) NOT NULL,
        [CreatedAt] datetime2 NOT NULL,
        [CompletedAt] datetime2 NULL,
        CONSTRAINT [PK_Refund] PRIMARY KEY ([Id]),
        CONSTRAINT [FK_Refund_OrderTable_OrderId] FOREIGN KEY ([OrderId]) REFERENCES [OrderTable] ([id]) ON DELETE CASCADE,
        CONSTRAINT [FK_Refund_Payment_PaymentId] FOREIGN KEY ([PaymentId]) REFERENCES [Payment] ([id])
    );
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260924112602_AddCheckoutShippingMvp'
)
BEGIN
    CREATE TABLE [ShippingEvent] (
        [Id] int NOT NULL IDENTITY,
        [ShippingInfoId] int NOT NULL,
        [ExternalEventId] nvarchar(100) NOT NULL,
        [Status] nvarchar(50) NOT NULL,
        [Location] nvarchar(100) NULL,
        [Note] nvarchar(max) NULL,
        [OccurredAt] datetime2 NOT NULL,
        [ReceivedAt] datetime2 NOT NULL,
        CONSTRAINT [PK_ShippingEvent] PRIMARY KEY ([Id]),
        CONSTRAINT [FK_ShippingEvent_ShippingInfo_ShippingInfoId] FOREIGN KEY ([ShippingInfoId]) REFERENCES [ShippingInfo] ([id]) ON DELETE CASCADE
    );
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260924112602_AddCheckoutShippingMvp'
)
BEGIN
    EXEC(N'CREATE UNIQUE INDEX [IX_ShippingInfo_IdempotencyKey] ON [ShippingInfo] ([IdempotencyKey]) WHERE [IdempotencyKey] IS NOT NULL');
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260924112602_AddCheckoutShippingMvp'
)
BEGIN
    EXEC(N'CREATE UNIQUE INDEX [IX_ShippingInfo_trackingNumber] ON [ShippingInfo] ([trackingNumber]) WHERE [trackingNumber] IS NOT NULL');
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260924112602_AddCheckoutShippingMvp'
)
BEGIN
    EXEC(N'CREATE UNIQUE INDEX [IX_Payment_IdempotencyKey] ON [Payment] ([IdempotencyKey]) WHERE [IdempotencyKey] IS NOT NULL');
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260924112602_AddCheckoutShippingMvp'
)
BEGIN
    EXEC(N'CREATE UNIQUE INDEX [IX_Payment_ProviderTransactionId] ON [Payment] ([ProviderTransactionId]) WHERE [ProviderTransactionId] IS NOT NULL');
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260924112602_AddCheckoutShippingMvp'
)
BEGIN
    EXEC(N'CREATE UNIQUE INDEX [IX_OrderTable_CheckoutKey] ON [OrderTable] ([CheckoutKey]) WHERE [CheckoutKey] IS NOT NULL');
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260924112602_AddCheckoutShippingMvp'
)
BEGIN
    CREATE UNIQUE INDEX [IX_NotificationOutbox_OrderId_EventType] ON [NotificationOutbox] ([OrderId], [EventType]);
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260924112602_AddCheckoutShippingMvp'
)
BEGIN
    CREATE UNIQUE INDEX [IX_Refund_IdempotencyKey] ON [Refund] ([IdempotencyKey]);
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260924112602_AddCheckoutShippingMvp'
)
BEGIN
    CREATE INDEX [IX_Refund_OrderId] ON [Refund] ([OrderId]);
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260924112602_AddCheckoutShippingMvp'
)
BEGIN
    CREATE INDEX [IX_Refund_PaymentId] ON [Refund] ([PaymentId]);
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260924112602_AddCheckoutShippingMvp'
)
BEGIN
    CREATE UNIQUE INDEX [IX_ShippingEvent_ExternalEventId] ON [ShippingEvent] ([ExternalEventId]);
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260924112602_AddCheckoutShippingMvp'
)
BEGIN
    CREATE INDEX [IX_ShippingEvent_ShippingInfoId] ON [ShippingEvent] ([ShippingInfoId]);
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260924112602_AddCheckoutShippingMvp'
)
BEGIN
    INSERT INTO [__EFMigrationsHistory] ([MigrationId], [ProductVersion])
    VALUES (N'20260924112602_AddCheckoutShippingMvp', N'8.0.31');
END;
GO

COMMIT;
GO

BEGIN TRANSACTION;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260924124145_AddCancellationDecision'
)
BEGIN
    ALTER TABLE [OrderTable] ADD [CancelDecisionReason] nvarchar(500) NULL;
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260924124145_AddCancellationDecision'
)
BEGIN
    ALTER TABLE [OrderTable] ADD [CancelPreviousStatus] nvarchar(20) NULL;
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260924124145_AddCancellationDecision'
)
BEGIN
    INSERT INTO [__EFMigrationsHistory] ([MigrationId], [ProductVersion])
    VALUES (N'20260924124145_AddCancellationDecision', N'8.0.31');
END;
GO

COMMIT;
GO

