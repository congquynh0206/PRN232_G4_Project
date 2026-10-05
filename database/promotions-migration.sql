-- Additive promotion deployment. Select the target database before running.
SET QUOTED_IDENTIFIER ON;
SET ANSI_NULLS ON;
SET XACT_ABORT ON;
IF DB_NAME() IN (N'master',N'model',N'msdb',N'tempdb') THROW 51000,'Select the application database first.',1;
BEGIN TRY
    BEGIN TRANSACTION;
    EXEC(N'IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N''20261004133301_AddPromotions''
)
BEGIN
    IF EXISTS (SELECT 1 FROM Coupon WHERE code IS NULL OR LEN(UPPER(LTRIM(RTRIM(code)))) NOT BETWEEN 3 AND 50
        OR UPPER(LTRIM(RTRIM(code))) COLLATE Latin1_General_100_BIN2 LIKE ''%[^A-Z0-9_-]%''
        OR discountPercent IS NULL OR discountPercent<=0 OR discountPercent>100
        OR (startDate IS NOT NULL AND endDate IS NOT NULL AND endDate<=startDate) OR maxUsage<=0)
        THROW 51001,''Legacy coupons contain invalid codes, dates or limits. Review before migrating.'',1;
    IF EXISTS (SELECT UPPER(LTRIM(RTRIM(code))) FROM Coupon GROUP BY UPPER(LTRIM(RTRIM(code))) HAVING COUNT(*)>1)
        THROW 51002,''Legacy coupon codes collide after normalization. Review before migrating.'',1;
    IF EXISTS (SELECT 1 FROM Coupon c LEFT JOIN Product p ON p.id=c.productId LEFT JOIN [User] u ON u.id=p.sellerId
        WHERE c.productId IS NOT NULL AND (p.sellerId IS NULL OR u.role<>N''seller'' OR u.id IS NULL))
        THROW 51003,''Legacy product coupon has no valid seller.'',1;
    IF EXISTS (SELECT 1 FROM Coupon WHERE productId IS NULL) AND NOT EXISTS (SELECT 1 FROM [User] WHERE role=N''admin'')
        THROW 51004,''An admin account is required to own platform coupons.'',1;
    IF EXISTS (SELECT 1 FROM OrderTable o WHERE o.CouponCode IS NOT NULL AND LTRIM(RTRIM(o.CouponCode))<>''''
        AND NOT EXISTS (SELECT 1 FROM Coupon c WHERE UPPER(LTRIM(RTRIM(c.code)))=UPPER(LTRIM(RTRIM(o.CouponCode)))))
        THROW 51005,''Historical order coupon has no mapping. Review before migrating.'',1;
END;');
    EXEC(N'IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N''20261004133301_AddPromotions''
)
BEGIN
    ALTER TABLE [OrderTable] ADD [PlatformSubsidy] decimal(18,2) NOT NULL DEFAULT 0.0;
END;');
    EXEC(N'IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N''20261004133301_AddPromotions''
)
BEGIN
    ALTER TABLE [OrderTable] ADD [PricingFingerprint] nvarchar(64) NULL;
END;');
    EXEC(N'IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N''20261004133301_AddPromotions''
)
BEGIN
    ALTER TABLE [OrderTable] ADD [PricingSchemaVersion] int NOT NULL DEFAULT 0;
END;');
    EXEC(N'IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N''20261004133301_AddPromotions''
)
BEGIN
    ALTER TABLE [OrderTable] ADD [SellerGoodsDiscount] decimal(18,2) NOT NULL DEFAULT 0.0;
END;');
    EXEC(N'IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N''20261004133301_AddPromotions''
)
BEGIN
    ALTER TABLE [OrderTable] ADD [SellerGrossSnapshot] decimal(18,2) NOT NULL DEFAULT 0.0;
END;');
    EXEC(N'IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N''20261004133301_AddPromotions''
)
BEGIN
    ALTER TABLE [OrderTable] ADD [ShippingBase] decimal(18,2) NOT NULL DEFAULT 0.0;
END;');
    EXEC(N'IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N''20261004133301_AddPromotions''
)
BEGIN
    ALTER TABLE [OrderTable] ADD [ShippingDiscount] decimal(18,2) NOT NULL DEFAULT 0.0;
END;');
    EXEC(N'IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N''20261004133301_AddPromotions''
)
BEGIN
    ALTER TABLE [OrderItem] ADD [PlatformDiscountSnapshot] decimal(18,2) NOT NULL DEFAULT 0.0;
END;');
    EXEC(N'IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N''20261004133301_AddPromotions''
)
BEGIN
    ALTER TABLE [OrderItem] ADD [SellerDiscountSnapshot] decimal(18,2) NOT NULL DEFAULT 0.0;
END;');
    EXEC(N'IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N''20261004133301_AddPromotions''
)
BEGIN
    CREATE TABLE [Promotion] (
        [Id] int NOT NULL IDENTITY,
        [Name] nvarchar(150) NOT NULL,
        [Type] nvarchar(20) NOT NULL,
        [FundingSource] nvarchar(20) NOT NULL,
        [SellerId] int NULL,
        [CreatedById] int NOT NULL,
        [LegacyCouponId] int NULL,
        [Code] nvarchar(50) NULL,
        [Value] decimal(18,2) NOT NULL,
        [IsPercent] bit NOT NULL,
        [FreeShipping] bit NOT NULL,
        [Cap] decimal(18,2) NULL,
        [MinSubtotal] decimal(18,2) NOT NULL,
        [MinQuantity] int NOT NULL,
        [StartAt] datetime2 NOT NULL,
        [EndAt] datetime2 NOT NULL,
        [IsPaused] bit NOT NULL,
        [AdminPaused] bit NOT NULL,
        [PauseReason] nvarchar(1000) NULL,
        [Version] int NOT NULL,
        [MaxUsage] int NULL,
        [MaxUsagePerBuyer] int NULL,
        [Budget] decimal(18,2) NULL,
        [CreatedAt] datetime2 NOT NULL,
        [UpdatedAt] datetime2 NOT NULL,
        CONSTRAINT [PK_Promotion] PRIMARY KEY ([Id]),
        CONSTRAINT [FK_Promotion_User_CreatedById] FOREIGN KEY ([CreatedById]) REFERENCES [User] ([id]),
        CONSTRAINT [FK_Promotion_User_SellerId] FOREIGN KEY ([SellerId]) REFERENCES [User] ([id])
    );
END;');
    EXEC(N'IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N''20261004133301_AddPromotions''
)
BEGIN
    CREATE TABLE [OrderPromotionSnapshot] (
        [Id] int NOT NULL IDENTITY,
        [OrderId] int NOT NULL,
        [PromotionId] int NOT NULL,
        [Name] nvarchar(150) NOT NULL,
        [Type] nvarchar(20) NOT NULL,
        [FundingSource] nvarchar(20) NOT NULL,
        [Code] nvarchar(50) NULL,
        [Version] int NOT NULL,
        [Amount] decimal(18,2) NOT NULL,
        [AllocationJson] nvarchar(max) NOT NULL,
        CONSTRAINT [PK_OrderPromotionSnapshot] PRIMARY KEY ([Id]),
        CONSTRAINT [FK_OrderPromotionSnapshot_OrderTable_OrderId] FOREIGN KEY ([OrderId]) REFERENCES [OrderTable] ([id]),
        CONSTRAINT [FK_OrderPromotionSnapshot_Promotion_PromotionId] FOREIGN KEY ([PromotionId]) REFERENCES [Promotion] ([Id])
    );
END;');
    EXEC(N'IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N''20261004133301_AddPromotions''
)
BEGIN
    CREATE TABLE [PromotionAudit] (
        [Id] int NOT NULL IDENTITY,
        [PromotionId] int NOT NULL,
        [ActorId] int NOT NULL,
        [Action] nvarchar(30) NOT NULL,
        [Reason] nvarchar(1000) NULL,
        [ChangesJson] nvarchar(max) NOT NULL,
        [CreatedAt] datetime2 NOT NULL,
        CONSTRAINT [PK_PromotionAudit] PRIMARY KEY ([Id]),
        CONSTRAINT [FK_PromotionAudit_Promotion_PromotionId] FOREIGN KEY ([PromotionId]) REFERENCES [Promotion] ([Id]),
        CONSTRAINT [FK_PromotionAudit_User_ActorId] FOREIGN KEY ([ActorId]) REFERENCES [User] ([id])
    );
END;');
    EXEC(N'IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N''20261004133301_AddPromotions''
)
BEGIN
    CREATE TABLE [PromotionTarget] (
        [Id] int NOT NULL IDENTITY,
        [PromotionId] int NOT NULL,
        [ProductId] int NULL,
        [CategoryId] int NULL,
        CONSTRAINT [PK_PromotionTarget] PRIMARY KEY ([Id]),
        CONSTRAINT [FK_PromotionTarget_Category_CategoryId] FOREIGN KEY ([CategoryId]) REFERENCES [Category] ([id]),
        CONSTRAINT [FK_PromotionTarget_Product_ProductId] FOREIGN KEY ([ProductId]) REFERENCES [Product] ([id]),
        CONSTRAINT [FK_PromotionTarget_Promotion_PromotionId] FOREIGN KEY ([PromotionId]) REFERENCES [Promotion] ([Id]) ON DELETE CASCADE
    );
END;');
    EXEC(N'IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N''20261004133301_AddPromotions''
)
BEGIN
    CREATE TABLE [PromotionTier] (
        [Id] int NOT NULL IDENTITY,
        [PromotionId] int NOT NULL,
        [MinQuantity] int NOT NULL,
        [Percent] decimal(18,2) NOT NULL,
        CONSTRAINT [PK_PromotionTier] PRIMARY KEY ([Id]),
        CONSTRAINT [FK_PromotionTier_Promotion_PromotionId] FOREIGN KEY ([PromotionId]) REFERENCES [Promotion] ([Id]) ON DELETE CASCADE
    );
END;');
    EXEC(N'IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N''20261004133301_AddPromotions''
)
BEGIN
    CREATE TABLE [PromotionUsage] (
        [Id] int NOT NULL IDENTITY,
        [PromotionId] int NOT NULL,
        [OrderId] int NOT NULL,
        [BuyerId] int NOT NULL,
        [Amount] decimal(18,2) NOT NULL,
        [State] nvarchar(20) NOT NULL,
        [ReservedAt] datetime2 NOT NULL,
        [ConsumedAt] datetime2 NULL,
        [ReleasedAt] datetime2 NULL,
        CONSTRAINT [PK_PromotionUsage] PRIMARY KEY ([Id]),
        CONSTRAINT [FK_PromotionUsage_OrderTable_OrderId] FOREIGN KEY ([OrderId]) REFERENCES [OrderTable] ([id]),
        CONSTRAINT [FK_PromotionUsage_Promotion_PromotionId] FOREIGN KEY ([PromotionId]) REFERENCES [Promotion] ([Id]),
        CONSTRAINT [FK_PromotionUsage_User_BuyerId] FOREIGN KEY ([BuyerId]) REFERENCES [User] ([id])
    );
END;');
    EXEC(N'IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N''20261004133301_AddPromotions''
)
BEGIN
    CREATE UNIQUE INDEX [IX_OrderPromotionSnapshot_OrderId_PromotionId] ON [OrderPromotionSnapshot] ([OrderId], [PromotionId]);
END;');
    EXEC(N'IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N''20261004133301_AddPromotions''
)
BEGIN
    CREATE INDEX [IX_OrderPromotionSnapshot_PromotionId] ON [OrderPromotionSnapshot] ([PromotionId]);
END;');
    EXEC(N'IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N''20261004133301_AddPromotions''
)
BEGIN
    EXEC(N''CREATE UNIQUE INDEX [IX_Promotion_Code] ON [Promotion] ([Code]) WHERE [Code] IS NOT NULL'');
END;');
    EXEC(N'IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N''20261004133301_AddPromotions''
)
BEGIN
    CREATE INDEX [IX_Promotion_CreatedById] ON [Promotion] ([CreatedById]);
END;');
    EXEC(N'IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N''20261004133301_AddPromotions''
)
BEGIN
    EXEC(N''CREATE UNIQUE INDEX [IX_Promotion_LegacyCouponId] ON [Promotion] ([LegacyCouponId]) WHERE [LegacyCouponId] IS NOT NULL'');
END;');
    EXEC(N'IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N''20261004133301_AddPromotions''
)
BEGIN
    CREATE INDEX [IX_Promotion_SellerId_Type_StartAt_EndAt] ON [Promotion] ([SellerId], [Type], [StartAt], [EndAt]);
END;');
    EXEC(N'IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N''20261004133301_AddPromotions''
)
BEGIN
    CREATE INDEX [IX_PromotionAudit_ActorId] ON [PromotionAudit] ([ActorId]);
END;');
    EXEC(N'IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N''20261004133301_AddPromotions''
)
BEGIN
    CREATE INDEX [IX_PromotionAudit_PromotionId] ON [PromotionAudit] ([PromotionId]);
END;');
    EXEC(N'IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N''20261004133301_AddPromotions''
)
BEGIN
    CREATE INDEX [IX_PromotionTarget_CategoryId] ON [PromotionTarget] ([CategoryId]);
END;');
    EXEC(N'IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N''20261004133301_AddPromotions''
)
BEGIN
    CREATE INDEX [IX_PromotionTarget_ProductId] ON [PromotionTarget] ([ProductId]);
END;');
    EXEC(N'IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N''20261004133301_AddPromotions''
)
BEGIN
    CREATE INDEX [IX_PromotionTarget_PromotionId] ON [PromotionTarget] ([PromotionId]);
END;');
    EXEC(N'IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N''20261004133301_AddPromotions''
)
BEGIN
    CREATE UNIQUE INDEX [IX_PromotionTier_PromotionId_MinQuantity] ON [PromotionTier] ([PromotionId], [MinQuantity]);
END;');
    EXEC(N'IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N''20261004133301_AddPromotions''
)
BEGIN
    CREATE INDEX [IX_PromotionUsage_BuyerId] ON [PromotionUsage] ([BuyerId]);
END;');
    EXEC(N'IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N''20261004133301_AddPromotions''
)
BEGIN
    CREATE UNIQUE INDEX [IX_PromotionUsage_OrderId_PromotionId] ON [PromotionUsage] ([OrderId], [PromotionId]);
END;');
    EXEC(N'IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N''20261004133301_AddPromotions''
)
BEGIN
    CREATE INDEX [IX_PromotionUsage_PromotionId_State_BuyerId] ON [PromotionUsage] ([PromotionId], [State], [BuyerId]);
END;');
    EXEC(N'IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N''20261004133301_AddPromotions''
)
BEGIN
    INSERT INTO Promotion (Name,Type,FundingSource,SellerId,CreatedById,LegacyCouponId,Code,Value,IsPercent,FreeShipping,
        MinSubtotal,MinQuantity,StartAt,EndAt,IsPaused,AdminPaused,Version,MaxUsage,CreatedAt,UpdatedAt)
    SELECT N''Coupon ''+UPPER(LTRIM(RTRIM(c.code))),N''Coupon'',CASE WHEN c.productId IS NULL THEN N''Platform'' ELSE N''Seller'' END,
        CASE WHEN c.productId IS NULL THEN NULL ELSE p.sellerId END,
        CASE WHEN c.productId IS NULL THEN (SELECT MIN(id) FROM [User] WHERE role=N''admin'') ELSE p.sellerId END,
        c.id,UPPER(LTRIM(RTRIM(c.code))),c.discountPercent,1,0,0,1,
        COALESCE(c.startDate,CONVERT(datetime2,''2000-01-01'')),COALESCE(c.endDate,CONVERT(datetime2,''9999-12-31'')),0,0,1,c.maxUsage,SYSUTCDATETIME(),SYSUTCDATETIME()
    FROM Coupon c LEFT JOIN Product p ON p.id=c.productId
    WHERE NOT EXISTS (SELECT 1 FROM Promotion v WHERE v.LegacyCouponId=c.id);
    INSERT INTO PromotionTarget (PromotionId,ProductId)
    SELECT p.Id,c.productId FROM Promotion p JOIN Coupon c ON c.id=p.LegacyCouponId WHERE c.productId IS NOT NULL
        AND NOT EXISTS(SELECT 1 FROM PromotionTarget t WHERE t.PromotionId=p.Id AND t.ProductId=c.productId);
    INSERT INTO PromotionUsage (PromotionId,OrderId,BuyerId,Amount,State,ReservedAt,ConsumedAt)
    SELECT p.Id,o.id,o.buyerId,o.DiscountAmount,
        CASE WHEN EXISTS(SELECT 1 FROM Payment pay WHERE pay.orderId=o.id AND pay.status=N''Succeeded'') THEN N''Consumed'' ELSE N''Reserved'' END,
        COALESCE(o.orderDate,SYSUTCDATETIME()),
        (SELECT MIN(pay.PaidAt) FROM Payment pay WHERE pay.orderId=o.id AND pay.status=N''Succeeded'')
    FROM OrderTable o JOIN Promotion p ON p.Code=UPPER(LTRIM(RTRIM(o.CouponCode)))
    WHERE o.buyerId IS NOT NULL
        AND (EXISTS(SELECT 1 FROM Payment pay WHERE pay.orderId=o.id AND pay.status=N''Succeeded'') OR o.status=N''AwaitingPayment'')
        AND NOT EXISTS(SELECT 1 FROM PromotionUsage u WHERE u.OrderId=o.id AND u.PromotionId=p.Id);
    INSERT INTO PromotionAudit (PromotionId,ActorId,Action,Reason,ChangesJson,CreatedAt)
    SELECT p.Id,p.CreatedById,N''Migrate'',N''Chuyển coupon hiện có; giữ lượt dùng lịch sử.'',N''{}'',SYSUTCDATETIME()
    FROM Promotion p WHERE p.LegacyCouponId IS NOT NULL AND NOT EXISTS(SELECT 1 FROM PromotionAudit a WHERE a.PromotionId=p.Id AND a.Action=N''Migrate'');
END;');
    EXEC(N'IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N''20261004133301_AddPromotions''
)
BEGIN
    INSERT INTO [__EFMigrationsHistory] ([MigrationId], [ProductVersion])
    VALUES (N''20261004133301_AddPromotions'', N''8.0.31'');
END;');
    COMMIT TRANSACTION;
END TRY
BEGIN CATCH
    IF XACT_STATE() <> 0 ROLLBACK TRANSACTION;
    THROW;
END CATCH;
