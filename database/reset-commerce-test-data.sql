/*
    Dọn dữ liệu giao dịch TEST của CloneEbayDB, giữ dữ liệu danh mục.

    Phạm vi: XÓA TOÀN BỘ đơn, thanh toán, vận đơn/tracking, trả hàng,
    refund, tranh chấp, settlement/ledger/payout và thông báo của database này.
    Giữ tài khoản, sản phẩm/URL ảnh/khối lượng/giá, địa chỉ, shop, coupon,
    cấu hình cấp/hạn mức/hold seller và lịch sử migration.
    Tồn kho của mọi sản phẩm được đặt về @RestockQuantity; bổ sung dòng thiếu.

    Trước khi thực thi:
    1. Xác nhận mọi giao dịch trong database này là dữ liệu test cần xóa.
    2. Dừng API/worker và các luồng test để không tạo giao dịch mới.
    3. Backup đầy đủ database và giữ file .bak để khôi phục khi cần.
    4. Chạy mặc định để xem số liệu. Kiểm tra rồi mới đổi @ConfirmReset=1,
       @BackupConfirmed=1 và @ExpectedOrderCount bằng số đơn đã duyệt.

    Không reset ID: tránh trùng mã vận đơn/khóa đã dùng ở cổng giả lập.
    Không dùng script này cho production hoặc database có order cần giữ.
*/
USE [CloneEbayDB];
SET NOCOUNT ON;
SET XACT_ABORT ON;

DECLARE @ConfirmReset bit = 0;
DECLARE @BackupConfirmed bit = 0;
DECLARE @ExpectedOrderCount int = 47;
DECLARE @RestockQuantity int = 100;

IF DB_NAME() <> N'CloneEbayDB'
    THROW 50001, N'Script chỉ dành cho CloneEbayDB.', 1;
IF @RestockQuantity <= 0
    THROW 50002, N'Tồn kho sau reset phải lớn hơn 0.', 1;
IF OBJECT_ID(N'dbo.PromotionUsage', N'U') IS NOT NULL
    OR OBJECT_ID(N'dbo.OrderPromotionSnapshot', N'U') IS NOT NULL
    THROW 50003, N'Database đã có dữ liệu khuyến mãi mới; cần cập nhật phạm vi script trước khi dọn.', 1;

IF @ConfirmReset = 0
BEGIN
    SELECT N'OrderTable' AS [TableName], COUNT_BIG(*) AS [RowCount] FROM dbo.OrderTable
    UNION ALL SELECT N'OrderItem', COUNT_BIG(*) FROM dbo.OrderItem
    UNION ALL SELECT N'Payment', COUNT_BIG(*) FROM dbo.Payment
    UNION ALL SELECT N'ShippingInfo', COUNT_BIG(*) FROM dbo.ShippingInfo
    UNION ALL SELECT N'ShippingEvent', COUNT_BIG(*) FROM dbo.ShippingEvent
    UNION ALL SELECT N'ReturnRequest', COUNT_BIG(*) FROM dbo.ReturnRequest
    UNION ALL SELECT N'Refund', COUNT_BIG(*) FROM dbo.Refund
    UNION ALL SELECT N'Dispute', COUNT_BIG(*) FROM dbo.Dispute
    UNION ALL SELECT N'DisputeEntry', COUNT_BIG(*) FROM dbo.DisputeEntry
    UNION ALL SELECT N'SellerSettlement', COUNT_BIG(*) FROM dbo.SellerSettlement
    UNION ALL SELECT N'FinancialTransaction', COUNT_BIG(*) FROM dbo.FinancialTransaction
    UNION ALL SELECT N'SellerPayout', COUNT_BIG(*) FROM dbo.SellerPayout
    UNION ALL SELECT N'NotificationOutbox', COUNT_BIG(*) FROM dbo.NotificationOutbox;

    SELECT p.id, p.title, i.quantity AS [CurrentQuantity],
        CASE WHEN i.id IS NULL THEN N'Sẽ thêm tồn kho' ELSE N'Sẽ đặt lại tồn kho' END AS [Action],
        @RestockQuantity AS [NewQuantity]
    FROM dbo.Product p LEFT JOIN dbo.Inventory i ON i.productId = p.id
    ORDER BY p.id;

    SELECT N'CHỈ XEM TRƯỚC. Chưa xóa hoặc thay đổi dữ liệu.' AS [Result];
    RETURN;
END;

IF @BackupConfirmed <> 1
    THROW 50004, N'Cần backup database trước khi xác nhận xóa dữ liệu test.', 1;

BEGIN TRY
    BEGIN TRANSACTION;

    -- Giữ khóa đến cuối giao dịch và kiểm tra lại số đơn đã được duyệt.
    DECLARE @ActualOrderCount bigint;
    SELECT @ActualOrderCount = COUNT_BIG(*) FROM dbo.OrderTable WITH (TABLOCKX, HOLDLOCK);
    IF @ActualOrderCount <> @ExpectedOrderCount
        THROW 50005, N'Số đơn đã thay đổi. Chạy xem trước và duyệt lại trước khi reset.', 1;

    -- Xóa con trước cha, giữ nguyên các ràng buộc khóa ngoại.
    DELETE FROM dbo.DisputeEntry;
    DELETE FROM dbo.Dispute;
    DELETE FROM dbo.ShippingEvent;
    DELETE FROM dbo.NotificationOutbox;
    DELETE FROM dbo.FinancialTransaction;
    DELETE FROM dbo.SellerPayout;
    DELETE FROM dbo.SellerSettlement;
    DELETE FROM dbo.Refund;
    DELETE FROM dbo.ReturnRequest;
    DELETE FROM dbo.ShippingInfo;
    DELETE FROM dbo.Payment;
    DELETE FROM dbo.OrderItem;
    DELETE FROM dbo.OrderTable;

    UPDATE dbo.SellerAccount
    SET ProcessingBalance = 0, AvailableBalance = 0, OnHoldBalance = 0,
        NegativeBalance = 0, MonthlySalesAmount = 0, Status = N'Active',
        SalesMonth = DATEFROMPARTS(YEAR(SYSUTCDATETIME()), MONTH(SYSUTCDATETIME()), 1),
        UpdatedAt = SYSUTCDATETIME();

    UPDATE i SET quantity = @RestockQuantity, lastUpdated = SYSUTCDATETIME()
    FROM dbo.Inventory i INNER JOIN dbo.Product p ON p.id = i.productId;

    INSERT INTO dbo.Inventory (productId, quantity, lastUpdated)
    SELECT p.id, @RestockQuantity, SYSUTCDATETIME()
    FROM dbo.Product p
    WHERE NOT EXISTS (SELECT 1 FROM dbo.Inventory i WHERE i.productId = p.id);

    IF EXISTS (SELECT 1 FROM dbo.OrderTable)
        OR EXISTS (SELECT 1 FROM dbo.Payment)
        OR EXISTS (SELECT 1 FROM dbo.FinancialTransaction)
        OR EXISTS (SELECT 1 FROM dbo.Product p WHERE NOT EXISTS (
            SELECT 1 FROM dbo.Inventory i WHERE i.productId = p.id AND i.quantity = @RestockQuantity))
        THROW 50006, N'Kiểm tra dữ liệu sau reset không đạt. Hủy toàn bộ thay đổi.', 1;

    COMMIT TRANSACTION;
    SELECT N'Đã dọn giao dịch test, số dư về 0 và bổ sung tồn kho.' AS [Result],
        @ActualOrderCount AS [DeletedOrders], @RestockQuantity AS [QuantityPerProduct];
END TRY
BEGIN CATCH
    IF @@TRANCOUNT > 0 ROLLBACK TRANSACTION;
    THROW;
END CATCH;
