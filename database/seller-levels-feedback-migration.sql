BEGIN TRANSACTION;
GO

ALTER TABLE [SellerAccount] ADD [LastLevelEvaluatedAt] datetime2 NULL;
GO

ALTER TABLE [SellerAccount] ADD [LevelActivityStartedAt] datetime2 NULL;
GO

CREATE TABLE [SellerLevelEvaluation] (
    [Id] bigint NOT NULL IDENTITY,
    [SellerId] int NOT NULL,
    [PreviousLevel] int NOT NULL,
    [ResultLevel] int NOT NULL,
    [Source] nvarchar(30) NOT NULL,
    [Reason] nvarchar(1000) NOT NULL,
    [SnapshotJson] nvarchar(max) NOT NULL,
    [CreatedAt] datetime2 NOT NULL,
    CONSTRAINT [PK_SellerLevelEvaluation] PRIMARY KEY ([Id]),
    CONSTRAINT [FK_SellerLevelEvaluation_User_SellerId] FOREIGN KEY ([SellerId]) REFERENCES [User] ([id])
);
GO

CREATE TABLE [SellerOrderFeedback] (
    [Id] bigint NOT NULL IDENTITY,
    [OrderId] int NOT NULL,
    [BuyerId] int NOT NULL,
    [SellerId] int NOT NULL,
    [Rating] int NOT NULL,
    [Comment] nvarchar(2000) NOT NULL,
    [RequestKey] nvarchar(100) NOT NULL,
    [CreatedAt] datetime2 NOT NULL,
    CONSTRAINT [PK_SellerOrderFeedback] PRIMARY KEY ([Id]),
    CONSTRAINT [CK_SellerOrderFeedback_Rating] CHECK ([Rating] >= 1 AND [Rating] <= 5),
    CONSTRAINT [FK_SellerOrderFeedback_OrderTable_OrderId] FOREIGN KEY ([OrderId]) REFERENCES [OrderTable] ([id]),
    CONSTRAINT [FK_SellerOrderFeedback_User_BuyerId] FOREIGN KEY ([BuyerId]) REFERENCES [User] ([id]),
    CONSTRAINT [FK_SellerOrderFeedback_User_SellerId] FOREIGN KEY ([SellerId]) REFERENCES [User] ([id])
);
GO

CREATE INDEX [IX_SellerAccount_LastLevelEvaluatedAt] ON [SellerAccount] ([LastLevelEvaluatedAt]);
GO

CREATE INDEX [IX_SellerLevelEvaluation_SellerId_Id] ON [SellerLevelEvaluation] ([SellerId], [Id]);
GO

CREATE UNIQUE INDEX [IX_SellerOrderFeedback_BuyerId_RequestKey] ON [SellerOrderFeedback] ([BuyerId], [RequestKey]);
GO

CREATE UNIQUE INDEX [IX_SellerOrderFeedback_OrderId] ON [SellerOrderFeedback] ([OrderId]);
GO

CREATE INDEX [IX_SellerOrderFeedback_SellerId_CreatedAt] ON [SellerOrderFeedback] ([SellerId], [CreatedAt]);
GO

INSERT INTO [__EFMigrationsHistory] ([MigrationId], [ProductVersion])
VALUES (N'20261015102627_AddSellerLevelsAndOrderFeedback', N'8.0.31');
GO

COMMIT;
GO

