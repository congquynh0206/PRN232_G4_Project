# Kế hoạch: Email, nhật ký tích hợp và chi tiết thanh toán/hoàn tiền

Ngày đề xuất: 28/09/2026. Cập nhật 05/10/2026: **Phần A/B đã triển khai code và kiểm thử QA; migration database chính chờ duyệt riêng. Phần C chưa triển khai.**

Các mục bên dưới giữ đề xuất ban đầu để tham khảo. Thiết kế đã duyệt và thực thi A/B xem [email-integration-design](superpowers/specs/2026-10-05-email-integration-design.md), cách dùng/test xem [EMAIL_INTEGRATION_TEST_GUIDE.md](EMAIL_INTEGRATION_TEST_GUIDE.md). Phiên bản thực thi dùng JWT hiện có, Capture mặc định, admin retry và phân quyền theo đơn; không áp dụng những giả định role giả lập hoặc SMTP mặc định trong đề xuất cũ.

## 1. Mục tiêu và phạm vi

1. Buyer nhận được email khi thanh toán, giao hàng hoặc hoàn tiền có kết quả.
2. Người vận hành demo tra được một đơn đã gọi dịch vụ nào, thành công hay lỗi và thử lại bao nhiêu lần.
3. Buyer/seller xem rõ tiền đã thanh toán và hoàn lại trong chi tiết đơn.

Giữ một seller, buyer/seller giả lập trong Development, PayPal Sandbox, Credit giả lập, USD và hoàn tiền toàn bộ theo nghiệp vụ hiện tại. Không bổ sung COD, hoàn tiền một phần, đăng nhập thật, Redis, webhook, microservice hoặc hạ tầng nhiều backend trong đợt này.

Mặc định đề xuất: dùng SMTP mail catcher cục bộ để demo (ví dụ Mailpit). Gửi đến Gmail/hộp thư bên ngoài là cấu hình tùy chọn sau; không bắt buộc cung cấp tài khoản email để triển khai. Hộp thư thử nghiệm nhận thư do ứng dụng gửi nhưng không chuyển thư ra Internet.

## 2. Điểm xuất phát đã kiểm tra trong code

- `NotificationOutbox` và `CommerceMaintenanceWorker` đã tồn tại. Worker gửi SMTP nếu có `Mail:Host`; nếu không, đánh dấu `Captured`. Lỗi SMTP hiện tăng Attempts nhưng chưa có giới hạn, lịch retry và màn thử lại rõ ràng.
- Đã phát thông báo PaymentSucceeded, Delivered, DeliveryFailed và Refunded. Cơ chế chống trùng hiện dựa vào OrderId + EventType.
- Payment đã lưu ProviderOrderId, ProviderTransactionId, ErrorCode và các mốc thời gian. Refund đã có ProviderRefundId, số tiền, tiền tệ, lý do và thời gian hoàn tất.
- Đã có log console và retry tạo vận đơn, nhưng chưa có bảng/màn nhật ký tích hợp tập trung.
- My Orders và Seller & Tracking đã có chi tiết đơn, tracking hai chiều và thông tin hoàn tiền cơ bản.

## 3. Phần A — Email

### Người dùng sẽ thấy gì?

Tab Email demo hiển thị mã đơn, loại thông báo, người nhận, tiêu đề, trạng thái, số lần gửi, thời điểm và lỗi gần nhất. Có bộ lọc theo đơn/trạng thái và nút xem nội dung. Seller giả lập có nút “Thử gửi lại” cho thư Failed; buyer chỉ xem.

Mẫu thư tiếng Việt gồm mã đơn, nội dung kết quả, số tiền/tiền tệ nếu liên quan và mã vận đơn khi có. Có HTML và nội dung text; dữ liệu động phải được encode. Liên kết chi tiết đơn chỉ thêm khi frontend có địa chỉ mở đúng đơn được kiểm chứng.

### Quy tắc đề xuất

- Bốn loại thư: thanh toán thành công; giao tới buyer thành công; giao tới buyer thất bại; hoàn tiền thành công.
- Chỉ tạo thư sau khi kết quả nghiệp vụ tương ứng được lưu thành công. Lưu outbox cùng giao dịch thay đổi trạng thái khi có thể để tránh lưu đơn mà mất thông báo.
- Giữ phạm vi hiện tại: một thư cho mỗi loại sự kiện của một đơn; callback lặp không sinh thư mới. Chưa gửi một email riêng cho từng lần giao thất bại.
- Hai chế độ rõ ràng: Capture (chỉ lưu để xem) và Smtp (gửi tới SMTP). Không tự chuyển một lỗi SMTP thành Captured.
- Trạng thái gửi: Pending → Processing → Sent; lỗi tạm thời trở về Pending có NextAttemptAt; hết lượt chuyển Failed. Capture dùng Captured.
- Mỗi đợt tối đa 3 lần gửi tổng cộng; sau lỗi lần 1 đợi 30 giây, sau lỗi lần 2 đợi 2 phút. Worker có thể xử lý muộn hơn vài giây theo chu kỳ quét.
- “Thử gửi lại” mở một đợt mới trên cùng thông báo Failed, giữ tổng số lần đã thử và lịch sử cũ. Không cho thử lại thư Sent hoặc đang xử lý.
- Claim bản ghi bằng cập nhật có điều kiện; có hạn giữ Processing để khôi phục sau khi ứng dụng dừng đột ngột. Chặn hai lần bấm retry đồng thời.
- SentAt chỉ có khi SMTP chấp nhận thư. Captured có thời điểm capture riêng; dữ liệu Captured cũ cần được chuyển đổi cho đúng ý nghĩa.
- Sent không có nghĩa người nhận đã đọc hoặc thư chắc chắn vào Inbox. SMTP không bảo đảm gửi đúng một lần: nếu dừng sau khi SMTP nhận nhưng trước khi lưu Sent, retry có thể gửi lặp. Dùng Message-ID ổn định để hỗ trợ truy vết, không tuyên bố loại bỏ hoàn toàn khả năng này.

### Cấu hình và dữ liệu

- Tách `IEmailSender`, SMTP sender và dịch vụ xử lý outbox khỏi worker để kiểm thử bằng sender giả.
- Cấu hình dự kiến: Mail:Mode, Host, Port, From, EnableSsl, Username, Password; mật khẩu đặt trong User Secrets/biến môi trường, không commit.
- Khi chưa đặt Mode, giữ tương thích cấu hình cũ: có Host thì Smtp, không có Host thì Capture. Tài liệu mới yêu cầu đặt Mode rõ ràng.
- Bổ sung outbox: AttemptsInCycle, NextAttemptAt, LastAttemptAt, LastErrorCode, LastErrorSummary, ProcessingUntil, CapturedAt; giữ Attempts làm tổng số lần đã thử. Chốt kiểu/độ dài khi viết migration.
- Không cần thay email seed để nhận thư trong mail catcher. Với SMTP ngoài Internet, phải cấu hình địa chỉ nhận thử hợp lệ; không tự gửi ra ngoài trong kiểm thử tự động.

## 4. Phần B — Nhật ký tích hợp

### Người dùng sẽ thấy gì?

Tab “Nhật ký tích hợp” dành cho seller/vận hành demo, có phân trang và lọc theo mã đơn, dịch vụ, kết quả, thời gian. Từ chi tiết đơn có thể mở nhật ký đã lọc theo đơn đó.

Ví dụ:

```text
Đơn #12 | PayPal  | Capture        | Thành công | 850 ms
Đơn #12 | Carrier | CreateLabel #1 | Lỗi 503    | 220 ms
Đơn #12 | Carrier | CreateLabel #2 | Thành công | 310 ms
Đơn #12 | Email   | Send #1        | Thành công | 120 ms
```

### Nội dung và quy tắc

- Bảng IntegrationLog dự kiến: Id, OrderId (nullable), CorrelationId, Service, Operation, EntityId (nullable), Attempt, Outcome, HttpStatus (nullable), DurationMs, ProviderReference, ErrorCode, ErrorSummary, CreatedAt.
- Index cho OrderId + CreatedAt và Service + CreatedAt; kết quả phân trang mặc định 20 dòng, tối đa 100.
- Ghi từng lần gọi thực tế: PayPal OAuth/create/capture/query/refund, tạo vận đơn, gửi email. Credit giả lập có nhãn rõ “Simulated”, không giả thành giao dịch từ nhà cung cấp thật.
- CorrelationId nối các thao tác trong một request; worker tạo mã riêng và liên kết theo OrderId/NotificationId. Không dùng mã ngẫu nhiên mới cho từng dòng thuộc cùng request.
- Ghi mã giao dịch, mã refund, mã vận đơn hoặc debug ID nhà cung cấp nếu có; không lưu request/response nguyên bản.
- Không lưu Authorization, access token, Client Secret, mật khẩu SMTP, số thẻ/CVV hoặc nội dung chứa dữ liệu cá nhân không cần thiết. Lỗi được rút gọn và che dữ liệu trước khi lưu.
- Outcome phân biệt Succeeded, Failed, Unknown cho trường hợp timeout/chưa biết kết quả. Một cuộc gọi lỗi không tự động đồng nghĩa đơn thanh toán thất bại.
- Nhật ký phục vụ chẩn đoán; Payment/Refund mới là nguồn xác định trạng thái tiền. Ghi log lỗi không được kích hoạt gọi thu/hoàn tiền lần nữa.
- Ghi nhật ký bằng cơ chế tách khỏi DbContext nghiệp vụ đang bị lỗi; nếu lưu nhật ký thất bại, ghi cảnh báo console đã che dữ liệu, không đổi một giao dịch đã thành công thành thất bại.
- Màn này dùng giới hạn role giả lập hiện có và chỉ Development; không tuyên bố là phân quyền production. Chưa có sửa/xóa log trên UI.

## 5. Phần C — Chi tiết thanh toán và hoàn tiền

Trong My Orders và Seller & Tracking, thêm khối “Thanh toán & hoàn tiền”, gồm:

1. Tổng quan: tổng đơn, tổng thanh toán thành công, tổng hoàn tiền thành công, số tiền còn lại sau hoàn tiền.
2. Lịch sử các lần thanh toán: phương thức, số tiền USD, trạng thái, thời gian, mã giao dịch và lỗi thân thiện nếu có. Credit ghi rõ giả lập; PayPal ghi Sandbox.
3. Lịch sử hoàn tiền: số tiền, lý do, trạng thái, thời điểm yêu cầu/hoàn tất, mã refund và giao dịch thanh toán liên quan.

Quy tắc:

- Không chỉ lấy bản ghi thanh toán cuối: hiển thị cả lần bị từ chối, đang xác minh và lần thành công.
- Tổng đã thu chỉ cộng Payment thành công; tổng đã hoàn chỉ cộng Refund Succeeded. Không tính tiền pending/failed như đã thu hoặc đã hoàn.
- Không tự coi đơn Cancelled/Closed là đã hoàn tiền. Hiển thị trạng thái tiền độc lập trạng thái đơn.
- Dữ liệu thiếu hiển thị “Chưa có”; không tạo mã giao dịch giả để lấp chỗ trống. Trường hợp nhiều thanh toán thành công bất thường phải hiển thị trung thực và cảnh báo để kiểm tra.
- Hiển thị giờ địa phương; dữ liệu backend giữ UTC. Thứ tự lịch sử ổn định theo thời gian và Id.
- Đây là màn đọc dữ liệu, không thêm nút thu tiền/hoàn tiền mới, không làm lại giao dịch khi refresh.

## 6. API dự kiến

Giữ tương thích API chi tiết đơn hiện có. Chốt DTO cụ thể trong triển khai:

| API | Mục đích | Vai trò demo |
|---|---|---|
| GET /api/inbox | Giữ route cũ; bổ sung trường trạng thái gửi cần thiết | Buyer/seller |
| GET /api/notifications | Danh sách thư có lọc/phân trang | Buyer/seller |
| GET /api/notifications/{id} | Nội dung và trạng thái một thư | Buyer/seller |
| POST /api/seller/notifications/{id}/retry | Retry thư Failed bằng cập nhật có điều kiện | Seller |
| GET /api/seller/integration-logs | Nhật ký có lọc/phân trang | Seller |
| GET /api/orders/{id}/financial-details | Tổng hợp, lịch sử thanh toán và hoàn tiền | Buyer/seller |

Frontend tiếp tục đi qua proxy hiện có; không đưa key/secret xuống trình duyệt.

## 7. Thứ tự triển khai và task Jira đề xuất

Triển khai kỹ thuật bắt đầu bằng schema dùng chung, sau đó hoàn thiện lần lượt email → log → chi tiết tiền. Mỗi task có thể là một commit; task có file dùng chung cần stage theo từng thay đổi, không gom cả file khi chứa nhiều task chưa tách.

| Task | Đầu ra | File/khu vực dự kiến | Khó 1–5 |
|---|---|---|---:|
| T1. Schema và migration | Outbox mở rộng, IntegrationLog, migration nâng cấp DB đang có | G4.Domain/Entities; G4.Infrastructure/Persistence; database script nâng cấp mới | 3 |
| T2. Gửi email và template | Sender SMTP/Capture, template tiếng Việt, cấu hình | G4.Application/Integrations; G4.Infrastructure/Notifications; G4.Api/appsettings.json | 3 |
| T3. Retry email và màn quản lý | Worker, claim/retry, API, bộ lọc, xem thư/thử lại | CommerceMaintenanceWorker.cs; CheckoutService.cs; NotificationsController.cs; frontend Views/Checkout, checkout.js, checkout.css | 4 |
| T4. Thu thập nhật ký | Correlation, thời gian gọi, lỗi đã che, từng attempt | G4.Infrastructure/Diagnostics (mới); PayPalSandboxGateway.cs; HttpCarrierGateway.cs; ShipmentService.cs; dịch vụ email; luồng Credit | 4 |
| T5. Màn nhật ký | API lọc/phân trang và UI tra theo đơn | G4.Api/Controllers; frontend Views/Checkout, checkout.js, checkout.css | 3 |
| T6. Chi tiết tiền | DTO tổng hợp và lịch sử Payment/Refund, UI | G4.Contracts; G4.Api/Controllers; frontend/wwwroot/js/checkout.js, css/checkout.css | 3 |
| T7. Kiểm thử và hướng dẫn | Test nghiệp vụ, SQL, SMTP, UI; tài liệu chạy VS | tests/G4.Commerce.Tests/Program.cs; tests/smoke-commerce.ps1; docs/PROJECT_RUN.md; docs thiết kế hiện có | 3 |

Các test quan trọng được viết/chạy cùng task tương ứng; T7 là kiểm tra tích hợp cuối, không hoãn toàn bộ kiểm thử đến cuối.

Không sửa baseline để bắt người đang có DB tạo lại. Tạo migration/script nâng cấp mới, giữ dữ liệu cũ; kiểm tra nullable/default và backfill Captured. Dữ liệu tiền và lịch sử đơn phải giữ nguyên sau nâng cấp. Khi dùng SQL Server thật, xác định database thử riêng trước khi chạy các test tạo dữ liệu/lỗi.

## 8. Tiêu chí nghiệm thu

### Email

- Thanh toán, giao thành công, giao thất bại và hoàn tiền đều sinh đúng loại thư.
- Capture không gọi SMTP, không hiển thị là đã gửi. SMTP local nhận được thư đúng nội dung; tắt SMTP tạo lỗi có thể tra cứu.
- Retry đúng lịch, tối đa 3 lần mỗi đợt; Failed có thể thử lại; không gửi lại Sent khi bấm/reload.
- Callback lặp không tạo thêm thư cùng OrderId + EventType; test SQL xác nhận unique constraint thực sự hoạt động.
- Khôi phục bản ghi Processing hết hạn; kiểm thử đồng thời không claim cùng thông báo hai lần trong cùng thời hạn.

### Nhật ký

- Mỗi lần gọi có dịch vụ, thao tác, thời gian, kết quả và liên kết đơn phù hợp. Timeout được thể hiện rõ.
- Mô phỏng carrier lỗi rồi thành công: nhìn thấy từng attempt, cùng đơn, đúng chiều vận đơn.
- Kiểm thử redaction với secret/token/thẻ giả trong lỗi; các giá trị đó không xuất hiện trong log DB hoặc response UI.
- Buyer không xem được API nhật ký seller; lọc/phân trang có giới hạn và trả kết quả đúng.
- Lỗi ghi nhật ký không dẫn đến gọi thanh toán/hoàn tiền thêm lần nữa.

### Chi tiết tiền và hồi quy

- Thẻ bị từ chối rồi thử thành công: hiển thị hai lần, chỉ cộng tiền lần thành công.
- Đơn chưa thanh toán, đang xác minh, đã trả tiền, hủy và đã hoàn tiền đều hiển thị đúng, kể cả dữ liệu cũ thiếu trường.
- Hoàn tiền thành công hiển thị đúng mã refund và số tiền; pending/failed không cộng vào tổng đã hoàn.
- Reload màn chi tiết không tạo Payment/Refund mới. Tracking hai chiều, hủy đơn và hết hạn thanh toán vẫn hoạt động.
- Build solution, test console, smoke test và UI desktop/mobile đều đạt. SQL Server được kiểm thử riêng; InMemory không được coi là bằng chứng cho transaction/index/unique constraint.
- PayPal Sandbox tự động dùng gateway giả; đối chiếu Sandbox thực tế là kịch bản nghiệm thu riêng khi có cấu hình. Không dùng tiền thật.

## 9. Tài liệu và bàn giao sau triển khai

- Cập nhật PROJECT_RUN: migration mới, cấu hình bằng Visual Studio Manage User Secrets, chạy SMTP thử nghiệm, cách xem thư/log/chi tiết tiền và khắc phục lỗi.
- Cập nhật docs thiết kế MVP, ghi rõ phạm vi hoàn thành và giới hạn SMTP/role giả lập.
- Cung cấp danh sách file theo task để người dùng tự commit/Jira; không tự commit hoặc push.
- Báo cáo test nào đã chạy, test nào chưa chạy và lý do. Không ghi một tính năng là hoàn thành chỉ vì có code.

## 10. Những mặc định người dùng có thể sửa trước khi duyệt

1. SMTP cục bộ là bước đầu; chưa cần thư Gmail thật.
2. Retry email 3 lần mỗi đợt, đợi 30 giây rồi 2 phút; seller chỉ thử lại thư Failed.
3. Một email mỗi loại sự kiện trên một đơn, giữ tương thích nghiệp vụ hiện tại.
4. Nhật ký cho seller/vận hành demo; buyer chỉ xem chi tiết tiền và thư của luồng demo.
5. Không thêm nghiệp vụ thu/hoàn tiền mới. Những mặc định trên là đề xuất, chỉ được xem là chốt khi người dùng duyệt.
