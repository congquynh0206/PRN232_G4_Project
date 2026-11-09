# KLARNA1 — Pay Later qua Hosted Payment Page (sandbox)

## Phạm vi và quyết định nghiệp vụ

Đặc tả nhóm: buyer chọn Klarna tại checkout; hệ thống gửi giỏ hàng, tổng USD và
địa chỉ tới Klarna; buyer được duyệt khoản trả góp trên trang Klarna; đơn được
ủy quyền; khi bắt đầu giao hàng hệ thống **capture toàn bộ**; hoàn tiền từ đơn đã
capture được gửi cho Klarna. Klarna, không phải ứng dụng G4, quản lý lịch thu
các kỳ, tín dụng và hoàn lại cho thẻ của buyer.

Trong tích hợp Klarna Payments/HPP dành cho hàng vật lý, `PLACE_ORDER` chỉ tạo
**ủy quyền**. `Authorized` cho phép seller chuẩn bị hàng nhưng **chưa phải tiền
đã thu**. Chỉ `Succeeded` sau capture toàn bộ mới ghi `PaidAt`, bút toán seller
và báo cáo tài chính. Việc chuyển tiền thật từ Klarna cho merchant diễn ra theo
chu kỳ settlement/hợp đồng, **không phải 100% tiền mặt ngay khi buyer bấm đặt**;
không có API trong ứng dụng để giả lập payout ngay hoặc lịch trả góp. Ví seller
vẫn tuân thủ chính sách hold/fee và payout hiện hành của G4. Cần nhóm xác nhận
lại câu “trả trước 100% ngay lập tức cho seller” trong SRS nếu muốn đối chiếu
settlement thực tế; không thể suy diễn số tiền trừ phí từ provider khi chưa có
hợp đồng/settlement feed.

## Luồng đã triển khai

1. Tạo đơn theo từng seller từ giỏ. Chỉ hỗ trợ sandbox US/USD với địa chỉ US,
   mã bang hai chữ và ZIP thực (5 số hoặc ZIP+4); thêm `postalCode` tùy chọn
   vào sổ địa chỉ, chụp snapshot địa chỉ vào đơn. Không tự tạo mã ZIP cho dữ
   liệu demo cũ. Buyer khác không được đọc hoặc khởi tạo thanh toán của nhau.
2. `POST /api/orders/{id}/pay/klarna` với `{ "key": "<UUID ổn định>" }` tạo
   Klarna Payments session và HPP session (`PLACE_ORDER`), lưu hai ID và URL,
   trả `redirectUrl`. Khóa lặp dùng lại phiên đã tạo. Merchant playground mới
   chỉ trả category hợp nhất `klarna` (không còn `pay_later`/`pay_over_time`);
   gateway nhận cả ba và gửi đúng category Klarna trả về sang HPP (kiểm tra
   playground NA ngày 09/11/2026). Lỗi API ghi kèm `error_code` và
   `correlation_id` để tra cứu với Klarna. Trang web chỉ chuyển hướng
   tới URL HTTPS do Klarna trả về. Với giỏ nhiều seller, các đơn tạo riêng và
   chỉ đơn đầu chuyển sang HPP; các đơn khác thanh toán riêng trong Đơn của tôi.
3. Buyer về `/Buyer/KlarnaReturn?orderId=...`; frontend gọi
   `POST /api/orders/{id}/pay/klarna/reconcile`. Server đọc HPP và đơn Klarna,
   so ID, amount, currency và merchant reference trước khi chuyển sang
   `Authorized`. Không tin `order_id` từ browser. Trạng thái HPP `FAILED`,
   `BACK`, `ERROR` chưa kết thúc và có thể thử tiếp cùng phiên; `CANCELLED` và
   `DISABLED` kết thúc lượt. Worker đọc lại các phiên Pending ngay cả khi buyer
   đóng tab. Trước khi hết hạn và giải phóng stock, worker tắt HPP rồi đối chiếu.
4. Seller tạo vận đơn outbound: capture toàn bộ một lần với idempotency key.
   Kết quả mạng chưa rõ giữ `Verifying`, không giao khi chưa xác minh; worker
   đọc trạng thái capture rồi mới ghi nhận tiền, consume coupon và ghi ledger.
   Hạn mức tháng reserve cho authorization/capture, không đếm hai lần.
5. Hủy đơn `Authorized` khi chưa capture: gọi cancel order, không gọi refund;
   hủy hoàn tất mới giải phóng stock/promotion. Với đơn đã capture, luồng
   return/refund hiện có gửi `POST /ordermanagement/v1/orders/{id}/refunds`,
   số tiền nguyên cent, idempotency key và ghi ledger refund như trước.

Không lưu thông tin thẻ, điểm tín dụng, kỳ trả góp của buyer. Không tự ghi
`Succeeded` chỉ vì thấy browser trở về; giao dịch Klarna không phải PayPal/cart
capture. Hoàn tiền và tranh chấp vẫn theo quy tắc G4, không suy đoán trạng thái
ngân hàng hay khoản nợ buyer với Klarna.

## Cấu hình máy dev

- .NET 8 SDK, SQL Server/SSMS, Node.js; chạy baseline/seed đúng hướng dẫn
  [PROJECT_RUN.md](PROJECT_RUN.md) **chỉ nếu database chưa tồn tại**.
- Đối với database hiện có, áp EF migration
  `20261108142843_AddKlarnaHostedPayment` bằng:

```powershell
cd 'D:\FU Learning\Fall2026\PRN232\Project'
$env:ASPNETCORE_ENVIRONMENT = 'Development'
dotnet ef database update --project backend/G4.Infrastructure --startup-project backend/G4.Api
```

- Xin **API credentials playground North America** từ Klarna Merchant Portal;
  không dùng API production hay bịa thông tin. Lưu cục bộ bằng User Secrets:

```powershell
dotnet user-secrets set 'Klarna:Username' '<playground-username>' --project backend/G4.Api
dotnet user-secrets set 'Klarna:Password' '<playground-password>' --project backend/G4.Api
dotnet user-secrets set 'Frontend:PublicBaseUrl' 'http://localhost:5131' --project backend/G4.Api
```

`Klarna:BaseUrl` mặc định khóa ở `https://api-na.playground.klarna.com` để
tránh gửi dữ liệu demo vào môi trường thật. Nếu chính sách Merchant Portal không
cho phép HPP callback về HTTP loopback, dùng HTTPS domain/dev tunnel thuộc
quyền nhóm và cập nhật `Frontend:PublicBaseUrl` tương ứng. Không commit secrets.

```powershell
dotnet restore G4_Project.sln
dotnet build G4_Project.sln
dotnet run --project backend/G4.Api --launch-profile http
# terminal thứ hai:
dotnet run --project frontend --launch-profile http
```

Vào `http://localhost:5131`, buyer đăng nhập, thêm địa chỉ US/ZIP **có thật**,
checkout chọn Klarna. Kiểm tra redirect, hoàn tất/từ chối trong playground và
quay về; mở Đơn của tôi hoặc Lịch sử giao dịch để xem Pending/Authorized.
Seller tạo outbound để capture; xem transaction `Succeeded`, `PaidAt` và seller
finance một lần; sau đó tạo return/refund và xem provider refund trong portal.
Với nhiều seller, lặp thanh toán riêng cho từng đơn.

## Kiểm thử tự động và giới hạn chứng cứ

```powershell
dotnet run --project tests/G4.Commerce.Tests -- --klarna
dotnet run --project tests/G4.Commerce.Tests
node --test frontend/tests/*.test.cjs
dotnet ef migrations has-pending-model-changes --project backend/G4.Infrastructure --startup-project backend/G4.Api
```

Test gateway HTTP giả lập và service InMemory chứng minh hợp đồng request,
trạng thái HPP, idempotency, capture/refund/cancel, lệch amount, stock/finance
và không credit khi mới authorize. Không chứng minh Klarna sẽ duyệt người mua,
trả tiền/settle ở thời điểm nào, hoàn tiền vào ngân hàng, hoặc giao dịch thật.
Cần QA end-to-end trên Merchant playground với credential do nhóm cung cấp;
không seed payment Klarna `Succeeded` giả vào database chính.

Tham khảo chính thức: Klarna HPP create/read session, HPP lifecycle, Order
Management capture, cancel và refund trên docs.klarna.com. Những phần trong
đặc tả mang tính ví dụ (Pay in 4, giữ kỳ đầu, hai tuần/tháng, phí) do Klarna
quyết định theo thị trường và hồ sơ buyer; không hardcode trong G4.
