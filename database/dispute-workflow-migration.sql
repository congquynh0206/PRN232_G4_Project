BEGIN TRANSACTION;
GO


IF EXISTS (
    SELECT 1 FROM sys.indexes
    WHERE [name] = N'IX_Dispute_orderId'
      AND [object_id] = OBJECT_ID(N'[Dispute]')
)
    DROP INDEX [IX_Dispute_orderId] ON [Dispute];
GO

ALTER TABLE [Dispute] ADD [BuyerResponseDueAt] datetime2 NULL;
GO

ALTER TABLE [Dispute] ADD [ClosedAt] datetime2 NULL;
GO

ALTER TABLE [Dispute] ADD [CreatedAt] datetime2 NOT NULL DEFAULT '0001-01-01T00:00:00.0000000';
GO

ALTER TABLE [Dispute] ADD [EscalatedAt] datetime2 NULL;
GO

ALTER TABLE [Dispute] ADD [IsOpen] bit NOT NULL DEFAULT CAST(0 AS bit);
GO

ALTER TABLE [Dispute] ADD [Outcome] nvarchar(30) NULL;
GO

ALTER TABLE [Dispute] ADD [Proposal] nvarchar(30) NULL;
GO

ALTER TABLE [Dispute] ADD [RowVersion] rowversion NOT NULL;
GO

ALTER TABLE [Dispute] ADD [SellerRespondedAt] datetime2 NULL;
GO

ALTER TABLE [Dispute] ADD [SellerResponseDueAt] datetime2 NOT NULL DEFAULT '0001-01-01T00:00:00.0000000';
GO

ALTER TABLE [Dispute] ADD [UpdatedAt] datetime2 NOT NULL DEFAULT '0001-01-01T00:00:00.0000000';
GO

ALTER TABLE [Dispute] ADD [WorkflowEnabled] bit NOT NULL DEFAULT CAST(0 AS bit);
GO

CREATE TABLE [DisputeEntry] (
    [Id] int NOT NULL IDENTITY,
    [DisputeId] int NOT NULL,
    [ActorId] int NULL,
    [ActorRole] nvarchar(20) NOT NULL,
    [Kind] nvarchar(30) NOT NULL,
    [Description] nvarchar(4000) NOT NULL,
    [EvidenceLinksJson] nvarchar(max) NOT NULL,
    [CreatedAt] datetime2 NOT NULL,
    CONSTRAINT [PK_DisputeEntry] PRIMARY KEY ([Id]),
    CONSTRAINT [FK_DisputeEntry_Dispute_DisputeId] FOREIGN KEY ([DisputeId]) REFERENCES [Dispute] ([id]),
    CONSTRAINT [FK_DisputeEntry_User_ActorId] FOREIGN KEY ([ActorId]) REFERENCES [User] ([id])
);
GO

CREATE UNIQUE INDEX [IX_Dispute_orderId] ON [Dispute] ([orderId]) WHERE [IsOpen] = 1 AND [WorkflowEnabled] = 1 AND [orderId] IS NOT NULL;
GO

CREATE INDEX [IX_Dispute_WorkflowEnabled_status_SellerResponseDueAt] ON [Dispute] ([WorkflowEnabled], [status], [SellerResponseDueAt]);
GO

CREATE INDEX [IX_DisputeEntry_ActorId] ON [DisputeEntry] ([ActorId]);
GO

CREATE INDEX [IX_DisputeEntry_DisputeId_Id] ON [DisputeEntry] ([DisputeId], [Id]);
GO

INSERT INTO [__EFMigrationsHistory] ([MigrationId], [ProductVersion])
VALUES (N'20261003035220_AddDisputeWorkflow', N'8.0.31');
GO

COMMIT;
GO

