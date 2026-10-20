BEGIN TRANSACTION;
GO

ALTER TABLE [OrderTable] ADD [ReturnWindowClosesAt] datetime2 NULL;
GO

ALTER TABLE [OrderTable] ADD [ReturnWindowPolicyVersion] int NOT NULL DEFAULT 0;
GO

ALTER TABLE [OrderTable] ADD [ReturnWindowSeconds] int NULL;
GO

ALTER TABLE [OrderTable] ADD [ReturnWindowStartedAt] datetime2 NULL;
GO

UPDATE o SET ReturnWindowPolicyVersion = 0, ReturnWindowSeconds = 604800,
                ReturnWindowStartedAt = s.DeliveredAt, ReturnWindowClosesAt = DATEADD(second, 604800, s.DeliveredAt)
                FROM OrderTable o JOIN ShippingInfo s ON s.orderId = o.id AND s.Direction = N'Outbound'
                WHERE s.DeliveredAt IS NOT NULL AND o.ReturnWindowClosesAt IS NULL;
GO

INSERT INTO [__EFMigrationsHistory] ([MigrationId], [ProductVersion])
VALUES (N'20261020162229_SnapshotReturnWindowDeadlines', N'8.0.31');
GO

COMMIT;
GO

