# Đồng hồ demo và tiền giữ theo từng đơn (23/10/2026)

Theo yêu cầu đã duyệt: đặt đồng hồ ngay tại thao tác/trạng thái có deadline, cập nhật mỗi giây, gần hết hạn đổi màu. Dùng thời gian server và deadline thực tế; tải lại/chuyển tab không bắt đầu lại thời hạn. Không tạo timer cho bước chưa có deadline như payout, xử lý refund pending, seller chưa duyệt return thường hoặc các nhánh RT2/INR1 chưa triển khai.

## Các vị trí

| Vị trí | Nguồn thời hạn | Khi hết giờ |
| --- | --- | --- |
| Danh sách/chi tiết đơn chưa thanh toán | Order.PaymentExpiresAt | Chờ đối soát/hết hạn từ hệ thống; không tự coi payment thất bại |
| Buyer yêu cầu trả hàng và popup báo giá | ReturnWindowClosesAt | Hết hạn mở mới; hồ sơ gửi hợp lệ tiếp tục |
| Seller/Buyer xem hàng trả thường; tab trả hàng Seller | ReturnRequest.ConfirmationDueAt | Chờ hệ thống xác nhận và refund; chưa phải refund thành công |
| Tranh chấp chờ seller/buyer | DisputeSummary.ResponseDueAt | Seller im lặng chờ chuyển admin; buyer im lặng chờ đóng, không tự refund |
| Trả hàng theo thỏa thuận tranh chấp | ReturnConfirmationDueAt do server tính theo policy của dispute | Chờ thực hiện thỏa thuận/refund |
| Đổi địa chỉ Buyer/Seller/Shipper | AddressChangeRequest.ExpiresAt | Hết hạn xác nhận; giữ địa chỉ cũ tới khi có kết quả thực tế |
| Ví Seller và chi tiết đơn | HoldStartedAt/ReleaseAt/HoldSecondsSnapshot | Đủ thời gian vẫn phải kiểm tra trạng thái đơn/return/dispute |
| Cấp seller của Seller/Admin | NextEvaluationAt | Chờ lượt tự đánh giá; không tự đổi level |
| Nợ của Seller/Admin | WarningAt, DueAt, EarliestWriteOffAt | Chờ hệ thống kiểm tra; mốc write-off chỉ là dự kiến nếu không có thu hồi mới |

Hạn nhận hàng trả cũ thiếu ConfirmationDueAt được API suy ra từ Return Delivered và policy fallback giống worker; không ghi lại dữ liệu. Thời hạn trong thỏa thuận dùng Disputes:ReturnReceiptSeconds riêng, không lấy nhầm Returns:ReceiptSeconds. Khi có tranh chấp khác cản trở, không hiển thị lời hứa tự refund return thường.

## Tiền giữ theo từng đơn

Seller mở **Ví & Rút tiền → Tiền giữ theo từng đơn**. Mỗi dòng có mã đơn, số tiền Processing còn lại, số tiền OnHold còn lại, level/chính sách đã chốt, thời điểm bắt đầu và hạn đã lưu. Đơn chưa giao chưa có ReleaseAt thì ghi chưa bắt đầu đếm. Có thể mở đơn hoặc tranh chấp từ dòng tương ứng; danh sách có phân trang.

API mới **GET /api/seller/finance/funds/page?page=1&pageSize=10** chỉ cho seller đang đăng nhập; bỏ qua mọi seller ID do client tự suy diễn. Chi tiết order chỉ bổ sung sellerFunds khi người đọc là seller sở hữu đơn. Không mở rộng quyền Buyer/Shipper/Admin vào endpoint này.

- Processing lấy ProcessingAmount hiện tại của settlement, không dùng Net ban đầu sau khi đã có refund/chi phí.
- OnHold lấy FundHold mới nhất trừ FundHoldConsumed và HoldReleased sau bút toán đó, như cách service tài chính tính khoản còn giữ. Bút toán giữ cũ không cộng lại; khoản đã giải phóng/tiêu hết không hiện như tiền còn giữ.
- Dùng snapshot/lịch cũ của từng settlement. Không tính lại thời hạn theo level/cấu hình hiện tại. Có return/dispute hoặc trạng thái chưa đủ điều kiện thì hiển thị lý do chặn ngay cả khi đồng hồ hết.
- Truy vấn chỉ đọc, dùng transaction Serializable trên SQL để đọc settlement, ledger và hồ sơ cản trở nhất quán. Không ghi tiền/ledger/deadline, không tạo hay chạy migration.

## Cập nhật và khả năng truy cập

countdowns.js dùng chung cho bốn role. common.api đồng bộ đồng hồ từ X-Server-Time do proxy chuyển tiếp từ Date của API; dùng thời gian đã trôi đơn điệu để tránh người dùng đổi đồng hồ máy làm timer nhảy. Ngày UTC từ SQL thiếu hậu tố được hiểu là UTC. Dữ liệu thiếu/sai deadline không sinh đồng hồ giả.

Đồng hồ dùng role=timer và aria-live=off, không đọc thành tiếng mỗi giây; nhãn và trạng thái hết hạn vẫn là chữ có thể đọc. Không dùng màu làm dấu hiệu duy nhất. Chuỗi giờ và trạng thái dài xuống dòng trên mobile.

Khi hết hạn chỉ yêu cầu đọc lại dữ liệu, mỗi deadline/phạm vi báo một lần để tránh vòng lặp tải. Tiền giữ/cấp seller/nợ đang xem có đọc lại định kỳ 10 giây; timer không gửi POST, không tự hoàn tiền, đổi level, cập nhật địa chỉ hoặc mở khóa ví. Không làm mới phần đang được tương tác/đọc lịch sử ở danh sách tiền giữ hoặc cấp seller. Các cơ chế realtime/refresh chi tiết cũ vẫn hoạt động.

## Kiểm thử

- Build solution, Node UI và backend console theo PROJECT_RUN. Bộ riêng backend: `dotnet run --project tests/G4.Commerce.Tests -- --seller-funds`.
- `tests/countdowns-ui.test.cjs`: UTC, lệch giờ, đổi đồng hồ máy, đếm tới 0, refresh một lần, không deadline giả, không thông báo lặp mỗi giây, bucket tiền riêng và lý do chặn.
- `SellerFundsChecks`: quyền seller, phân trang, hold bị tiêu một phần/đã trả, snapshot cũ, return/dispute và hai policy receipt khác nhau; dịch truy vấn thật sang SQL Server mà không mở kết nối database.
- `tests/countdowns.e2e.cjs`: build frontend rồi chạy với Playwright trong NODE_PATH; FRONTEND_DLL chọn đầu ra build riêng, CHROME_PATH chọn Chrome. Dùng Razor thật và login/API fixture; không kết nối SQL/provider. Kiểm tra bốn role ở 320/390/1280px, lệch đồng hồ 9 giờ, reload, các vị trí timer, tiền giữ từng đơn, không tràn và không POST. Ảnh ở `artifacts/countdown-ui/`.

Kết quả đợt 23/10/2026: solution build 0 warning/error, backend mặc định và Node 132/132 đạt; Chrome countdown, điều hướng và hồi quy RT1/PAY1 desktop/mobile đạt. SQL chỉ kiểm tra dịch truy vấn, chưa QA trực tiếp trên database/provider. Log `artifacts/countdown-validation/`.

Không suy từ QA fixture thành trạng thái database chính. Không có migration mới; khởi động lại API/frontend và tải lại trang để nhận payload/script/view mới.
