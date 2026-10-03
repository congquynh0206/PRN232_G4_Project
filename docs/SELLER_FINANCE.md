# Nghiệp vụ tài chính người bán

Gói tài chính mô phỏng cách marketplace thu tiền buyer, trừ phí, giữ tiền trước khi cho seller rút và xử lý trường hợp seller không còn đủ tiền để hoàn lại. Tất cả số tiền đều là dữ liệu thử nghiệm; PayPal Sandbox cũng không chuyển tiền thật.

## Luồng tiền

1. Buyer thanh toán thành công. Hệ thống ghi **Gross** là toàn bộ tiền buyer đã trả.
2. Hệ thống thu **Platform Fee = 12% Gross + 0,30 USD/đơn**. Các mức này chỉnh được trong `Finance` của `appsettings.json`.
3. Phần còn lại là **Net** và vào `Processing`. Seller chưa rút được khoản này.
4. Khi đơn đã `Delivered` hoặc `Closed` và hết thời gian giữ, worker chuyển Net từ `Processing` sang `Available`.
5. Seller chỉ payout từ `Available`. Payout lần lượt đi qua `Created → InProgress → FundsSent → Completed`. Nếu giả lập ngân hàng từ chối, payout thành `Returned` và tiền được cộng lại vào `Available`.

Trong Development, `Finance:AcceleratedHoldSeconds = 30` giúp trình diễn mà không phải chờ nhiều ngày. Môi trường khác không đặt khóa này và dùng đúng số ngày theo level.

## Các số dư

| Số dư | Ý nghĩa |
| --- | --- |
| `Processing` | Tiền bán hàng đã trừ phí nhưng còn chờ giao hàng/hết hạn giữ. |
| `Available` | Tiền seller được phép payout. |
| `OnHold` | Tiền bị khóa vì tranh chấp, seller không thể rút. |
| `Negative` | Nghĩa vụ seller còn nợ hệ thống sau refund/chargeback khi các số dư khác không đủ. |

`FinancialTransaction` là sổ cái chỉ thêm mới. Mỗi giao dịch có `EntryKey` duy nhất để retry không ghi phí, refund hay payout hai lần. `SellerSettlement` là bảng đối soát theo từng order/payment; `SellerAccount` giữ số dư hiện tại; `SellerPayout` giữ lịch sử rút tiền.

## Level và giới hạn

| Level | Doanh số tối đa/tháng | Giữ tiền | Điều kiện lên cấp |
| --- | ---: | ---: | --- |
| 1 | 5.000 USD | 21 ngày | Mặc định |
| 2 | 10.000 USD | 7 ngày | 20 đơn hoàn tất, feedback dương ≥ 90%, giao đúng hạn ≥ 90% |
| 3 (`Instant`) | 1.000.000 USD | 0 ngày | 100 đơn hoàn tất, feedback dương ≥ 97%, giao đúng hạn ≥ 95% |

Doanh số được tính theo tháng UTC và tự về 0 khi sang tháng. Backend kiểm tra giới hạn trước khi gọi cổng thanh toán và kiểm tra lại lúc ghi nhận thanh toán. Nút **Đánh giá lại cấp** trên tab **Seller Finance** tính level từ dữ liệu đơn, feedback và lần giao hàng hiện có.

## Hoàn tiền và số dư âm

Khi refund thành công, hệ thống hoàn lại phần phí nền tảng theo tỷ lệ số tiền refund. Nghĩa vụ còn lại được trừ lần lượt từ tiền của settlement đang `Processing`, rồi `Available`, rồi `OnHold`. Phần vẫn còn thiếu trở thành `Negative`.

Seller có `Negative > 0` không được tạo payout. Khi có giao dịch bán mới, hệ thống tự dùng Net mới để bù `Negative` trước; phần dư mới vào `Processing`. Vì vậy seller bỏ tài khoản không làm khoản nợ biến mất trong dữ liệu. Việc khóa danh tính seller hoặc thu nợ thật nằm ngoài phạm vi mô phỏng.

## Giữ tiền khi tranh chấp

`POST /api/orders/{orderId}/fund-hold` với vai `buyer` chuyển tiền của order từ `Processing` hoặc `Available` sang `OnHold`. Endpoint nhận `reason` và dùng idempotency theo order.
Buyer có thể gọi luồng này bằng nút **Mở tranh chấp & giữ tiền** trong chi tiết đơn đã giao.

`POST /api/orders/{orderId}/fund-hold/resolve` chỉ nhận JWT của tài khoản `admin`:

- `releaseToSeller: true`: seller thắng, tiền từ `OnHold` trở lại `Available` nếu đã giao và hết hạn giữ; nếu chưa đủ điều kiện thì trở lại `Processing`.
- `releaseToSeller: false`: buyer thắng, tiền tiếp tục nằm trong `OnHold` và API tự khởi tạo refund. Nếu cổng refund lỗi, settlement ở `RefundPending`; Admin có thể thử lại và worker cũng tự retry. Chỉ khi refund thành công, khoản hold của **đúng order đó** mới được trừ và ghi fee credit đúng một lần. Nếu seller thắng trước khi đơn đủ điều kiện mở khóa tiền, khoản hold trở lại `Processing` thay vì vào `Available` sớm.

## API và giao diện

- `GET /api/seller/finance`: số dư, level, tiến độ, ledger và payout.
- `POST /api/seller/finance/evaluate-level`: tính lại level.
- `POST /api/seller/payouts`: tạo payout với `amount`, `key`, `simulateFailure`.
- `GET /api/orders/{id}`: có thêm `settlement` để seller xem Gross, phí, Net và thời điểm mở khóa.
- Trang **Seller** có khu vực tài chính hiển thị toàn bộ dữ liệu trên và cho tạo payout.

Worker `CommerceMaintenanceWorker` chạy định kỳ để backfill các payment/refund cũ, mở khóa khoản đến hạn và tiến payout. Backfill dùng khóa duy nhất nên có thể chạy lại an toàn.

## Database và cấu hình

Migration `AddSellerFinance` tạo bốn bảng `SellerAccount`, `SellerSettlement`, `FinancialTransaction`, `SellerPayout`. File SQL để duyệt hoặc chạy thủ công là [seller-finance-migration.sql](../database/seller-finance-migration.sql). Không thêm các bảng này trực tiếp vào `baseline.sql`, vì baseline là schema gốc và migration chịu trách nhiệm nâng database hiện có.

Các khóa cấu hình nằm ở `Finance` trong `backend/G4.Api/appsettings.json`; Development chỉ ghi đè thời gian giữ bằng `backend/G4.Api/appsettings.Development.json`.
