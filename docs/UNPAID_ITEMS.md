# Đơn Buy It Now chưa thanh toán và điểm phạt Unpaid Item (07/11/2026)

Theo yêu cầu mới nhất của người dùng (ảnh nghiệp vụ "Buy It Now thông thường"). Yêu cầu này
thay quy tắc hết hạn tự động sau 15 phút trong đặc tả 18/10 cho môi trường chạy bằng
`appsettings`.

## Quy tắc

| Bước | Hành vi |
| --- | --- |
| Tạo đơn | Đơn `AwaitingPayment`, giữ tồn ngay. `PaymentExpiresAt = now + Checkout:PaymentHoldMinutes`. |
| Giữ hàng | 4 ngày (5760 phút) mặc định; Development 5 phút. Không ai khác mua được phần tồn đang giữ. |
| Đồng hồ | `countdowns.js` đếm tới `PaymentExpiresAt`. Hết giờ, đơn nằm ở mục Cần xử lý của seller. |
| Quá hạn | Worker **không** tự hủy (`Checkout:AutoExpireUnpaid=false`). Nút **Hủy đơn quá hạn thanh toán** mở cho seller. |
| Seller hủy | `POST api/seller/orders/{id}/cancel-unpaid` (nhập lại mật khẩu). Đơn `Cancelled`, `CancelReasonCode=UnpaidItem`. |
| Hoàn kho | `stock = stock + quantity` đúng một lần; giải phóng lượt promotion. |
| Điểm phạt | Thêm một dòng `UnpaidItemStrike` (OrderId duy nhất nên retry không cộng hai lần). |
| Khóa tài khoản | Đủ `Checkout:UnpaidStrikeLimit` (2) điểm trong `Checkout:UnpaidStrikeWindowMinutes` thì `User.BannedAt` được đặt. |
| Tài khoản bị khóa | Không đăng nhập được (403 kèm lý do) và không tạo được đơn mới. |

Seller chỉ hủy được khi đơn còn `AwaitingPayment`, đã quá `PaymentExpiresAt`, không có payment
`Verifying`/`Succeeded` và thuộc đúng seller. Thanh toán sau hạn vẫn bị chặn như trước.

## Cấu hình

| Khóa | appsettings.json | Development | Nếu thiếu khóa (code/test cũ) |
| --- | --- | --- | --- |
| `Checkout:PaymentHoldMinutes` | 5760 | 5 | 15 |
| `Checkout:AutoExpireUnpaid` | false | (kế thừa) | true |
| `Checkout:UnpaidStrikeLimit` | 2 | (kế thừa) | 2 |
| `Checkout:UnpaidStrikeWindowMinutes` | 43200 (30 ngày) | 60 | 43200 |

## Dữ liệu

Migration `20261107090000_AddUnpaidItemStrikes` (chưa áp dụng lên database nào): thêm
`User.BannedAt`, `User.BanReason`, `OrderTable.CancelReasonCode` và bảng `UnpaidItemStrike`.
Chạy `dotnet ef database update` trên database có dữ liệu; không chạy lại baseline.

## Thay đổi 11/11/2026: thanh toán riêng một người bán

Nút **Buy It Now (Không ép trả ngay)** và `POST api/cart/buy-now` đã bị gỡ theo thiết kế
kiểu eBay ([quyết định](BUSINESS_REQUIREMENTS.md)). Thay vào đó, khi giỏ có từ hai người
bán, mỗi nhóm có **Chỉ thanh toán người bán này**: mở tab Thanh toán chỉ với nhóm đó và
dùng `POST api/cart/checkout` với một seller; đơn được tạo rồi thu tiền ngay theo phương
thức đã chọn. Chỉ nhóm đã thanh toán rời giỏ. Thanh toán lỗi thì đơn ở `AwaitingPayment`
và áp dụng hạn giữ hàng / Unpaid Item như mô tả ở trên. Test: `PayOnlyOneSellerAsync`
trong `--unpaid-items` và test 8 trong `tests/buyer-cart-ui.test.cjs`.

Phần dưới là mô tả nút cũ, chỉ giữ làm lịch sử.

### (Đã gỡ) Nút Buy It Now trong giỏ hàng

Mỗi nhóm seller trong tab Giỏ hàng có nút **Buy It Now (Không ép trả ngay)** ở cuối khung;
số lượng, nút Xóa và giá nằm bên trái nút.

| Bước | Hành vi |
| --- | --- |
| Bấm nút | `POST api/cart/buy-now` với `sellerId`, địa chỉ đang chọn, `expectedTotal`, `pricingFingerprint`, phiên bản giỏ. |
| Kết quả | Tạo **một** đơn của seller đó ở trạng thái `AwaitingPayment` (UNPAID), giữ tồn ngay, chưa thu tiền. |
| Giỏ hàng | Chỉ bỏ các dòng của seller đó; seller còn lại giữ nguyên, phiên bản giỏ đổi. |
| Màn hình | Vẫn ở tab Giỏ hàng, hiện thông báo có mã đơn và hạn giữ hàng, nhắc vào Đơn hàng để Thanh toán PayPal. |
| Retry | Cùng khóa trả lại đúng đơn cũ; phiên bản giỏ cũ bị từ chối nên không tạo đơn thứ hai. |

Chưa có địa chỉ giao hàng thì nút chuyển sang bước Thanh toán và mở sổ địa chỉ. Giá/khuyến mãi
đổi so với báo giá đã xem thì server từ chối và buyer phải cập nhật báo giá.
Nút "Tiếp tục thanh toán" cũ vẫn thanh toán ngay cả giỏ như trước.

Test thêm: `BuyItNowAsync` trong `--unpaid-items` (backend) và test 8 trong
`tests/buyer-cart-ui.test.cjs` (Node, đã chạy: 8/8 đạt).

## Kiểm thử

`dotnet run --project tests/G4.Commerce.Tests -- --unpaid-items` (InMemory): hạn giữ theo
cấu hình, seller chỉ hủy sau hạn, đúng seller, một strike mỗi đơn, hoàn kho một lần, hai strike
khóa tài khoản và chặn tạo đơn, strike cũ ngoài cửa sổ không tính, đơn đã Paid không bị hủy.

Trạng thái: mã nguồn đã viết và kiểm tra cú pháp; **chưa build/chạy test** vì môi trường viết
code không tải được gói NuGet. Hãy build solution và chạy bộ test trước khi demo.
