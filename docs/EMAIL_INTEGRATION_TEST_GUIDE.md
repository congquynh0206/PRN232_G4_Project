# Test email thông báo và nhật ký tích hợp

Ngày 05/10/2026. Code và migration đã kiểm thử trên `G4_Email_Test_20261005`; migration vào `CloneEbayDB` là bước triển khai riêng cần người dùng duyệt. Các đơn QA không được tạo trong database chính.

## 1. Chuẩn bị chạy

- Database cần migration `20261005062401_AddEmailIntegrationDiagnostics`, sau `AddPromotions`. SQL để duyệt: [email-integration-migration.sql](../database/email-integration-migration.sql). Dừng API, sao lưu database và kiểm tra dữ liệu trước/sau khi triển khai.
- Chạy API và frontend theo [PROJECT_RUN.md](PROJECT_RUN.md). Development mặc định `Mail:Mode=Capture`: chỉ lưu nội dung trong app, không gửi thư ra ngoài.
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

Trong Capture, trạng thái **Đã lưu để xem — chưa gửi**, `CapturedAt` có giá trị, `SentAt` trống. Thư đã lưu không được tạo lại nội dung khi xem hoặc tải lại trang.

## 3. SMTP và gửi lại khi lỗi

Ví dụ chạy với SMTP/mail catcher **cục bộ đã có sẵn**:

```powershell
$env:Mail__Mode='Smtp'
$env:Mail__Host='127.0.0.1'
$env:Mail__Port='1025'
$env:Mail__EnableSsl='false'
$env:Mail__From='g4@example.test'
dotnet run --project backend/G4.Api --urls http://localhost:5251
```

`Mail__Mode` phải đổi rõ ràng vì Development mặc định Capture; chỉ đặt Host không ghi đè Mode đã cấu hình. Username/Password nếu cần đặt qua User Secrets hoặc biến môi trường, không ghi vào source.

Để thử lỗi kết nối, tắt mail catcher hoặc dùng cổng local chưa có dịch vụ, tạo **một đơn mới** rồi thanh toán. Email đang Pending sẽ được worker xử lý: lần 1, sau lỗi đợi 30 giây, lần 2, đợi thêm 2 phút, lần 3 rồi Failed. Thời gian thực tế có thể muộn hơn theo chu kỳ worker. Lỗi cấu hình/địa chỉ không hợp lệ dừng ngay để kiểm tra.

Bật lại mail catcher, đăng nhập **admin** → Email thông báo → lọc Failed → **Thử gửi lại**. Một đợt mới được mở, giữ tổng Attempts và lịch sử cũ. Buyer/seller không có quyền gửi lại. Thư Sent/Captured hoặc đang xử lý không được retry. Retry không sửa người nhận đã lưu; email thiếu/sai người nhận vẫn Failed.

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
