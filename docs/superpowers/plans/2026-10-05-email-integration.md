# Email and Integration Diagnostics Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Hiển thị email đúng nội dung và phạm vi đơn, gửi lại có giới hạn, và tra được từng lần gọi thanh toán/vận đơn/email theo mã đơn.

**Architecture:** Mở rộng outbox và thêm IntegrationLog bằng migration additive. Tách template, sender, processor và log writer khỏi CommerceMaintenanceWorker; logger dùng context riêng để không ảnh hưởng transaction nghiệp vụ. UI dùng panel chung cho buyer/seller/admin và JWT ownership hiện có.

**Tech Stack:** .NET 8, ASP.NET Core, EF Core 8.0.31/SQL Server, SMTP qua System.Net.Mail, Razor + JavaScript/CSS hiện có, console commerce tests + Node node:test.

**Spec:** ../specs/2026-10-05-email-integration-design.md — người dùng duyệt ngày 05/10/2026.

## Global Constraints

- Bốn event: PaymentSucceeded, Delivered, DeliveryFailed, Refunded; một outbox cho OrderId + EventType.
- Development mặc định Capture; Smtp qua cấu hình. Capture không gọi SMTP, có CapturedAt và không có SentAt.
- SMTP tối đa 3 lần mỗi cycle; sau lỗi lần 1 đợi 30 giây, sau lỗi lần 2 đợi 2 phút. Attempts tổng không reset.
- Chỉ admin retry Failed; buyer/seller chỉ xem theo ownership; shipper bị chặn. API mới giữ giới hạn Development hiện có.
- SMTP không bảo đảm exactly-once khi crash; Message-ID ổn định, token lease ngăn ghi đè kết quả từ worker cũ.
- Không lưu payload/token/password/Authorization/số thẻ/CVV hoặc thông tin cá nhân vào nhật ký tích hợp.
- Nhật ký không được thay đổi kết quả nghiệp vụ hoặc tạo thêm cuộc gọi gateway/retry.
- Không thay đổi giá/snapshot khuyến mãi, tiền, tồn kho, trả hàng, tranh chấp, phí nền tảng.
- Kiểm thử SQL trên database riêng G4_Email_Test_20261005; DB chính triển khai riêng sau QA và người dùng duyệt. Không tự commit/push/tạo branch.
- Thực hiện trực tiếp trong chat hiện tại, từng task; không tự chia sang chat khác.

## Review Focus

1. Worker chết sau claim hoặc SMTP nhận thư nhưng chưa lưu Sent: recovery có giới hạn và token cũ không ghi đè; Task 3/7.
2. SQL transaction nghiệp vụ rollback hoặc log writer mất kết nối: không có thư báo thành công giả, log không làm hỏng giao dịch; Task 2/4/7.
3. Buyer/seller đổi id/order filter hoặc proxy request: không xem nội dung người khác và không retry trái quyền; Task 5/6.
4. Order trả bằng Promotion $0 và thư Captured trước nâng cấp: đúng số tiền/trạng thái, không tạo SMTP/refund giả; Task 1/2/7.
5. Provider trả lỗi/response bất thường chứa dữ liệu nhạy cảm: lỗi được rút gọn, không lộ secrets và vẫn có kết quả Unknown phù hợp; Task 4/7.

## File Map

- Domain: mở rộng NotificationOutbox; thêm IntegrationLog và EmailRetryRules (thuần, kiểm thử được).
- Contracts: Checkout/NotificationContracts.cs và IntegrationLogContracts.cs cho DTO/query.
- Application/Integrations: IEmailSender.cs và IIntegrationLogWriter.cs cho adapter gửi/log.
- Infrastructure/Notifications: NotificationTemplates.cs, NotificationService.cs, SmtpEmailSender.cs, NotificationProcessor.cs.
- Infrastructure/Diagnostics: IntegrationContext.cs, IntegrationCallRecorder.cs, IntegrationLogWriter.cs, IntegrationLogQuery.cs.
- Persistence: ApplicationDbContext.Diagnostics.cs, configuration outbox, migration mới và model snapshot.
- API: NotificationsController cập nhật, IntegrationLogsController mới; Program middleware correlation.
- Frontend: _NotificationsPanel/_IntegrationLogsPanel, notifications.js/integration-logs.js/diagnostics.css; nối role pages và order details.
- Tests: NotificationTemplateChecks, NotificationProcessingChecks, IntegrationDiagnosticsChecks, NotificationAuthorizationChecks; Node notifications-ui/integration-logs-ui; SQL QA script.

## Shared Interfaces

Tên/signature dưới đây là hợp đồng giữa các task; các record giữ nullable như đặc tả, không cần thêm package.

```csharp
// G4.Application.Integrations
public sealed record EmailEnvelope(int NotificationId, string MessageId, string From,
    string Recipient, string Subject, string TextBody, string? HtmlBody);
public interface IEmailSender { Task SendAsync(EmailEnvelope email, CancellationToken ct); }
public sealed record IntegrationLogEntry(int? OrderId, string CorrelationId,
    string Service, string Operation, string Mode, string? EntityId, int Attempt,
    string Outcome, int? HttpStatus, long DurationMs, string? ProviderReference,
    string? ErrorCode, string? ErrorSummary, int? NotificationId, int? Cycle,
    DateTime CreatedAt);
public interface IIntegrationLogWriter {
    Task WriteAsync(IntegrationLogEntry entry, CancellationToken ct);
}
// G4.Infrastructure.Notifications
public sealed record NotificationContent(string Subject, string TextBody, string HtmlBody);
public sealed record NotificationTemplateInput(int OrderId, string EventType,
    decimal? Amount, string Currency, string? Method, string? TrackingNumber,
    string? Reason, string? ProviderReference, string? OrderUrl);
public static NotificationContent NotificationTemplates.Build(NotificationTemplateInput input);
public Task NotificationService.EnqueueAsync(OrderTable order, string eventType, CancellationToken ct);
public Task<int> NotificationProcessor.ProcessDueAsync(int batchSize, CancellationToken ct);
public Task<bool> NotificationProcessor.RetryFailedAsync(int id, CancellationToken ct);
// Domain rule: null nghĩa không còn retry trong cycle.
public static DateTime? EmailRetryRules.NextAttemptAt(int attemptsInCycle, DateTime now);
```

EmailNotificationView: id/orderId/eventType/recipient/from/subject/status/attempts/attemptsInCycle/cycle/createdAt/lastAttemptAt/nextAttemptAt/sentAt/capturedAt/lastErrorCode/lastErrorSummary. Email detail thêm body/htmlBody và danh sách lần thử. List không trả body lớn. IntegrationLogView chứa các trường IntegrationLogEntry và id; không có payload.

Query records: NotificationQuery(int? OrderId, string? EventType, string? Status, int Page=1, int PageSize=20); IntegrationLogQueryOptions(int? OrderId, string? Service, string? Outcome, DateTime? From, DateTime? To, int Page=1, int PageSize=20). Page result có page/pageSize/totalCount/items theo pattern hiện có.

### Task 1: Schema, retry rule và hợp đồng

**Files:** NotificationOutbox.cs; mới Domain/Entities/IntegrationLog.cs, Domain/Rules/EmailRetryRules.cs; mới hai Contracts; Persistence/ApplicationDbContext.Diagnostics.cs, configuration/model snapshot/migration; tests/G4.Commerce.Tests/NotificationProcessingChecks.cs, Program.cs.

**Produces:** Shared Interfaces records, DbSet<IntegrationLog>, fields outbox: HtmlBody/From nullable, AttemptsInCycle/Cycle, NextAttemptAt/LastAttemptAt/LastErrorCode/LastErrorSummary/ProcessingUntil/ProcessingToken/CapturedAt.

- [x] Viết test thuần với các assertion: attempt1 -> now+30s; attempt2 -> now+120s; attempt3 -> null; 0/4 không được coi là lần retry hợp lệ.
- [x] Chạy `dotnet run --project tests/G4.Commerce.Tests -c Release`, xác nhận test mới fail vì rule/model chưa có.
- [x] Thêm model/DTO: Cycle default1; ProcessingToken nullable Guid; các thời điểm UTC nullable. IntegrationLog OrderId/NotificationId không có FK; max lengths correlation64/service32/operation64/mode20/entity100/outcome20/provider200/errorCode64/errorSummary300. Body/HtmlBody giữ nvarchar(max).
- [x] Cấu hình index outbox (Status, NextAttemptAt)/(Status, ProcessingUntil), giữ unique event; log (OrderId, CreatedAt, Id)/(Service, CreatedAt, Id)/(NotificationId, CreatedAt, Id). ProcessingToken cùng điều kiện hoàn tất là concurrency guard.
- [x] Tạo migration `AddEmailIntegrationDiagnostics`: Captured cũ chuyển SentAt -> CapturedAt, fallback CreatedAt; SentAt null; không sửa Body/recipient/status tiền hoặc dựng log lịch sử.
- [x] Chạy test rule/model và build Infrastructure. Xuất SQL nâng cấp tương đương có guard/transaction vào database/email-integration-migration.sql; Task 7 kiểm thử SQL thật.

### Task 2: Template và outbox cùng transaction nghiệp vụ

**Files:** mới Notifications/NotificationTemplates.cs, NotificationService.cs; sửa CheckoutService.cs, PayPalPaymentService.cs, ShipmentService.cs, ReturnService.cs; tests/NotificationTemplateChecks.cs, Program.cs.

**Consumes:** Task 1 model; **Produces:** NotificationTemplates.Build, NotificationService.EnqueueAsync. Service dùng cùng ApplicationDbContext nghiệp vụ, không tự Commit/Save khi enqueue.

- [x] Viết test: Payment Card/PayPal/Promotion0; refund chỉ Amount Succeeded; HTML encode `<script>`/quotes; thiếu tracking/reference vẫn xem được; URL orderId đúng; delivery Return không tạo thư Delivered cho buyer.
- [x] Chạy commerce suite, xác nhận test mới fail trước implementation.
- [x] Implement pure template bốn event, text/HTML tiếng Việt; thông báo amount USD dùng invariant format trong payload, mô tả phương thức đúng Sandbox/giả lập/khuyến mãi. Không nhận HTML từ user.
- [x] Implement enqueue đọc payment/refund/shipment liên quan theo event sau khi kết quả được cập nhật. Include tracked entity mới chưa Save khi cần; dedupe cả ChangeTracker và DB theo OrderId/EventType. Recipient thiếu/không hợp lệ lưu thư Failed với error code cố định, không gửi fallback.
- [x] Thêm optional IConfiguration ở cuối CheckoutService constructor để test cũ tương thích; QueueEmailAsync giữ signature cũ nhưng delegate sang service mới, không sử dụng subject/body cũ để thay template. Các caller PayPal/Return/Shipping truyền config đang có. Config thiếu thì From g4@example.test, không có link.
- [x] Đặt enqueue trước Save cuối cùng trong cùng transaction của payment/shipping/refund; validation rollback không để outbox thành công sống sót. Giữ toàn bộ finance/promotions/stock/idempotency hiện có.
- [x] Chạy commerce suite: template pass, paid/zero/refund snapshot pass, không tạo thư cho failed/verifying và không tăng số thư trên replay. SQL rollback kiểm tra ở Task 7.

### Task 3: Processor, Capture/SMTP và retry

**Files:** mới Notifications/SmtpEmailSender.cs, NotificationProcessor.cs; sửa CommerceMaintenanceWorker.cs, DependencyInjection.cs, appsettings.Development.json; tests/NotificationProcessingChecks.cs, test helper FakeSmtpServer.cs.

**Consumes:** model/rule/envelope Task 1; **Produces:** IEmailSender, ProcessDueAsync, RetryFailedAsync. Inject TimeProvider, factory tạo context độc lập, IIntegrationLogWriter (tạm no-op test đến Task 4).

- [x] Test fake sender/time: Capture gọi sender0 lần, SentAt null; SMTP success1 lần; failures tại t0/t+30/t+150, sau lần3 Failed; retry reset cycle nhưng giữ Attempts; Sent/Captured/Processing retry false; cancellation không đánh dấu Sent.
- [x] Chạy commerce suite và ghi nhận failure đúng lý do processor chưa có.
- [x] Cấu hình factory ApplicationDbContext scoped cùng SQL options, giữ resolve ApplicationDbContext cho services hiện có. Worker resolve processor từ scope; bỏ loop SMTP cũ, không đổi thứ tự maintenance nghiệp vụ.
- [x] Claim SQL bằng conditional UPDATE status/due/expired lease, token Guid mới. Lease60s, SMTP timeout10s, batch20. Increment Attempts/AttemptsInCycle khi bắt đầu, đảm bảo <=3; ProcessDue quét Pending và Processing hết hạn. Complete WHERE id+token+Processing, token cũ update0 rows.
- [x] Cycle recovery: nếu hết lease sau attempt3 thì Failed; nếu còn lượt thì retry theo rule từ LastAttemptAt. Capture claim và hoàn tất Captured, CapturedAt có, không SMTP. Không nuốt shutdown cancellation.
- [x] SMTP sender dùng From/Recipient hợp lệ, text + HTML alternate view, Message-ID ổn định `g4-notification-{id}@g4.example.test`; cấu hình credentials/TLS qua config, không secrets trong file. Không tự fallback Capture khi lỗi.
- [x] Admin retry conditional Failed -> Pending, Cycle+1, AttemptsInCycle0, NextAttemptAt=now, giữ Attempts/lịch sử. Permanent configuration/address error Failed ngay; error summary từ mã lỗi kiểm soát.
- [x] FakeSmtpServer loopback TcpListener kiểm tra EHLO/MAIL/RCPT/DATA/QUIT, nhận nội dung text/HTML; kịch bản 451/timeout. Không mở dịch vụ public hoặc gửi Internet.
- [x] Chạy unit tests + SMTP loopback checks; SQL cạnh tranh/lease ở Task 7. Update worker không gom context log với transaction nghiệp vụ.

### Task 4: Log writer và instrumentation các lần gọi

**Files:** mới Diagnostics/IntegrationContext.cs, IntegrationCallRecorder.cs, IntegrationLogWriter.cs; sửa PayPalSandboxGateway.cs, HttpCarrierGateway.cs, RefundGateway.cs, PayPalPaymentService.cs, CheckoutService.cs, ReturnService.cs, ShipmentService.cs, processor/DI, API Program.cs; tests/IntegrationDiagnosticsChecks.cs.

**Interfaces:** IntegrationContext.Begin(int? orderId, string correlationId, int attempt=1, int? notificationId=null, int? cycle=null) trả IDisposable restore context cũ. Current context dùng AsyncLocal. Recorder.Start(string service,string operation,string mode,string? entityId=null) trả IntegrationAttempt; CompleteAsync(string outcome,int? httpStatus,string? providerReference,string? errorCode,string? safeSummary,CancellationToken ct) đúng một lần. Dispose chưa complete ghi Unknown. Stopwatch đo elapsed, không đọc raw payload.

- [x] Test fake HttpMessageHandler/writer: OAuth+Create đủ2 dòng, Capture/Query/Refund có op/reference, timeout Unknown, Carrier lỗi lần1/lần2 rồi thành công3 dòng; log writer throw không đổi kết quả gateway hoặc số lần gọi.
- [x] Test secrets-canary trong exception/response và request credentials: persisted log không chứa canary, email/address/card/CVV; nested scopes restore correlation; parallel requests không trộn order.
- [x] Chạy suite, xác nhận failure trước adapter/recorder.
- [x] Writer tạo context riêng, suppress ambient TransactionScope, save chỉ log table không FK. Timeout ghi2s; bắt lỗi ghi warning an toàn. Request cancellation không biến log failure thành business retry. No-op writer phục vụ direct-constructor tests cũ; runtime DI đăng ký writer thật.
- [x] Middleware tạo correlation Guid server, không phản chiếu giá trị client tùy ý. Scope caller gắn orderId trước gateway; email scope dùng NotificationId/cycle/attempt. Worker/gateway độc lập có fallback correlation.
- [x] PayPal log từng HTTP OAuth và operation thực tế, bắt status/debug ID/reference whitelist, bao cả lỗi parse/response không hợp lệ; không thêm calls. Timeout ở capture/refund là Unknown, service reconciliation giữ nguyên. Carrier retry loop đặt attempt context1-3, direction op rõ ràng, mỗi HttpCarrierGateway call một dòng.
- [x] Card/new Promotion payment và Card/Promotion refund thực sự ghi Mode Simulated/Internal; replay không thêm attempt giả. Capture email Mode Capture, SMTP Mode Smtp; lưu từng failure/success để detail đọc lịch sử.
- [x] Chạy suite và fake HTTP tests, so sánh số gateway calls với baseline tests; không thay exceptions/status nghiệp vụ thành success vì logging.

### Task 5: Query API, ownership và admin retry

**Files:** NotificationsController.cs; mới IntegrationLogsController.cs, Diagnostics/IntegrationLogQuery.cs; tests/NotificationAuthorizationChecks.cs; DTO Task 1.

**Produces:** GET notifications, notifications/{id}, integration-logs; POST admin/notifications/{id}/retry; giữ inbox/inbox/page tương thích admin.

- [x] Test hai buyer/hai seller/admin/shipper: list/detail/order-filter đúng ownership; log OrderId null chỉ admin; buyer không xem logs; seller/shipper không retry; admin retry Failed đúng1 lần.
- [x] Test filter invalid status/event/service/outcome, date range đảo, pageSize0/101, orderId âm: lỗi400 rõ ràng; page ngoài phạm vi clamp theo pattern hiện có. UTC filter end boundary được định nghĩa inclusive From, exclusive To.
- [ ] Chạy tests, ghi failure endpoint/query thiếu.
- [x] Query ownership qua OrderTable.BuyerId/SellerId, không theo recipient string hoặc client role/id. DTO projection list không trả HtmlBody/Body; detail có attempts log NotificationId đúng. Sorting CreatedAt DESC, Id DESC; default20/max100.
- [x] Retry admin kiểm tra JWT+Development trước service; Failed update thành công trả202, trạng thái không hợp lệ409, không có404; refresh không gửi email. Giữ route cũ và quyền admin cũ.
- [x] Chạy ownership + retry tests bằng controller và live API SQL Task 7, không coi UI ẩn nút là bằng chứng phân quyền.

### Task 6: Panel email, nhật ký và nối role pages

**Files:** mới Views/Shared/_NotificationsPanel.cshtml, _IntegrationLogsPanel.cshtml, wwwroot/js/notifications.js, integration-logs.js, css/diagnostics.css; sửa Buyer/Seller/Admin Index, buyer.js/seller.js/admin.js, common.js; tests/notifications-ui.test.cjs, integration-logs-ui.test.cjs, ui-navigation.test.cjs.

**Interfaces:** `G4.Notifications.init(role)`, `.load(page=1)`, `.open(id)`; `G4.IntegrationLogs.init(role)`, `.load(page=1)`, `.openForOrder(orderId)`. Shared panel mỗi page một instance, DOM IDs prefix riêng để không xung đột modal/disputes/promotions.

- [x] Node VM tests: render actual stored subject/body (không mailText), escape malicious text, Capture label rõ chưa gửi, retry chỉ admin+Failed; filter/page/refresh giữ state và no duplicate handler. Log Unknown/Simulated hiển thị đúng, không render HTML từ ErrorSummary.
- [ ] Chạy `node --test tests/notifications-ui.test.cjs tests/integration-logs-ui.test.cjs tests/ui-navigation.test.cjs`, xác nhận fail trước UI implementation.
- [x] Dùng skill ui-ux-pro-max trước thiết kế UI chi tiết. Buyer thêm email tab; Seller email/log tabs; Admin nâng tab inbox và thêm log. Bộ lọc order/type/status/service/outcome/from/to, paging, loading/empty/error. Admin retry disable đang request, refresh list+detail sau thành công.
- [x] HTML preview qua sandbox iframe srcdoc với CSP `default-src 'none'; style-src 'unsafe-inline'`, không script/form/top navigation. Text là fallback cho thư cũ. Giữ link đơn từ text bằng nút UI được encode/route kiểm chứng; không mở arbitrary URLs trong preview.
- [x] Nối tab handler có branch rõ ràng để seller không gọi loadFinance cho tab mới; buyer giữ promotions/coupon/checkout; admin xóa mailText override. Seller order detail mở log với order filter. Không cấp log cho buyer/shipper.
- [x] Chạy Node tests, build frontend; browser QA desktop390px/mobile: không overflow, modal keyboard/focus/close, empty/error states và nội dung thư thực.

### Task 7: QA SQL/SMTP/hồi quy, migration và bàn giao

**Files:** mới tests/email-integration-sql.ps1, docs/EMAIL_INTEGRATION_TEST_GUIDE.md; update docs/PROJECT_RUN.md, EMAIL_INTEGRATION_PAYMENT_PLAN.md; migration SQL Task 1.

- [ ] Chuẩn bị QA connection chỉ database G4_Email_Test_20261005; script từ chối DB chính và mọi tên khác. Tạo seed nhỏ chỉ QA; ghi manifest trước migration và giữ dữ liệu cũ có Captured/Sent/Pending.
- [x] Apply migration QA: tất cả dữ liệu order/payment/refund/ledger/promotion/product/images/inventory/user/address giữ nguyên; Captured legacy chuyển đúng timestamp; chạy SQL lần2 no-op; fixture lỗi trong transaction rollback cả schema/history.
- [x] SQL uniqueness event và transaction rollback không sinh thư giả; log writer independent vẫn ghi được khi business tx rollback và khi commit chưa giải phóng. Test log writer unavailable không làm giao dịch đã thành công fail.
- [x] Hai contexts claim một email chỉ một owner; admin retry đồng thời một202/một409; lease token cũ không ghi đè; attempt3 crash recovery Failed; stable Message-ID. SMTP giả loopback nhận thư đúng; tắt server cho lịch retry/failure, không dùng Internet.
- [x] Live API JWT ownership + filters + pagination + retry, không đọc email/log chéo shop. Carrier simulated lỗi rồi thành công có3 calls/3 logs; fake PayPal timeout/capture/query/refund đúng số lần và trạng thái tiền.
- [x] `dotnet build G4_Project.sln --configuration Release --no-restore -p:UseAppHost=false`: 0 errors; `dotnet tests/G4.Commerce.Tests/bin/Release/net8.0/G4.Commerce.Tests.dll`: mọi check pass.
- [x] `node --test tests/notifications-ui.test.cjs tests/integration-logs-ui.test.cjs tests/promotions-ui.test.cjs tests/dispute-ui.test.cjs tests/shipper-ui.test.cjs tests/ui-navigation.test.cjs`: mọi test pass; EF has-pending-model-changes: none.
- [x] Browser QA buyer/seller/admin desktop+mobile và một order payment/delivery/refund; screenshot và checks không overflow/JS error. Các test Sandbox thực tế chỉ thực hiện khi có cấu hình phù hợp; ghi rõ giới hạn, không giả tuyên bố đã test gateway thật.
- [x] Cập nhật run guide/test guide: Capture mặc định, cấu hình secrets SMTP, loại thư/retry/log/permissions, migration additive, cách test sender failure bằng SMTP loopback. Old plan đánh dấu phạm vi A/B đã hoàn tất, C vẫn chưa triển khai.
- [x] Bàn giao thay đổi chưa commit và bằng chứng QA. Chuẩn bị backup/script/manifest để xin duyệt bước migration DB chính riêng; không tự áp dụng chỉ vì QA pass. Dừng helper QA để không khóa DLL.

## Self-review và handoff

- Spec sections1-3: Task5/6; section4: Task2; section5: Task1/3; section6: Task4; section7: Task1/5; section8: Task1/7; section9: tests trong từng task và Task7.
- Thống nhất default Capture/admin retry/ownership/3 attempts; không có thay đổi nghiệp vụ return hoặc financial detail C.
- Implementation method được giữ từ spec: thực hiện trực tiếp trong chat hiện tại bằng executing-plans. Người dùng duyệt kế hoạch trước khi bắt đầu code.
- Không tự commit dù template skill có bước commit: yêu cầu repo docs bàn giao cho người dùng tự commit được giữ nguyên.

## Execution ledger

Tasks 1–7 đã triển khai và kiểm thử cuối trên QA. Source ở master, chưa commit/push. Migration chính chưa chạy; bước xin duyệt triển khai được giữ riêng theo spec mục 8.

Bằng chứng: build 0 warnings/errors; commerce suite, SQL concurrent claim/retry/lease/rollback, SMTP loopback và Node 46/46 pass; EF không có pending model changes. Live API JWT kiểm tra bốn event, retry202/409 và Carrier3calls/3logs. Browser buyer/seller/admin desktop+390px, nội dung thực/preview/focus/Escape/open-order/deep-link/filter/empty/nooverflow/noJSerror pass. Xem EMAIL_INTEGRATION_TEST_GUIDE và ledger local artifacts/email-integration/progress.md.

Các ô chưa tick: không có bằng chứng RED riêng cho Task5/6, nên không suy ra test đã chạy fail trước code. Backup QA không có lịch sử Sent/Pending trước nâng cấp; đã test chuyển Captured legacy thật và các trạng thái khác bằng processor/SQL sau nâng cấp. Đây là giới hạn bằng chứng lịch sử, không phải phần chức năng chưa triển khai.

Rulings sau review: DiagnosticsQueryService thay IntegrationLogQuery; BufferedIntegrationLogWriter chạy nền (queue512, nonblocking) để việc ghi SQL không tiêu thụ budget3s hoàn tiền. Log là best-effort. Invalid recipient vẫn Failed sau retry Capture; phản hồi PayPal COMPLETED thiếu dữ liệu, malformed2xx/5xx và SMTP transport ambiguity ghi Unknown. Regression tests cho invalid recipient/incomplete COMPLETED đã fail trước sửa và pass sau sửa.
