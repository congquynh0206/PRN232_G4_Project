BEGIN TRANSACTION;
GO

DROP INDEX [IX_NotificationOutbox_OrderId_EventType] ON [NotificationOutbox];
GO

ALTER TABLE [SellerAccount] ADD [DebtCycleNumber] int NOT NULL DEFAULT 0;
GO

ALTER TABLE [SellerAccount] ADD [DebtDueAt] datetime2 NULL;
GO

ALTER TABLE [SellerAccount] ADD [DebtStartedAt] datetime2 NULL;
GO

ALTER TABLE [SellerAccount] ADD [DebtSuspended] bit NOT NULL DEFAULT CAST(0 AS bit);
GO

DECLARE @var0 sysname;
SELECT @var0 = [d].[name]
FROM [sys].[default_constraints] [d]
INNER JOIN [sys].[columns] [c] ON [d].[parent_column_id] = [c].[column_id] AND [d].[parent_object_id] = [c].[object_id]
WHERE ([d].[parent_object_id] = OBJECT_ID(N'[NotificationOutbox]') AND [c].[name] = N'OrderId');
IF @var0 IS NOT NULL EXEC(N'ALTER TABLE [NotificationOutbox] DROP CONSTRAINT [' + @var0 + '];');
ALTER TABLE [NotificationOutbox] ALTER COLUMN [OrderId] int NULL;
GO

ALTER TABLE [NotificationOutbox] ADD [EventKey] nvarchar(100) NULL;
GO

ALTER TABLE [NotificationOutbox] ADD [SellerId] int NULL;
GO

CREATE TABLE [SellerDebtCycle] (
    [Id] bigint NOT NULL IDENTITY,
    [SellerAccountId] int NOT NULL,
    [Number] int NOT NULL,
    [OpeningBalance] decimal(18,2) NOT NULL,
    [StartedAt] datetime2 NOT NULL,
    [DueAt] datetime2 NOT NULL,
    [WarningAt] datetime2 NOT NULL,
    [WarnedAt] datetime2 NULL,
    [SuspendedAt] datetime2 NULL,
    [ClosedAt] datetime2 NULL,
    CONSTRAINT [PK_SellerDebtCycle] PRIMARY KEY ([Id]),
    CONSTRAINT [FK_SellerDebtCycle_SellerAccount_SellerAccountId] FOREIGN KEY ([SellerAccountId]) REFERENCES [SellerAccount] ([Id])
);
GO

CREATE TABLE [SellerDebtEvent] (
    [Id] bigint NOT NULL IDENTITY,
    [SellerAccountId] int NOT NULL,
    [CycleNumber] int NOT NULL,
    [EventType] nvarchar(40) NOT NULL,
    [EntryKey] nvarchar(100) NOT NULL,
    [Balance] decimal(18,2) NOT NULL,
    [Description] nvarchar(1000) NOT NULL,
    [CreatedAt] datetime2 NOT NULL,
    CONSTRAINT [PK_SellerDebtEvent] PRIMARY KEY ([Id]),
    CONSTRAINT [FK_SellerDebtEvent_SellerAccount_SellerAccountId] FOREIGN KEY ([SellerAccountId]) REFERENCES [SellerAccount] ([Id])
);
GO

CREATE TABLE [SellerDebtRepayment] (
    [Id] bigint NOT NULL IDENTITY,
    [SellerAccountId] int NOT NULL,
    [CycleNumber] int NOT NULL,
    [Amount] decimal(18,2) NOT NULL,
    [ExpectedDebt] decimal(18,2) NOT NULL,
    [RemainingDebt] decimal(18,2) NOT NULL,
    [Currency] nvarchar(3) NOT NULL,
    [RequestKey] nvarchar(100) NOT NULL,
    [SimulateFailure] bit NOT NULL,
    [Status] nvarchar(20) NOT NULL,
    [BankReferenceId] nvarchar(100) NOT NULL,
    [CreatedAt] datetime2 NOT NULL,
    CONSTRAINT [PK_SellerDebtRepayment] PRIMARY KEY ([Id]),
    CONSTRAINT [FK_SellerDebtRepayment_SellerAccount_SellerAccountId] FOREIGN KEY ([SellerAccountId]) REFERENCES [SellerAccount] ([Id])
);
GO

CREATE INDEX [IX_SellerAccount_NegativeBalance_DebtDueAt] ON [SellerAccount] ([NegativeBalance], [DebtDueAt]);
GO

CREATE UNIQUE INDEX [IX_NotificationOutbox_EventKey] ON [NotificationOutbox] ([EventKey]) WHERE [EventKey] IS NOT NULL;
GO

CREATE UNIQUE INDEX [IX_NotificationOutbox_OrderId_EventType] ON [NotificationOutbox] ([OrderId], [EventType]) WHERE [OrderId] IS NOT NULL;
GO

CREATE INDEX [IX_NotificationOutbox_SellerId] ON [NotificationOutbox] ([SellerId]);
GO

CREATE UNIQUE INDEX [IX_SellerDebtCycle_SellerAccountId_Number] ON [SellerDebtCycle] ([SellerAccountId], [Number]);
GO

CREATE UNIQUE INDEX [IX_SellerDebtEvent_EntryKey] ON [SellerDebtEvent] ([EntryKey]);
GO

CREATE INDEX [IX_SellerDebtEvent_SellerAccountId_Id] ON [SellerDebtEvent] ([SellerAccountId], [Id]);
GO

CREATE UNIQUE INDEX [IX_SellerDebtRepayment_SellerAccountId_RequestKey] ON [SellerDebtRepayment] ([SellerAccountId], [RequestKey]);
GO

ALTER TABLE [NotificationOutbox] ADD CONSTRAINT [FK_NotificationOutbox_User_SellerId] FOREIGN KEY ([SellerId]) REFERENCES [User] ([id]);
GO

INSERT INTO [__EFMigrationsHistory] ([MigrationId], [ProductVersion])
VALUES (N'20261014102533_AddSellerDebtManagement', N'8.0.31');
GO

COMMIT;
GO

