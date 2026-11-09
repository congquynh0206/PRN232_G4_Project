# Dữ liệu trình diễn G4

Chạy G4_Demo.sql trong SSMS để tạo database mới G4_Demo. Script từ chối database đã tồn tại; không xóa hoặc sửa database hiện có.

Mật khẩu chung mọi tài khoản: **G4@123456**. Toàn bộ tài khoản, giao dịch và định danh đều giả lập.

## Chạy ứng dụng với dữ liệu demo

Mở hai cửa sổ PowerShell tại repo, chạy helper kèm theo (không sửa cấu hình gốc):

```powershell
.\database\demo\Run-Demo.ps1 -App Api -SqlServer localhost
.\database\demo\Run-Demo.ps1 -App Frontend
```

Mở http://localhost:5137. API demo ở http://localhost:5257. Helper trỏ đúng G4_Demo, dùng HMAC demo và Mail Capture; không gửi thư/call cổng tiền thật.
Nếu giải nén ở ngoài repo, thêm `-ProjectRoot 'D:\...\G4_Project'`. Nếu SQL Server dùng instance khác, thay -SqlServer. Kết nối mặc định Windows Authentication.

## Tài khoản

| Email | Role | Dùng để demo | Định danh giả lập |
| --- | --- | --- | --- |
| buyer@demo.test | buyer | Buyer trình diễn | — |
| buyer.other@demo.test | buyer | Buyer khác để kiểm tra quyền | — |
| buyer.history@demo.test | buyer | Buyer lịch sử hỗ trợ xét level | — |
| shipper@demo.test | shipper | Shipper chính | — |
| shipper.other@demo.test | shipper | Shipper khác để kiểm tra quyền | — |
| admin@demo.test | admin | Admin trình diễn | — |
| seller.l1@demo.test | seller | Shop cấp 1 - đơn hàng/return/dispute | 999100000001 |
| seller.l2@demo.test | seller | Shop cấp 2 - giữ tiền 15 phút | 999100000002 |
| seller.l3@demo.test | seller | Shop cấp 3 - tiền vào ví ngay | 999100000003 |
| seller.up2@demo.test | seller | Shop đủ tiêu chí lên cấp 2 - chờ bấm xét lại | 999100000004 |
| seller.up3@demo.test | seller | Shop đủ tiêu chí lên cấp 3 - chờ bấm xét lại | 999100000005 |
| seller.debt@demo.test | seller | Shop nợ chưa đến hạn - có thể trả nợ | 999100000006 |
| seller.overdue@demo.test | seller | Shop nợ quá hạn - bị hạn chế | 999100000007 |
| seller.writeoff@demo.test | seller | Shop đã write-off - khóa bán vĩnh viễn | 999100000008 |
| seller.recovered@demo.test | seller | Shop đã trả hết sau write-off - vẫn khóa bán | 999100000009 |
| seller.limit@demo.test | seller | Shop cấp 1 gần chạm hạn mức tháng | 999100000010 |

## Kịch bản có sẵn

| Mã | Loại/ID | Seller liên quan | Role thao tác đầu | Thao tác | Kết quả |
| --- | --- | --- | --- | --- | --- |
| LEVEL-L2 | Seller #8 | seller.l2@demo.test | seller | Xem level, số liệu và lịch sử xét cấp | Cấp 2 có dữ liệu đơn/feedback/tracking hỗ trợ |
| LEVEL-L3 | Seller #9 | seller.l3@demo.test | seller | Xem level, số liệu và lịch sử xét cấp | Cấp 3 có dữ liệu đơn/feedback/tracking hỗ trợ |
| LEVEL-UP2 | Seller #10 | seller.up2@demo.test | seller | Bấm Xét lại cấp để chạy xét thật | Đủ tiêu chí lên 2; chưa tự nâng trước lúc demo |
| LEVEL-UP3 | Seller #11 | seller.up3@demo.test | seller | Bấm Xét lại cấp để chạy xét thật | Đủ tiêu chí lên 3; chưa tự nâng trước lúc demo |
| PAY-READY | Order #241 | seller.l1@demo.test | buyer | Buyer mở đơn, thanh toán thẻ hoặc PayPal Sandbox đã cấu hình | Đơn chờ thanh toán; hạn 15 phút, nên trình diễn sớm |
| PAY-FAILED | Order #242 | seller.l1@demo.test | buyer | Buyer xem lần thanh toán bị từ chối rồi thử thẻ mới | Có lịch sử Failed; vẫn chờ thanh toán trong thời hạn |
| EXPIRED | Order #243 | seller.l1@demo.test | buyer | Buyer/Admin xem đơn hết hạn | Expired; tồn kho đã trả lại đúng một lần |
| CANCEL-UNPAID | Order #244 | seller.l1@demo.test | buyer | Buyer xem đơn đã hủy khi chưa thanh toán | Cancelled, không có refund |
| PAID-READY | Order #245 | seller.l1@demo.test | seller | Seller chuẩn bị hàng/tạo vận đơn | Có payment và settlement, chưa có vận đơn |
| CANCEL-REQUEST | Order #246 | seller.l1@demo.test | seller | Seller duyệt hoặc từ chối hủy | CancelRequested; duyệt thì refund, từ chối giữ đơn |
| SHIP-LABELCREATED | Order #247 | seller.l1@demo.test | shipper | Shipper nhận vận đơn đang chờ | Vận đơn 241, trạng thái LabelCreated |
| SHIP-PICKEDUP | Order #248 | seller.l1@demo.test | shipper | Shipper chính cập nhật bước tiếp theo | Vận đơn 242, trạng thái PickedUp |
| SHIP-INTRANSIT | Order #249 | seller.l1@demo.test | shipper | Shipper chính cập nhật bước tiếp theo | Vận đơn 243, trạng thái InTransit |
| SHIP-OUTFORDELIVERY | Order #250 | seller.l1@demo.test | shipper | Shipper chính cập nhật bước tiếp theo | Vận đơn 244, trạng thái OutForDelivery |
| SHIP-FAILED | Order #251 | seller.l1@demo.test | shipper | Shipper lên lịch giao lại | Lên lịch không tăng lượt; bắt đầu giao mới tăng lượt |
| SHIP-REATTEMPT | Order #252 | seller.l1@demo.test | shipper | Shipper bấm bắt đầu giao lại | Lên lịch không tăng lượt; bắt đầu giao mới tăng lượt |
| SHIP-RETURNING | Order #253 | seller.l1@demo.test | shipper | Shipper ghi ReturnedToSeller khi hàng thực sự về | Chiều đi chuyển hoàn, khác buyer trả hàng |
| DELIVERED-READY | Order #254 | seller.l1@demo.test | buyer | Buyer mở yêu cầu trả hàng/đánh giá; Seller xem tiền giữ | Delivered, cửa sổ return 2 giờ theo profile demo |
| LEVEL3-INSTANT | Order #255 | seller.l3@demo.test | seller | Seller cấp 3 mở ví và chi tiết đơn chưa giao | Net vào Available ngay sau payment; chưa giao vẫn không phải Processing |
| LEVEL2-HOLD | Order #256 | seller.l2@demo.test | seller | Seller cấp 2 xem lịch giữ tiền | Processing; hold snapshot 15 phút từ Delivered |
| MONTH-LIMIT | Order #257 | seller.limit@demo.test | buyer | Buyer tạo/thanh toán thêm đơn trên 22,90 USD cho shop này | Doanh số 4977,10 USD; hạn mức mặc định cấp 1 là 5000 USD/tháng UTC |
| RETURN-REQUEST-CHANGEDMIND | Order #258 | seller.l1@demo.test | seller | Seller xem báo giá và duyệt hoặc từ chối return | Requested; phí/tiền hoàn đã snapshot theo lý do |
| RETURN-REQUEST-NOTASDESCRIBED | Order #259 | seller.l1@demo.test | seller | Seller xem báo giá và duyệt hoặc từ chối return | Requested; phí/tiền hoàn đã snapshot theo lý do |
| RETURN-REJECTED | Order #260 | seller.l1@demo.test | buyer | Buyer xem bị từ chối và mở tranh chấp | Không tự refund khi bị từ chối; RT2 deadline riêng chưa triển khai |
| RETURN-SHIPPING | Order #261 | seller.l1@demo.test | shipper | Shipper nhận vận đơn chiều Return và giao về seller | Hai chiều có tracking riêng; chưa refund |
| RETURN-RECEIVE | Order #262 | seller.l1@demo.test | seller | Seller xác nhận nhận hàng hợp lệ hoặc báo vấn đề | Return Delivered; chờ kiểm tra, receipt 2 giờ theo profile |
| RETURN-REFUNDED | Order #263 | seller.l1@demo.test | buyer | Buyer/Seller/Admin xem kết quả tiền hoàn và ledger | Refund Card mô phỏng thành công; không gọi PayPal |
| REVIEW-READY | Order #264 | seller.l1@demo.test | seller | Seller vào Đánh giá sản phẩm và trả lời review | Review sản phẩm có verified purchase, chưa có reply |
| DISPUTE-SELLER | Order #265 | seller.l1@demo.test | seller | Seller gửi phương án | Dispute có lịch sử và khoản hold thực qua service |
| DISPUTE-BUYER | Order #266 | seller.l1@demo.test | buyer | Buyer chấp nhận hoặc chuyển admin | Dispute có lịch sử và khoản hold thực qua service |
| DISPUTE-ADMIN | Order #267 | seller.l1@demo.test | admin | Admin preview và ra phán quyết | Dispute có lịch sử và khoản hold thực qua service |
| DISPUTE-RESOLVED | Order #268 | seller.l1@demo.test | seller | Admin/Buyer xem quyết định và lịch sử hoàn một phần | Dispute có lịch sử và khoản hold thực qua service |
| ADDRESS-SELLER | Order #269 | seller.l1@demo.test | seller | Seller duyệt, chưa có shipper nên áp dụng ngay | Pending; mới áp dụng khi đủ xác nhận |
| ADDRESS-SHIPPER | Order #270 | seller.l1@demo.test | shipper | Shipper quyết định địa chỉ sau seller | AwaitingShipper; địa chỉ cũ còn hiệu lực |
| PROMOTIONS | Seller #7 | seller.l1@demo.test | buyer | Seller xem promotion; Buyer random giỏ và nhập DEMO10 hoặc SAN5 theo shop | Sale, volume/order, free shipping, coupon seller và coupon sàn; không có free returns chưa triển khai |
| DEBT-DEBT | Seller #12 | seller.debt@demo.test | seller | Seller xem và trả một phần/toàn bộ nợ; Admin xem hạn chế | Nợ chưa đến hạn |
| DEBT-OVERDUE | Seller #13 | seller.overdue@demo.test | seller | Seller xem và trả một phần/toàn bộ nợ; Admin xem hạn chế | Nợ quá hạn, hạn chế bán/payout |
| DEBT-WRITEOFF | Seller #14 | seller.writeoff@demo.test | admin | Admin xem quyết định, blacklist và thử đăng ký bằng định danh của seller này | Đã write-off, còn nợ và khóa bán vĩnh viễn |
| DEBT-RECOVERED | Seller #15 | seller.recovered@demo.test | admin | Admin xem write-off/thu hồi, Seller vẫn không thể bán/rút | Nợ đã về 0, vẫn khóa bán vĩnh viễn |
| PAYOUT-COMPLETED | Payout #5 | seller.l3@demo.test | seller | Seller xem lịch sử từng bước và số đã chi | Completed, trừ ví một lần |
| PAYOUT-RETURNED | Payout #6 | seller.l3@demo.test | seller | Seller xem payout ngân hàng trả về | Returned, trả tiền vào ví đúng một lần |
| PAYOUT-READY | Seller #9 | seller.l3@demo.test | seller | Seller nhập số tiền, tạo payout thành công hoặc bật mô phỏng lỗi | Available còn đủ; worker chuyển Created/InProgress/FundsSent/Completed hoặc Returned |
| PAYOUT-L1-READY | Seller #7 | seller.l1@demo.test | seller | Seller cấp 1 tạo payout bằng Available từ đơn lịch sử đã mở khóa | Cấp 1 cũng rút qua ngân hàng giả lập; không cần lên cấp 3 |

## Lưu ý lúc trình diễn

- SQL dùng UTC lúc chạy để đặt lại mốc tương đối. Chạy SQL ngay trước buổi demo. Đơn chưa thanh toán vẫn có hạn 15 phút nên làm PAY-READY/PAY-FAILED trước; sau đó có thể tạo mới từ giỏ.
- Profile demo kéo cửa sổ return/receipt/dispute/đổi địa chỉ lên 2 giờ, hold cấp 1/2 thành 30/15 phút, nợ đến hạn 1 giờ/write-off 2 giờ. Đây là cấu hình demo riêng, không thay mặc định nghiệp vụ của repo.
- Tự xét level đặt 24 giờ để không nâng các seller up2/up3 trước khi bấm. Bấm Xét lại vẫn chạy service thật. Seller l2/l3 có 20/100 đơn và feedback/tracking hỗ trợ, không chỉ gán số level.
- Write-off có sẵn được tạo bằng service sau refund khi tiền đã payout, giữ cả ledger/nợ/history/blacklist. Có seller đã trả hết sau write-off nhưng vẫn bị khóa.
- Để thử blacklist dùng định danh seller.writeoff hoặc nút mẫu blacklist 999000000002. Phải dùng khóa HMAC demo do helper cấp; chỉ đổi DB mà dùng khóa local khác sẽ bị từ chối an toàn.
- Không tạo PayPal Succeeded/Verifying giả. Có thể chọn thẻ mô phỏng 4111111111111111, expiry 12/30; 4000000000000002 để bị từ chối. PayPal thật cần credential Sandbox riêng.
- Link example.com trong dispute chỉ là link bằng chứng mô phỏng, không phải ảnh đã được xác minh. Email Capture hiển thị trong app, không gửi SMTP. Hộp chuông sinh từ outbox thực.
- INR1, FR1, LIM1, A2 và RT2 đầy đủ chưa có source: SQL không tạo thêm chức năng hoặc trạng thái giả cho các nhánh này. Dữ liệu bao phủ các nhánh hiện có, không mô phỏng lỗi mạng/đồng thời.
- Quota đăng nhập là 10 lần/phút/IP. Khi đổi nhiều tài khoản liên tiếp có thể phải đợi 429; các tài khoản đã đăng nhập có quota riêng theo UserId.
- Dữ liệu sẽ thay đổi khi worker chạy/thao tác. SQL không tự reset database đã tồn tại. Muốn buổi demo mới, đổi biến @DatabaseName ở đầu SQL sang G4_Demo_Lan2 và truyền -DatabaseName tương ứng cho helper.

## Tra cứu từ SQL

```sql
USE G4_Demo;
SELECT * FROM demo.Scenario ORDER BY Code;
SELECT * FROM demo.LoginAccount ORDER BY Email;
```

