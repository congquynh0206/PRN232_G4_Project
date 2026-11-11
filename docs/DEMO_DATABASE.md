# Database trình diễn (cập nhật 09/11/2026)

Bộ file nằm trong `database/demo/`: `G4_Demo.sql`, `HUONG_DAN_DEMO.md`,
`Run-Demo.ps1`, `validation.txt`. Đây là dữ liệu trình diễn, không phải
bộ kiểm thử tải/đồng thời/provider hoặc báo cáo hoàn thành tất cả backlog.
Gói ZIP `G4_Demo_2026-10-31.zip` là bản cũ trước Best Offer/Buy It Now; không dùng.

## Cập nhật 11/11/2026 — địa chỉ, SĐT, phí vùng (ADDR2)

- `G4_Demo.sql` sinh lại bằng `tools/G4.DemoData` đã sửa: gồm migration
  `20261111034557_AddDeliveryPhoneAndAddressChangeFields`; địa chỉ dùng tỉnh/phường chuẩn
  ("Hà Nội", "Phường Hoàn Kiếm", "Hồ Chí Minh") và SĐT `+84…`; buyer chính có thêm địa chỉ
  Hoa Kỳ (`+1 (212) 555-0123`, NY 10118) để demo đầu số +1, ZIP bắt buộc và phí VN ↔ US.
  Đơn mẫu lưu đủ snapshot người nhận (tên/đường/phường/tỉnh/SĐT) như checkout thật.
  Ca `ADDRESS-SELLER`/`ADDRESS-SHIPPER` đổi cả địa chỉ và SĐT.
- Database `G4_Demo` đang có trên máy: đã áp migration trên bằng `dotnet ef database update`
  (không tạo lại) và chuẩn hóa 14 địa chỉ + thêm địa chỉ Hoa Kỳ + điền SĐT người nhận cho
  279 đơn. Chuỗi snapshot địa chỉ của đơn cũ trong database này vẫn dạng "Hanoi", và hai
  yêu cầu `ADDRESS-SELLER`/`ADDRESS-SHIPPER` cũ sẽ bị từ chối khi duyệt vì báo giá nay gồm
  SĐT/ZIP; chạy lại `G4_Demo.sql` để có bộ dữ liệu mới hoàn toàn khớp file.
- Kiểm chứng 11/11: generator sinh 50 kịch bản; SQL nhập vào QA, `@ReplaceExisting = 0` từ
  chối, chạy lại với `1` tạo lại; 16 đăng nhập, 10 ví, blacklist, 50 kịch bản đạt. Database
  build/QA tạm của lần sinh này đã xóa (ổ C còn ít chỗ). App qua `Run-Demo.ps1` trên
  `G4_Demo`: đăng nhập buyer, sổ địa chỉ, gợi ý VietMap thật và báo giá đổi địa chỉ đạt.
- Gợi ý số nhà VietMap dùng khóa trong user-secrets của G4.Api; `Run-Demo.ps1` chạy
  Development nên tự đọc khóa, không cần biến môi trường thêm.

## Cập nhật 09/11/2026

Theo yêu cầu người dùng: chỉ cần chạy một file SQL trong SSMS là có database demo đủ.

- SQL chứa baseline và **toàn bộ migration hiện có** (gồm Public coupon, Unpaid Item,
  Best Offer, Klarna, Immediate checkout/saga, refund thẻ). Không cần `dotnet ef`.
- `@ReplaceExisting = 1` (mặc định): nếu database `G4_Demo...` đã có thì xóa và tạo lại.
  Chỉ nhận tên bắt đầu bằng `G4_Demo`; đặt `0` để dừng như trước. Tắt API demo trước.
- Kịch bản mới: Unpaid Item (`UNPAID-OVERDUE`, `UNPAID-BAN` thay `EXPIRED` vì đơn
  không còn tự hết hạn), Best Offer (`BO-ACCEPTED`, `BO-AUTO`, `BO-PENDING-SELLER`,
  `BO-PENDING-BUYER`), Buy It Now (`BUY-NOW`, `BUY-NOW-LAST-ONE`); DEMO10/SAN5 là
  coupon công khai ở tab Ưu đãi.
- Profile demo nới thời hạn để trạng thái chuẩn bị giữ đúng khoảng 4 giờ sau khi chạy
  SQL: return/receipt/dispute/đổi địa chỉ 6 giờ, giữ tiền cấp 1/2 là 4/2 giờ, nợ đến
  hạn 6 giờ, write-off 12 giờ, giữ hàng chờ thanh toán 6 giờ, cửa sổ điểm phạt 30 ngày.
  `Run-Demo.ps1` và `tools/G4.DemoData` dùng cùng giá trị; mặc định repo không đổi.
- Đầu file có các lệnh `SET QUOTED_IDENTIFIER ON`... để chạy được cả bằng `sqlcmd`
  (sqlcmd mặc định tắt, làm lỗi tạo filtered index).

Kiểm chứng 09/11: generator build 0 lỗi; dựng 50 kịch bản bằng service thật; SQL nhập
vào database QA riêng, `@ReplaceExisting = 0` từ chối, chạy lại với `1` xóa và tạo lại;
kiểm tra số dòng, FK/check, 16 đăng nhập, 10 ví, blacklist, 50 kịch bản đạt. Chạy chính
file bằng `sqlcmd` tạo `G4_Demo` trong 56 giây (gồm 7 migration mới). API/frontend qua
`Run-Demo.ps1`: 31/32 đơn mở bằng buyer chính (đơn của buyer.other bị 403 đúng quyền),
coupon công khai, 2 sản phẩm Buy It Now, seller chấp nhận Best Offer, tự từ chối dưới
ngưỡng, thanh toán thẻ đơn Best Offer, seller hủy Unpaid Item làm buyer.other bị khóa
(403), seller.debt chưa quá hạn; log API không có lỗi. Database build/QA tạm đã xóa;
`G4_Demo` trên máy đã bị thao tác khi kiểm thử nên cần chạy lại SQL trước khi demo.
Không chạm `CloneEbayDB`.

## Cách dùng

1. Mở `database/demo/G4_Demo.sql` bằng SSMS và chạy toàn bộ. Script tạo (hoặc tạo lại)
   database `G4_Demo`, bảng nền, mọi migration và dữ liệu. Không cần baseline,
   seed-data, SQLCMD mode hay công cụ EF.
2. Mở hai PowerShell tại repo, chạy
   `powershell -ExecutionPolicy Bypass -File .\database\demo\Run-Demo.ps1 -App Api -SqlServer localhost`
   và `powershell -ExecutionPolicy Bypass -File .\database\demo\Run-Demo.ps1 -App Frontend`.
   `-ExecutionPolicy Bypass` tránh lỗi "running scripts is disabled" và chỉ áp dụng cho lệnh đó.
3. Mở `http://localhost:5137`. Mật khẩu chung là `G4@123456`; bảng tài khoản và
   mã đơn/hồ sơ theo từng kịch bản có trong hướng dẫn và bảng `demo.Scenario`.

Muốn giữ bản cũ thì đổi `@DatabaseName` sang tên khác bắt đầu bằng `G4_Demo` và
truyền `-DatabaseName` tương ứng vào helper.
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
- Tên, mô tả và link ảnh sản phẩm lấy từ 15 sản phẩm có ảnh trong bảng `Product` của
  CloneEbayDB (10/11/2026), dùng lặp lại cho 32 sản phẩm demo và giữ tiền tố seller
  như `[L1]`. Giá, cân nặng, tồn kho và cấu hình Best Offer vẫn theo demo vì kịch bản
  dựa vào các số này. Ảnh là link ngoài (gstatic/amazon) nên máy trình diễn cần mạng;
  mất mạng thì app hiện ô ảnh mặc định.

Thời hạn của profile demo xem mục Cập nhật 09/11 ở trên. Tự xét cấp mỗi 24 giờ để
giữ nguyên ca chờ bấm xét; thao tác xét thủ công vẫn dùng service thật. Không thay
các giá trị mặc định đã chốt của repo.

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
