# PayPal cả giỏ, trả hàng seller, SignalR và modal

**Căn cứ mới 18/10/2026:** [đặc tả mục tiêu](BUSINESS_REQUIREMENTS.md) và [backlog](IMPLEMENTATION_BACKLOG.md) ưu tiên hơn các ghi nhận phiên bản bên dưới. Giữ PayPal capture + reconcile và reservation hợp lệ, không thêm webhook như yêu cầu bắt buộc. Các workflow mới (địa chỉ đủ xác nhận, return/INR, free returns, hạn mức seller, payout chung) phải nối notification/realtime đúng quyền; cập nhật đồng bộ UI, trạng thái và test sau code.

## Thay đổi sử dụng

- Buyer chọn PayPal ở checkout: các order chưa trả của cùng CheckoutBatch được đưa vào **một lượt xác nhận**. Một purchase unit cho mỗi order, reference `order-{id}`; mỗi đơn lưu capture riêng. Refund tiếp tục dùng capture của chính đơn cần hoàn.
- Sau return từ PayPal, mục **Đơn của tôi** có bảng kết quả từng đơn. Đơn đã trả không bị thu lại khi tiếp tục các đơn còn thiếu. Khi đang xác minh, không mở lượt thanh toán khác cho cùng đơn. Chi tiết đơn chờ trả có **Xem thanh toán PayPal cả giỏ** để tìm lại lượt đang dở.
- Seller có tab **Trả hàng / Hoàn tiền**, mặc định **Chờ duyệt**, với badge và bộ lọc chờ duyệt/đang trả/chờ kiểm tra/chờ hoàn hoặc lỗi/tranh chấp/đã kết thúc/tất cả. Mỗi dòng có lý do, buyer, khoản hoàn dự kiến, phí trả và nút mở đơn/duyệt/từ chối. Danh sách chỉ trả dữ liệu của seller đăng nhập.
- **Giả lập lỗi đơn vị vận chuyển** thực hiện luôn một lần tạo vận đơn chiều đi với cả ba lần gọi carrier đều lỗi. Sau đó bấm **Tạo lại vận đơn**. Trong chi tiết yêu cầu Requested có **Duyệt và giả lập lỗi tạo vận đơn trả** để thử chiều về, rồi **Tạo lại vận đơn trả**. Nút giả lập chỉ chạy ở Development. Không giả lập lại khi đã có nhãn hợp lệ, không tự đổi trạng thái một vận đơn đang giao thành lỗi tạo nhãn. Thất bại tạo nhãn chưa thu phí; retry giữ một ShippingInfo/key và thu phí đúng một lần khi tạo nhãn thành công.
- Modal xác nhận 420px, đánh giá 440px, trả hàng 460px, phán quyết 620px, khuyến mãi 640px, chi tiết 860px tối đa trên desktop; mobile theo viewport. Các modal có giới hạn chiều cao/cuộn, fieldset/select/textarea được chuẩn hóa và focus rõ. Tải lại chi tiết nền không thay nội dung khi modal con đang mở.

## PayPal, retry và đối soát

`PayPalCheckout` liên kết CheckoutBatch và buyer. Tạo lượt + Payment cho từng order được commit trước gọi provider. Key provider được suy ra ổn định từ toàn bộ định danh lượt và thời điểm tạo, tránh chỉ dùng số ID có thể lặp giữa các DB. Tạo lại cùng yêu cầu trả approval cũ; cổng trả dữ liệu theo thứ tự khác vẫn ghép theo reference, không lấy capture đầu rồi áp cho mọi đơn.

Trước capture, khóa promotion/order theo thứ tự cố định và kiểm tra trạng thái, hạn, tài khoản, hạn mức. Lưu các Payment **Verifying** trước khi gọi PayPal. Từ thời điểm này việc hủy/expire/thanh toán khác phải chờ đối soát. Payment cart lưu thời điểm và SellerGross đã đặt trước trong hạn mức; lần capture khác tính cả khoản đang được đặt trước. Một khoản thực sự đã thu theo reservation hợp lệ vẫn được ghi nhận nếu cấp seller vừa giảm trong khi chờ phản hồi; hạn mức mới tiếp tục chặn các giao dịch mới.

Capture response cần đúng provider ID, reference, số tiền và tiền tệ. Capture IDs hoàn tất không được trùng giữa đơn. Không khớp thì giữ Verifying để đối soát, không cộng tiền dựa trên dữ liệu sai. Worker query các lượt Verifying; mất phản hồi capture không bị coi là chưa thu tiền. Succeeded units giữ nguyên, các units chưa rõ tiếp tục xác minh. Units bị từ chối cho kết quả PartiallyPaid/Failed; tạo lượt mới chỉ chứa đơn còn chờ trả. Huỷ approval chưa capture không huỷ order: có thể thử lại trong hạn thanh toán. Đơn expired/cancelled bị chặn trước gọi thu tiền.

Các endpoint cũ single-order capture/reconcile chuyển qua dịch vụ cart khi Payment có PayPalCheckoutId. Luồng PayPal một order cũ tiếp tục hoạt động. Số tiền/tập order luôn đọc từ CheckoutBatch thuộc buyer, không nhận giá hoặc danh sách tùy ý từ browser. Giỏ toàn đơn miễn phí không gọi PayPal.

API buyer:

- `POST /api/buyer/paypal-checkouts`: `{ checkoutKey, key }`.
- `GET /api/buyer/paypal-checkouts/{id}`: kết quả từng đơn.
- `POST .../{id}/capture`: `{ providerOrderId }`.
- `POST .../{id}/reconcile`: kiểm tra kết quả provider.
- `POST .../{id}/cancel`: hủy lượt chưa capture, giữ đơn để trả lại sau.

Thiết kế purchase units/reference dựa trên [PayPal Orders v2](https://developer.paypal.com/sdk/orders/v2/definitions/order_request). API hỗ trợ 1–10 units; giỏ hiện tại của dự án chia hai seller. Các seller vẫn nhận doanh thu qua sổ tài chính nội bộ của dự án.

## SignalR và bảo vệ dữ liệu

Hub API: `/hubs/commerce`, yêu cầu JWT và role buyer/seller/admin/shipper. Groups do server gán từ claim; không có JoinGroup/Send method cho client tự chọn tài khoản. Browser lấy phiên qua `/Realtime/Session` có cookie đăng nhập và NoStore; token chỉ giữ trong bộ nhớ, không ghi local/sessionStorage. Chỉ endpoint hub nhận access_token query cho WebSocket/SSE; không mở cơ chế này cho REST khác.

Khi SaveChanges có thay đổi order/payment/refund/shipping/return/dispute/finance/level/notification/address request, context ghi CommerceChange cùng transaction. Worker riêng chỉ đọc sự kiện đã commit, gửi tới chủ đơn/seller/shipper được gán. Admin nhận các phạm vi admin được xem; shipper nhận invalidation danh sách công việc chung. Payload chỉ chứa version/topics, không chứa mã đơn riêng, tiền, bằng chứng hay định danh. API GET vẫn kiểm tra quyền trước khi trả dữ liệu. Claim shipment bằng ExecuteUpdate cũng ghi event cùng transaction.

Dispatch có thể lặp nếu tiến trình dừng sau gửi nhưng trước đánh dấu; browser chỉ tải lại dữ liệu nên không tạo hiệu ứng tài chính. Cập nhật được gộp và hoãn khi nhập liệu, mở modal con, thao tác phân trang hoặc đang tải hồ sơ. Khi offline chủ động dừng kết nối; online/reconnect tải bù dữ liệu. Mất kết nối có fallback làm mới chậm. Đếm ngược deadline vẫn tính trên client và nghiệp vụ deadline vẫn do worker xử lý, không chuyển cho hub.

Thư viện `@microsoft/signalr` **8.0.17** được đóng gói tại `frontend/wwwroot/lib/signalr/`, có MIT LICENSE, không tải CDN khi mở app. Cách auth/reconnect theo [Microsoft SignalR authentication](https://learn.microsoft.com/en-us/aspnet/core/signalr/authn-and-authz?view=aspnetcore-8.0) và [JavaScript client](https://learn.microsoft.com/en-us/aspnet/core/signalr/javascript-client?view=aspnetcore-8.0).

Cấu hình local mặc định: frontend `http://localhost:5131`, API `http://localhost:5251`. API `Realtime:AllowedOrigins` phải chứa đúng origin frontend; không dùng wildcard. Nếu đổi host/port hoặc chạy HTTPS, đặt frontend `Realtime:PublicApiBaseUrl` thành API public có thể truy cập từ browser, và `Frontend:PublicBaseUrl` trên API thành frontend public để PayPal return đúng nơi. Ví dụ HTTPS: API `https://localhost:7087`, frontend `https://localhost:7018`. Không ghi token query vào access log của reverse proxy.

Docker Compose đã đặt public frontend 5000, public API 8080, API nội bộ `backend:8080` và Carrier trỏ cùng API. Browser hub dùng public API, không dùng hostname nội bộ container. Chạy nhiều API instance cần SignalR backplane/managed service và thiết kế dispatcher tương ứng; cấu hình hiện tại phục vụ một API instance như dự án local.

## Migration và khởi động

Migration mới: **20261015161703_AddPayPalCartAndRealtime**. Chỉ thêm PayPalCheckout, CommerceChange, các cột nullable vào Payment và index/FK; giữ order/payment/capture/refund/ledger/số dư cũ. SQL để duyệt: [paypal-cart-realtime-migration.sql](../database/paypal-cart-realtime-migration.sql).

Dừng API/frontend cũ, chạy từ gốc repo:

```powershell
dotnet ef database update --project backend/G4.Infrastructure --startup-project backend/G4.Api
dotnet run --project backend/G4.Api
# Cửa sổ khác
dotnet run --project frontend
```

Giữ connection string và credentials Sandbox đang dùng; sau build/migration cần khởi động lại cả hai app và tải lại trang. Không chạy lại baseline trên DB có dữ liệu. Migration mới trong đợt này chỉ được áp dụng trên **G4_CommerceRefresh_Test_20261015**, không tự nâng database chính.

## Kiểm thử

```powershell
dotnet build G4_Project.sln -c Release --no-restore -m:1 -p:UseSharedCompilation=false -p:UseAppHost=false
dotnet tests/G4.Commerce.Tests/bin/Release/net8.0/G4.Commerce.Tests.dll
dotnet tests/G4.Commerce.Tests/bin/Release/net8.0/G4.Commerce.Tests.dll --paypal-cart
node --test tests/*.test.cjs
```

SQL test chỉ chấp nhận database QA sau, bootstrap schema/migration/seed tại đó và không xóa dữ liệu có sẵn:

```powershell
$env:G4_REFRESH_SQL_TEST_CONNECTION='Server=localhost;Database=G4_CommerceRefresh_Test_20261015;Integrated Security=True;TrustServerCertificate=True'
dotnet tests/G4.Commerce.Tests/bin/Release/net8.0/G4.Commerce.Tests.dll --commerce-refresh-sql
```

Test SQL kiểm tra giữ số dư/trạng thái cũ, hai start/capture đồng thời, một settlement mỗi order, capture độc lập và rollback order kéo theo rollback sự kiện realtime. Seed thêm buyer QA `isolated-refresh@example.test` để kiểm tra không nhận sự kiện của người khác.

E2E chạy API QA 5255, frontend QA 5135, đúng DB QA trên. Đặt API `Frontend__PublicBaseUrl=http://127.0.0.1:5135`, `Realtime__AllowedOrigins__0=http://127.0.0.1:5135`, `Carrier__BaseUrl=http://127.0.0.1:5255`, `Mail__Mode=Capture`, `Finance__WriteOff__Enabled=false`, `Finance__LevelPromotion__EvaluationSeconds=3600`. Frontend `ApiSettings__BaseUrl=http://127.0.0.1:5255`.

Chỉ cho QA, API `PayPal__BaseUrl=http://127.0.0.1:5265`, ClientId/ClientSecret là chuỗi giả `qa-local-stub`. Script tự mở HTTP stub provider ở 5265, không gọi PayPal bên ngoài. Cần Playwright + Chromium/Edge:

```powershell
$env:G4_REFRESH_E2E_URL='http://127.0.0.1:5135'
node tests/commerce-refresh.e2e.cjs
```

E2E: xác nhận PayPal một lần cho hai đơn; provider thu xong trả 503 rồi worker đối soát; modal đánh giá mobile và giữ dữ liệu nhập khi có push; lỗi/retry nhãn cả hai chiều không trùng phí; seller inbox tự nhận yêu cầu trả; refund đúng capture/amount; offline/online tải bù; hub từ chối anonymous/tự join admin; buyer khác không nhận sự kiện riêng. Ảnh tại `artifacts/commerce-refresh-qa/`.

Test HTTP stub xác minh hợp đồng HTTP và luồng nội bộ; **không thay thế nghiệm thu trực tiếp với tài khoản PayPal Sandbox bên ngoài**. Với credentials Sandbox của bạn, tạo giỏ hai seller, xác nhận một lần, kiểm tra hai order Paid rồi refund riêng một order; order kia và capture của nó phải giữ nguyên.
