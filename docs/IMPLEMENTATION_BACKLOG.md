# Backlog triển khai theo đặc tả ngày 18/10/2026

## UI-NAV — Điều hướng Buyer/Seller/Admin ngày 21/10/2026

- Hoàn thành source theo yêu cầu người dùng: Buyer 4 mục, Thanh toán từ giỏ và có nút quay lại; Seller/Admin sidebar 230px có nhóm, ≤1000px dùng drawer; Buyer ≤760px dùng bottom navigation. Shipper giữ nguyên. Badge giữ ID duy nhất; native dialog quản lý focus/Escape, chọn mục đóng menu, resize chuyển cùng nav. URL fragment hỗ trợ Back/Forward/tải lại, link query cũ/PayPal return và fallback fragment không hợp lệ.
- Thay đổi ở `_RoleNavigation`, `_RoleLayout`, ba role views/scripts và CSS/JS điều hướng chung. Không thay API, quyền server, tiền hoặc workflow; đã đồng bộ đặc tả, [hướng dẫn role](AUTHORIZATION_AND_ROLE_PAGES.md#điều-hướng-ngày-21102026), PROJECT_RUN và nội dung checkout PayPal cả giỏ.
- Bằng chứng mới: `dotnet build frontend/frontend.csproj --no-restore -m:1 --disable-build-servers` đạt 0 warning/error; `node --test tests/*.test.cjs` đạt 128/128. Chrome `tests/role-navigation.e2e.cjs` dùng frontend Razor thật, login/API fixture: đủ mục, badge, Back/Forward/reload/query link, checkout/quay lại, keyboard/Escape/focus, resize drawer, không tràn ngang ở 320–1440px hoặc request ghi dữ liệu; ảnh đã xem tại `artifacts/role-navigation-ui/`. `tests/return-payout-ui.e2e.cjs` đã nối bộ điều hướng mới và hồi quy RT1/PAY1 desktop/mobile đạt.
- Không có migration mới; không seed/migrate hoặc sửa database chính/QA. Browser dùng dữ liệu giả lập, chưa E2E nối SQL/provider; không ghi nhận các task nghiệp vụ khác hoàn thành. Restart frontend/tải lại để nhận UI mới.
- Hai script E2E cấp seller/đăng ký seller đã đổi selector menu sang `data-tab` ổn định và đạt `node --check`; chưa chạy lại hai kịch bản nối API/SQL này trong đợt UI.

**Nguồn nghiệp vụ:** [BUSINESS_REQUIREMENTS.md](BUSINESS_REQUIREMENTS.md), phiên bản 2.0. Backlog này cụ thể hóa đầu vào code, không phải xác nhận đã làm. Mốc hiện trạng là đối chiếu source ngày 18/10; đợt cập nhật này chỉ sửa tài liệu, chưa chạy lại app/tests hoặc áp migration. Khi bắt đầu task phải đọc lại source mới nhất.

**Nhãn trạng thái:** `Cần triển khai` = thiếu luồng chính; `Cần điều chỉnh` = đã có nền tảng nhưng khác nghiệp vụ mới; `Giữ/hồi quy` = đã có source, cần giữ đúng; `Có source, cần xác minh triển khai` = có ghi nhận QA/migration riêng, chưa xác nhận database hiện dùng. Không đánh dấu done khi chỉ có tài liệu hoặc migration chưa nghiệm thu.

**Rà soát source/test 19/10/2026:** [báo cáo đối chiếu thực tế](IMPLEMENTATION_AUDIT_2026-10-19.md) xác nhận 7 task thiếu luồng và 5 task cần điều chỉnh bên dưới; B1/A1/S2/N1 có source và regression hiện tại đạt. Build solution đạt 0 warning/error, bộ backend mặc định đạt (SMTP loopback chạy ngoài sandbox), UI 115/115 đạt. Có 10 probe InMemory tái hiện các điểm lệch, gồm 3 lỗi server bổ sung cần sửa: lưu giỏ hơn hai seller (G1), API tạo đơn cũ nhận Quantity > 5 (G2), PayPal một đơn Verifying không giữ hạn mức (G3). Đây là audit, chưa sửa các lỗi hoặc hoàn thành task; chưa chạy SQL/browser E2E mới, không áp migration và không thay database chính. Các ghi nhận QA cũ bên dưới giữ nguyên theo từng đợt.

## 1. Danh mục và phụ thuộc

| Mã | Nội dung | Mốc hiện trạng | Đặc tả |
| --- | --- | --- | --- |
| B1 | CRUD địa chỉ/mặc định và lấy báo giá lại | Đã có source và hồi quy hợp nhất; chờ SQL QA/E2E địa chỉ | §3; bốn task ưu tiên §2; BUYER_ADDRESS_BOOK.md |
| B2 | Lịch sử payment/refund buyer | Cần triển khai màn tổng hợp; đã có dữ liệu trong từng đơn | §5; bốn task ưu tiên §1 |
| S1 | Báo cáo tài chính theo thời gian | Đã hợp nhất master; SQL/HTTP/browser local và regression đạt 22/10; còn snapshot đọc đồng thời, review và nghiệm thu | §2, §9; bốn task ưu tiên §4 |
| S2 | Feedback sản phẩm và trả lời | Có source, cần xác minh triển khai; giữ tách seller feedback | §2; PRODUCT_FEEDBACK.md |
| A1 | Admin tra cứu order/payment/refund | Đã có source tra cứu chỉ đọc; chờ build và QA nghiệm thu | §2; bốn task ưu tiên §3; [ADMIN_ORDER_LOOKUP.md](ADMIN_ORDER_LOOKUP.md) |
| A2 | Admin yêu cầu trả trước phán quyết | Cần triển khai; không nhầm với ReturnRefund qua thương lượng | §8 |
| N1 | Email nghiệp vụ đã bổ sung | Có source, cần xác minh triển khai; nối sự kiện mới theo từng task | §12 |
| AD1 | Đổi địa chỉ áp dụng sau đủ xác nhận | Đã triển khai/nghiệm thu source 20/10: backend, SQL QA và browser fixture đạt; migration chưa áp database chính | §6; ORDER_ADDRESS_CHANGES.md |
| SH1 | ReattemptScheduled và actor từng event | Đã triển khai/nghiệm thu source 20/10: backend, SQL QA và browser fixture đạt; migration chưa áp database chính | §6; RETURNS_AND_SHIPPING.md |
| RT1 | Return window và snapshot deadline | Đã triển khai/nghiệm thu source 20/10: backend, SQL QA và browser fixture đạt; migration chưa áp database chính | §7, §12.1; RETURN_WINDOW_POLICY.md |
| RT2 | Đóng/kháng nghị return từ chối | Cần triển khai deadline/worker; chưa có nhánh đủ theo RejectedAt | §7 |
| INR1 | Hồ sơ chưa nhận hàng | Cần triển khai; OpenAsync hiện yêu cầu Delivered | §8.1 |
| FR1 | Free returns theo mặt hàng | Cần triển khai policy/snapshot/phân phí; đã có giá return theo lý do | §3, §7 |
| LIM1 | Admin override hạn mức seller | Cần triển khai; mức hiện từ cấu hình ứng dụng theo level | §10 |
| PAY1 | Payout chung mọi level | Đã triển khai/nghiệm thu source 20/10: backend, SQL QA và browser fixture đạt; migration chưa áp database chính | §9; PAYOUT_WORKFLOW.md |
| LED1 | Ledger chỉ thêm và đối soát nguồn/đích | Cần củng cố; có FinancialTransaction/key, chưa có guard bất biến tổng quát | §9 |
| FS1 | Free shipping dùng promotion hiện có | Giữ/hồi quy; Shipping/FreeShipping đã có source | §4 |
| BASE | Kg lẻ, policy phí, hold, level, nợ/write-off | Giữ/hồi quy theo quy tắc 18/10 | §3.1, §9–11 |

Thứ tự thực hiện theo task được giao. B2/B1/A1/S1 giữ thứ tự ưu tiên đã ghi ngày 17/10 nếu không có chỉ đạo mới. Với các luồng mới: RT1 trước RT2; FR1 cung cấp snapshot cho giá return/A2; SH1 và ngày giao dự kiến phục vụ INR1; LIM1 phải xử lý capture reservations; PAY1 phối hợp LED1 và N1. Đổi deadline/state phải đồng bộ tất cả consumer trong cùng thay đổi, tránh patch chỉ UI.

## 2. B1/B2/S1/A1 và phần đã có

Chi tiết UI, filter, ngày tính tổng, phân trang và quyền tiếp tục theo [FOUR_TASK_REQUIREMENTS_2026-10-17.md](FOUR_TASK_REQUIREMENTS_2026-10-17.md). Không viết lại giỏ/catalog hoặc sửa tay giao dịch tài chính để làm báo cáo.

- **B1:** xem `CatalogController`, `buyer-cart.js`, `buyer.js`. Tối đa một mặc định/buyer, thêm đầu tiên mặc định, xóa mặc định chọn ID còn lại nhỏ nhất; chỉ chọn checkout hiện hành quyết định địa chỉ giao. Không làm mất tham chiếu/snapshot order khi xóa địa chỉ. Báo giá cũ phải vô hiệu khi địa chỉ thay/xóa, gồm điều kiện promotion; chặn checkout khi chưa có quote mới.
- **B2:** dùng Payment/Refund, quan hệ order và provider reference; hiện Order detail chưa phải màn lịch sử xuyên đơn. Tách ngày tạo dùng filter danh sách với PaidAt/CompletedAt dùng tổng trong kỳ; pending/failed/Verifying không thành tiền đã thu/hoàn. Tổng vòng đời order không bị cắt bởi thời gian/trang.
- **S1:** đọc settlement/ledger và funding snapshot; không cộng đơn chưa trả tiền. Trình bày gross/refund/fee/subsidy/điều chỉnh/net, biểu đồ/bảng ngày/tuần/tháng; net khác Available/PaidOut. Không trừ refund/fee hai lần.
- **A1:** API admin toàn cục chỉ đọc, tìm order/provider capture/refund/buyer/seller, filter/phân trang; che thông tin nhạy cảm. Không nới quyền order detail cho vai trò khác ngoài phạm vi cần thiết.
- **S2/N1:** giữ review theo product khác SellerOrderFeedback, trả lời đúng quyền; giữ outbox transaction/key/người nhận/retry. Xem PRODUCT_FEEDBACK và NOTIFICATION_COMPLETION để xác minh migration/QA thực tế, không tự coi database chính đã nâng.

**Hợp nhất S1 vào master ngày 21/10/2026:** xử lý conflict mục lục tài liệu và script Seller, giữ cả báo cáo tài chính lẫn PAY1. Build solution đạt 0 warning/error; backend `--seller-finance-report` và `--payout-workflow` đạt; UI Node 128/128 đạt. Không tạo/áp migration, không thay database; chưa chạy lại SQL/HTTP/browser. Hai lỗi đã review (đọc báo cáo thiếu snapshot nhất quán khi có giao dịch đồng thời, bật/tắt chuỗi làm hiện biểu đồ cũ sau lỗi tải) chưa sửa trong đợt hợp nhất này; S1 chưa được nghiệm thu hoàn tất.

**QA S1 22/10/2026:** nhánh sửa từ master `fix/seller-finance-report-qa` xử lý DST ở ranh giới ngày, cảnh báo refund thiếu settlement, chống biểu đồ cũ và busy state; thêm InMemory/SQL/UI regression. Solution 0 warning/error, backend mặc định và SQL QA riêng đạt, Node 130/130. `CloneEbayDB` local đã được sao lưu/verify và nâng 4 migration còn thiếu tới 24/24; browser nối DB local cho seller xác nhận số tiền, lọc tuần, bảng và bút toán. Không suy ra database nhóm/production đã nâng. Còn rủi ro truy vấn nhiều lượt không chung snapshot khi dữ liệu đang thay đổi; chưa nghiệm thu cuối. Xem [SELLER_FINANCE_REPORT.md](SELLER_FINANCE_REPORT.md).

**Hợp nhất hoanglt/hungntm 19/10/2026:** đã tích hợp B1 và A1, giữ cấu hình frontend HTTP 5251 và thay đổi tên hiển thị. Sửa xóa địa chỉ có tham chiếu bằng lưu trữ ngoài sổ, giữ nguyên AddressId/snapshot/tiền và checkout idempotency; khóa form địa chỉ khi đang gửi; cập nhật harness giỏ nạp module địa chỉ thật. Build solution pass 0 warning/error, bộ backend mặc định pass, 115/115 test UI pass. QA Chrome desktop/mobile admin bằng fixture pass. Chưa SQL QA/E2E CRUD địa chỉ trên DB thật; không áp migration hoặc thay database chính. Xem [BUYER_ADDRESS_BOOK.md](BUYER_ADDRESS_BOOK.md).

Nghiệm thu: success/failure/verifying/refund pending; tổng đúng toàn bộ phạm vi, page/filter ổn định; truy cập chéo bị chặn; CRUD địa chỉ không sửa đơn cũ; báo cáo khớp settlement và ledger.

**A1 cập nhật 19/10/2026:** đã thêm API admin chỉ đọc, tìm kiếm đa mã giao dịch, lọc nhanh/trạng thái, phân trang theo đơn và chi tiết payment/refund/tracking. Solution build thành công; chưa chạy QA/test nghiệp vụ; xem [ADMIN_ORDER_LOOKUP.md](ADMIN_ORDER_LOOKUP.md). Không phát sinh migration.

**A1 sửa tìm mã/UTC và UX 19/10/2026:** hoàn thành hai lỗi được giao: mã đơn tách khỏi ID payment/refund, provider exact-match; timestamp DTO có UTC/Z và UI giữ offset. Đã thiết kế lại tìm kiếm có loại mã, lọc nâng cao thu gọn, nhãn điều kiện, danh sách responsive và chi tiết chia tab. Build output riêng và backend mặc định pass, test A1 InMemory/SQL translation pass, 113/113 test UI pass; QA trình duyệt bằng fixture desktop/mobile pass. Chưa E2E trên SQL Server thật cho đợt này, không áp migration; không tuyên bố hoàn tất toàn bộ nghiệm thu A1. Xem [ADMIN_ORDER_LOOKUP.md](ADMIN_ORDER_LOOKUP.md).

## 3. AD1 — Xác nhận địa chỉ trước khi áp dụng

**Hoàn thành source ngày 20/10/2026:** buyer xem/xác nhận quote, seller duyệt, shipper đã nhận chấp thuận khả năng giao trước khi đổi snapshot/tiền; chưa có shipper thì seller duyệt cuối. Chặn OutForDelivery khi chờ, deadline snapshot 24 giờ/Development 60 giây, stale cap/shipper/quote yêu cầu xác nhận lại. Request Approved cũ giữ nguyên. Build/backend, UI 120/120, SQL cạnh tranh/rollback và browser fixture desktop/mobile đạt. Migration chỉ áp `G4_AddressConfirmation_Test_20261020`, chưa áp database chính. Xem [ORDER_ADDRESS_CHANGES.md](ORDER_ADDRESS_CHANGES.md).

**Điểm bắt đầu:** `AddressChangeService`, `ShipmentService`, `AddressChangesController`, `OrdersController`, UI address-changes/seller/shipper, snapshot và migration liên quan.

- Buyer gửi lý do/địa chỉ và xác nhận quote; seller chấp thuận; shipper đã nhận chấp thuận khả năng giao. Chưa gán shipper thì không chờ một shipper chưa tồn tại.
- Trong lúc chờ, địa chỉ cũ hiệu lực. Chặn bắt đầu OutForDelivery khi đang chờ; deadline chờ cấu hình được, hết hạn/từ chối giữ nguyên địa chỉ/lượt đổi. Không giữ vô thời hạn.
- Chỉ sau đủ xác nhận mới ghi snapshot order/shipment, chi phí/refund/bộ đếm; khóa/đọc lại order, shipper, cap và giá. Đổi shipper/cap/quote quan trọng làm mất hiệu lực xác nhận cũ.
- Tăng carrier cost trong cap: seller chịu; vượt cap từ chối, không thêm payment buyer. Giảm carrier cost không tự thành refund buyer; tối đa phần ship buyer thực trả còn được hưởng sau promotion/refund.
- Migration lưu các xác nhận/actor/deadline/trạng thái; tên state mới chọn khi code, cập nhật UI/query/worker theo cùng mô hình. Request cũ đã Approved giữ snapshot/kết quả cũ, không áp lại chi phí hoặc refund.

**Nghiệm thu:** seller approve nhưng shipper chưa approve chưa đổi địa chỉ; chưa shipper thì approve kết thúc hợp lệ; reject/timeout không mất lượt; lần thành công thứ hai bị chặn; đồng thời OutForDelivery/cap/shipper thay đổi không áp quote cũ; free ship không tạo refund khoản chưa thu; retry không nhân chi phí/thông báo.

## 4. SH1 — Tracking giao lại và actor

**Hoàn thành source ngày 20/10/2026:** bắt buộc bước lên lịch, thời gian UTC/lý do và actor server cho cả hai chiều; lịch không tăng lượt, chỉ bắt đầu giao mới tăng (tối đa hai). Lịch là dự kiến, không tự kích hoạt khi đến giờ. Migration thêm cột nullable giữ lịch sử, không suy diễn actor cũ. Build/backend, 117/117 UI, SQL cạnh tranh/rollback và browser fixture desktop/mobile đạt; migration chỉ áp database QA riêng, chưa áp database chính. Bằng chứng và cách chạy ở [RETURNS_AND_SHIPPING.md](RETURNS_AND_SHIPPING.md#sh1--lên-lịch-giao-lại-và-actor-20102026).

**Điểm bắt đầu:** `ShippingState`, `ShipmentService.RecordEventAsync`, ShippingEvent/ShippingInfo, contract/UI/timeline, các shipping tests.

- Bắt buộc `DeliveryFailed → ReattemptScheduled → OutForDelivery`; bỏ đường giao lại trực tiếp. Lưu lịch giao lại dự kiến và lý do.
- Tăng DeliveryAttempts khi bắt đầu OutForDelivery, tối đa hai lượt mặc định; lên lịch không tăng. Chuyển hoàn qua ReturningToSender/ReturnedToSeller theo event thực tế.
- Cùng state machine cho Direction Outbound/Return. Event có actor phía server và thời gian UTC, location/note, event ID unique; không nhận actor giả từ client. Legacy event giữ được đọc, không gán bừa actor lịch sử.
- Cập nhật query “cần hành động”, claimability, UI, log/thông báo/realtime khi thêm state. Không biến ReturnedToSeller thành Delivered buyer.

**Nghiệm thu:** không nhảy bước, lịch không tiêu lượt, lượt ba bị chặn; chỉ shipper được gán thao tác; event lặp không tăng lượt; tracking return không tăng số đơn giao thành công cho buyer.

## 5. RT1 — Deadline dùng chung và return demo 5 phút

**Hoàn thành source 20/10/2026:** snapshot cửa sổ tại Outbound Delivered, Development 300 giây/mặc định 7 ngày; quote/request/dispute/worker/UI dùng chung deadline; legacy giữ 7 ngày và request cũ không bị viết lại. Receipt vẫn 45 giây; request/dispute hợp lệ tiếp tục sau cửa sổ. Rejected return được bảo vệ chuyển tiếp, RT2 deadline/auto-close vẫn riêng. Build/backend, SQL race/rollback và Chrome fixture đạt; UI cả đợt 125/125. Migration chỉ áp QA, chưa áp database chính. Xem [RT1](RETURN_WINDOW_POLICY.md).

**Điểm bắt đầu:** `ReturnQuoteBuilder`, `ReturnService`, `CheckoutService.CloseDeliveredOutsideReturnWindowAsync`, `DisputeService`, Orders/return UI, configuration và test TimeProvider.

- Return window mặc định 7 ngày, Development 300 giây từ outbound DeliveredAt. Dùng chính sách chung cho quote/request/close và điều kiện dispute liên quan; tìm hết hardcode `AddDays(-7)` trước sửa.
- Không đổi Returns:ReceiptSeconds sang 300: receipt là 48 giờ/Development 45 giây từ return Delivered. Disputes:ReturnReceiptSeconds phục vụ nhánh thỏa thuận phải khớp receipt policy liên quan.
- Lưu deadline/policy version theo order/hồ sơ; cấu hình đổi không reset deadline hoặc sửa quyền lợi đã snapshot. Chốt cách backfill legacy trước migration; không suy diễn bản cũ là demo 5 phút khi chưa lưu bằng chứng đó.
- Hồ sơ mở đúng hạn vẫn tiếp tục sau cửa sổ. RT2 từ RejectedAt và INR từ estimated delivery không bị cắt bởi return window.

**Nghiệm thu:** các mốc sát hạn server/UI/quote/worker nhất quán, không chỉ thay text; mock thời gian thay vì ngủ 5 phút; không đóng đơn khi return/dispute/quyền phản đối còn cản trở; receipt vẫn 45 giây; deadline cũ không thay khi config đổi.

## 6. RT2 — Return bị từ chối và quyền phản đối

**Điểm bắt đầu:** `ReturnService.RejectAsync/MaintainAsync`, `DisputeService.OpenAsync`, ReturnRequest workflow, list/UI, worker và notifications.

- RejectedAt và deadline phản đối 3 ngày, Development 45 giây bằng cấu hình riêng; hiển thị trong quyết định/email.
- Buyer chấp nhận hoặc im lặng hết hạn thì Closed đúng một lần và xử lý phần giữ tiền theo điều kiện hiện tại, không tự tạo nhãn/refund.
- Phản đối đúng hạn tạo/liên kết dispute và chặn tự đóng/release, kể cả ngoài Delivered + return window. Không cho mở return trùng để reset deadline.
- Khóa/đọc lại khi race buyer phản đối với worker đóng; cần một kết quả nhất quán. Legacy rejection cần chính sách backfill/chuyển tiếp, không tự đóng hàng loạt vì deadline suy diễn.

**Nghiệm thu:** rejection không refund; im lặng đóng một lần; phản đối đúng hạn không bị gate Delivered + 7 ngày chặn; phản đối/worker đồng thời không vừa đóng vừa tạo quyết định tài chính khác nhau.

## 7. INR1 — Chưa nhận hàng

**Điểm bắt đầu:** Dispute/DisputeEntry/contracts/controller/UI, order/shipment estimated date, finance hold, deadlines và notifications.

- Snapshot/hiển thị ngày giao dự kiến; order đã trả tiền và quá hạn được báo chưa nhận. Loại INR khác return dispute, không dùng gate chỉ Delivered hiện có.
- Cửa sổ từ ngày giao dự kiến: mặc định 30 ngày, Development 300 giây; cấu hình riêng. Đây là mặc định mô phỏng, không mặc nhận thành policy eBay mọi thị trường.
- Buyer chỉ cần mô tả; không bắt URL ảnh chứng minh không nhận. Seller nộp tracking/bằng chứng hoặc đề xuất; quá hạn/chưa thống nhất chuyển admin theo deadline hồ sơ.
- Không tạo return shipment khi buyer không có hàng. Không auto-refund lúc mở; tracking Delivered nhưng buyer phủ nhận vẫn được xét bằng chứng khi đủ điều kiện. Một dispute mở/order, không refund xung đột.
- Hàng đến muộn ghi bổ sung, buyer xác nhận/admin xem xét; không tự đóng vì carrier event hoặc tự thu lại sau refund. Trạng thái carrier/order không đồng nghĩa kết quả hồ sơ/tiền.

**Nghiệm thu:** đơn chưa Delivered vẫn mở được khi quá ngày dự kiến; chưa đến ngày bị chặn; đã Delivered bị phủ nhận có luồng xem xét; không bắt URL buyer; không return giả; hold/refund theo quyết định và retry không trùng.

## 8. FR1 — Free returns theo mặt hàng

**Điểm bắt đầu:** policy Product/contract có giới hạn, OrderItem snapshot, checkout/cart UI, ReturnPricing/ReturnQuoteBuilder, DisputeDecisionPricing/finance, migration và tests.

- Hiển thị policy trước mua trong giỏ random/checkout; seller chỉ sửa policy của mặt hàng mình bằng UI/API gọn phục vụ task, không bổ sung catalog/CRUD toàn diện.
- Snapshot policy/version và khối lượng theo item; đơn cũ cần phiên bản tương thích, không hồi tố thành free returns khi seller đổi policy.
- Đổi ý: seller chịu phần free returns, buyer chịu phần non-free. Một nhãn toàn order phân theo trọng lượng snapshot, xử lý cent dư xác định để tổng đúng nhãn. Lỗi seller xử lý theo trách nhiệm bất kể cờ free.
- Free returns chỉ miễn phí chiều trả, không tự hoàn ship đi khi đổi ý. Quote ghi rõ buyer thực nhận, phí mỗi bên, ship đi giữ lại, funding; xác nhận trước request. Không tạo refund âm.
- Không thu nhãn hai lần; buyer chịu phần nào thì ReturnShippingRecovery chỉ phục hồi phần đó sau refund thành công. Tôn trọng policy/subsidy/fee credit, tránh trừ fee/refund hai lần.

**Nghiệm thu:** ví dụ hàng 100, ship đi 5, nhãn 5: đổi ý non-free nhận 95; all-free nhận 100, seller chịu nhãn; lỗi seller full nhận 105. Đơn hỗn hợp phân phí đúng tổng/cents; đổi policy sau mua không đổi quote cũ; buyer không nhận tiền ship chưa trả. Giữ FS1 shipping promotion hiện có.

## 9. LIM1 — Admin hạn mức riêng seller

**Điểm bắt đầu:** SellerAccount/level policy, SellerFinanceService monthly validation, PayPal single/cart reservations, admin API/UI và audit history.

- Theo level là mặc định 5.000/10.000/1.000.000 USD/tháng UTC theo SellerGross thành công. Override per seller ưu tiên khi hiệu lực; số tiền dương, reason/actor/trước-sau/ngày lưu/ngày hiệu lực và key chống trùng.
- Hiệu lực hiện tại/tương lai; không sửa/xóa lịch sử, có kết thúc override quay lại mặc định. Đổi level không ngầm bỏ override còn hiệu lực; cấm seller/client tự đặt.
- Hạ dưới doanh số đã đạt không sửa/hủy giao dịch cũ. Chặn thu mới vượt mức, tính capture reservations, kiểm tra dưới khóa; payment đã capture theo reservation hợp lệ phải ghi nhận/đối soát đúng.
- Không đổi logic tự xét cấp hoặc lịch hold/fee snapshot của settlement cũ. Làm rõ override có hiệu lực ở bước kiểm tra nào và lưu bằng chứng mức đã áp dụng.

**Nghiệm thu:** immediate/future policy đúng thời điểm; kết thúc override về level; replay key không nhân audit; seller bị từ chối; cap giảm giữa capture không bỏ tiền đã thu; hai payment đồng thời không vượt reservation; tháng UTC mới reset doanh số đúng.

## 10. PAY1 và LED1 — Payout và ledger

**PAY1 hoàn thành source 20/10/2026; LED1 vẫn còn riêng.** Mọi level payout qua Created/InProgress/FundsSent/Completed hoặc Returned, lưu lịch sử actor/UTC chỉ thêm, tổng đang chuyển/đã chi và terminal-only email/realtime. Level 3 vẫn instant vào ví; legacy giữ kết quả, không backfill lịch sử giả hoặc trừ tiền lại. Build/backend, SQL concurrent/rollback và Chrome fixture đạt; UI 125/125. Migration chỉ áp QA, chưa áp database chính. [PAY1](PAYOUT_WORKFLOW.md). Guard mới bảo vệ SellerPayoutEvent, chưa phải guard/đối soát toàn bộ FinancialTransaction của LED1.

**Điểm bắt đầu:** SellerFinanceService.RequestPayoutAsync/AdvancePayoutsAsync/AddEntry, FinancialTransaction, SellerPayout, DbContext guards, finance UI, notifications và kiểm thử.

- Mọi level tạo Created và đi InProgress/FundsSent/Completed; bỏ shortcut Level 3 Completed tức thời ở request. Net Level 3 vào Available ngay vẫn giữ nguyên.
- Pending payout trừ Available atomic, không thể rút lại khoản đó; tính tổng đang chuyển từ các payout chưa kết thúc thay vì bắt buộc thêm bucket trùng. Dùng ngân hàng ảo/đích che, không thêm ngân hàng production hoặc payout theo lịch.
- Returned chỉ trả hệ thống một lần, sau đó bù nợ; nếu có bù nợ không hứa toàn bộ sẽ còn Available. Worker race/retry không tạo tiền; payout đã Completed/Returned cũ không chạy lại.
- Bổ sung lịch sử chuyển bước/actor; email Completed/Returned chỉ theo kết quả đã commit, giữ EventKey/RecipientUserId và quyền N1.
- Ledger chỉ thêm, guard sửa/xóa FinancialTransaction, adjustment có lý do/tham chiếu. Chuyển nguồn/đích cùng transaction, khóa duy nhất, đối soát và rollback. Không bổ sung dòng đối ứng rồi vô tình tính đôi các report/hold đang đọc ledger cũ; cần version/compatibility và hồi quy các consumer.
- Không viết lại toàn bộ thành kế toán kép. Test meaningful cho tiền/đồng thời; không kiểm tra chỉ vì tên field tồn tại.

**Nghiệm thu:** Level 3 chỉ instant vào ví; mọi payout qua đủ bước; 87,70 rút 50 còn Available 37,70 + đang chuyển 50; Returned hoàn 50 một lần trước bù nợ. Không sửa/xóa lịch sử, đối soát nguồn/đích và không trừ phí/hoàn hai lần; trạng thái terminal cũ giữ nguyên.

## 11. A2 — Admin yêu cầu trả trước kết luận

- Dùng DisputeService/ReturnService/ShipmentService/finance hiện có; chỉ admin đúng phạm vi có quyền yêu cầu, ghi actor/time/reason/căn cứ.
- Tạo hoặc dùng lại return/nhãn phù hợp, không nhân phí; có trạng thái nhãn lỗi/đang trả/đã về/giao trả thất bại và hướng dẫn buyer/seller.
- Giữ hold, không coi yêu cầu trả là phán quyết cuối. Chờ kết quả hàng về rồi admin tiếp tục SellerWins/FullRefund/PartialRefund theo preview và policy FR1.
- Worker receipt thường không tự refund trong nhánh đang chờ admin. Refund lỗi giữ pending và retry, không đóng giả. Nối outbox/realtime với N1, đúng người nhận và chống trùng.

**Nghiệm thu:** nhánh mới và tái sử dụng hồ sơ cũ; nhãn lỗi/retry không tạo hai shipment/fee; return Delivered chưa tự refund/đóng dispute; admin quyết định tiếp đúng tiền; giao trả thất bại còn hồ sơ để xử lý.

## 12. Hoàn thành và cập nhật trạng thái

Mỗi task khi hoàn thành ghi ngay bên dưới phần của task hoặc thêm bản ghi ngày: thay đổi/API/config/migration, kiểm thử đã chạy, QA database, migration chính đã/chưa áp, giới hạn còn lại. Đồng bộ hướng dẫn module và PROJECT_RUN khi hành vi chạy thực tế thay đổi. Đánh dấu hoàn thành chỉ khi đạt nghiệm thu phù hợp, không dựa vào các kết quả test cũ trong docs.

Đợt **18/10/2026 cập nhật tài liệu**: chưa task mới nào ở trên được ghi hoàn thành; không sửa code, không đổi cấu hình runtime và không áp database migration. Các thay đổi source chưa commit của S2/N1 và phần khác được giữ nguyên.

## 13. G1/G2 — Giới hạn seller và số lượng tại server

**19/10/2026 — đã sửa và đạt regression source.** G1: SaveAsync đếm seller trên toàn bộ sản phẩm được giữ lại trước khi ghi giỏ, chặn hơn hai seller và giữ nguyên version khi từ chối; giỏ cũ sai có thể sửa xuống một/hai seller hoặc xóa hết, giữ khả năng xóa hàng không còn bán được. G2: ValidateAndQuoteAsync kiểm tra số lượng 1–5 cho cả báo giá và tạo đơn mới qua API một seller; retry đúng đơn đã tồn tại giữ snapshot và không trừ tồn lần nữa. ArgumentException được handler hiện có ánh xạ HTTP 400; không thay quyền API hoặc công thức tiền.

- Thêm `CheckoutLimitChecks` vào backend mặc định và tùy chọn `--checkout-limits`. Kiểm tra seller thứ ba gồm dòng không đổi/giỏ cũ, giỏ một/hai seller/rỗng; số lượng -1/0/6/int.MaxValue ở save/quote/create; biên 1/5 hợp lệ; không ghi giỏ/version/stock/order/batch/payment/promotion usage/ledger/outbox khi từ chối; idempotency và snapshot lịch sử.
- Test mới tái hiện G1 trước sửa; sau sửa test riêng đạt. Solution build output `artifacts/checkout-limits-validation` đạt 0 warning/error; backend mặc định đạt, gồm SMTP loopback ngoài sandbox; UI 115/115 đạt.
- Không phát sinh migration; không áp migration/seed và không thay database chính. Chưa SQL QA hoặc HTTP/browser E2E mới. Cần khởi động lại API để dùng source mới. Chi tiết tại [MULTI_SELLER_CART.md](MULTI_SELLER_CART.md).
- G3, AD1/SH1/RT1/PAY1/LED1 và các task còn lại chưa được sửa trong đợt này. Báo cáo audit phía trên giữ ghi nhận tại thời điểm trước bản sửa.

## 14. G3 — Reservation hạn mức PayPal một đơn

**19/10/2026 — đã sửa và đạt regression InMemory/SQL QA.** Single-order capture kiểm tra và ghi reservation SellerGross/Verifying trong cùng transaction giữ seller gate, commit trước gọi provider. Không đủ hạn mức thì Payment vẫn Pending và không capture; payment khác (PayPal/card) tính cả reservation đang chờ. Settlement một đơn tôn trọng reservation hợp lệ như cả giỏ; không kiểm tra lại hạn mức mới để bỏ lửng tiền đã thu. Failed chắc chắn giải phóng phần giữ bằng trạng thái, giữ field làm lịch sử; retry/new key, reconcile, timeout hoặc client cancellation không nhân thu/settlement/ledger/outbox. Legacy Verifying không có reservation vẫn tính vào phần giữ, Capture chỉ query provider; reservation còn chờ không mất chỉ vì sang tháng UTC.

- Build solution output `artifacts/paypal-reservation-validation` đạt 0 warning/error; backend mặc định đạt, gồm `PayPalReservationChecks` mới và SMTP loopback ngoài sandbox; UI Node 115/115 đạt.
- `--paypal-reservations`: giữ trước provider, chặn PayPal/card cạnh tranh, không để Pending thành Verifying khi preflight lỗi; các kết quả từ chối giải phóng đúng; subsidy tính trong SellerGross; retry/cap giảm/hết hạn; phản hồi sai tiền; client cancellation; tháng UTC và dữ liệu legacy.
- `--paypal-reservations-sql`: đã chạy trên `G4_PayPalReservation_Test_20261019`; hai request thật qua context riêng tranh 150 USD cho hai đơn 100 USD chỉ một request được tới gateway. Xác minh reservation commit trước provider, thả khóa trước chờ network, rollback settlement/payment/order/outbox, reconcile hoàn tất không capture lại, và race terminal success/failure với timeout đến muộn.
- Không có migration mới; đã bootstrap baseline khi QA riêng trống và áp các migration hiện có lên QA đó. Không seed/migrate/thay database chính. Provider dùng gateway giả, chưa PayPal Sandbox ngoài hệ thống hoặc browser E2E mới. Cần khởi động lại API; xem [COMMERCE_REFRESH.md](COMMERCE_REFRESH.md).
- G1/G2/G3 đã sửa trong source. AD1/SH1/RT1/PAY1/LED1 và các task thiếu luồng vẫn giữ trạng thái riêng, không được đánh dấu hoàn thành theo kết quả G3.

## 15. SH1 — Giao lại có lịch và actor ngày 20/10/2026

- Đã bỏ chuyển trực tiếp DeliveryFailed → OutForDelivery. Shipper được gán nhập lịch tương lai/lý do, sau đó bắt đầu giao thực tế; hai chiều dùng chung state machine. Hết hai lượt vẫn phải ghi ReturningToSender → ReturnedToSeller thực tế.
- Event lấy actor từ server, lưu UTC và snapshot lịch; event ID trùng cùng payload là no-op, khác payload bị từ chối. Thêm bộ lọc/claimability, timeline buyer/seller/shipper/admin, email theo từng shipment/event/người nhận và realtime từ transaction đã commit. UI giữ payload/event ID khi kết quả mạng chưa rõ để thử lại an toàn.
- Migration `20261020150408_AddShipmentReattemptSchedulesAndActors` và [SQL để duyệt](../database/shipment-reattempt-actors-migration.sql) chỉ thêm cột nullable/index/FK. Lịch sử cũ giữ actor null, timestamp/note/lượt giao không đổi. Đã áp và kiểm tra trên `G4_ShippingReattempt_Test_20261020`; **chưa áp database chính**, không chạy baseline/seed lên database chính.
- Build solution output `artifacts/shipping-reattempt-validation` đạt 0 warning/error; backend mặc định đạt (gồm SH1 và SMTP loopback), UI Node 117/117 đạt. SQL QA thật đạt: bảo toàn legacy, hai chiều, hai request lên lịch/bắt đầu đồng thời chỉ áp một lần, quyền shipper, claim/filter, giới hạn lượt và rollback shipment/event/email/realtime.
- Browser fixture dùng Razor/CSS/JS thật đạt trên Chrome 1440px/390px, cả Outbound/Return: validation bắt buộc/tương lai, UTC, thử lại cùng key sau lỗi mạng, timeline/actor, escape note, không tràn ngang. Ảnh `artifacts/shipping-reattempt-ui/`. Đây là API fixture, không phải E2E HTTP + SQL hoặc gọi carrier thật.
- AD1/RT1/PAY1/LED1 và các task thiếu luồng khác chưa làm trong đợt SH1. Báo cáo audit ngày 19/10 vẫn giữ bằng chứng trước sửa.

## 16. AD1 — Xác nhận địa chỉ trước áp dụng ngày 20/10/2026

- Đã có quote server và buyer xác nhận trước tạo request; state Pending → AwaitingShipper → Approved khi đã gán shipper, hoặc Pending → Approved khi chưa gán. Rejected/Expired không đổi địa chỉ/tiền/lượt. Chặn bắt đầu giao khi còn chờ; lưu actor/deadline/applied time phía server.
- Apply cuối kiểm tra lại trạng thái/snapshot, cap/revision, shipper, quote/payment/refund còn được hưởng. Cap update/claim cùng gate; stale request bị từ chối để buyer xác nhận lại. Free shipping dùng promotion hiện có, không refund khoản buyer chưa trả; mọi chi phí/refund mô phỏng và outbox/realtime cùng transaction, retry an toàn.
- Migration `20261020153620_AddAddressChangeConfirmations` và [SQL duyệt](../database/address-change-confirmations-migration.sql): thêm confirmation/deadline/workflow/revision, unique active request theo IsPending; giữ Approved cũ/actor null, Pending cũ có hạn CreatedAt + 24 giờ và cần quote mới. Chỉ bootstrap/migrate QA `G4_AddressConfirmation_Test_20261020`, **chưa áp database chính**.
- Build output `artifacts/address-confirmation-validation` đạt 0 warning/error; backend mặc định đạt (gồm AD1/SH1/SMTP loopback); UI Node 120/120 đạt. AD1 SQL thật đạt request/seller/shipper concurrent, cuối cùng ghi refund/cost một lần, race cap/claim/start/expiry và rollback sau finance SaveChanges rồi retry. Model/snapshot không thiếu migration.
- Browser fixture buyer/seller/shipper Chrome 1440px/390px đạt, gồm quote-before-submit, chờ shipper chưa đổi địa chỉ, accept/refuse và trạng thái/thao tác sau quyết định, escape, không lỗi JS/tràn ngang; ảnh đã xem ở `artifacts/address-confirmation-ui/`. Không phải E2E HTTP nối SQL hoặc provider/carrier thật. Không chạy lại QA SQL 13/10 hoặc thay database chính.
- Chỉ làm AD1 trong đợt này; RT1/PAY1/LED1 và bảy task thiếu luồng từ audit còn phạm vi riêng. Xem [hướng dẫn AD1](ORDER_ADDRESS_CHANGES.md).


## 17. RT1/PAY1 — Deadline trả hàng và payout ngày 20/10/2026

- RT1 đã dùng ReturnWindowPolicy chung cho quote/API cũ/mới/dispute/close, snapshot từ Outbound Delivered: 7 ngày/Development 300 giây, đúng hạn không mở mới; thay cấu hình không đổi đơn đã giao. Legacy giữ quyền 7 ngày và ReturnRequest.ReturnDeadline cũ. Request đúng hạn tiếp tục, receipt 45 giây riêng. Không đóng return/dispute còn cản trở; quyền phản đối rejection được bảo vệ chuyển tiếp, chưa triển khai RT2 deadline/auto-close.
- PAY1 đã bỏ Level 3 Completed tức thời: mọi level đi qua ngân hàng mô phỏng, Created/InProgress/FundsSent/Completed hoặc Returned. Ví trừ một lần, tổng pending/paid-out toàn bộ payout; history actor/UTC chỉ thêm và chống trùng, email terminal/realtime atomic, Returned bù nợ một lần. Legacy terminal không chạy lại; pending cũ tiếp tục từ bước thực, không bịa lịch sử hoặc trừ ví lại. Level 3 vẫn vào ví ngay sau payment/bù nợ; LED1 FinancialTransaction guard/đối soát vẫn chưa làm.
- Migration `20261020162229_SnapshotReturnWindowDeadlines` và `20261020162647_UnifyPayoutWorkflowAndHistory`; SQL duyệt [RT1](../database/return-window-deadlines-migration.sql), [PAY1](../database/payout-workflow-history-migration.sql). Chỉ bootstrap/migrate **G4_ReturnWindowPayout_Test_20261020**, **chưa áp database chính**, không reset dữ liệu chính.
- Build output `artifacts/return-window-payout-validation` 0 warning/error, backend mặc định đạt gồm các module trước và SMTP loopback. Test RT1 299/300 giây, policy cũ/mới, replay, tiếp tục return/appeal và receipt riêng; PAY1 ba level, số dư/pending/paid-out, lịch sử/actor, debt recovery, legacy/retry/guard. Hai fixture cũ đã dùng đồng hồ giả nhất quán cho DeliveredAt thay vì thời gian thực chạy sau thời điểm fake clock.
- SQL QA thật đạt bảo toàn migration/legacy, request/close cạnh tranh, snapshot/event rollback, payout cùng key và hai key tranh tiền, worker concurrent đủ bước, DTO/history/tổng, rollback request/Completed/Returned sau SaveChanges và retry. UI Node 125/125 đạt.
- Chrome fixture desktop 1440px/mobile 390px dùng dialog return và Seller Razor/CSS/JS thật: deadline, hết hạn vô hiệu quote, request payout, pending balances, các bước/history, Completed/Returned; không lỗi JS hoặc tràn ngang, ảnh đã xem tại `artifacts/return-window-payout-ui/`. Đây là API fixture, chưa E2E HTTP nối SQL hoặc carrier/bank/provider thật.
- Đã cập nhật [RT1](RETURN_WINDOW_POLICY.md), [PAY1](PAYOUT_WORKFLOW.md), module và PROJECT_RUN. Không làm các task khác hoặc áp migration chính trong đợt này.
