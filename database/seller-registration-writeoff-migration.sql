BEGIN TRANSACTION;
GO

ALTER TABLE [SellerDebtCycle] ADD [LastObservedBalance] decimal(18,2) NOT NULL DEFAULT 0.0;
GO

ALTER TABLE [SellerDebtCycle] ADD [LastRecoveryAt] datetime2 NULL;
GO

ALTER TABLE [SellerDebtCycle] ADD [RecoveryGraceSeconds] int NOT NULL DEFAULT 0;
GO

ALTER TABLE [SellerDebtCycle] ADD [WriteOffEligibleAt] datetime2 NULL;
GO

ALTER TABLE [SellerAccount] ADD [CurrentIdentityId] bigint NULL;
GO

ALTER TABLE [SellerAccount] ADD [IdentityVersion] int NOT NULL DEFAULT 0;
GO

ALTER TABLE [SellerAccount] ADD [PermanentSalesBlockedAt] datetime2 NULL;
GO

CREATE TABLE [SellerDebtWriteOff] (
    [Id] bigint NOT NULL IDENTITY,
    [SellerAccountId] int NOT NULL,
    [CycleNumber] int NOT NULL,
    [OriginalAmount] decimal(18,2) NOT NULL,
    [DebtStartedAt] datetime2 NOT NULL,
    [DebtDueAt] datetime2 NOT NULL,
    [EligibleAt] datetime2 NOT NULL,
    [LastRecoveryAt] datetime2 NOT NULL,
    [RecoveryGraceSeconds] int NOT NULL,
    [BlacklistedIdentities] int NOT NULL,
    [Actor] nvarchar(40) NOT NULL,
    [Reason] nvarchar(1000) NOT NULL,
    [CreatedAt] datetime2 NOT NULL,
    CONSTRAINT [PK_SellerDebtWriteOff] PRIMARY KEY ([Id]),
    CONSTRAINT [FK_SellerDebtWriteOff_SellerAccount_SellerAccountId] FOREIGN KEY ([SellerAccountId]) REFERENCES [SellerAccount] ([Id])
);
GO

CREATE TABLE [SellerIdentity] (
    [Id] bigint NOT NULL IDENTITY,
    [SellerId] int NOT NULL,
    [Kind] nvarchar(20) NOT NULL,
    [MaskedValue] nvarchar(100) NOT NULL,
    [Fingerprint] nvarchar(64) NOT NULL,
    [KeyId] nvarchar(40) NOT NULL,
    [RequestKey] nvarchar(100) NOT NULL,
    [RequestFingerprint] nvarchar(64) NOT NULL,
    [VerifiedAt] datetime2 NOT NULL,
    CONSTRAINT [PK_SellerIdentity] PRIMARY KEY ([Id]),
    CONSTRAINT [FK_SellerIdentity_User_SellerId] FOREIGN KEY ([SellerId]) REFERENCES [User] ([id])
);
GO

CREATE TABLE [SellerIdentityKey] (
    [Id] nvarchar(40) NOT NULL,
    [Verifier] nvarchar(64) NOT NULL,
    CONSTRAINT [PK_SellerIdentityKey] PRIMARY KEY ([Id])
);
GO

CREATE TABLE [SellerRegistration] (
    [Id] bigint NOT NULL IDENTITY,
    [SellerId] int NOT NULL,
    [RequestKey] nvarchar(100) NOT NULL,
    [RequestFingerprint] nvarchar(64) NOT NULL,
    [Email] nvarchar(100) NOT NULL,
    [StoreName] nvarchar(100) NOT NULL,
    [CreatedAt] datetime2 NOT NULL,
    CONSTRAINT [PK_SellerRegistration] PRIMARY KEY ([Id]),
    CONSTRAINT [FK_SellerRegistration_User_SellerId] FOREIGN KEY ([SellerId]) REFERENCES [User] ([id])
);
GO

CREATE TABLE [SellerBlacklist] (
    [Id] bigint NOT NULL IDENTITY,
    [Kind] nvarchar(20) NOT NULL,
    [MaskedValue] nvarchar(100) NOT NULL,
    [Fingerprint] nvarchar(64) NOT NULL,
    [KeyId] nvarchar(40) NOT NULL,
    [SourceSellerId] int NULL,
    [WriteOffId] bigint NULL,
    [IsDemo] bit NOT NULL,
    [Reason] nvarchar(1000) NOT NULL,
    [CreatedAt] datetime2 NOT NULL,
    CONSTRAINT [PK_SellerBlacklist] PRIMARY KEY ([Id]),
    CONSTRAINT [FK_SellerBlacklist_SellerDebtWriteOff_WriteOffId] FOREIGN KEY ([WriteOffId]) REFERENCES [SellerDebtWriteOff] ([Id]),
    CONSTRAINT [FK_SellerBlacklist_User_SourceSellerId] FOREIGN KEY ([SourceSellerId]) REFERENCES [User] ([id])
);
GO

CREATE TABLE [SellerWriteOffRecovery] (
    [Id] bigint NOT NULL IDENTITY,
    [WriteOffId] bigint NOT NULL,
    [Amount] decimal(18,2) NOT NULL,
    [CreatedAt] datetime2 NOT NULL,
    CONSTRAINT [PK_SellerWriteOffRecovery] PRIMARY KEY ([Id]),
    CONSTRAINT [FK_SellerWriteOffRecovery_SellerDebtWriteOff_WriteOffId] FOREIGN KEY ([WriteOffId]) REFERENCES [SellerDebtWriteOff] ([Id])
);
GO

CREATE INDEX [IX_SellerAccount_CurrentIdentityId] ON [SellerAccount] ([CurrentIdentityId]);
GO

CREATE UNIQUE INDEX [IX_SellerBlacklist_KeyId_Fingerprint] ON [SellerBlacklist] ([KeyId], [Fingerprint]);
GO

CREATE INDEX [IX_SellerBlacklist_SourceSellerId] ON [SellerBlacklist] ([SourceSellerId]);
GO

CREATE INDEX [IX_SellerBlacklist_WriteOffId] ON [SellerBlacklist] ([WriteOffId]);
GO

CREATE UNIQUE INDEX [IX_SellerDebtWriteOff_SellerAccountId_CycleNumber] ON [SellerDebtWriteOff] ([SellerAccountId], [CycleNumber]);
GO

CREATE UNIQUE INDEX [IX_SellerIdentity_KeyId_Fingerprint] ON [SellerIdentity] ([KeyId], [Fingerprint]);
GO

CREATE UNIQUE INDEX [IX_SellerIdentity_SellerId_RequestKey] ON [SellerIdentity] ([SellerId], [RequestKey]);
GO

CREATE UNIQUE INDEX [IX_SellerRegistration_RequestKey] ON [SellerRegistration] ([RequestKey]);
GO

CREATE UNIQUE INDEX [IX_SellerRegistration_SellerId] ON [SellerRegistration] ([SellerId]);
GO

CREATE INDEX [IX_SellerWriteOffRecovery_WriteOffId] ON [SellerWriteOffRecovery] ([WriteOffId]);
GO

ALTER TABLE [SellerAccount] ADD CONSTRAINT [FK_SellerAccount_SellerIdentity_CurrentIdentityId] FOREIGN KEY ([CurrentIdentityId]) REFERENCES [SellerIdentity] ([Id]);
GO

INSERT INTO [__EFMigrationsHistory] ([MigrationId], [ProductVersion])
VALUES (N'20261015041522_AddSellerRegistrationAndAutomaticWriteOff', N'8.0.31');
GO

COMMIT;
GO

