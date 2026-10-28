# Chuông thông báo theo tài khoản

## Quyết định ngày 27/10/2026

Buyer, Seller, Shipper và Admin đều có chuông trong thanh đầu trang. Badge là tổng
thông báo **chưa đọc của tài khoản**, hiển thị 1–9, trên 9 là `9+`; số 0 thì ẩn.
Bấm chuông mở danh sách gồm tiêu đề, nội dung thay đổi, giờ địa phương và trạng thái
đọc. Mở danh sách không đánh dấu đã đọc. Bấm một thông báo đánh dấu đã đọc trên server
rồi mở đơn hoặc mục liên quan. Có phân trang 10 mục và “Đánh dấu tất cả đã đọc”.

Thông báo và ReadAt lưu SQL Server, tồn tại sau reload/đăng nhập lại. Các tab/thiết bị
của cùng tài khoản được cập nhật qua SignalR. Mark-all chỉ áp dụng đến LatestId đã
nhận từ server; thông báo mới đến sau mốc đó tiếp tục chưa đọc. Không có tài khoản nào,
kể cả admin, được đọc hoặc đánh dấu hộp chuông của người khác.

Hộp chuông tách với outbox email và trạng thái gửi SMTP. Gửi email lỗi/retry không
tăng số chuông. Event trước migration không có snapshot nội dung được giữ nguyên,
không biến thành hàng loạt thông báo chưa đọc giả.

## Sự kiện và người nhận

| Thay đổi | Người nhận và liên kết |
| --- | --- |
| Tạo/chuyển trạng thái đơn, payment, refund, return | Buyer/seller của đơn; mở chi tiết đơn |
| Tạo vận đơn | Buyer/seller; shipper nhận tin công việc chung không có mã đơn/nội dung riêng |
| Nhận vận đơn, tracking | Buyer/seller và shipper được gán đúng vận đơn |
| Đổi địa chỉ | Buyer/seller và shipper được yêu cầu xác nhận nếu có |
| Tranh chấp, phản hồi/bằng chứng | Buyer/seller; admin khi hồ sơ đã chuyển admin |
| Số dư/trạng thái ví, nợ, payout | Seller; admin nhận cập nhật theo phạm vi quản trị |
| Đổi cấp seller, đánh giá seller | Tài khoản liên quan; admin theo phạm vi realtime hiện có |
| Đánh giá sản phẩm/phản hồi seller | Buyer/seller liên quan; mở đơn |

Chỉ thay đổi nghiệp vụ có ý nghĩa mới tạo nội dung chuông. Không phát chuông cho mỗi
lần worker đọc/đối soát, cập nhật timestamp nội bộ, gửi lại email hoặc đánh dấu đã đọc.
Payment Verifying ghi rõ chưa xác nhận thành công; FundsSent ghi rõ chờ ngân hàng.
Không lưu địa chỉ, bằng chứng, số dư cụ thể hoặc lỗi provider trong mô tả chuông.

## Luồng dữ liệu

1. SaveChanges tạo CommerceChange cùng transaction nghiệp vụ, kèm snapshot tiêu đề/
   nội dung ngắn. SQL claim vận đơn và reply review ghi event ngay tại use case.
2. CommerceChangeWorker đọc event đã commit, giải người nhận phía server từ order,
   vận đơn, tài khoản và nhóm role; BellNotificationService tạo UserNotification.
3. Unique `(UserId, CommerceChangeId)` chống trùng khi dispatcher retry. Inbox lưu
   **trước** khi gửi SignalR CommerceChanged với topic `notification-inbox`.
4. Browser lấy lại danh sách/số chưa đọc từ REST, không cộng số từ payload SignalR.
   Push có thể lặp; reconnect/online và fallback 30 giây tải bù dữ liệu.
5. Mark-read chỉ cập nhật ReadAt đang null. Cập nhật và invalidation cá nhân nằm cùng
   transaction. Lặp lại không đổi thời điểm đọc đầu tiên.

Chuông đăng ký listener immediate trên kết nối SignalR hiện có: badge vẫn cập nhật
khi form/modal nghiệp vụ mở, không thay nội dung hoặc focus form. Các loader khác
tiếp tục cơ chế trì hoãn cũ. Payload hub chỉ chứa version/topics, client không tự
chọn user/group. Vẫn triển khai một API instance; nhiều instance cần backplane và
điều phối dispatcher như [COMMERCE_REFRESH.md](COMMERCE_REFRESH.md).

## API và UI

JWT role buyer/seller/shipper/admin là bắt buộc. UserId lấy từ claim, không nhận qua
query/body; admin cũng chỉ thấy hộp chuông của chính tài khoản.

- `GET /api/me/notifications?page=1&pageSize=10`: Page, PageSize, TotalCount,
  UnreadCount toàn hộp, LatestId toàn hộp và Items; PageSize giới hạn 1–30.
- `POST /api/me/notifications/{id}/read`: 204 khi thuộc tài khoản, kể cả đã đọc;
  404 khi không tồn tại hoặc thuộc người khác.
- `POST /api/me/notifications/read-all`: `{ "throughId": LatestId }`, trả 204;
  ThroughId âm trả 400. Chỉ đánh dấu dòng của tài khoản có Id không vượt mốc.

Frontend gọi qua proxy cookie/JWT hiện có; escape nội dung và kiểm tra link nội bộ
đúng role. Chuông có tên truy cập, một live region đọc số chưa đọc theo câu hoàn chỉnh,
Escape/Đóng trả focus về chuông, click ngoài đóng và danh sách cuộn trên mobile.

## Migration và chạy

Migration `20261027153709_AddUserNotificationBell` chỉ thêm hai cột nullable vào
CommerceChange và bảng UserNotification/index/FK; không backfill ReadAt hoặc sửa
order/payment/ledger/email cũ. [SQL duyệt](../database/notification-bell-migration.sql).

Trước khi dùng binary mới trên database đã có baseline/migration cũ: dừng API, sao
lưu và áp migration tăng dần đúng connection string, rồi restart API/frontend:

```powershell
dotnet ef database update --project backend/G4.Infrastructure --startup-project backend/G4.Api
```

Không chạy lại baseline trên database có dữ liệu. Migration mới chỉ được áp dụng
trên **G4_NotificationBell_Test_20261027**, chưa áp database chính.

## Kiểm thử

Đã đạt solution build và backend Release 0 warning/error; backend mặc định đầy đủ
(gồm SMTP loopback), Node **135/135**, test chuông InMemory và SQL QA riêng. SQL xác
nhận migration giữ event cũ, key chống trùng, đọc cạnh tranh/idempotency, quyền và
rollback. Không dùng QA N1 cũ làm bằng chứng cho chuông mới.

Chrome headless nối Razor/API/SQL/SignalR thật đạt cả bốn role ở **320/390/1440px**:
không tràn ngang, badge 0/1–9/9+, mở không đánh dấu đọc, keyboard/Escape/focus,
phân trang, read/link theo role, mark-all đồng bộ tab khác, reload giữ trạng thái,
offline/online tải bù, badge vẫn cập nhật khi modal chứa draft. Đã kiểm tra HTTP
401 và 404 với thông báo người khác, frame notification-inbox thật tới bốn role,
lỗi tải tạm thời và escape HTML. Ảnh/log tại `artifacts/notification-bell-qa/`;
đã xem ảnh Buyer mobile, Shipper 320px và Admin desktop. Thẻ/carrier giả lập;
không gọi PayPal hoặc SMTP bên ngoài. Database chính và tiến trình app chính không
được nâng/restart trong đợt QA này.

```powershell
dotnet build G4_Project.sln --artifacts-path artifacts/bell-validation -m:1 --disable-build-servers -p:NuGetAudit=false -p:UseAppHost=false
dotnet artifacts/bell-validation/bin/G4.Commerce.Tests/debug/G4.Commerce.Tests.dll --notification-bell
$env:G4_BELL_SQL_CONNECTION='Server=localhost;Database=G4_NotificationBell_Test_20261027;Integrated Security=True;TrustServerCertificate=True'
dotnet artifacts/bell-validation/bin/G4.Commerce.Tests/debug/G4.Commerce.Tests.dll --notification-bell-sql
node --test tests/*.test.cjs
node tests/notification-bell.e2e.cjs
```

SQL test chỉ chấp nhận đúng database QA trên, chỉ bootstrap baseline khi QA trống,
giữ dữ liệu đã có. Bộ SQL không nằm trong backend mặc định.

Browser test tự mở API/frontend QA ở port loopback riêng và đóng khi xong; cần
`NODE_PATH` trỏ tới Playwright, tùy chọn `CHROME_PATH` trỏ Chrome cài sẵn. Mặc định
dùng DLL trong `artifacts/bell-validation/bin`; có thể đổi bằng `G4_BELL_BUILD`.
Chỉ chấp nhận connection database QA trên, không dùng app/database chính. Với sandbox
chặn SQL hoặc socket loopback, cần môi trường cho phép các kết nối cục bộ đó.
