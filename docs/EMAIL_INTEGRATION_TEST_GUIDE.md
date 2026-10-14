# Test email thông báo và nhật ký tích hợp

Cập nhật ngày 14/10/2026: Development dùng SMTP + MailHog. Tích hợp email trước đó được kiểm thử trên `G4_Email_Test_20261005`; bài test MailHog dùng outbox InMemory, không tạo đơn hoặc sửa database SQL.

## 1. Chuẩn bị chạy

- Không cần terminal: bấm đúp `start-mailhog.cmd` trong thư mục gốc dự án. File này chạy script bên dưới và mở hộp thư tại `http://127.0.0.1:8025`. Có thể tạo shortcut trên Desktop bằng **Send to → Desktop (create shortcut)**. Nếu MailHog đã chạy, file chỉ mở hộp thư, không tạo thêm server. Hoặc mở Docker Desktop → Containers → Start container `mailhog` đã có sẵn trên máy.
- Database cần migration `20261005062401_AddEmailIntegrationDiagnostics`, sau `AddPromotions`. SQL để duyệt: [email-integration-migration.sql](../database/email-integration-migration.sql). Dừng API, sao lưu database và kiểm tra dữ liệu trước/sau khi triển khai.
- Từ thư mục gốc, chạy `powershell -ExecutionPolicy Bypass -File scripts/start-mailhog.ps1`. Script tải MailHog Windows v1.0.1 từ GitHub chính thức vào `artifacts/mailhog` ở lần đầu, sau đó chạy nền; không cần Docker. File tải về và log không được commit. Những lần sau không cần tải lại; script tái sử dụng MailHog đang chạy.
- Hoặc mở Docker Desktop và chạy `docker compose up -d --no-deps mailhog`. Chỉ service MailHog được khởi động, không thay đổi SQL Server hoặc dữ liệu dự án. Chọn một trong hai cách; không chạy hai server cùng cổng.
- Nếu máy đã có MailHog từ dự án khác chiếm cổng `1025/8025`, dùng script trên để tái sử dụng; không tạo thêm container. Trên máy hiện tại, container có tên `mailhog` đã có sẵn và được tái sử dụng. Các lệnh `docker compose stop/start/logs mailhog` bên dưới dành cho service của dự án này; với container có sẵn, dùng `docker stop mailhog`, `docker start mailhog`, `docker logs mailhog` tương ứng.
- Chạy API và frontend theo [PROJECT_RUN.md](PROJECT_RUN.md). Development mặc định `Mail:Mode=Smtp`, host `127.0.0.1`, port `1025`, không TLS hoặc tài khoản SMTP. Docker Compose dùng host `mailhog` cho API trong container.
- Mở `http://localhost:8025` để xem email MailHog nhận. Cả bản Windows và Docker chỉ mở hai cổng `1025` và `8025` tại loopback.
- Dùng tài khoản demo buyer/seller/shipper/admin, mật khẩu `G4@123456`. Các API mới dùng JWT và chỉ mở trong Development như các màn demo hiện có.

## 2. Test bằng giao diện

| Case | Thao tác | Kết quả cần thấy |
|---|---|---|
| Thanh toán | Buyer tạo giỏ, thanh toán bằng thẻ demo thành công | Tab **Email thông báo** có thư thanh toán, đúng mã đơn, số tiền USD và phương thức. |
| Giao thành công | Seller tạo vận đơn; shipper nhận đơn rồi cập nhật đến **Đã giao** | Có thư giao thành công, đúng mã vận đơn chiều giao tới buyer. |
| Giao thất bại | Một đơn khác: shipper cập nhật **Giao thất bại** từ trạng thái đang giao tới người nhận | Có thư giao chưa thành công. Mỗi đơn chỉ có một thư cho mỗi loại sự kiện, không có thư riêng cho mọi lần giao thất bại. |
| Hoàn tiền | Buyer yêu cầu hủy đơn đã trả tiền nhưng chưa gửi hàng; seller chấp nhận | Có thư hoàn tiền, số tiền lấy từ refund thành công. |
| Đơn khuyến mãi $0 | Dùng khuyến mãi hợp lệ đưa tổng phải trả về $0 | Thư ghi `0.00 USD`, thanh toán bằng khuyến mãi; nhật ký ghi **Nội bộ**, không có cuộc gọi thẻ/PayPal giả. |
| Đọc thư | Bấm **Xem nội dung**, mở **Xem mẫu email** | Nội dung text và HTML thực đã lưu; buyer/seller bấm **Xem đơn hàng** để mở chi tiết. Thư cũ thiếu HTML vẫn đọc text được. |
| Bộ lọc | Lọc mã đơn, loại thông báo, trạng thái; bấm Xóa bộ lọc | Chỉ hiện kết quả phù hợp; không có dữ liệu thì hiện hướng dẫn rõ ràng. |

Với MailHog, sau khoảng một chu kỳ worker (5 giây), trạng thái phải là **Đã gửi tới máy chủ email** (`Sent`), `SentAt` có giá trị, `CapturedAt` trống. MailHog phải có thư với đúng người nhận, mã đơn, số tiền, tiếng Việt và hai phần text/HTML. Kiểm tra cả tab email trong ứng dụng và hộp thư MailHog cho từng sự kiện trong bảng trên.

Chỉ email mới hoặc email `Failed` được admin thử lại mới gửi tới MailHog. Các thư cũ đã `Captured` hoặc `Sent` không tự gửi lại sau khi đổi cấu hình. Thư đã lưu không được tạo lại nội dung khi xem hoặc tải lại trang.

## 3. SMTP và gửi lại khi lỗi

API đã có cấu hình SMTP trong `appsettings.Development.json`, nên không cần đặt biến môi trường khi chạy bằng Visual Studio. User Secrets và biến môi trường có thể ghi đè cấu hình này; nếu vẫn thấy `Captured`, kiểm tra `Mail:Mode` trong các nguồn đó rồi khởi động lại API.

Nếu chạy API bằng terminal, có thể đặt rõ cấu hình:

```powershell
$env:Mail__Mode='Smtp'
$env:Mail__Host='127.0.0.1'
$env:Mail__Port='1025'
$env:Mail__EnableSsl='false'
$env:Mail__From='g4@example.test'
dotnet run --project backend/G4.Api --urls http://localhost:5251
```

MailHog cục bộ không cần Username/Password. Nếu trước đây đã đặt credentials SMTP trong User Secrets/biến môi trường, bỏ chúng khi dùng MailHog. Không cấu hình máy chủ gửi ra ngoài hoặc dùng nút Release của MailHog trong bài test này.

Kiểm thử tự động với MailHog thật đang chạy:

```powershell
dotnet run --project tests/G4.Commerce.Tests -- --mailhog
```

Bài test đi qua `NotificationProcessor` và `SmtpEmailSender`, kiểm tra người nhận, Message-ID, nội dung UTF-8 text/HTML và trạng thái `Sent`. Sau đó dùng một cổng local đóng để thử ba lần thất bại, mở chu kỳ gửi lại và kiểm tra MailHog nhận thư thành công. Bài test không dừng MailHog, không xóa thư khác và không sửa SQL; hai thư QA được giữ lại để xem trên web.

Ngày 14/10/2026 đã chạy bài test này thành công với MailHog Windows v1.0.1 thực tế và chạy lại thành công với container MailHog có sẵn sau khi Docker Desktop khởi động. Cuối cùng đã dừng riêng bản Windows vừa tạo và giữ container có sẵn để sử dụng. Build không có warning/error và toàn bộ commerce checks qua. Service Docker Compose mới được kiểm tra bằng `docker compose config --quiet`; không tạo thêm container trùng cổng.

Để thử lỗi kết nối qua giao diện, tắt MailHog: bản Windows dùng `Get-Process -Name MailHog_windows_amd64 | Stop-Process`, bản Docker dùng `docker compose stop mailhog`. Tạo **một đơn mới** rồi thanh toán. Email đang Pending sẽ được worker xử lý: lần 1, sau lỗi đợi 30 giây, lần 2, đợi thêm 2 phút, lần 3 rồi Failed. Thời gian thực tế có thể muộn hơn theo chu kỳ worker. Lỗi cấu hình/địa chỉ không hợp lệ dừng ngay để kiểm tra.

Bật lại bằng script `start-mailhog.ps1` (Windows) hoặc `docker compose start mailhog` (Docker), đăng nhập **admin** → Email thông báo → lọc Failed → **Thử gửi lại**. Một đợt mới được mở, giữ tổng Attempts và lịch sử cũ. Kiểm tra trạng thái `Sent` trong app và thư mới trong MailHog. Buyer/seller không có quyền gửi lại. Thư Sent/Captured hoặc đang xử lý không được retry. Retry không sửa người nhận đã lưu; email thiếu/sai người nhận vẫn Failed.

MailHog dùng bộ nhớ mặc định: tắt/chạy lại tiến trình Windows hoặc restart/recreate container làm mất thư trong hộp thư thử nghiệm; lịch sử trong database ứng dụng vẫn còn. Log bản Windows nằm trong `artifacts/mailhog`; bản Docker xem bằng `docker compose logs mailhog`. Có thể quay lại Capture bằng User Secrets `Mail:Mode=Capture` hoặc biến môi trường `Mail__Mode=Capture`, rồi khởi động lại API; Capture chỉ lưu trong app và không cần MailHog.

**Đã gửi tới máy chủ email** chỉ có nghĩa adapter SMTP báo gửi thành công, không chứng minh người nhận đã đọc hoặc thư vào Inbox. Timeout/mất kết nối có thể khiến kết quả chưa xác định. Message-ID ổn định hỗ trợ truy vết, không bảo đảm không có thư trùng sau crash.

## 4. Nhật ký và phân quyền

- Seller mở **Nhật ký tích hợp**, hoặc bấm **Xem nhật ký xử lý đơn** từ chi tiết đơn. Lọc theo mã đơn, dịch vụ, kết quả và khoảng thời gian. Từ thời điểm được tính bao gồm, Trước thời điểm được tính loại trừ.
- Dùng nút giả lập lỗi vận đơn của seller rồi tạo vận đơn: mỗi lần gọi carrier có dòng riêng, số lần và kết quả đúng thực tế. Card ghi **Giả lập**, Promotion ghi **Nội bộ**, PayPal ghi **Sandbox**.
- Buyer chỉ xem email đơn của mình. Seller chỉ xem email/nhật ký đơn shop mình. Admin xem toàn bộ, kể cả nhật ký chưa gắn được đơn. Shipper không có quyền email/nhật ký.
- PayPal Capture/Refund phản hồi không đủ dữ liệu, timeout hoặc lỗi máy chủ có thể ghi **Chưa xác định kết quả**. Cần đối soát với trạng thái payment/refund trong đơn; không suy ra tiền đã hoàn chỉ từ một dòng log.
- Nhật ký được ghi nền qua hàng đợi tối đa 512 mục, nên có thể xuất hiện sau kết quả nghiệp vụ một chút. Khi database log lỗi, hàng đợi đầy hoặc app chết đột ngột, log có thể thiếu; việc ghi log không chặn hoặc phát lại giao dịch tiền. Không lưu token/password/payload/số thẻ/CVV trong bảng log.

## 5. Kiểm thử tự động và bằng chứng QA

```powershell
dotnet build G4_Project.sln --configuration Release --no-restore -p:UseAppHost=false
dotnet tests/G4.Commerce.Tests/bin/Release/net8.0/G4.Commerce.Tests.dll
node --test tests/notifications-ui.test.cjs tests/integration-logs-ui.test.cjs tests/promotions-ui.test.cjs tests/dispute-ui.test.cjs tests/shipper-ui.test.cjs tests/ui-navigation.test.cjs
dotnet ef migrations has-pending-model-changes --project backend/G4.Infrastructure --startup-project backend/G4.Api --configuration Release --no-build
```

SQL QA riêng, dừng helper API QA trước khi chạy để worker không tranh claim fixture:

```powershell
$env:G4_EMAIL_SQL_TEST_CONNECTION='Server=localhost;Database=G4_Email_Test_20261005;Integrated Security=True;TrustServerCertificate=True'
dotnet tests/G4.Commerce.Tests/bin/Release/net8.0/G4.Commerce.Tests.dll --email-sql
```

SQL checks từ chối mọi database khác. Chạy lại chỉ thay thế các fixture orphan mang ID đơn 100010/100011/100012/100099, không xóa đơn thật hoặc fixture Captured legacy 100001. `tests/email-integration-sql.ps1` kiểm tra migration với fixture legacy đã chuẩn bị trong QA: rollback có chủ ý khi chưa áp dụng, chạy lại no-op và hash 15 bảng nghiệp vụ giữ nguyên.

Đã kiểm tra SMTP bằng TcpListener loopback: text/HTML, Message-ID, phản hồi 451, lỗi kết nối và timeout; không gửi thư Internet. PayPal OAuth/Create/Capture/Query/Refund và capture timeout được kiểm tra bằng HTTP handler giả; **chưa gọi PayPal Sandbox thật trong đợt QA này**. API JWT, bốn loại email, retry đồng thời và carrier được thử trên app chạy thật với SQL QA. Browser kiểm tra buyer/seller/admin ở desktop và 390px, preview, mở/đóng modal, bộ lọc và empty state.

Log/screenshot QA local ở `artifacts/email-integration/` được Git ignore. Source và test chưa được tự commit/push.
