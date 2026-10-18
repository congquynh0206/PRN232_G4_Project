# Đổi địa chỉ cho đơn đã đặt

**Căn cứ mới 18/10/2026:** đọc [đặc tả §6](BUSINESS_REQUIREMENTS.md#6-giao-hàng-và-đổi-địa-chỉ) và [task AD1](IMPLEMENTATION_BACKLOG.md#3-ad1--xác-nhận-địa-chỉ-trước-khi-áp-dụng). Nội dung bên dưới mô tả triển khai trước nâng cấp: seller duyệt đã đổi snapshot rồi shipper ack. Hành vi mục tiêu mới là đủ xác nhận trước áp dụng, không chờ shipper nếu chưa gán, có timeout, và không hoàn chi phí ship buyer chưa trả. Chưa nhận task mới là đã làm chỉ từ hướng dẫn/QA cũ này.

Buyer mở chi tiết một đơn đã thanh toán và chọn **Yêu cầu đổi địa chỉ**. Nhập tên, đường, thành phố, tỉnh/quốc gia mới và lý do. Đơn được đổi thành công tối đa một lần, khi đang Paid/Preparing/Shipping và trước lần OutForDelivery đầu tiên. Không nhận yêu cầu khi đang có trả hàng/tranh chấp. Đơn chưa thanh toán có thể hủy và checkout lại với địa chỉ mới.

Yêu cầu lưu riêng trong `AddressChangeRequest`, có địa chỉ cũ/mới, phí theo tuyến, mức seller chịu, thời điểm và quyết định. Không sửa hồ sơ địa chỉ mặc định của buyer. Request key chống gửi trùng; mỗi đơn chỉ có một yêu cầu Pending. Yêu cầu bị từ chối không dùng lượt đổi thành công.

## Seller xử lý

Trong **Thiết lập giao hàng**, seller đặt **Mức chịu thêm phí ship (USD)**. Mặc định 0; chỉ mức tăng phí bằng hoặc thấp hơn cap mới có thể được duyệt. Yêu cầu vượt cap tự bị từ chối. Nếu seller giảm cap trong lúc yêu cầu đang chờ, quyết định duyệt kiểm tra lại cap hiện tại.

Danh sách seller đánh dấu **Chờ duyệt đổi địa chỉ**. Chi tiết hiển thị địa chỉ và phí cũ/mới, có nút duyệt/từ chối; từ chối phải ghi lý do. Khi bắt đầu OutForDelivery, yêu cầu còn Pending tự chuyển Rejected. Sau khi duyệt, cập nhật snapshot giao hàng của order và vận đơn chiều đi hiện có. Nếu chưa có vận đơn, vận đơn tạo sau đó lấy địa chỉ mới.

Phí tính từ kg và vùng snapshot của order, không đọc lại hồ sơ buyer/seller đang có thể sửa. Đơn mới lưu riêng tỉnh/quốc gia ở checkout. Đơn cũ có thể lấy hai phần cuối của snapshot đủ năm thành phần (tên, đường, thành phố, tỉnh, quốc gia); snapshot thiếu thông tin bị chặn đổi địa chỉ thay vì đoán vùng.

## Tiền và vận chuyển

- Phí tăng trong cap: buyer không trả thêm. Seller chịu khoản tăng, ghi ledger và trừ tiền đang xử lý của đơn; thiếu tiền tạo Negative. Tổng tiền/payment, phí nền tảng và promotion snapshot gốc được giữ nguyên.
- Phí giảm: hoàn chênh lệch **giả lập nội bộ**, tối đa phần ship buyer đã trả và số tiền captured còn lại. Không hoàn phần ship do promotion tài trợ; đơn trả 0 không tạo refund tiền. Mã hoàn bắt đầu `SIM-SHIP-`, không gọi refund PayPal thật. Settlement vẫn giữ lịch mở khóa; không hoàn phí nền tảng hoặc đảo subsidy chỉ vì đổi tuyến giao.
- Nếu hủy/trả hàng hợp lệ sau đó, hoàn phần captured còn lại, tính cả khoản shipping-adjustment đã hoàn để không vượt tổng payment.
- Shipper chỉ xác nhận địa chỉ của vận đơn mình nhận. Sau khi địa chỉ đổi được duyệt, mọi cập nhật tracking bị chặn đến khi shipper bấm **Xác nhận địa chỉ mới**. Địa chỉ cũ/mới và thời điểm xác nhận được lưu; nhận vận đơn mới không thay cho việc xác nhận địa chỉ.
- Buyer và seller nhận thông báo trạng thái qua outbox hiện có. Tạo request, duyệt, refund giả lập, ledger và cập nhật địa chỉ nằm trong transaction SQL; request/decision/ack retry không đổi hoặc hoàn hai lần.

## Migration và chạy thử

Dừng API, rồi chạy tại thư mục gốc:

```powershell
dotnet ef database update --project backend/G4.Infrastructure --startup-project backend/G4.Api
```

Migration mới: `20261013091245_AddOrderAddressChanges`. SQL để duyệt: [order-address-changes-migration.sql](../database/order-address-changes-migration.sql). Không cần seed lại hoặc tạo database mới. Không chạy API mới trên schema cũ.

```powershell
dotnet build G4_Project.sln --no-restore -m:1 -p:UseSharedCompilation=false
dotnet run --project tests/G4.Commerce.Tests --no-build
node --test tests/address-changes-ui.test.cjs tests/buyer-cart-ui.test.cjs tests/promotions-ui.test.cjs tests/ui-navigation.test.cjs
```

Kiểm thử SQL chỉ nhận database QA riêng `G4_AddressChange_Test_20261013`, đã có baseline/migration/seed:

```powershell
$env:G4_ADDRESS_SQL_TEST_CONNECTION = 'Server=localhost;Database=G4_AddressChange_Test_20261013;Integrated Security=True;TrustServerCertificate=True'
dotnet run --project tests/G4.Commerce.Tests --no-build -- --address-sql
```

Đã kiểm tra trên QA ngày 13/10/2026: build không lỗi/cảnh báo; toàn bộ commerce tests và 56 test JavaScript qua. Bao gồm cap, quyền buyer/seller/shipper, chống duyệt/gửi/ack đồng thời, phí kg lẻ, giữ snapshot giá/payment/tồn kho/hồ sơ, hoàn ship và hủy đơn hoàn phần còn lại. Kiểm thử hồi quy xác nhận tiền vẫn mở khóa sau giao thành công và buyer vẫn mở được tranh chấp sau hoàn ship; tranh chấp không tự đóng vì khoản hoàn ship và có thể hoàn phần captured còn lại. Trình duyệt xác minh form buyer, seller duyệt, cấu hình cap, vận đơn địa chỉ mới và shipper xác nhận để tiếp tục lấy hàng.

Sau đó đã áp dụng `AddOrderAddressChanges` trên database local `CloneEbayDB` để sửa lỗi checkout 500 do thiếu cột snapshot địa chỉ. Migration này cần cho checkout trên phiên bản code hiện tại, kể cả khi chưa dùng chức năng đổi địa chỉ. Các máy/database khác vẫn cần chạy lệnh migration trước khi khởi động API.
