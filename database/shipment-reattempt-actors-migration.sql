BEGIN TRANSACTION;
GO

ALTER TABLE [ShippingInfo] ADD [ReattemptAt] datetimeoffset NULL;
GO

ALTER TABLE [ShippingInfo] ADD [ReattemptReason] nvarchar(1000) NULL;
GO

ALTER TABLE [ShippingEvent] ADD [ActorRole] nvarchar(20) NULL;
GO

ALTER TABLE [ShippingEvent] ADD [ActorUserId] int NULL;
GO

ALTER TABLE [ShippingEvent] ADD [ReattemptAt] datetimeoffset NULL;
GO

CREATE INDEX [IX_ShippingEvent_ActorUserId] ON [ShippingEvent] ([ActorUserId]);
GO

ALTER TABLE [ShippingEvent] ADD CONSTRAINT [FK_ShippingEvent_User_ActorUserId] FOREIGN KEY ([ActorUserId]) REFERENCES [User] ([id]);
GO

INSERT INTO [__EFMigrationsHistory] ([MigrationId], [ProductVersion])
VALUES (N'20261020150408_AddShipmentReattemptSchedulesAndActors', N'8.0.31');
GO

COMMIT;
GO

