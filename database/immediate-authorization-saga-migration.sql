BEGIN TRANSACTION;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261108150534_AddImmediateAuthorizationSaga'
)
BEGIN
    ALTER TABLE [ImmediatePaymentAttempt] ADD [Amount] decimal(10,2) NOT NULL DEFAULT 0.0;
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261108150534_AddImmediateAuthorizationSaga'
)
BEGIN
    ALTER TABLE [ImmediatePaymentAttempt] ADD [AuthorizationId] nvarchar(100) NULL;
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261108150534_AddImmediateAuthorizationSaga'
)
BEGIN
    ALTER TABLE [ImmediatePaymentAttempt] ADD [AuthorizationOutcome] nvarchar(30) NOT NULL DEFAULT N'';
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261108150534_AddImmediateAuthorizationSaga'
)
BEGIN
    ALTER TABLE [ImmediatePaymentAttempt] ADD [CaptureId] nvarchar(100) NULL;
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261108150534_AddImmediateAuthorizationSaga'
)
BEGIN
    ALTER TABLE [ImmediatePaymentAttempt] ADD [FinalizationAttempts] int NOT NULL DEFAULT 0;
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261108150534_AddImmediateAuthorizationSaga'
)
BEGIN
    ALTER TABLE [ImmediatePaymentAttempt] ADD [InventoryHeld] bit NOT NULL DEFAULT CAST(0 AS bit);
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261108150534_AddImmediateAuthorizationSaga'
)
BEGIN
    ALTER TABLE [ImmediatePaymentAttempt] ADD [ProcessingToken] uniqueidentifier NULL;
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261108150534_AddImmediateAuthorizationSaga'
)
BEGIN
    ALTER TABLE [ImmediatePaymentAttempt] ADD [ProcessingUntil] datetime2 NULL;
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261108150534_AddImmediateAuthorizationSaga'
)
BEGIN
    ALTER TABLE [ImmediatePaymentAttempt] ADD [Stage] nvarchar(30) NOT NULL DEFAULT N'';
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261108150534_AddImmediateAuthorizationSaga'
)
BEGIN
    ALTER TABLE [ImmediatePaymentAttempt] ADD [UpdatedAt] datetime2 NOT NULL DEFAULT '0001-01-01T00:00:00.0000000';
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261108150534_AddImmediateAuthorizationSaga'
)
BEGIN
    CREATE TABLE [SimulatedCardAuthorization] (
        [Id] nvarchar(100) NOT NULL,
        [Amount] decimal(10,2) NOT NULL,
        [Status] nvarchar(30) NOT NULL,
        [CaptureId] nvarchar(100) NULL,
        [UpdatedAt] datetime2 NOT NULL,
        CONSTRAINT [PK_SimulatedCardAuthorization] PRIMARY KEY ([Id])
    );
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261108150534_AddImmediateAuthorizationSaga'
)
BEGIN
    CREATE INDEX [IX_ImmediatePaymentAttempt_Status_ProcessingUntil] ON [ImmediatePaymentAttempt] ([Status], [ProcessingUntil]);
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261108150534_AddImmediateAuthorizationSaga'
)
BEGIN

    UPDATE [ImmediatePaymentAttempt]
    SET [Stage] = CASE WHEN [Status] = 'Succeeded' THEN 'Completed'
        WHEN [Status] = 'Failed' THEN 'Declined' ELSE 'Authorizing' END,
        [UpdatedAt] = [CreatedAt];
    UPDATE a SET a.[Amount] = ISNULL(o.[totalPrice], 0)
    FROM [ImmediatePaymentAttempt] a
    JOIN [OrderTable] o ON o.[id] = a.[OrderId];

END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261108150534_AddImmediateAuthorizationSaga'
)
BEGIN
    INSERT INTO [__EFMigrationsHistory] ([MigrationId], [ProductVersion])
    VALUES (N'20261108150534_AddImmediateAuthorizationSaga', N'8.0.31');
END;
GO

COMMIT;
GO

BEGIN TRANSACTION;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261108151440_AddSimulatedCardRefundHistory'
)
BEGIN
    ALTER TABLE [SimulatedCardAuthorization] ADD [RefundedAmount] decimal(10,2) NOT NULL DEFAULT 0.0;
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261108151440_AddSimulatedCardRefundHistory'
)
BEGIN
    ALTER TABLE [SimulatedCardAuthorization] ADD [RefundsJson] nvarchar(max) NOT NULL DEFAULT N'{}';
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261108151440_AddSimulatedCardRefundHistory'
)
BEGIN

    UPDATE [SimulatedCardAuthorization]
    SET [RefundedAmount] = [Amount],
        [RefundsJson] = CONCAT('{"legacy-full-refund":',
            CONVERT(varchar(40), [Amount]), '}')
    WHERE [Status] = 'Refunded';

END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261108151440_AddSimulatedCardRefundHistory'
)
BEGIN
    INSERT INTO [__EFMigrationsHistory] ([MigrationId], [ProductVersion])
    VALUES (N'20261108151440_AddSimulatedCardRefundHistory', N'8.0.31');
END;
GO

COMMIT;
GO

