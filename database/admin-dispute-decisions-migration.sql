BEGIN TRANSACTION;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261014091619_AddAdminDisputeDecisions'
)
BEGIN
    ALTER TABLE [Refund] ADD [DisputeDecisionId] int NULL;
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261014091619_AddAdminDisputeDecisions'
)
BEGIN
    CREATE TABLE [DisputeDecision] (
        [Id] int NOT NULL IDENTITY,
        [DisputeId] int NOT NULL,
        [OrderId] int NOT NULL,
        [AdminId] int NOT NULL,
        [Outcome] nvarchar(30) NOT NULL,
        [Responsibility] nvarchar(20) NOT NULL,
        [Reason] nvarchar(4000) NOT NULL,
        [EvidenceEntryIdsJson] nvarchar(2000) NOT NULL,
        [RequestKey] nvarchar(100) NOT NULL,
        [PricingFingerprint] nvarchar(64) NOT NULL,
        [CreatedAt] datetime2 NOT NULL,
        [RequestedAmount] decimal(18,2) NULL,
        [MaximumRefund] decimal(18,2) NOT NULL,
        [BuyerRefund] decimal(18,2) NOT NULL,
        [OutboundShippingRetained] decimal(18,2) NOT NULL,
        [ReturnShippingCost] decimal(18,2) NOT NULL,
        [BuyerShippingRecovery] decimal(18,2) NOT NULL,
        [SubsidyReversal] decimal(18,2) NOT NULL,
        [SellerGrossRefund] decimal(18,2) NOT NULL,
        [FeeCredit] decimal(18,2) NOT NULL,
        [SellerNetDebit] decimal(18,2) NOT NULL,
        [ShippingPayer] nvarchar(20) NULL,
        [Currency] nvarchar(3) NOT NULL,
        CONSTRAINT [PK_DisputeDecision] PRIMARY KEY ([Id]),
        CONSTRAINT [FK_DisputeDecision_Dispute_DisputeId] FOREIGN KEY ([DisputeId]) REFERENCES [Dispute] ([id]),
        CONSTRAINT [FK_DisputeDecision_OrderTable_OrderId] FOREIGN KEY ([OrderId]) REFERENCES [OrderTable] ([id]),
        CONSTRAINT [FK_DisputeDecision_User_AdminId] FOREIGN KEY ([AdminId]) REFERENCES [User] ([id])
    );
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261014091619_AddAdminDisputeDecisions'
)
BEGIN
    CREATE INDEX [IX_Refund_DisputeDecisionId] ON [Refund] ([DisputeDecisionId]);
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261014091619_AddAdminDisputeDecisions'
)
BEGIN
    CREATE INDEX [IX_DisputeDecision_AdminId] ON [DisputeDecision] ([AdminId]);
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261014091619_AddAdminDisputeDecisions'
)
BEGIN
    CREATE UNIQUE INDEX [IX_DisputeDecision_DisputeId] ON [DisputeDecision] ([DisputeId]);
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261014091619_AddAdminDisputeDecisions'
)
BEGIN
    CREATE INDEX [IX_DisputeDecision_OrderId] ON [DisputeDecision] ([OrderId]);
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261014091619_AddAdminDisputeDecisions'
)
BEGIN
    CREATE UNIQUE INDEX [IX_DisputeDecision_RequestKey] ON [DisputeDecision] ([RequestKey]);
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261014091619_AddAdminDisputeDecisions'
)
BEGIN
    ALTER TABLE [Refund] ADD CONSTRAINT [FK_Refund_DisputeDecision_DisputeDecisionId] FOREIGN KEY ([DisputeDecisionId]) REFERENCES [DisputeDecision] ([Id]);
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261014091619_AddAdminDisputeDecisions'
)
BEGIN
    INSERT INTO [__EFMigrationsHistory] ([MigrationId], [ProductVersion])
    VALUES (N'20261014091619_AddAdminDisputeDecisions', N'8.0.31');
END;
GO

COMMIT;
GO

