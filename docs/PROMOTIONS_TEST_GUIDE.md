# Test quản lý khuyến mãi

Đã triển khai đủ 5 loại cho seller, coupon do nền tảng tài trợ cho admin và áp dụng tại checkout buyer. Tất cả số tiền là USD.

## Database và triển khai

- Database QA: `G4_Promotions_Test_20261004`, API `http://localhost:5252`, frontend `http://localhost:5132`.
- Đây là môi trường đã dùng để kiểm thử; helper QA đã dừng sau kiểm thử để không khóa DLL. App chính vẫn dùng cấu hình/cổng hiện có (API 5251, frontend 5131).
- Database chính `CloneEbayDB` đã chạy migration sau khi người dùng duyệt bước triển khai riêng. Đối chiếu trước/sau: vẫn 0 đơn, 10 sản phẩm, 10 inventory tổng 1000 món; user, sản phẩm/URL ảnh, coupon và địa chỉ không đổi. Không đưa dữ liệu QA vào database chính.
- Migration: `20261004133301_AddPromotions`; SQL tương đương: `database/promotions-migration.sql`. Chọn đúng database trước khi chạy script. Script có transaction/rollback, kiểm tra dữ liệu coupon cũ và có thể chạy lại an toàn.
- Giữ nguyên bảng Coupon, đơn/payment/refund/ledger cũ. Coupon toàn sàn chuyển sang nguồn nền tảng cho đơn mới; các đơn cũ giữ giá và quy tắc tài chính cũ.
- Backup trước triển khai: `C:\Program Files\Microsoft SQL Server\MSSQL16.MSSQLSERVER\MSSQL\Backup\CloneEbayDB-before-promotions-20261004-212231.bak`; COPY_ONLY/CHECKSUM và RESTORE VERIFYONLY đã thành công.
- Đăng nhập demo QA: `seller@example.test`, `buyer@example.test`, `admin@example.test`; mật khẩu `G4@123456`. Dữ liệu test QA không được sao chép ngược vào database chính.

## Test nhanh trên giao diện

1. Seller → **Khuyến mãi** → **Tạo khuyến mãi**. Chọn một sản phẩm để dễ theo dõi. Cho thời gian bắt đầu trước hiện tại, kết thúc ngày mai, bỏ chọn tạm dừng.
2. Test từng loại riêng, tạm dừng các chương trình khác cùng phạm vi để dễ tính kết quả.
3. Buyer → Checkout, chọn đúng sản phẩm và bấm tính giá. Số tiền giảm, nguồn tài trợ và phí ship phải hiện trong bảng tính. Khi đổi số lượng, địa chỉ hoặc coupon phải tính lại.

| Loại | Dữ liệu thử | Kết quả mong đợi |
| --- | --- | --- |
| Giảm giá sản phẩm | Giảm 10% | Giá hàng giảm 10%; giá gốc trong Product không đổi. |
| Giảm theo số lượng | Từ 2 món giảm 15%; từ 5 món giảm 20% | 1 món không giảm; 2–4 món giảm 15%; từ 5 món giảm 20%. Số lượng tính riêng từng sản phẩm. |
| Giảm theo đơn | Hàng đủ điều kiện từ 50, giảm 5 | Hàng dưới 50 không giảm; đạt 50 giảm 5. Phí ship không tính vào ngưỡng. |
| Mã giảm giá | `SELLER10`, giảm 10%, trần 8, tổng lượt 2, mỗi buyer 1 | Nhập mã có khoảng trắng/chữ thường vẫn được chuẩn hóa; giảm tối đa 8 trên phần hàng còn lại đủ điều kiện. |
| Giảm phí vận chuyển | Hàng từ 30, miễn phí ship | Đạt 30 thì phí ship buyer trả bằng 0, vẫn thấy phí ship gốc và khoản giảm riêng. |

Sửa chương trình rồi kiểm tra phiên bản mới; tìm theo tên/mã, lọc loại/trạng thái và chuyển trang. Form chỉ hiện trường phù hợp loại được chọn. Thử tên rỗng, giảm 0, phần trăm trên 100, ngày kết thúc trước bắt đầu và bậc số lượng trùng để thấy lỗi.

## Quy tắc kết hợp

- Sale có thể kết hợp Order; Volume không kết hợp Order trong cùng giỏ.
- Mỗi sản phẩm chọn Sale hoặc Volume, không cộng cả hai; hai sản phẩm khác nhau có thể chọn khác nhau.
- Nhập coupon thì chỉ kết hợp Sale và Shipping, không kết hợp Volume/Order.
- Một chương trình giảm ship tốt nhất. Server chọn tổng thanh toán thấp nhất trong các phương án hợp lệ; tie theo ít chương trình và ID ổn định.

## Lượt dùng, ngân sách và báo giá

1. Tính giá nhiều lần: không tăng đã dùng/đang giữ.
2. Tạo đơn có coupon: đang giữ tăng 1. Hủy đơn chưa thanh toán hoặc hết 15 phút: đang giữ giảm, buyer có thể dùng lại.
3. Thẻ mô phỏng `4000000000000002` bị từ chối: đơn còn được thanh toán và lượt vẫn đang giữ. Thử lại bằng `4111111111111111`, expiry `12/30`: đã dùng tăng 1; đang giữ giảm 1.
4. Thanh toán/callback lại: không tạo thêm lượt dùng, settlement hay ledger. Hoàn tiền không trả lại lượt/ngân sách đã dùng.
5. Tổng lượt 1 hoặc ngân sách đủ đúng một đơn: hai buyer đặt đồng thời chỉ một đơn được chấp nhận, đơn còn lại nhận thông báo hết lượt/ngân sách.
6. Seller sửa/tạm dừng sau khi buyer tạo đơn: đơn vẫn dùng snapshot cũ khi thanh toán. Seller sửa sau tính giá nhưng trước tạo đơn: buyer nhận yêu cầu tính lại; không âm thầm tạo đơn với giá mới.
7. Đổi thông tin địa chỉ trên cùng ID cũng làm báo giá cũ không còn hợp lệ.
8. Khi PayPal đang xác minh, không tạo lần thanh toán khác, không hủy/expire và không giải phóng lượt trước khi có kết quả rõ ràng.

## Admin và đối soát

- Admin → Khuyến mãi: tạo mã nguồn **Nền tảng**, toàn sàn hoặc phạm vi shop/sản phẩm/danh mục; không tạo Sale/Volume/Order/Shipping.
- Admin xem chương trình seller và tạm dừng kèm lý do. Seller thấy lý do, không tự kích hoạt lại. Lịch sử hiển thị người thực hiện, thời gian và cấu hình đã lưu.
- Seller khác không được xem/sửa chương trình của shop khác, kể cả gọi API trực tiếp.

Ví dụ đối soát: hàng 100, seller giảm 10, ship 5, coupon nền tảng giảm 5 → buyer trả 90, seller gross 95. Với phí hiện tại 12% + 0.30: fee 11.70, net 83.30. Hoàn toàn bộ trả buyer 90; seller gross đảo 95, gồm khoản nền tảng 5; hoàn lại fee 11.70. Không hoàn thêm 5 cho buyer.

Đơn còn 0: seller miễn ship và coupon nền tảng giảm toàn bộ phần hàng → đơn tự Paid, payment phương thức Promotion, không gọi PayPal 0 USD. Hủy/hoàn đơn tạo refund nội bộ 0 và đảo khoản tài trợ đúng một lần. Nếu seller tài trợ toàn bộ thì gross/net/fee đều 0.

## Kiểm tra tự động

```powershell
dotnet build G4_Project.sln --configuration Release --no-restore -p:UseAppHost=false
dotnet build tests/G4.Commerce.Tests --configuration Release --no-restore -p:UseAppHost=false
dotnet tests/G4.Commerce.Tests/bin/Release/net8.0/G4.Commerce.Tests.dll
node --test tests/promotions-ui.test.cjs tests/dispute-ui.test.cjs tests/shipper-ui.test.cjs tests/ui-navigation.test.cjs
```

Trước build, dừng đúng các server đang chạy từ thư mục binary tương ứng để tránh khóa DLL trên Windows. Bộ commerce hiện có chạy cùng các kiểm tra mới về quản lý quyền, 15 tình huống pricing, reservation/snapshot/PayPal và 9 tình huống finance/refund. Test race SQL và browser desktop/mobile được thực hiện trên QA riêng.

Khởi động lại API/frontend sau khi cập nhật code. Seller/admin vào tab **Khuyến mãi**, buyer áp dụng tại **Thanh toán**. PayPal được kiểm tra timeout/reconciliation bằng gateway mô phỏng; live SQL/browser dùng thẻ mô phỏng và hoàn tiền nội bộ.
