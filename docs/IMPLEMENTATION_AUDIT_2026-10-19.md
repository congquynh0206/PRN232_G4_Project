# Đối chiếu source với đặc tả ngày 19/10/2026

Kiểm tra trên source tại commit `b4a114f` (`Sửa lỗi địa chỉ sau hợp nhất`), working tree ban đầu sạch. Căn cứ là BUSINESS_REQUIREMENTS phiên bản 2.0 ngày 18/10, bốn task ưu tiên ngày 17/10 và hướng dẫn module. Không lấy nhãn trạng thái trong backlog làm bằng chứng duy nhất.

**Kết quả:** 7 hạng mục chưa đủ luồng yêu cầu, 5 hạng mục đã có nhưng cần điều chỉnh; phát hiện thêm 3 lỗi kiểm tra phía server. B1, A1, S2 và N1 có triển khai và bộ kiểm thử tương ứng chạy đạt ở lần rà soát này. Thiếu SQL/E2E hoặc chưa biết migration của database chính không đồng nghĩa chưa có chức năng.

**Cập nhật sau audit ngày 19/10:** G1/G2/G3 đã được sửa tại server. G1/G2 có `CheckoutLimitChecks`; G3 có `PayPalReservationChecks` và SQL QA thật trên `G4_PayPalReservation_Test_20261019`. Build/backend và UI 115/115 đạt. 12 hạng mục thiếu/lệch bên dưới vẫn giữ trạng thái riêng. Các mô tả/probe trong báo cáo này giữ bằng chứng source trước sửa; xem [backlog mục 13–14](IMPLEMENTATION_BACKLOG.md#13-g1g2--giới-hạn-seller-và-số-lượng-tại-server), [giỏ hàng](MULTI_SELLER_CART.md) và [PayPal](COMMERCE_REFRESH.md) cho trạng thái sau sửa. Không có migration mới hoặc thay database chính; G3 chỉ bootstrap/migrate QA riêng. Chưa PayPal Sandbox ngoài hệ thống hoặc browser E2E mới.

## 1. Phạm vi và bằng chứng kiểm tra

**Cập nhật tiếp AD1 ngày 20/10/2026:** AD1 đã sửa/nghiệm thu source, còn **10 hạng mục thiếu/lệch** của audit gốc (7 thiếu luồng, 3 cần điều chỉnh: RT1/PAY1/LED1). Buyer quote confirmation, seller/shipper duyệt trước áp dụng, chưa có shipper không chờ, deadline, stale cap/shipper/quote và transaction tiền đã triển khai. Build/backend, UI 120/120, SQL QA concurrent/rollback và Chrome fixture desktop/mobile đạt; migration chỉ áp `G4_AddressConfirmation_Test_20261020`, chưa áp database chính. Xem [backlog mục 16](IMPLEMENTATION_BACKLOG.md#16-ad1--xác-nhận-địa-chỉ-trước-áp-dụng-ngày-20102026), [AD1](ORDER_ADDRESS_CHANGES.md). Các ghi nhận audit/SH1 phía dưới giữ mốc lịch sử trước AD1.

**Cập nhật SH1 ngày 20/10/2026:** SH1 đã sửa và nghiệm thu source; còn **11 hạng mục thiếu/lệch** từ danh sách 12 hạng mục audit ban đầu (7 thiếu luồng, 4 cần điều chỉnh). Bắt buộc ReattemptScheduled, lịch/lý do UTC, actor server, hai chiều và giới hạn lượt đã có; build/backend, UI 117/117, SQL QA cạnh tranh/rollback và Chrome browser fixture desktop/mobile đạt. Migration chỉ áp `G4_ShippingReattempt_Test_20261020`, chưa áp database chính. Nội dung audit/probe phía dưới là bằng chứng lịch sử trước sửa; xem [backlog mục 15](IMPLEMENTATION_BACKLOG.md#15-sh1--giao-lại-có-lịch-và-actor-ngày-20102026) và [hướng dẫn SH1](RETURNS_AND_SHIPPING.md#sh1--lên-lịch-giao-lại-và-actor-20102026) cho trạng thái hiện tại.

- Đọc controller, contract, entity/EF mapping, service, worker, Razor/JavaScript và test liên quan; đối chiếu xuyên các lớp cho từng nhóm chức năng.
- Build solution với output riêng trong `artifacts/implementation-audit-20261019`: đạt, 0 warning và 0 error.
- Chạy toàn bộ console backend trên binary vừa build: đạt, gồm B1, A1, S2, N1, checkout, promotions, payment, finance, return/dispute, shipper, level, debt/write-off và SMTP loopback.
- Chạy `node --test tests/*.test.cjs`: **115/115 đạt**, không skip. Đây là test UI bằng harness; không phải một lượt browser E2E/SQL mới.
- Viết và chạy 10 probe tái hiện hành vi lệch yêu cầu, dùng EF InMemory và gateway giả, output tại [probes-results.txt](../artifacts/implementation-audit-20261019/probes-results.txt). Mã probe tại [Program.cs](../artifacts/implementation-audit-20261019/probes/Program.cs).
- Không chạy seed, migration, test SQL ghi dữ liệu, hoặc gọi PayPal Sandbox bên ngoài. Không đọc trạng thái schema/database chính và không thay dữ liệu database chính. Không sửa source nghiệp vụ trong lần rà soát.

Bộ backend lần đầu bị sandbox chặn socket SMTP localhost (10013); chạy lại cùng binary ngoài sandbox đã đạt toàn bộ. Đây là hạn chế môi trường ở lần chạy đầu, không được ghi thành lỗi nghiệp vụ/email của dự án.

Lệnh chính đã dùng:

```powershell
dotnet build G4_Project.sln --artifacts-path artifacts/implementation-audit-20261019 -m:1 --disable-build-servers -p:NuGetAudit=false
dotnet artifacts/implementation-audit-20261019/bin/G4.Commerce.Tests/debug/G4.Commerce.Tests.dll
node --test tests/*.test.cjs
dotnet build artifacts/implementation-audit-20261019/probes/AuditProbes.csproj --artifacts-path artifacts/implementation-audit-20261019 -m:1 --disable-build-servers -p:NuGetAudit=false -p:UseAppHost=false
dotnet artifacts/implementation-audit-20261019/bin/AuditProbes/debug/AuditProbes.dll
```

Probe được lưu trong artifacts bị Git ignore, phục vụ tái hiện tại workspace này; không nằm trong bộ regression đã commit. InMemory xác nhận logic service/DbContext, không chứng minh locking, rollback hoặc concurrency trên SQL Server.

## 2. Bảy hạng mục chưa đủ luồng yêu cầu

| Mã | Đã có thực tế | Phần thực sự còn thiếu và bằng chứng |
| --- | --- | --- |
| B2 | `OrdersController.Get` trả danh sách payment/refund trong từng đơn; admin cũng có lịch sử ở chi tiết A1. | Không có tab lịch sử giao dịch buyer, API lịch sử xuyên đơn và tổng theo kỳ/toàn vòng đời độc lập phân trang theo yêu cầu. `frontend/Views/Buyer/Index.cshtml` chưa có tab này; API PaymentsController chỉ có thao tác payment/refund. Không thể tính B2 đã xong chỉ vì đơn trả mảng payment/refund. |
| S1 | `FinanceController` có summary ví, ledger và payout phân trang. | Chưa có báo cáo phát sinh theo kỳ/ngày/tuần/tháng, bộ tổng thu/refund/phí/subsidy/net và biểu đồ/bảng đối chiếu. `frontend/Views/Seller/Index.cshtml` chỉ có tab tài chính hiện hữu; các API finance hiện nhận phân trang, chưa nhận bộ lọc báo cáo và nhóm kỳ. |
| A2 | Admin đã có SellerWins/FullRefund/PartialRefund, preview tài chính và giữ tiền; seller/buyer có thỏa thuận ReturnRefund. | Chưa có hành động admin yêu cầu trả trước phán quyết, trạng thái chờ hàng về và luồng tiếp tục kết luận. `DisputeDecisionPricing.cs:13` chỉ chấp nhận ba kết quả cuối; `_DisputeDecisionDialog.cshtml:9` cũng chỉ có ba lựa chọn này. ReturnRefund của seller không thay thế quyền A2. |
| RT2 | Seller từ chối return và buyer có thể mở dispute thường nếu còn đủ điều kiện Delivered + 7 ngày. | Chưa có deadline phản đối riêng từ thời điểm từ chối, nhánh chấp nhận/tự đóng, liên kết phản đối và xử lý race theo deadline. `ReturnService.cs:188` chỉ ghi Rejected/DecisionReason/DecidedAt; `DisputeService.cs:66` vẫn chặn theo thời điểm Delivered. Probe: return mở từ trước, bị từ chối hôm nay nhưng Delivered đã 8 ngày; buyer phản đối ngay bị chặn và worker đóng order ngay. |
| INR1 | Có dispute thường và entity `ShippingInfo.EstimatedArrival`. | Chưa có luồng chưa nhận hàng trước Delivered, deadline riêng và ngoại lệ không yêu cầu link bằng chứng. `DisputeService.OpenAsync` gọi ValidateEvidence rồi bắt Delivered. `EstimatedArrival` có cột nhưng tìm trong source ứng dụng chỉ thấy khai báo/mapping và logic đọc để xét level, chưa thấy luồng tạo nhãn gán hoặc giao diện hiển thị ngày dự kiến. Cần hoàn thiện dữ liệu đầu vào này, không coi tên cột là chức năng đã chạy. |
| FR1 | Có snapshot khối lượng item, giá return theo lý do, funding và phí nhãn. | Chưa có policy free returns Product, snapshot quyền lợi OrderItem, API/UI seller và công bố trước mua. `ReturnPricing.cs:29` luôn bắt buyer chịu toàn bộ nhãn khi đổi ý; không có phân phí theo khối lượng free/non-free. Không nhầm với Shipping/FreeShipping promotion đã có. |
| LIM1 | Có hạn mức theo level, tính doanh số tháng UTC, kiểm tra hạn mức và reservation PayPal cả giỏ. | Chưa có admin override riêng seller, lịch hiệu lực/kết thúc và audit. `SellerFinanceService.cs:883` gán hạn mức theo level/cấu hình; không có controller/contract/entity cho policy override. Có thêm lỗi reservation một đơn tại mục 4. |

## 3. Năm hạng mục có luồng nhưng lệch đặc tả

### AD1 — đổi địa chỉ

- `AddressChangeService.cs:175` sửa snapshot order, tăng `AddressChangeCount` ở dòng 178 và cập nhật vận đơn ngay khi seller duyệt; shipper chưa xác nhận. Probe có shipper được gán xác nhận được điều này.
- `ShipmentService.cs:138` cho OutForDelivery tiếp tục rồi tự từ chối các request Pending. Đặc tả yêu cầu chặn bắt đầu giao trong khi chờ đủ xác nhận; probe tái hiện được việc giao tiếp tục.
- Chưa có deadline chờ, trạng thái shipper từ chối, hoặc bước buyer xem/xác nhận quote trước gửi. Contract AddressChangeInput chỉ có địa chỉ, lý do và key.
- Nếu chưa có shipper khi áp dụng, source vẫn gắn AddressChangeRequestId lên nhãn tạo sau đó (`ShipmentService.cs:43`) và chặn tracking cho đến khi shipper mới acknowledge (`:130`). Cần theo quy tắc seller duyệt cuối nếu chưa gán shipper tại thời điểm áp dụng; acknowledge đọc thông tin không thay thế workflow chấp thuận khả năng giao.
- Phần đã có cần giữ: cap seller, vượt cap từ chối, một lần thành công, refund ship giả lập và idempotency/ownership.

### SH1 — giao lại và actor

- `backend/G4.Domain/Rules/ShippingState.cs:12` cho `DeliveryFailed → OutForDelivery`; không hỗ trợ ReattemptScheduled. Probe xác nhận cả hai kết quả. `frontend/wwwroot/js/shipper-ui.js` cũng dùng đường tắt này.
- ShippingInfo chưa có thời gian/lý do lịch giao lại; ShippingEvent chưa lưu actor, dù API/service đã kiểm tra shipper được gán.
- Đã có hai chiều dùng Direction, tối đa hai lượt, chống event trùng và mốc chuyển hoàn thực tế. Không cần xây lại toàn bộ tracking.

### RT1 — return window và snapshot deadline

- Hardcode 7 ngày tại `ReturnQuoteBuilder.cs:19`, `ReturnService.cs:86`, `CheckoutService.cs:307` và `DisputeService.cs:66`.
- Development chỉ cấu hình `Returns:ReceiptSeconds = 45`, chưa có policy mở return 300 giây dùng chung. Probe: quote vẫn chấp nhận sau 6 phút.
- `ReturnService.cs:51`/`:95` ghi ReturnDeadline = thời điểm tạo request + 7 ngày; giá trị này không phải deadline mở return được chốt từ outbound Delivered và không được gate quote/close sử dụng.
- Receipt chiều trả 45 giây và snapshot ConfirmationDueAt đã có; không đổi receipt thành 300 giây để sửa thiếu sót này.

### PAY1 — payout

- `SellerFinanceService.cs:697`/`:700` cho Level 3 Completed và CompletedAt ngay trong RequestPayoutAsync. Probe rút 50 USD tái hiện Completed tức thì.
- Payout Level 1/2 và nhánh failure đã có worker Created/InProgress/FundsSent/Completed/Returned; cần thống nhất cho mọi level và bổ sung lịch sử chuyển bước/actor. SellerPayout hiện chỉ giữ trạng thái và thời điểm hiện tại, chưa có bảng lịch sử bước.
- Level 3 tự nhận Net vào Available sau payment đã có đúng luồng tại `SellerFinanceService.cs:132`; giữ hành vi đó, không đồng nhất nó với payout ngân hàng.

### LED1 — ledger

- Có FinancialTransaction, unique EntryKey và các transaction/gate tài chính. Không kết luận hệ thống chưa có ledger hoặc chưa có chống trùng.
- `ApplicationDbContext.Finance.cs:38` chỉ bảo vệ lịch sử trả nợ/sự kiện nợ và policy phí; chưa chặn Modified/Deleted của FinancialTransaction. Probe sửa Amount đã lưu rồi xóa dòng, cả hai SaveChanges đều thành công.
- `SellerFinanceService.cs:943` chuyển Processing sang Available nhưng chỉ thêm FundsReleased vào Available ở dòng 952; chưa có biểu diễn nguồn/đích đầy đủ như yêu cầu. Cần compatibility/version để không cộng đôi các consumer cũ.
- Đây là thiếu guard và đối soát; không có bằng chứng API hiện tại cho người dùng sửa trực tiếp ledger. Không diễn giải probe DbContext thành lỗ hổng quyền API đã được khai thác.

## 4. Ba lỗi server bổ sung đã tái hiện

### G1 — lưu giỏ có ba seller

- Nơi xử lý: `BuyerCartService.SaveAsync`, dòng 85; API `POST /api/cart/save` gọi thẳng service sau kiểm tra role.
- Gửi ba dòng hợp lệ thuộc ba seller: save thành công và giỏ được lưu ba seller. Chỉ ValidateAsync của quote/checkout kiểm tra nhóm tối đa hai seller.
- Kỳ vọng: server từ chối giỏ hơn hai seller ngay khi lưu. UI random chọn đúng hai seller không bảo vệ API save khỏi request khác.
- Probe: `CART-max-two-sellers`.

### G2 — API tạo đơn một seller nhận số lượng vượt 5

- Nơi xử lý: `CheckoutService.ValidateAndQuoteAsync`, dòng 358; API `POST /api/orders` vẫn mở cho buyer và gọi service này.
- Một dòng Quantity = 6, tồn kho đủ: tạo order thành công. SaveCart có kiểm tra 1–5 nhưng không bảo vệ endpoint tạo đơn cũ.
- Kỳ vọng: quote/create order kiểm tra cùng giới hạn 1–5 ở server, không chỉ luồng cart.
- Probe: `CHECKOUT-max-five-quantity`.

### G3 — PayPal một đơn không giữ hạn mức khi đang capture/Verifying

- `PayPalPaymentService.CaptureAsync` chuyển Verifying ở dòng 91, kiểm tra hạn mức tại dòng 113, nhưng không lưu CaptureReservedAt/ReservedSellerGross như luồng PayPal cả giỏ.
- `SellerFinanceService.cs:26` chỉ cộng payment Verifying có CaptureReservedAt trong tháng; các attempt một đơn này bị bỏ qua. Settlement chỉ tôn trọng authorizedReservation khi có PayPalCheckoutId (`:57`), nên việc bổ sung reservation phải xử lý cả ghi nhận/đối soát tiền đã capture.
- Probe không gọi PayPal thật: seller còn 150 USD hạn mức; capture một đơn 102,10 USD giả lập mất phản hồi và còn Verifying, hai field reservation null; kiểm tra cho đơn khác 102,10 USD vẫn được chấp nhận. Tổng hai khoản vượt 150 USD.
- Kỳ vọng: khoản đang capture/chưa rõ kết quả chiếm hạn mức, giao dịch khác không được dùng lại phần đó; reconcile phải tôn trọng reservation hợp lệ.
- Đây là bằng chứng kiểm tra hạn mức bỏ sót attempt đang xử lý. Chưa chạy race SQL hoặc thu hai khoản tiền ngoài thực tế; không tuyên bố đã tái hiện double charge/provider capture ngoài hệ thống.
- Probe: `PAYPAL-single-reservation`.

## 5. Những phần đã có, không đưa vào danh sách thiếu chức năng

| Nhóm | Bằng chứng source/test hiện tại | Giới hạn xác minh |
| --- | --- | --- |
| B1 — sổ địa chỉ | BuyerAddressesController, BuyerAddressService, buyer-addresses.js; CRUD/mặc định, khóa theo buyer, archive địa chỉ đã tham chiếu, giữ snapshot/retry; BuyerAddressBookChecks trong bộ backend mặc định và test UI chạy đạt. | Chưa chạy SQL QA concurrency/CRUD hoặc browser E2E thật ở lượt này. Không cần migration mới theo source. |
| A1 — admin lookup | Controller/service/contract/UI riêng; tìm chính xác mã, lọc/phân trang, mọi attempt, tổng Succeeded, UTC, quyền; AdminOrderLookupChecks và test UI chạy đạt. | Test translation SQL không phải truy vấn DB thật. Dòng backlog cũ “chờ build” không còn phản ánh kết quả build hiện tại. |
| S2 — feedback sản phẩm | API buyer review/seller reply, service, UI, ownership/idempotency và test ProductFeedbackChecks chạy đạt; tách SellerOrderFeedback. | Chưa kiểm tra migration AddProductFeedbackReplies trên database chính; chưa lặp SQL/browser E2E. |
| N1 — email | BusinessNotificationService, outbox/recipient/key, processor/retry, controller/query quyền và UI; test N1 và SMTP loopback chạy đạt. | Những sự kiện thuộc AD1/RT2/INR1/PAY1/A2 phải nối khi các workflow này được sửa/thêm. Chưa kiểm tra migration CompleteBusinessNotifications trên database chính. |
| FS1 — free shipping | Promotion loại Shipping và cờ FreeShipping, engine quote/snapshot/funding; test pricing/checkout/finance chạy đạt. | Không thay thế free returns và không nghiệm thu thay các task mới. |
| Nền tảng checkout/payment | Giỏ random hai seller, snapshot, transaction split checkout, stock/retry, hủy/hết hạn, thẻ mô phỏng, PayPal cả giỏ/capture/reconcile đã có và regression đạt. | Còn các bypass G1/G2 và thiếu reservation single-order G3; chưa gọi PayPal Sandbox thật. |
| Kg, fee, hold, level/nợ/write-off | Công thức kg lẻ; fee 12% + 0,30; hold 30/15 giây từ Delivered; Instant wallet; tháng UTC; level/feedback thật; nợ/write-off/blacklist tự động; bộ kiểm thử tương ứng đạt. | Không coi tất cả nghiệp vụ mới đã đạt. Đặc biệt EstimatedArrival chưa được luồng tạo nhãn điền, nên dữ liệu đơn tạo bình thường thiếu đầu vào giao đúng hạn; tests level dùng fixture có dữ liệu này. |

## 6. Kết luận sử dụng cho các task tiếp theo

Danh sách 12 task trong mục 2–3 được xác nhận bằng source hiện tại, không chỉ từ backlog. Tuy nhiên danh sách đó chưa bao quát các lỗi G1–G3 tìm thêm trong lần rà soát. Ưu tiên xử lý G3 về reservation, ràng buộc server G1/G2, rồi các thay đổi workflow theo phụ thuộc trong backlog và task người dùng giao.

Không đánh dấu các task hoàn tất sau audit. Không sửa đặc tả để hợp thức hóa source cũ. Database chính và browser E2E thực tế vẫn cần được kiểm tra riêng khi nghiệm thu triển khai.
