# Bổ sung thông báo và email N1

## Kế hoạch

- [x] Bổ sung khóa sự kiện theo từng lần thanh toán/yêu cầu/payout và người nhận; giữ dữ liệu email cũ.
- [x] Phát outbox cùng giao dịch nghiệp vụ: thanh toán thất bại đã xác định; hủy ngay/yêu cầu hủy; tạo/duyệt/từ chối trả hàng; payout hoàn tất/trả lại.
- [x] Nội dung tiếng Việt, lọc email và liên kết phù hợp buyer/seller; kiểm tra quyền theo tài khoản nhận.
- [x] Kiểm thử nghiệp vụ, SQL migration/đồng thời/rollback, gửi thất bại/retry và UI.

Outbox lưu cùng transaction nghiệp vụ; không gọi SMTP từ payment/return/payout. Worker gửi và retry riêng. Không phát PaymentFailed cho timeout/Verifying hoặc buyer tự hủy lượt PayPal. Retry cùng sự kiện/người nhận không tạo email mới; các lần thử thanh toán khác nhau và các kỳ yêu cầu hủy khác nhau có thông báo riêng.

**PAY1 ngày 20/10:** bỏ email Completed tức thời khi Level 3 yêu cầu payout. Mọi level chỉ có PayoutCompleted/PayoutReturned sau bước xác nhận ngân hàng mô phỏng terminal; giữ EventKey/RecipientUserId hiện có. Created/InProgress/FundsSent không phát email kết quả. Lịch sử payout và realtime cùng transaction; SQL QA lỗi sau SaveChanges trước commit xác nhận không phát email/state terminal giả và retry chỉ một thông báo. Không gửi bù cho payout terminal cũ. Xem [PAY1](PAYOUT_WORKFLOW.md).

**SH1 ngày 20/10:** thêm email `ReattemptScheduled` cho buyer/seller, identity `shipment:{shipmentId}:reattempt:{eventId}` và hậu tố tài khoản nhận theo AddAsync hiện có. Mỗi chiều có thông báo riêng với lịch UTC/lý do; ghi cùng transaction shipment/event, worker gửi sau commit. Replay event không thêm email, rollback không để lại email/realtime. Template có hướng dẫn theo dõi đúng chiều, không coi lịch là đã bắt đầu hoặc Delivered. Bộ lọc Email có sự kiện mới. Đã kiểm tra hai chiều, SQL concurrent/rollback và nội dung escape; không gửi bù lịch sử. Dùng các cột outbox hiện có; migration SH1 chỉ bổ sung tracking, [xem hướng dẫn](RETURNS_AND_SHIPPING.md#sh1--lên-lịch-giao-lại-và-actor-20102026).

## Người nhận

**AD1 ngày 20/10:** thông báo đổi địa chỉ mới dùng EventType `AddressChangePending`, `AddressChangeAwaitingShipper`, `AddressChangeApproved`, `AddressChangeRejected`, `AddressChangeExpired`; EventKey `address-change:{requestId}:{status}:user:{userId}` và RecipientUserId giữ quyền theo người nhận. Định danh legacy `AddressChange:{requestId}:{status}:{userId}` và email cũ vẫn được giữ. Bộ lọc Email có năm sự kiện mới. Pending/Approved/Rejected/Expired gửi buyer/seller; AwaitingShipper gửi thêm shipper được gán, nêu địa chỉ cũ còn hiệu lực và hạn chờ UTC. Chỉ Approved mới nói địa chỉ/hoàn ship đã áp dụng. Ghi cùng transaction địa chỉ/finance; SQL QA xác minh retry/concurrent/rollback không nhân email hoặc phát realtime sai. Không gửi bù lịch sử, không đổi nội dung email cũ. Xem [AD1](ORDER_ADDRESS_CHANGES.md).

| Sự kiện | Người nhận |
| --- | --- |
| PaymentFailed | Buyer của đơn. |
| OrderCancelled (đơn chưa trả tiền, hủy ngay) | Buyer xác nhận hủy thành công. |
| CancellationRequested (đơn đã trả tiền) | Buyer xác nhận tiếp nhận; seller cần duyệt/từ chối. |
| ReturnRequested | Buyer xác nhận tiếp nhận; seller cần xử lý. |
| ReturnApproved / ReturnRejected | Buyer nhận quyết định và bước tiếp theo. |
| PayoutCompleted / PayoutReturned | Seller chủ payout. |
| ReattemptScheduled (SH1, 20/10) | Buyer và seller của đơn, cho cả Outbound/Return; có chiều, lịch UTC, lý do và bước theo dõi tiếp. |

Nhánh admin yêu cầu trả hàng mới A2 chưa được triển khai. Khi A2 có luồng tạo return, dùng cơ chế thông báo theo yêu cầu/người nhận này; N1 không tự phát một phán quyết admin chưa tồn tại.

## Luồng và nội dung

- Thẻ giả lập bị từ chối, hết hạn hoặc không hợp lệ tạo `PaymentFailed` cho đúng buyer, ghi mã lần thanh toán, số tiền và hướng dẫn. PayPal một đơn hoặc cả giỏ chỉ phát sự kiện khi đã xác định từ chối/void/denied/failed; timeout, dữ liệu không khớp và trạng thái Verifying không được báo như thất bại đã xác định. Buyer tự hủy approval PayPal không phải lỗi cổng thanh toán.
- Đơn chưa trả tiền hủy ngay thì chỉ xác nhận `OrderCancelled` cho buyer. Đơn đã trả tiền tạo `CancellationRequested` cho buyer và seller, giải thích rằng đây mới là yêu cầu chờ quyết định, chưa phải hủy hoặc refund thành công. `CancelRequestNumber` phân biệt yêu cầu gửi lại sau một lần bị từ chối; gọi lặp khi đang chờ không tạo kỳ mới.
- Tạo return qua báo giá mới hoặc luồng cũ/thỏa thuận đều phát `ReturnRequested`. Duyệt gửi `ReturnApproved` trước bước tạo nhãn, nên nội dung nói rõ phải mở đơn xem tracking, không khẳng định đã có nhãn hoặc đã hoàn tiền. Tạo nhãn lỗi/retry không phát lại email duyệt. Từ chối gửi `ReturnRejected` với lý do được encode và hướng dẫn mở tranh chấp trong thời hạn hiện tại.
- Payout Level 3 hoàn tất ngay phát `PayoutCompleted` trong giao dịch tạo payout; payout qua các bước worker chỉ thông báo khi Completed hoặc Returned. Returned được phát sau ghi tiền trở về hệ thống; nội dung nhắc khả năng bù nợ, không hứa toàn bộ tiền chắc chắn còn Available.
- Tất cả mẫu mới có text + HTML tiếng Việt, encode lý do, không chứa số thẻ/token/raw lỗi từ provider. Link đơn trỏ Buyer/Seller theo người nhận; payout trỏ `/Seller?tab=finance`. Seller đã hỗ trợ mở đơn hoặc tab tài chính từ link này.

## Outbox và quyền truy cập

Thêm `RecipientUserId` và `PayoutId` nullable vào outbox. Email N1 chỉ được buyer/seller đúng `RecipientUserId` đọc; admin được tra cứu, shipper bị từ chối. Email cũ không có `RecipientUserId` giữ phạm vi theo đơn/seller như trước, không tự suy diễn chủ email từ chuỗi địa chỉ nhận.

EventType mới có tên ổn định để lọc. EventKey gồm định danh lần thanh toán/yêu cầu/payout và tài khoản nhận; unique index EventKey chống trùng. Index legacy `(OrderId, EventType)` chỉ áp dụng khi EventKey null và OrderId khác null, giữ khả năng đọc/lưu dữ liệu cũ, không chặn hai người nhận hoặc nhiều lần thử mới trên cùng đơn.

Không gọi SMTP trong các service payment/return/payout. Outbox được lưu trong cùng transaction với thay đổi nghiệp vụ. Worker chỉ thấy dữ liệu đã commit; rollback không để lại email cho một thao tác không xảy ra. Email thiếu/không hợp lệ thành Failed với `RecipientInvalid`, không làm hủy thao tác nghiệp vụ hợp lệ. Lỗi SMTP theo retry hiện có: tối đa ba lần trong một đợt, admin có thể mở đợt retry trên cùng dòng Failed. Capture có CapturedAt, không tự coi là Sent. Không tạo lại email cho các giao dịch lịch sử trước nâng cấp.

Realtime email N1 gửi invalidation cho đúng người nhận; payload không chứa nội dung email. Khi mở tab **Email thông báo**, buyer/seller/admin có thêm các bộ lọc cho sự kiện mới. Payout hiển thị **Lần rút tiền #...**, không bị gắn nhãn Khoản nợ hoặc một mã đơn giả.

## Migration và chạy

Migration mới: **20261017050657_CompleteBusinessNotifications**, sau **20261017042643_AddProductFeedbackReplies**. SQL để duyệt: [business-notifications-migration.sql](../database/business-notifications-migration.sql).

Migration thêm cột nullable RecipientUserId/PayoutId, cột CancelRequestNumber mặc định 0 và cập nhật index. Không sửa nội dung/trạng thái email cũ, số dư hoặc transaction cũ. SQL QA kiểm tra cả các email legacy có OrderId null trùng EventType.

Đợt này chỉ áp dụng trên **G4_N1_Test_20261017**, chưa cập nhật database chính. Khi nâng database đang dùng, kiểm tra connection, dừng app cũ rồi chạy từ gốc repo:

```powershell
dotnet ef database update --project backend/G4.Infrastructure --startup-project backend/G4.Api
```

Khởi động lại API/frontend sau build và migration. Giữ Mail:Mode=Capture để xem email trong app, hoặc dùng cấu hình SMTP thử nghiệm hiện có. Không chạy lại baseline trên database chứa dữ liệu.

## Kiểm thử

- Build Release đạt, không lỗi/cảnh báo; bộ console commerce đầy đủ đạt, gồm SMTP loopback và hồi quy payment/return/dispute/finance.
- N1 unit: thẻ lỗi/hết hạn; retry cùng key; nhiều lần lỗi; hủy ngay và hai kỳ yêu cầu hủy; return mới/cũ, duyệt và từ chối; payout instant/returned; recipient privacy; encode lý do; SMTP lỗi không đổi nghiệp vụ, retry có giới hạn và Capture không giả là Sent; email người nhận không hợp lệ không chặn hủy đơn.
- PayPal: cả giỏ có một unit bị từ chối, hủy approval, capture timeout/dữ liệu sai rồi reconcile; luồng single-order DENIED/FAILED/PENDING và replay. Không gọi cổng PayPal bên ngoài trong kiểm thử.
- SQL QA: giữ email cũ sau migration, nhiều attempt trên cùng đơn, hai người nhận cùng sự kiện, gọi đồng thời request/payout, unique EventKey, quyền đọc và rollback payment+outbox.
- UI: 106 test Node đạt. Browser buyer/seller/admin lọc sự kiện mới, xem nội dung/HTML an toàn, mở link đơn/tài chính, hủy thực rồi gửi lại vẫn một email, đúng quyền list/detail, desktop/mobile không overflow hoặc lỗi JavaScript. Ảnh QA ở `artifacts/n1-notifications-qa/`.

Các lệnh từ gốc repo:

```powershell
dotnet build G4_Project.sln -c Release --no-restore -m:1 -p:UseSharedCompilation=false -p:UseAppHost=false
dotnet tests/G4.Commerce.Tests/bin/Release/net8.0/G4.Commerce.Tests.dll --business-notifications
node --test tests/*.test.cjs

$env:G4_N1_SQL_CONNECTION='Server=localhost;Database=G4_N1_Test_20261017;Integrated Security=True;TrustServerCertificate=True;Connect Timeout=5'
dotnet tests/G4.Commerce.Tests/bin/Release/net8.0/G4.Commerce.Tests.dll --business-notifications-sql
```

SQL test chỉ chấp nhận đúng tên database QA trên. Nếu chưa có database, tạo data/log trong `.temp-appdata/n1-sql/` thuộc workspace trên ổ D để tránh phụ thuộc dung lượng ổ hệ thống. Không xóa/reset dữ liệu có sẵn. Mỗi lần chạy thêm fixture riêng và ghi `artifacts/n1-notifications-qa/fixture.json`; tài khoản chỉ dùng QA, mật khẩu `NoticeDemo123!`.

Browser QA: API `http://127.0.0.1:5259` dùng database QA trên, `Mail__Mode=Capture`, `Finance__WriteOff__Enabled=false`, `Frontend__PublicBaseUrl=http://127.0.0.1:5139`, `Realtime__AllowedOrigins__0=http://127.0.0.1:5139`. Frontend `http://127.0.0.1:5139` dùng `ApiSettings__BaseUrl` và `Realtime__PublicApiBaseUrl=http://127.0.0.1:5259`. Sau khi tạo fixture SQL:

```powershell
$env:G4_N1_E2E_URL='http://127.0.0.1:5139'
node tests/business-notifications.e2e.cjs
```

Script chỉ chấp nhận frontend QA này, cần Playwright và Chromium/Edge. Chạy SQL fixture lại trước lần E2E tiếp theo để có đơn chưa hủy. Nếu ổ C hết chỗ, đặt TEMP/TMP vào thư mục `.temp-appdata/n1-temp` trong workspace trước khi build/chạy; đợt này không xóa dữ liệu người dùng để giải phóng ổ C.
