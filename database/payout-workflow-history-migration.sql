BEGIN TRANSACTION;
GO

ALTER TABLE [SellerPayout] ADD [WorkflowVersion] int NOT NULL DEFAULT 0;
GO

CREATE TABLE [SellerPayoutEvent] (
    [Id] bigint NOT NULL IDENTITY,
    [PayoutId] int NOT NULL,
    [FromStatus] nvarchar(20) NULL,
    [ToStatus] nvarchar(20) NOT NULL,
    [ActorUserId] int NULL,
    [ActorRole] nvarchar(20) NOT NULL,
    [OccurredAt] datetime2 NOT NULL,
    [BankReferenceId] nvarchar(100) NULL,
    CONSTRAINT [PK_SellerPayoutEvent] PRIMARY KEY ([Id]),
    CONSTRAINT [FK_SellerPayoutEvent_SellerPayout_PayoutId] FOREIGN KEY ([PayoutId]) REFERENCES [SellerPayout] ([Id]),
    CONSTRAINT [FK_SellerPayoutEvent_User_ActorUserId] FOREIGN KEY ([ActorUserId]) REFERENCES [User] ([id])
);
GO

CREATE INDEX [IX_SellerPayoutEvent_ActorUserId] ON [SellerPayoutEvent] ([ActorUserId]);
GO

CREATE UNIQUE INDEX [IX_SellerPayoutEvent_PayoutId_ToStatus] ON [SellerPayoutEvent] ([PayoutId], [ToStatus]);
GO

INSERT INTO [__EFMigrationsHistory] ([MigrationId], [ProductVersion])
VALUES (N'20261020162647_UnifyPayoutWorkflowAndHistory', N'8.0.31');
GO

COMMIT;
GO

