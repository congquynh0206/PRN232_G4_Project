BEGIN TRANSACTION;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261017042643_AddProductFeedbackReplies'
)
BEGIN
    IF EXISTS (SELECT 1 FROM sys.indexes WHERE object_id = OBJECT_ID(N'[Review]') AND name = N'IX_Review_productId')
                    DROP INDEX [IX_Review_productId] ON [Review];
                    IF EXISTS (SELECT 1 FROM sys.indexes WHERE object_id = OBJECT_ID(N'[Review]') AND name = N'IX_Review_reviewerId')
                    DROP INDEX [IX_Review_reviewerId] ON [Review];
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261017042643_AddProductFeedbackReplies'
)
BEGIN
    ALTER TABLE [Review] ADD [OrderId] int NULL;
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261017042643_AddProductFeedbackReplies'
)
BEGIN
    ALTER TABLE [Review] ADD [RequestKey] nvarchar(100) NULL;
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261017042643_AddProductFeedbackReplies'
)
BEGIN
    CREATE TABLE [ProductReviewReply] (
        [Id] int NOT NULL IDENTITY,
        [ReviewId] int NOT NULL,
        [SellerId] int NOT NULL,
        [Comment] nvarchar(2000) NOT NULL,
        [RequestKey] nvarchar(100) NOT NULL,
        [CreatedAt] datetime2 NOT NULL,
        CONSTRAINT [PK_ProductReviewReply] PRIMARY KEY ([Id]),
        CONSTRAINT [FK_ProductReviewReply_Review_ReviewId] FOREIGN KEY ([ReviewId]) REFERENCES [Review] ([id]),
        CONSTRAINT [FK_ProductReviewReply_User_SellerId] FOREIGN KEY ([SellerId]) REFERENCES [User] ([id])
    );
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261017042643_AddProductFeedbackReplies'
)
BEGIN
    EXEC(N'CREATE UNIQUE INDEX [IX_Review_OrderId_productId] ON [Review] ([OrderId], [productId]) WHERE [OrderId] IS NOT NULL');
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261017042643_AddProductFeedbackReplies'
)
BEGIN
    CREATE INDEX [IX_Review_productId_createdAt] ON [Review] ([productId], [createdAt]);
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261017042643_AddProductFeedbackReplies'
)
BEGIN
    EXEC(N'CREATE UNIQUE INDEX [IX_Review_reviewerId_RequestKey] ON [Review] ([reviewerId], [RequestKey]) WHERE [RequestKey] IS NOT NULL');
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261017042643_AddProductFeedbackReplies'
)
BEGIN
    CREATE UNIQUE INDEX [IX_ProductReviewReply_ReviewId] ON [ProductReviewReply] ([ReviewId]);
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261017042643_AddProductFeedbackReplies'
)
BEGIN
    CREATE UNIQUE INDEX [IX_ProductReviewReply_SellerId_RequestKey] ON [ProductReviewReply] ([SellerId], [RequestKey]);
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261017042643_AddProductFeedbackReplies'
)
BEGIN
    ALTER TABLE [Review] ADD CONSTRAINT [FK_Review_OrderTable_OrderId] FOREIGN KEY ([OrderId]) REFERENCES [OrderTable] ([id]);
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261017042643_AddProductFeedbackReplies'
)
BEGIN
    INSERT INTO [__EFMigrationsHistory] ([MigrationId], [ProductVersion])
    VALUES (N'20261017042643_AddProductFeedbackReplies', N'8.0.31');
END;
GO

COMMIT;
GO

