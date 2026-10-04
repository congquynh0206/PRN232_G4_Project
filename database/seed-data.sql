/*
    Reference data for the checkout and shipping application.

    Run this script once after baseline.sql and the EF Core migrations.
    It is safe to run again: existing rows are reused and inventory is never
    reset, so running it cannot restore stock consumed by existing orders.
*/

USE [CloneEbayDB];
GO

SET NOCOUNT ON;
SET XACT_ABORT ON;

BEGIN TRY
    BEGIN TRANSACTION;

    IF NOT EXISTS (SELECT 1 FROM dbo.[User] WHERE email = N'buyer@example.test')
       AND EXISTS (SELECT 1 FROM dbo.[User] WHERE email = N'demo.buyer@example.test')
    BEGIN
        UPDATE dbo.[User]
        SET username = N'buyer', email = N'buyer@example.test', role = N'buyer'
        WHERE email = N'demo.buyer@example.test';
    END;
    UPDATE dbo.[User]
    SET role = N'buyer', [password] = N'PBKDF2$100000$ZzQtYnV5ZXItYXV0aC1zYWx0LTIwMjY=$Wgm3b6IizRGo8CDLurHUJAAlcbSLY1ASzgQLLysRq6Q='
    WHERE email = N'buyer@example.test';

    IF NOT EXISTS (SELECT 1 FROM dbo.[User] WHERE email = N'buyer@example.test')
    BEGIN
        INSERT INTO dbo.[User] (username, email, [password], role, avatarURL)
        VALUES (N'buyer', N'buyer@example.test', N'PBKDF2$100000$ZzQtYnV5ZXItYXV0aC1zYWx0LTIwMjY=$Wgm3b6IizRGo8CDLurHUJAAlcbSLY1ASzgQLLysRq6Q=', N'buyer', NULL);
    END;

    IF NOT EXISTS (SELECT 1 FROM dbo.[User] WHERE email = N'seller@example.test')
       AND EXISTS (SELECT 1 FROM dbo.[User] WHERE email = N'demo.seller@example.test')
    BEGIN
        UPDATE dbo.[User]
        SET username = N'seller', email = N'seller@example.test', role = N'seller'
        WHERE email = N'demo.seller@example.test';
    END;
    UPDATE dbo.[User]
    SET role = N'seller', [password] = N'PBKDF2$100000$ZzQtc2VsbGVyLWF1dGgtc2FsdC0yMDI2$bvbBUVZpDtB6R8yq4JMzCmYk75mQrXZFzoj0dLX7VnA='
    WHERE email = N'seller@example.test';

    IF NOT EXISTS (SELECT 1 FROM dbo.[User] WHERE email = N'admin@example.test')
    BEGIN
        INSERT INTO dbo.[User] (username, email, [password], role, avatarURL)
        VALUES (N'admin', N'admin@example.test', N'PBKDF2$100000$ZzQtYWRtaW4tYXV0aC1zYWx0LTIwMjY=$ea5GVHF/3FKmN145Rx2b0HRerQzGX1LsR72OA2yjPaI=', N'admin', NULL);
    END
    ELSE
        UPDATE dbo.[User] SET role = N'admin', [password] = N'PBKDF2$100000$ZzQtYWRtaW4tYXV0aC1zYWx0LTIwMjY=$ea5GVHF/3FKmN145Rx2b0HRerQzGX1LsR72OA2yjPaI='
        WHERE email = N'admin@example.test';

    IF NOT EXISTS (SELECT 1 FROM dbo.[User] WHERE email = N'shipper@example.test')
    BEGIN
        INSERT INTO dbo.[User] (username, email, [password], role, avatarURL)
        VALUES (N'shipper', N'shipper@example.test', N'PBKDF2$100000$ZzQtc2hpcHBlci1hdXRoLXNhbHQtMjAyNg==$1AdrOKl50QYwOhEZFyWTfkU90Wv84Zq68bZBxqpX9Pk=', N'shipper', NULL);
    END
    ELSE
        UPDATE dbo.[User] SET role = N'shipper', [password] = N'PBKDF2$100000$ZzQtc2hpcHBlci1hdXRoLXNhbHQtMjAyNg==$1AdrOKl50QYwOhEZFyWTfkU90Wv84Zq68bZBxqpX9Pk='
        WHERE email = N'shipper@example.test';

    IF NOT EXISTS (SELECT 1 FROM dbo.[User] WHERE email = N'seller@example.test')
    BEGIN
        INSERT INTO dbo.[User] (username, email, [password], role, avatarURL)
        VALUES (N'seller', N'seller@example.test', N'PBKDF2$100000$ZzQtc2VsbGVyLWF1dGgtc2FsdC0yMDI2$bvbBUVZpDtB6R8yq4JMzCmYk75mQrXZFzoj0dLX7VnA=', N'seller', NULL);
    END;

    DECLARE @BuyerId INT = (SELECT id FROM dbo.[User] WHERE email = N'buyer@example.test');
    DECLARE @SellerId INT = (SELECT id FROM dbo.[User] WHERE email = N'seller@example.test');

    IF NOT EXISTS (SELECT 1 FROM dbo.Store WHERE sellerId = @SellerId)
    BEGIN
        INSERT INTO dbo.Store (sellerId, storeName, [description], bannerImageURL)
        VALUES (@SellerId, N'G4 Store', N'Checkout and shipping store', NULL);
    END;

    IF NOT EXISTS (
        SELECT 1 FROM dbo.[Address]
        WHERE userId = @BuyerId AND state = N'Hanoi')
    BEGIN
        INSERT INTO dbo.[Address] (userId, fullName, phone, street, city, state, country, isDefault)
        VALUES (@BuyerId, N'Buyer', N'0900000000', N'1 Example Street', N'Hanoi', N'Hanoi', N'Vietnam', 1);
    END;

    IF NOT EXISTS (
        SELECT 1 FROM dbo.[Address]
        WHERE userId = @BuyerId AND state = N'HCM')
    BEGIN
        INSERT INTO dbo.[Address] (userId, fullName, phone, street, city, state, country, isDefault)
        VALUES (@BuyerId, N'Buyer', N'0900000000', N'2 Example Street', N'Ho Chi Minh City', N'HCM', N'Vietnam', 0);
    END;

    IF NOT EXISTS (SELECT 1 FROM dbo.[Address] WHERE userId = @SellerId AND isDefault = 1)
    BEGIN
        INSERT INTO dbo.[Address] (userId, fullName, phone, street, city, state, country, isDefault)
        VALUES (@SellerId, N'G4 Demo Seller', N'0900000001', N'3 Demo Pickup Street', N'Hanoi', N'Hanoi', N'Vietnam', 1);
    END;

    DECLARE @Products TABLE
    (
        title NVARCHAR(255) NOT NULL,
        price DECIMAL(10,2) NOT NULL,
        weightKg DECIMAL(10,3) NOT NULL,
        [description] NVARCHAR(MAX) NOT NULL
    );

    -- Explicit fictional weights for these five demo fixtures only.
    INSERT INTO @Products (title, price, weightKg, [description])
    VALUES
        (N'Wireless Headphones', 29.90, 0.300, N'Bluetooth over-ear headphones'),
        (N'Compact Camera',      49.00, 0.600, N'Travel-friendly digital camera'),
        (N'Smart Watch',         35.50, 0.200, N'Fitness and notification watch'),
        (N'Phone Case',           9.99, 0.050, N'Protective case for smartphone'),
        (N'Portable Charger',    19.95, 0.250, N'Fast charging power bank');

    INSERT INTO dbo.Product (title, [description], price, WeightKg, images, categoryId, sellerId, isAuction, auctionEndTime)
    SELECT source.title, source.[description], source.price, source.weightKg, NULL, NULL, @SellerId, 0, NULL
    FROM @Products AS source
    WHERE NOT EXISTS
    (
        SELECT 1
        FROM dbo.Product AS existing
        WHERE existing.sellerId = @SellerId AND existing.title = source.title
    );

    UPDATE product SET WeightKg = source.weightKg
    FROM dbo.Product AS product INNER JOIN @Products AS source ON source.title = product.title
    WHERE product.sellerId = @SellerId AND product.WeightKg IS NULL;

    INSERT INTO dbo.Inventory (productId, quantity, lastUpdated)
    SELECT product.id, 20, SYSUTCDATETIME()
    FROM dbo.Product AS product
    INNER JOIN @Products AS source ON source.title = product.title
    WHERE product.sellerId = @SellerId
      AND NOT EXISTS
      (
          SELECT 1 FROM dbo.Inventory AS inventory WHERE inventory.productId = product.id
      );

    IF NOT EXISTS (SELECT 1 FROM dbo.Coupon WHERE code = N'G4SAVE10')
    BEGIN
        INSERT INTO dbo.Coupon (code, discountPercent, startDate, endDate, maxUsage, productId)
        VALUES (N'G4SAVE10', 10.00, '2024-01-01T00:00:00', '2099-12-31T23:59:59', 100, NULL);
    END;

    COMMIT TRANSACTION;
END TRY
BEGIN CATCH
    IF XACT_STATE() <> 0 ROLLBACK TRANSACTION;
    THROW;
END CATCH;
GO

SELECT
    (SELECT COUNT(*) FROM dbo.[User] WHERE email IN (N'buyer@example.test', N'seller@example.test', N'admin@example.test', N'shipper@example.test')) AS applicationUsers,
    (SELECT COUNT(*) FROM dbo.Product WHERE sellerId = (SELECT id FROM dbo.[User] WHERE email = N'seller@example.test')) AS sellerProducts,
    (SELECT COUNT(*) FROM dbo.[Address] WHERE userId = (SELECT id FROM dbo.[User] WHERE email = N'buyer@example.test')) AS buyerAddresses,
    (SELECT COUNT(*) FROM dbo.Coupon WHERE code = N'G4SAVE10') AS checkoutCoupons;
GO
