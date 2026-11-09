# Database trình diễn ngày 31/10/2026

Bộ tải xuống nằm trong `database/demo/`: `G4_Demo.sql`, `HUONG_DAN_DEMO.md`,
`Run-Demo.ps1`, `validation.txt` và gói ZIP. Đây là dữ liệu trình diễn, không phải
bộ kiểm thử tải/đồng thời/provider hoặc báo cáo hoàn thành tất cả backlog.

## Cách dùng

1. Giữ các file trong thư mục `database/demo` của repo. Mở `G4_Demo.sql` bằng SSMS
   và chạy toàn bộ. Script tự tạo database mới `G4_Demo`, bảng nền và các migration
   hiện hành; không cần chạy thêm baseline hoặc seed-data. Chạy bình thường, không
   cần SQLCMD mode, file include hay công cụ EF trên máy trình diễn.
2. Mở hai PowerShell tại repo, chạy `Run-Demo.ps1 -App Api -SqlServer localhost`
   và `Run-Demo.ps1 -App Frontend`. Xem lệnh đầy đủ trong hướng dẫn kèm bộ dữ liệu.
3. Mở `http://localhost:5137`. Mật khẩu chung là `G4@123456`; bảng tài khoản và
   mã đơn/hồ sơ theo từng kịch bản có trong hướng dẫn và bảng `demo.Scenario`.

Script từ chối tên database đã tồn tại. Nếu muốn một lượt demo sạch khác, đổi
biến `@DatabaseName` ở đầu SQL thành tên mới bắt đầu bằng `G4_Demo`, rồi truyền
`-DatabaseName` tương ứng vào helper. Không tự xóa/reset dữ liệu của lượt trước.
Helper chỉ cấu hình process/child hiện tại và khôi phục môi trường khi kết thúc;
không sửa appsettings, User Secrets, kết nối chính hoặc khóa định danh local.

## Phạm vi dữ liệu

- 16 tài khoản: buyer chính/buyer khác/buyer lịch sử, hai shipper, admin và 10 seller.
- Seller level 1/2/3; level 2/3 có 20/100 đơn lịch sử, tracking đúng hạn, feedback
  tích cực và lịch sử xét cấp. Hai seller khác đủ tiêu chí nhưng chờ bấm nâng cấp.
- Các seller có nợ chưa đến hạn/quá hạn, đã write-off, đã trả hết sau write-off;
  quyết định/ledger/blacklist sinh bằng service tài chính hiện tại.
- 43 kịch bản: chờ thanh toán/thất bại/hết hạn/hủy, chuẩn bị/nhận/giao/giao lại/
  chuyển hoàn, return và refund, dispute chờ seller/buyer/admin và quyết định,
  đổi địa chỉ, promotion, review, hạn mức tháng, ví và payout.
- Level 1/2/3 đều có Available để tạo payout; đồng thời có các khoản Processing/
  OnHold phù hợp để giải thích cơ chế tiền. Có lịch sử payout Completed/Returned.
- Giá và policy của đơn đã mua giữ snapshot; promotion mới được chuẩn bị sau các
  đơn mẫu nên không tự giảm lại giá của đơn đã tồn tại. Buyer có thể dùng giỏ mới
  để trình diễn DEMO10/SAN5, sale, volume/order và miễn ship chiều đi.

Profile riêng kéo return/receipt/dispute/đổi địa chỉ lên hai giờ, hold level 1/2
thành 30/15 phút, nợ đến hạn một giờ/write-off hai giờ. Tự xét cấp mỗi 24 giờ để
giữ nguyên ca chờ bấm xét; thao tác xét thủ công vẫn dùng service thật. Đơn chưa
thanh toán giữ hạn 15 phút, nên thực hiện sớm hoặc tạo mới từ giỏ. Không thay các
giá trị mặc định đã chốt của repo.

Định danh giả lập dùng HMAC demo trong helper; core database chỉ giữ dấu đối chiếu
và phần đã che, các giá trị nhập mẫu nằm trong hướng dẫn. Phải chạy đúng cấu hình
để thử blacklist. Email dùng Capture, không gửi SMTP; không seed payment PayPal
Succeeded/Verifying giả hoặc gọi provider tiền thật. INR1/FR1/LIM1/A2 và RT2 đầy đủ
chưa được source hỗ trợ nên không tạo trạng thái giả cho các nhánh này.

## Bằng chứng kiểm chứng

- Generator build 0 warning/error. SQL đã nhập thật vào database trống riêng trên
  SQL Server 2022; chạy lại cùng tên bị từ chối, không ghi đè.
- Đối chiếu số dòng toàn bộ bảng dbo, FK/check constraints đều hợp lệ và được tin
  cậy; 16 tài khoản đăng nhập được, 10 ví không âm, Processing khớp settlement,
  doanh số tháng khớp payment, level 2/3 đúng và blacklist chặn đăng ký thật.
- API riêng trên database vừa nhập đã đọc finance/debt/level, write-off/blacklist,
  lịch sử buyer/dispute và 30 chi tiết đơn. Thực thi thử card payment, duyệt return/
  nhãn, payout ba level và nâng cấp seller 2/3 trên QA thành công.
- Parse PowerShell và format generator đạt; không thay nghiệp vụ hoặc chạy lại
  hồi quy toàn bộ ứng dụng vì thay đổi chỉ phục vụ xuất dữ liệu demo.
- Các database build/QA chỉ phục vụ kiểm chứng; không áp migration/seed lên
  database chính. Log tại `artifacts/demo-data-*.log` và `artifacts/demo-api-*.log`.

Trong lúc QA phát hiện ổ C hết chỗ. Chỉ dọn database thử lỗi/tạo dở của phiên này,
không đổi cấu hình tempdb hoặc xóa dữ liệu có sẵn. SQL cuối đặt filegrowth 8 MB
cho database demo mới để tránh bước tăng 64 MB mặc định. Máy chạy vẫn cần đủ chỗ
trống để tạo database và log.

Generator tái tạo bộ dữ liệu nằm ở `tools/G4.DemoData`. Lệnh chạy từ repo:

```powershell
dotnet run --project tools/G4.DemoData -- --server localhost
```

Công cụ chỉ tạo tên `G4_Demo_Build_*`/`G4_Demo_QA_*`, dùng Windows Authentication,
fake carrier/refund thẻ và các service hiện tại. Không thêm project này vào luồng
startup ứng dụng. File tải xuống đã có đủ schema/data nên người trình diễn không
cần chạy generator.
