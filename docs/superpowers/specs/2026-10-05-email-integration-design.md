# Email thông báo và nhật ký tích hợp

Ngày: 05/10/2026. Trạng thái: thiết kế và kế hoạch đã duyệt; code đã triển khai, kiểm thử QA riêng. Migration database chính chờ duyệt ở bước triển khai riêng.

Chi tiết thực thi sau review: runtime ghi log qua hàng đợi nền giới hạn 512 mục, enqueue không chờ SQL/capacity; consumer dùng context độc lập với timeout 2 giây. Cách này giữ budget thanh toán/hoàn tiền. Nhật ký là best-effort, có thể thiếu khi queue đầy, SQL lỗi hoặc process chết; không tạo thêm gateway calls. Xem [hướng dẫn kiểm thử](../../EMAIL_INTEGRATION_TEST_GUIDE.md).

## 1. Yêu cầu và kết quả mong đợi

Người dùng đã chọn triển khai email thông báo, gửi lại khi lỗi và nhật ký tích hợp tra theo mã đơn. Mục đích là demo được thông báo thanh toán/giao hàng/hoàn tiền và tìm được nguyên nhân lỗi của một đơn.

Phạm vi lần này:

- Bốn loại email đang có: PaymentSucceeded, Delivered, DeliveryFailed, Refunded.
- Nội dung thư tiếng Việt có dữ liệu thật được chốt tại thời điểm tạo thông báo.
- Capture để xem thư ngay trong app; SMTP được hỗ trợ qua cấu hình.
- Gửi lại tự động có giới hạn, lưu lịch sử lần thử và gửi lại thủ công cho thư Failed.
- Nhật ký PayPal Sandbox, carrier giả lập, email và thanh toán/hoàn tiền giả lập.
- Buyer, seller, admin có quyền xem theo phạm vi đơn. Admin là người thao tác gửi lại.
- Migration cộng thêm schema, giữ đơn, sản phẩm/URL ảnh, khuyến mãi, tồn kho và dữ liệu tiền.

Không mở rộng nghiệp vụ đổi địa chỉ, chính sách trả hàng, partial refund, nợ xấu hay phí nền tảng. Phần C trong EMAIL_INTEGRATION_PAYMENT_PLAN.md (màn lịch sử tài chính mới) không nằm trong lần này; tiếp tục dùng chi tiết đơn hiện có.

## 2. Căn cứ và khác biệt với kế hoạch cũ

Đã đọc EMAIL_INTEGRATION_PAYMENT_PLAN.md và kiểm tra code hiện tại:

- NotificationsController hiện chỉ cho admin xem /api/inbox và /api/inbox/page.
- Admin có tab Nhật ký thư điện tử, nhưng admin.js thay nội dung thư bằng mẫu theo EventType.
- NotificationOutbox có unique index OrderId + EventType, Body text, Attempts và SentAt.
- CommerceMaintenanceWorker chạy mỗi 5 giây, gửi SMTP nếu Mail:Host có cấu hình, nếu không đánh dấu Captured; SMTP lỗi chưa giới hạn retry.
- PayPalSandboxGateway có OAuth/Create/Capture/Get/Refund; carrier thực hiện các lần gọi tạo nhãn từ ShipmentService.
- Phân quyền hiện dùng JWT và ownership; API nghiệp vụ vẫn giới hạn Development. Giữ nguyên ranh giới này.

Kế hoạch cũ đề xuất seller gửi lại và SMTP cục bộ mặc định. Thiết kế được duyệt dùng admin gửi lại để phù hợp quyền vận hành hiện tại; Capture là mặc định để chạy app hiện có không cần thêm dịch vụ. Câu trả lời duyệt áp dụng cho mặc định Capture đã trình bày; vẫn hỗ trợ SMTP khi cấu hình.

## 3. Giao diện và quyền

### Buyer

Thêm tab Thông báo email, lọc mã đơn/loại thông báo/trạng thái và phân trang. Chỉ xem email của đơn mà BuyerId trùng tài khoản. Xem chi tiết thư, số tiền, mã vận đơn nếu có và thời điểm; không có nút gửi lại hoặc nhật ký nhà cung cấp.

### Seller

Thêm tab Thông báo email và Nhật ký tích hợp. Chỉ xem dữ liệu liên quan đơn mà SellerId trùng tài khoản. Email là bản ghi thông báo gửi cho buyer của đơn đó, không ngụ ý seller được gửi thêm một thư. Có thể mở nhật ký đã lọc từ chi tiết đơn.

### Admin

Nâng tab email hiện có: bộ lọc, trạng thái, số lần thử, nội dung thực đã lưu, lỗi thân thiện và lịch sử. Admin được gửi lại một thư Failed và xem nhật ký toàn hệ thống. Không có sửa/xóa nội dung thư, đơn hoặc nhật ký.

### Trình bày

Dùng component chung cho buyer/seller/admin, giữ phong cách role pages hiện có. Desktop hiển thị danh sách dễ quét; mobile không tràn ngang, bộ lọc xuống dòng. Có trạng thái đang tải, không có dữ liệu và lỗi. Bộ lọc reset trang về 1; làm mới giữ bộ lọc. Nội dung động encode, không đưa HTML bất kỳ vào innerHTML. Xem thư HTML chỉ qua nội dung template do server tạo, sandbox iframe không có script/form/navigation; thư cũ chưa có HTML xem text.

Nhãn Captured: Đã lưu để xem, chưa gửi email. Sent: Đã gửi tới máy chủ email; không khẳng định người nhận đã đọc. Unknown trong nhật ký: Chưa xác định kết quả.

## 4. Nội dung và tạo email

Giữ một thông báo cho mỗi OrderId + EventType và phát sinh sau khi lưu kết quả nghiệp vụ thành công. Cùng transaction nghiệp vụ khi luồng hiện có hỗ trợ. Không tạo email thành công cho thanh toán/refund Failed hoặc Verifying.

- PaymentSucceeded: mã đơn, số tiền thực trả/currency, phương thức (PayPal Sandbox, thẻ giả lập hoặc khuyến mãi), hướng dẫn theo dõi đơn.
- Delivered: mã đơn, mã vận đơn chiều đi, thời điểm giao, hướng dẫn xem chi tiết đơn.
- DeliveryFailed: mã đơn, mã vận đơn, lý do/thông tin giao thất bại được phép hiển thị, hướng dẫn theo dõi giao lại.
- Refunded: mã đơn, số tiền refund thành công thực tế/currency, phương thức và mã refund nếu có.

Subject, Body text, HtmlBody và địa chỉ From được lưu thành snapshot; xem/retry không tính lại nội dung theo dữ liệu mới. Dynamic values được encode, template không chứa secrets/số thẻ. Không fallback gửi đến email giả khác khi recipient thiếu/không hợp lệ: giữ bản ghi và đánh dấu lỗi cấu hình/dữ liệu để admin kiểm tra, không sửa danh tính người nhận ngầm.

Chỉ thêm link chi tiết đơn khi xác minh route buyer hiện có (?orderId=...). Thiếu PublicBaseUrl thì thư vẫn có đủ nội dung và mã đơn, không tạo link sai. Không tự đọc URL ngoài để dựng nội dung.

## 5. Chế độ gửi, retry và đồng thời

Mail:Mode = Capture hoặc Smtp. Đề xuất Development mặc định Capture, hỗ trợ Smtp khi người dùng cấu hình. Nếu Mode chưa có thì giữ tương thích cấu hình cũ: Host có giá trị dùng Smtp, ngược lại Capture. Mode không hợp lệ phải báo cấu hình rõ ràng.

Capture không gọi SMTP, lưu CapturedAt, không dùng SentAt. Không tự chuyển SMTP lỗi thành Captured. Cấu hình SMTP: Host, Port, From, EnableSsl, Username, Password; secrets qua User Secrets/biến môi trường, không commit. Timeout gửi nhỏ hơn lease.

Luồng SMTP: Pending -> Processing -> Sent. Mỗi cycle tối đa 3 lần bắt đầu gửi; lỗi lần 1 hẹn 30 giây, lỗi lần 2 hẹn 2 phút, lỗi lần 3 chuyển Failed. Attempts tổng không reset; AttemptsInCycle reset khi admin mở cycle mới. Lỗi dữ liệu/cấu hình vĩnh viễn có thể chuyển Failed ngay, không cố gửi 3 lần.

Claim dùng cập nhật có điều kiện trên trạng thái, NextAttemptAt và lease; mỗi lease có ProcessingToken riêng. Chỉ owner của token được hoàn tất lần xử lý. Worker dừng đột ngột thì ProcessingUntil hết hạn cho phép khôi phục, vẫn tôn trọng giới hạn cycle. Hủy do shutdown không bị báo giả là Sent; không nuốt cancellation.

Retry thủ công: chỉ admin, chỉ Failed, cập nhật nguyên tử sang Pending, reset cycle và lịch gửi, giữ tổng lần thử/lịch sử. Hai lần bấm đồng thời chỉ một lần được chấp nhận; Sent/Captured/Processing không được gửi lại bằng endpoint này.

SMTP không bảo đảm exactly-once sau crash: máy chủ có thể nhận email nhưng app chưa lưu Sent. Dùng Message-ID ổn định và ghi nhận từng attempt, không hứa loại bỏ hoàn toàn khả năng thư lặp. API/worker không tạo thêm bản ghi outbox khi retry.

## 6. Nhật ký tích hợp

Ghi từng cuộc gọi thực tế:

- PayPal: OAuth, CreateOrder, Capture, QueryOrder, Refund. Mỗi cuộc gọi HTTP có một dòng, liên kết order khi caller biết order, timeout thể hiện Unknown khi chưa xác định được kết quả.
- Carrier: từng lần CreateLabel của Outbound/Return; không gộp các lần lỗi và lần thành công. Log không tạo thêm retry mới.
- Email: từng lần Capture hoặc SMTP Send, liên kết NotificationId/OrderId/cycle/attempt. Capture ghi rõ là lưu thử nghiệm.
- Card/Promotion: ghi rõ xử lý giả lập/nội bộ khi có thao tác thanh toán hoặc hoàn tiền thực sự; replay cùng key không giả thành một cuộc gọi nhà cung cấp mới.

Mỗi dòng có Id, OrderId nullable, CorrelationId, Service, Operation, EntityId nullable, Attempt, Outcome, HttpStatus nullable, DurationMs, ProviderReference, ErrorCode, ErrorSummary, CreatedAtUtc. Outcome gồm Succeeded/Failed/Unknown; Mode/Service cho biết simulated/capture.

CorrelationId được server tạo/kiểm tra độ dài, thống nhất trong request/cycle worker. Context order truyền từ application service xuống gateway; không suy OrderId từ chuỗi provider ID. OAuth thuộc request có OrderId khi caller cung cấp; tác vụ không biết order được phép log OrderId null, chỉ admin xem.

Không lưu payload HTTP, token, Authorization, Client Secret, SMTP password, số thẻ/CVV hoặc email/địa chỉ vào log. ErrorSummary dùng mẫu kiểm soát, không lưu thẳng exception.Message từ nhà cung cấp. ProviderReference giới hạn chiều dài và chỉ giữ mã giao dịch/debug/mã vận đơn, không URL tùy ý.

Nhật ký chỉ phục vụ tra cứu, Payment/Refund mới xác định tiền. Writer dùng context độc lập khỏi transaction nghiệp vụ; bảng log không có FK gây phụ thuộc transaction order đang chưa commit. Lưu log thất bại chỉ ghi cảnh báo đã che dữ liệu; không làm giao dịch thành công bị đổi sang thất bại hoặc gọi gateway lần nữa. Thời gian ghi log có giới hạn. Không tạo lịch sử giả cho các cuộc gọi xảy ra trước migration.

## 7. Dữ liệu và API

Mở rộng NotificationOutbox: HtmlBody/From nullable cho dữ liệu cũ, AttemptsInCycle, NextAttemptAt, LastAttemptAt, LastErrorCode, LastErrorSummary, ProcessingUntil, ProcessingToken, CapturedAt. Giữ Attempts tổng và unique OrderId + EventType. Index Status + NextAttemptAt/ProcessingUntil cho worker.

Thêm IntegrationLog, index OrderId + CreatedAt + Id và Service + CreatedAt + Id; có NotificationId/cycle/attempt để truy lịch sử gửi. Các trường enum/string có giới hạn độ dài; DurationMs >= 0. UTC trong DB, giờ địa phương trên UI. Không xóa lịch sử cũ.

API dự kiến, Development + JWT:

| Route | Quyền và mục đích |
| --- | --- |
| GET /api/inbox và /api/inbox/page | Giữ tương thích quyền admin hiện có |
| GET /api/notifications | Buyer/seller theo ownership; admin tất cả; lọc orderId/eventType/status, phân trang |
| GET /api/notifications/{id} | Nội dung và lịch sử trạng thái/lần gửi, kiểm tra ownership |
| POST /api/admin/notifications/{id}/retry | Chỉ admin, chỉ Failed, claim nguyên tử |
| GET /api/integration-logs | Seller theo đơn của mình, admin tất cả; lọc orderId/service/outcome/from/to |

Page mặc định 1, pageSize 20, giới hạn 1-100; thứ tự CreatedAt DESC rồi Id DESC. Kiểm tra filter/date/orderId; không tin sellerId/buyerId client gửi. Shipper không được xem email/log tài chính. Frontend dùng ApiProxy hiện có với JWT của session.

## 8. Migration và triển khai

Migration additive từ AddPromotions; không sửa baseline. Captured cũ: CapturedAt = SentAt cũ nếu có, nếu không CreatedAt; SentAt trở về null để đúng nghĩa. Giữ Subject/Body, recipient, Attempts và trạng thái nghiệp vụ khác. Không dựng HTML mới hoặc sự kiện giả cho lịch sử cũ.

Kiểm thử trên database riêng trước. Manifest trước/sau xác minh số lượng và dữ liệu order/payment/refund/ledger/promotion/product/images/inventory/user/address không đổi. SQL script có transaction + XACT_ABORT, history guard và rollback khi lỗi.

Không migration vào CloneEbayDB trong bước thiết kế. Khi code và kiểm thử QA hoàn tất, chuẩn bị backup, script và kết quả đối chiếu để người dùng duyệt bước triển khai DB chính. Không tự commit/push; bàn giao danh sách file để người dùng tự commit.

## 9. Kiểm thử và nghiệm thu

- Template: dữ liệu thật, số tiền zero/discount/refund đúng, encode nội dung, thiếu field cũ xem được.
- Capture: không gọi SMTP, CapturedAt có, SentAt null, đọc đúng nội dung đã lưu.
- SMTP: máy chủ test cục bộ nhận đúng text/HTML; lỗi/timeout đúng lịch, tối đa 3 lần/cycle; không gửi ra hộp thư Internet trong test.
- SQL thật: unique event, hai worker claim, retry đồng thời, lease hết hạn và token cũ không ghi đè kết quả mới.
- Bảo toàn transaction: rollback nghiệp vụ không lưu thông báo thành công; log vẫn tra cứu được khi transaction nghiệp vụ lỗi; log writer lỗi không làm hỏng nghiệp vụ.
- PayPal HTTP giả: từng OAuth/Create/Capture/Get/Refund có dòng đúng; timeout Unknown, query sau đó có kết quả riêng; không thay đổi idempotency/reconciliation.
- Carrier: lỗi rồi thành công có từng attempt, đúng direction/order, không tăng số cuộc gọi vì thêm logging.
- Ownership: buyer/seller không đọc đơn khác bằng list/id/order filter; shipper bị chặn; admin retry đúng quyền/trạng thái.
- UI: bộ lọc, paging, chi tiết thư/lịch sử, retry; desktop/mobile, keyboard, loading/empty/error; không dùng mailText để thay nội dung thực.
- Hồi quy: build solution, console commerce suite, Node UI tests; checkout/khuyến mãi/PayPal unknown/tracking/return/dispute/finance vẫn đạt.
- Docs chạy/test: chế độ Capture/SMTP, secrets, migration, các case demo lỗi/gửi lại/tra theo mã đơn, giới hạn SMTP và QA Sandbox.

## 10. Phương án thực hiện

Thực hiện trong chat hiện tại theo thứ tự: schema/contracts -> outbox/templates/sender -> log instrumentation -> API/UI -> SQL/SMTP/UI QA và tài liệu test. Tách service notifications và diagnostics để worker không chứa toàn bộ logic mới. Lập kế hoạch file và kiểm thử sau khi người dùng duyệt thiết kế này. Giữ workspace hiện tại, không tự tạo branch, commit hoặc push.
