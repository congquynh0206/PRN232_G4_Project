# Giỏ hàng hai seller

Buyer tạo giỏ ngẫu nhiên gồm 2–5 sản phẩm khác nhau từ đúng hai seller. Các seller phải có địa chỉ lấy hàng mặc định; hàng phải có giá, khối lượng và còn tồn kho. Không bổ sung trang danh sách hoặc chi tiết sản phẩm.

Giỏ lưu trong bảng `BuyerCart` theo buyer đăng nhập, khôi phục khi mở lại trang hoặc đăng nhập lại. Buyer đổi số lượng (1–5), xóa sản phẩm hoặc tạo giỏ mới. Tạo giỏ mới thay thế giỏ cũ. Giỏ chưa giữ tồn kho hay lượt khuyến mãi. Phiên bản giỏ ngăn hai tab ghi đè thay đổi; khi báo xung đột, bấm **Tải lại giỏ**. Nếu xóa hết hàng của một seller, checkout một seller vẫn hoạt động.

**G1/G2 — sửa giới hạn server ngày 19/10/2026:** `POST /api/cart/save` kiểm tra toàn bộ seller của các dòng được giữ lại, không chỉ các dòng thêm/tăng số lượng; từ chối hơn hai seller trước khi ghi giỏ hoặc đổi version. Giỏ cũ đã lưu ba seller có thể được giảm còn một/hai seller hoặc xóa hết; hàng không còn bán được vẫn xóa được. `POST /api/quote` và tạo đơn mới qua `POST /api/orders` cùng kiểm tra số lượng mỗi dòng 1–5, như luồng cart, nên không vượt giới hạn bằng API tạo đơn cũ. Service trả ArgumentException cho đầu vào sai, được exception handler API ánh xạ HTTP 400. Retry đúng key/nội dung của đơn đã tồn tại giữ snapshot cũ, kể cả đơn lịch sử có số lượng vượt 5; key mới vẫn bị chặn.

Từ bản hợp nhất 19/10, checkout có sổ địa chỉ thêm/sửa/xóa/mặc định và tính lại báo giá từng seller. Địa chỉ đang dùng trong đơn được lưu lịch sử khi xóa khỏi sổ để không làm hỏng retry tạo đơn. Xem [BUYER_ADDRESS_BOOK.md](BUYER_ADDRESS_BOOK.md). Không có migration mới cho phần sổ địa chỉ.

Checkout hiển thị sản phẩm, mã giảm giá, phí ship và tổng riêng theo seller. Một mã áp dụng trên mỗi đơn seller; mã toàn sàn áp dụng cho hai đơn sẽ giữ hai lượt và phải đáp ứng hạn mức của từng đơn. Server xác nhận lại giá và tồn kho, rồi tạo một đơn cho mỗi seller trong cùng một transaction SQL Server. Lỗi ở bất kỳ seller nào rollback cả nhóm. Bảng `CheckoutBatch` lưu khóa và đơn đã tạo; retry đúng request trả về cùng đơn, kể cả khi giỏ đã được xóa.

Sau khi tạo đơn thành công, hàng được chuyển khỏi giỏ sang **Đơn của tôi**. Thẻ giả lập thanh toán từng đơn, hiển thị đơn còn thất bại để thử lại. PayPal chuyển hướng cho đơn đầu tiên còn chờ; sau khi trở về, mở đơn còn lại trong **Đơn của tôi** và chọn **Thanh toán PayPal**. Các đơn có vận chuyển, hủy, trả hàng, settlement và tiền seller riêng theo luồng hiện có.

## Chuẩn bị database

Dừng API, chạy migration trên database đã có baseline và các migration trước:

```powershell
dotnet ef database update --project backend/G4.Infrastructure --startup-project backend/G4.Api
sqlcmd -S localhost -E -C -i database/seed-data.sql
```

Migration mới: `20261008083121_AddBuyerCartCheckoutBatch`. SQL để duyệt: [buyer-cart-migration.sql](../database/buyer-cart-migration.sql). Seed thêm `seller2@example.test`, shop, địa chỉ HCM và năm sản phẩm riêng; mật khẩu Development giống seller thứ nhất. Seed không khôi phục tồn kho đã bán. Khởi động lại API/frontend sau khi cập nhật schema.

## Kiểm thử

Đợt sửa G1/G2 ngày 19/10: solution build output riêng đạt 0 warning/error; `--checkout-limits` và bộ backend mặc định đạt; UI Node 115/115 đạt. Regression dùng InMemory xác nhận chặn seller thứ ba kể cả dòng không đổi, sửa giỏ cũ, xóa hàng không còn bán được, giỏ một/hai seller/rỗng, số lượng -1/0/6/int.MaxValue bị chặn ở save/quote/create, biên 1/5 hợp lệ, request bị từ chối không đổi giỏ/version/stock/order/batch/payment/promotion usage/ledger/outbox và retry không trừ tồn hai lần. Test riêng đã tái hiện G1 trên source trước sửa. Chưa chạy SQL QA hoặc HTTP/browser E2E mới cho đợt này. Không có migration mới, không áp migration/seed hoặc thay database chính; khởi động lại API để dùng binary mới.

```powershell
dotnet build G4_Project.sln --artifacts-path artifacts/checkout-limits-validation -m:1 --disable-build-servers -p:NuGetAudit=false
dotnet artifacts/checkout-limits-validation/bin/G4.Commerce.Tests/debug/G4.Commerce.Tests.dll --checkout-limits
```

```powershell
dotnet build G4_Project.sln --no-restore
dotnet run --project tests/G4.Commerce.Tests --no-restore
node --test tests/buyer-cart-ui.test.cjs tests/promotions-ui.test.cjs tests/ui-navigation.test.cjs
```

SQL checks chỉ chạy trên database QA riêng `G4_MultiSeller_Test_20261008`, đã có baseline, migration và seed:

```powershell
$env:G4_CART_SQL_TEST_CONNECTION = 'Server=localhost;Database=G4_MultiSeller_Test_20261008;Integrated Security=True;TrustServerCertificate=True'
dotnet run --project tests/G4.Commerce.Tests --no-build -- --cart-sql
```

Kiểm tra tay: random 2/3/5 sản phẩm → tải lại trang vẫn có giỏ → sửa/xóa → báo giá từng seller → checkout tạo hai đơn → thanh toán thẻ/PayPal → đăng nhập từng seller chỉ thấy đơn của mình. Thử thẻ bị từ chối và thanh toán lại; thử hai tab sửa cùng giỏ và hai checkout cùng khóa.

Đã xác minh ngày 08/10/2026: build không có lỗi/cảnh báo; bộ kiểm thử commerce và 51 kiểm thử JavaScript đều qua. SQL QA xác minh rollback khi coupon ở seller thứ hai hết lượt, checkout đồng thời không tạo trùng, sửa giỏ đồng thời chỉ chấp nhận một phiên và thanh toán hai đơn độc lập. Trình duyệt QA xác minh khôi phục giỏ sau reload, tạo/thanh toán hai đơn bằng thẻ giả lập và bố cục không tràn ngang ở 1280 px/390 px. PayPal chuyển hướng và các đơn còn lại được kiểm thử bằng dữ liệu giả; chưa kiểm thử giao dịch PayPal Sandbox thật cho đợt này. Database ứng dụng `CloneEbayDB` chưa áp dụng migration mới.
