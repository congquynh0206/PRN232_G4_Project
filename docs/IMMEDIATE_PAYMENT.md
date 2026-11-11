# Buy It Now + Immediate Payment

Cập nhật ngày 08/11/2026 theo yêu cầu authorize / Capture / Void và chỉ INSERT
Order PAID. Bản này thay thế cách tạo AwaitingPayment bên trong transaction của
phiên đầu. Provider đang dùng là **bộ giả lập thẻ hiện có, mở rộng vòng đời**;
không dùng tiền thật, không bổ sung PayPal Immediate hoặc Klarna.

## Luồng hiện tại

1. Seller chọn Normal hoặc Immediate theo từng sản phẩm, khi đăng hoặc sửa.
   Sản phẩm cũ mặc định Normal; lịch sử order/payment giữ nguyên.
2. Buyer Buy It Now, chọn địa chỉ/số lượng/coupon và xem checkout. Chỉ lưu
   ImmediateCheckout và snapshot báo giá. Không giữ/trừ kho, không tạo Order.
   Sản phẩm vẫn xuất hiện trong catalog Buyer. Session hết hạn sau 30 phút;
   đây không phải thời gian giữ hàng.
3. Pay Now kiểm tra lại owner, giá, địa chỉ, cấu hình, promotion và stock. Tạo
   attempt có key duy nhất, lưu ý định authorize trước khi gọi gateway.
4. Authorize bên ngoài transaction order. Sau authorize, transaction ngắn kiểm
   tra lại báo giá và claim tồn bằng conditional UPDATE Quantity >= số lượng.
5. Người giành được tồn mới được gọi Capture. Người không giành được tồn chuyển
   sang Voiding, gọi Void và báo OutOfStock sau khi Void được xác nhận. Không
   tạo Order cho người thua; nếu Void lỗi, UI chờ đối soát, worker thử lại.
6. Chỉ sau khi gateway xác nhận Captured mới gọi CreateCapturedImmediateOrderAsync.
   Lần INSERT Order đầu tiên có Status = Paid, PaymentExpiresAt = null; không
   INSERT AwaitingPayment ở bước trung gian. Order, Payment Succeeded, promotion
   consumption, settlement và session Succeeded commit cùng transaction.
7. Nếu đã capture ngoài ý muốn mà không có claim, hoặc báo giá/cấu hình thay đổi
   trước ghi đơn, hệ thống Refund. Lỗi DB ghi đơn giữ Captured/Verifying để khôi
   phục; quá ba lần ghi thất bại đã xác định thì Refund và trả tồn sau xác nhận.
8. Unknown/timeout luôn Get trạng thái gateway trước khi retry cùng provider key.
   Attempt đang xử lý chặn tạo attempt mới dù client gửi key khác. Session đã
   thành công luôn trả đúng order cũ. Không coi Pending/Verifying là Failed.
9. Worker mỗi chu kỳ xử lý authorize/capture chưa rõ kết quả, Void/Refund lỗi và
   lease xử lý hết hạn sau khi process chết. Không cần lưu hoặc xin lại số thẻ.

**Không giữ transaction SQL mở khi gọi gateway.** Claim tồn chỉ xuất hiện khi
Pay Now, không phải khi mở checkout. Claim chưa rõ kết quả Capture được giữ đến
khi đối soát chứng minh thành công hoặc Void/Refund đã hoàn tất. Không tự nhả
stock theo timer trong khi có thể đã Capture.

Provider giả lập có bảng SimulatedCardAuthorization riêng và DbContext/transaction
riêng, nên capture vẫn tồn tại nếu transaction order rollback. Gateway dùng lại
CardPaymentSimulator để xác thực thẻ thử, hỗ trợ authorize/capture/query/void và
refund toàn bộ hoặc từng phần theo key. Không hardcode tất cả giao dịch thành công.
Refund sau mua tiếp tục dùng RefundGateway/ReturnService cũ, có amount và key
idempotency; refund từng phần không tự biến thành hoàn toàn bộ.

## State và bảo vệ đồng thời

- Attempt: Authorizing → Authorized → Capturing → Captured → Completed.
- Nhánh bù: Voiding → Voided hoặc Refunding → Refunded. Declined là thất bại
  authorize. Status UI dùng Pending/Verifying/Succeeded/Failed.
- SQL Server application lock dùng chung với checkout thường/sửa sản phẩm;
  conditional inventory UPDATE và kiểm tra affected rows trong transaction ngắn.
- Lease xử lý có token; worker cũ không được ghi tiếp sau khi token bị thay thế.
- Stock claim/release và cờ InventoryHeld cập nhật cùng transaction, tránh trừ
  hoặc trả kho hai lần. Provider có key và transaction độc lập cho mỗi authorization.
- Checkout key trùng luồng khác không được biến một order UNPAID thành kết quả
  Immediate thành công; captured payment phải đúng provider reference.
- Không có tranh chấp giữa hai khoản capture được chấp nhận: quyền Capture chỉ
  được cấp cho request đã giành được tồn. Người thua có thể authorize nhưng phải Void.

## Database và migration

Giữ migration đầu `20261108144118_AddImmediateCheckout`: Product có
ImmediatePaymentRequired (default false), IsListed (default true), session và attempt.

Bổ sung tăng dần:

- `20261108150534_AddImmediateAuthorizationSaga`: Stage, amount, authorization/capture
  ID, InventoryHeld, lease/token, số lần ghi đơn và UpdatedAt; bảng ngân hàng giả lập
  SimulatedCardAuthorization. Metadata terminal cũ được backfill, không sửa tiền/order cũ.
- `20261108151440_AddSimulatedCardRefundHistory`: số tiền hoàn tích lũy và lịch sử
  refund theo key trong provider giả lập, bảo vệ partial refund/replay.

Không lưu PAN/CVV. Không reset database hoặc chạy lại baseline trên dữ liệu có sẵn.
Migration chưa áp lên database chính/QA trong môi trường này: socket SQL bị chặn.
SQL để duyệt: database/immediate-checkout-migration.sql (migration đầu) và
 database/immediate-authorization-saga-migration.sql (hai migration bổ sung).

## API và màn hình

| Method | Route dưới /api | Tác dụng |
| --- | --- | --- |
| POST | seller/products | Đăng sản phẩm, chọn ImmediatePaymentRequired |
| GET | seller/products | Seller xem sản phẩm thuộc mình, mới nhất trước |
| POST | seller/products/{id}/payment-settings | Owner sửa mode, giá và IsListed |
| GET | catalog/products | Buyer xem sản phẩm/badge/tồn, 100 sản phẩm mới nhất |
| GET | catalog/products/{id} | Buyer xem một sản phẩm đang bán, dùng cho trang checkout |
| POST | buyer/immediate-checkouts | Lấy checkout session không giữ kho |
| GET | buyer/immediate-checkouts/{guid} | Owner xem trạng thái/snapshot/stage |
| POST | buyer/immediate-checkouts/{guid}/pay/card | Pay Now, Number/Expiry/Key |
| POST | buyer/immediate-checkouts/{guid}/reconcile | Owner kiểm tra và khôi phục attempt |

**Cập nhật 11/11/2026:** `/Buyer/Products` là trang Sản phẩm chung (menu buyer), mỗi
sản phẩm có Buy It Now / Thêm vào giỏ / Trả giá; `catalog/products` trả thêm seller, ảnh
và `allowOffers`. Sản phẩm Immediate có nhãn "Yêu cầu thanh toán ngay", không thêm được
vào giỏ, checkout chỉ hiện credit card và nút đổi tên "Xác nhận và thanh toán". Sản phẩm
thường cũng dùng trang checkout này nhưng tạo đơn rồi thu tiền ngay bằng credit card,
PayPal hoặc Klarna. PayPal cho Immediate chưa làm (đợt riêng).

**Cập nhật 11/11/2026 (2):** bỏ trang riêng `/Buyer/Checkout` (link cũ chuyển hướng). Buy It
Now mở tab Thanh toán `/Buyer?tab=checkout&product={id}#checkout` (`buyer-buy-now.js`, tab
do `buyer-cart.js` quản lý): báo giá tự tính theo địa chỉ/số lượng/mã qua `POST quote`;
sản phẩm Immediate chỉ tạo session khi bấm "Xác nhận và thanh toán" (tổng session khác báo
giá đang xem thì dừng để buyer xác nhận lại), cùng session được thử lại với key mới sau
thẻ lỗi. Đổi số lượng/mã/địa chỉ thì tạo session mới.

Seller: /Seller/Products. Buyer: /Buyer/Products chỉ là danh sách; Buy It Now
mở tab Thanh toán như trên. Khi đã tạo session, URL thêm &checkout={session}. UI giữ session qua URL,
khóa double submit, xóa số thẻ sau gửi và không cho thanh toán lại khi Pending/
Verifying. Chỉ báo Void/Refund đã xong sau kết quả server. Order History và Seller
Order Management dùng dữ liệu Paid hiện có. Normal checkout vẫn tạo unpaid order
như cũ; sản phẩm Immediate bị chặn đi qua API tạo đơn thường.

## Chạy và demo

```powershell
dotnet ef database update --project backend/G4.Infrastructure --startup-project backend/G4.Api
dotnet run --project backend/G4.Api --urls http://localhost:5251
dotnet run --project frontend --urls http://localhost:5131
```

- Seller cần địa chỉ lấy hàng hợp lệ; Buyer cần địa chỉ giao đã lưu.
- Đăng sản phẩm Immediate stock 1. Hai Buyer cùng mở checkout: tồn vẫn là 1,
  không có Order mới. Pay Now: chỉ người thắng được Capture/Order Paid; người
  thua authorize phải Voided, không có capture ID hoặc order.
- Thẻ thử 4111111111111111 thành công, 4000000000000002 bị từ chối; hạn 12/30
  (hoặc MM/YY tương lai nếu ngày demo đã qua hạn đó). Không dùng thẻ thật.
- Trong DB kiểm tra ImmediatePaymentAttempt.Stage/AuthorizationId/CaptureId,
  SimulatedCardAuthorization.Status/RefundedAmount, OrderTable.Status và inventory.

## Kiểm thử

```powershell
dotnet build G4_Project.sln
dotnet run --project tests/G4.Commerce.Tests -- --immediate-checkout
node --test tests/*.test.cjs
$env:G4_IMMEDIATE_SQL_CONNECTION = 'Server=YOUR_SERVER;Database=G4_ImmediateCheckout_Test_20261108;Integrated Security=True;TrustServerCertificate=True'
dotnet run --project tests/G4.Commerce.Tests -- --immediate-checkout-sql
```

Runner SQL chỉ nhận đúng tên QA trên, không drop/reset dữ liệu. Chỉ bootstrap
baseline khi DB hoàn toàn trống, sau đó migrate tăng dần. Barrier buộc cả hai hoặc
20 Buyer authorize trước khi tranh stock, rồi kiểm tra đúng 1/5 Captured và các
loser Voided, không có capture ID. Có case duplicate submit và lỗi save cuối
transaction sau khi ghi Order/Payment để kiểm tra rollback, giữ capture độc lập
và reconcile không thu hai lần. Không dùng InMemory để kết luận SQL concurrency.

Bộ functional chạy cùng runner hiện có, gồm kiểm tra Seller/quyền, no-hold,
normal regression, insert PAID, timeout authorize/capture, loser Void, capture
failure, Void/Refund retry, quote thay đổi sau Capture, DB failure recovery/refund,
partial refund/replay, lease hết hạn và không có transaction tại gateway calls.
Kết quả lượt cuối được ghi ở backlog; kết quả các phiên cũ không thay bằng chứng
SQL/E2E. Chưa có test project xUnit độc lập.

## Giới hạn xác minh

SQL Server/HTTP/browser E2E chưa chạy thành công vì môi trường chặn socket
10013. Chưa áp migration chính/QA. Provider là giả lập thẻ có state bền vững,
không phải thử với ngân hàng hoặc PayPal thật. PayPal cũ giữ nguyên; không tìm thấy
module Klarna để kiểm thử. Không báo hoàn tất nghiệm thu SQL chỉ từ build/Node/
InMemory. Webhook ngoài không được bật; query/reconciliation là luồng chính.

## File chính của lần sửa authorize / Capture / Void

- backend/G4.Application/Integrations/IImmediateCardGateway.cs
- backend/G4.Domain/Entities/ImmediateCheckout.cs
- backend/G4.Infrastructure/Payments/SimulatedImmediateCardGateway.cs
- backend/G4.Infrastructure/Checkout/ImmediateCheckoutService.cs
- backend/G4.Infrastructure/Checkout/ImmediateCheckoutService.Payment.cs
- backend/G4.Infrastructure/Checkout/CheckoutService.cs
- backend/G4.Infrastructure/Integrations/Refunds/RefundGateway.cs
- backend/G4.Infrastructure/Notifications/CommerceMaintenanceWorker.cs
- backend/G4.Infrastructure/DependencyInjection.cs
- backend/G4.Infrastructure/Persistence/ApplicationDbContext.ImmediateCheckout.cs
- backend/G4.Infrastructure/Persistence/Migrations/20261108150534_AddImmediateAuthorizationSaga.cs
- backend/G4.Infrastructure/Persistence/Migrations/20261108151440_AddSimulatedCardRefundHistory.cs
- Designer của hai migration và ApplicationDbContextModelSnapshot.cs
- backend/G4.Api/Controllers/ImmediateCheckoutController.cs
- frontend/wwwroot/js/buyer-buy-now.js (thay immediate-checkout.js từ 11/11/2026)
- tests/G4.Commerce.Tests/ImmediateCheckoutChecks.cs
- tests/G4.Commerce.Tests/ImmediateAuthorizationChecks.cs
- tests/G4.Commerce.Tests/ImmediateCheckoutSqlChecks.cs
- tests/buyer-buy-now-ui.test.cjs (thay immediate-checkout-ui.test.cjs)
- database/immediate-authorization-saga-migration.sql
- docs/BUSINESS_REQUIREMENTS.md, docs/IMPLEMENTATION_BACKLOG.md,
  docs/PROJECT_RUN.md, docs/BUYER_CHECKOUT_FLOW.md, docs/IMMEDIATE_PAYMENT.md

## Kết quả xác minh cuối phiên sửa saga

| Hạng mục | Kết quả thực tế |
| --- | --- |
| dotnet build G4_Project.sln --no-restore -m:1 -p:NuGetAudit=false | PASS, 0 warning, 0 error |
| --immediate-checkout | PASS functional/API và bộ saga, gồm partial refund, lease, key collision |
| node --test tests/*.test.cjs | PASS 154/154, gồm 10 test UI Immediate |
| Backend tổng | Đã chạy; dừng tại RateLimitChecks với socket 10013, không PASS toàn bộ |
| --immediate-checkout-sql | Đã gọi runner; SQL socket 10013 chặn trước case, chưa xác minh concurrency/SQL rollback |
| Migration SQL idempotent | Đã xuất hai migration mới trong immediate-authorization-saga-migration.sql |
| Database chính/QA | Chưa áp migration, không reset hoặc xóa dữ liệu |
| Browser E2E | Chưa chạy mới |

Không có lệnh commit, merge hoặc push trong phiên sửa này. Các thay đổi đang có
trước phiên được giữ nguyên.
