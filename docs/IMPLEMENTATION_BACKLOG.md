# Backlog triển khai theo đặc tả ngày 18/10/2026

**Nguồn nghiệp vụ:** [BUSINESS_REQUIREMENTS.md](BUSINESS_REQUIREMENTS.md), phiên bản 2.0. Backlog này cụ thể hóa đầu vào code, không phải xác nhận đã làm. Mốc hiện trạng là đối chiếu source ngày 18/10; đợt cập nhật này chỉ sửa tài liệu, chưa chạy lại app/tests hoặc áp migration. Khi bắt đầu task phải đọc lại source mới nhất.

**Nhãn trạng thái:** `Cần triển khai` = thiếu luồng chính; `Cần điều chỉnh` = đã có nền tảng nhưng khác nghiệp vụ mới; `Giữ/hồi quy` = đã có source, cần giữ đúng; `Có source, cần xác minh triển khai` = có ghi nhận QA/migration riêng, chưa xác nhận database hiện dùng. Không đánh dấu done khi chỉ có tài liệu hoặc migration chưa nghiệm thu.

## 1. Danh mục và phụ thuộc

| Mã | Nội dung | Mốc hiện trạng | Đặc tả |
| --- | --- | --- | --- |
| B1 | CRUD địa chỉ/mặc định và lấy báo giá lại | Cần triển khai thêm/xóa/mặc định; đã có xem/sửa | §3; bốn task ưu tiên §2 |
| B2 | Lịch sử payment/refund buyer | Cần triển khai màn tổng hợp; đã có dữ liệu trong từng đơn | §5; bốn task ưu tiên §1 |
| S1 | Báo cáo tài chính theo thời gian | Cần triển khai; đã có số dư/ledger/payout phân trang | §2, §9; bốn task ưu tiên §4 |
| S2 | Feedback sản phẩm và trả lời | Có source, cần xác minh triển khai; giữ tách seller feedback | §2; PRODUCT_FEEDBACK.md |
| A1 | Admin tra cứu order/payment/refund | Đã có source tra cứu chỉ đọc; chờ build và QA nghiệm thu | §2; bốn task ưu tiên §3; [ADMIN_ORDER_LOOKUP.md](ADMIN_ORDER_LOOKUP.md) |
| A2 | Admin yêu cầu trả trước phán quyết | Cần triển khai; không nhầm với ReturnRefund qua thương lượng | §8 |
| N1 | Email nghiệp vụ đã bổ sung | Có source, cần xác minh triển khai; nối sự kiện mới theo từng task | §12 |
| AD1 | Đổi địa chỉ áp dụng sau đủ xác nhận | Cần điều chỉnh; code seller duyệt đã sửa snapshot rồi mới ack | §6 |
| SH1 | ReattemptScheduled và actor từng event | Cần điều chỉnh; hiện DeliveryFailed → OutForDelivery, event chưa có actor | §6 |
| RT1 | Return window và snapshot deadline | Cần điều chỉnh; mở/đóng hiện hardcode 7 ngày, ReceiptSeconds dev 45 | §7, §12.1 |
| RT2 | Đóng/kháng nghị return từ chối | Cần triển khai deadline/worker; chưa có nhánh đủ theo RejectedAt | §7 |
| INR1 | Hồ sơ chưa nhận hàng | Cần triển khai; OpenAsync hiện yêu cầu Delivered | §8.1 |
| FR1 | Free returns theo mặt hàng | Cần triển khai policy/snapshot/phân phí; đã có giá return theo lý do | §3, §7 |
| LIM1 | Admin override hạn mức seller | Cần triển khai; mức hiện từ cấu hình ứng dụng theo level | §10 |
| PAY1 | Payout chung mọi level | Cần điều chỉnh; Level 3 hiện có Completed ngay khi tạo | §9 |
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

Nghiệm thu: success/failure/verifying/refund pending; tổng đúng toàn bộ phạm vi, page/filter ổn định; truy cập chéo bị chặn; CRUD địa chỉ không sửa đơn cũ; báo cáo khớp settlement và ledger.

**A1 cập nhật 19/10/2026:** đã thêm API admin chỉ đọc, tìm kiếm đa mã giao dịch, lọc nhanh/trạng thái, phân trang theo đơn và chi tiết payment/refund/tracking. Solution build thành công; chưa chạy QA/test nghiệp vụ; xem [ADMIN_ORDER_LOOKUP.md](ADMIN_ORDER_LOOKUP.md). Không phát sinh migration.

## 3. AD1 — Xác nhận địa chỉ trước khi áp dụng

**Điểm bắt đầu:** `AddressChangeService`, `ShipmentService`, `AddressChangesController`, `OrdersController`, UI address-changes/seller/shipper, snapshot và migration liên quan.

- Buyer gửi lý do/địa chỉ và xác nhận quote; seller chấp thuận; shipper đã nhận chấp thuận khả năng giao. Chưa gán shipper thì không chờ một shipper chưa tồn tại.
- Trong lúc chờ, địa chỉ cũ hiệu lực. Chặn bắt đầu OutForDelivery khi đang chờ; deadline chờ cấu hình được, hết hạn/từ chối giữ nguyên địa chỉ/lượt đổi. Không giữ vô thời hạn.
- Chỉ sau đủ xác nhận mới ghi snapshot order/shipment, chi phí/refund/bộ đếm; khóa/đọc lại order, shipper, cap và giá. Đổi shipper/cap/quote quan trọng làm mất hiệu lực xác nhận cũ.
- Tăng carrier cost trong cap: seller chịu; vượt cap từ chối, không thêm payment buyer. Giảm carrier cost không tự thành refund buyer; tối đa phần ship buyer thực trả còn được hưởng sau promotion/refund.
- Migration lưu các xác nhận/actor/deadline/trạng thái; tên state mới chọn khi code, cập nhật UI/query/worker theo cùng mô hình. Request cũ đã Approved giữ snapshot/kết quả cũ, không áp lại chi phí hoặc refund.

**Nghiệm thu:** seller approve nhưng shipper chưa approve chưa đổi địa chỉ; chưa shipper thì approve kết thúc hợp lệ; reject/timeout không mất lượt; lần thành công thứ hai bị chặn; đồng thời OutForDelivery/cap/shipper thay đổi không áp quote cũ; free ship không tạo refund khoản chưa thu; retry không nhân chi phí/thông báo.

## 4. SH1 — Tracking giao lại và actor

**Điểm bắt đầu:** `ShippingState`, `ShipmentService.RecordEventAsync`, ShippingEvent/ShippingInfo, contract/UI/timeline, các shipping tests.

- Bắt buộc `DeliveryFailed → ReattemptScheduled → OutForDelivery`; bỏ đường giao lại trực tiếp. Lưu lịch giao lại dự kiến và lý do.
- Tăng DeliveryAttempts khi bắt đầu OutForDelivery, tối đa hai lượt mặc định; lên lịch không tăng. Chuyển hoàn qua ReturningToSender/ReturnedToSeller theo event thực tế.
- Cùng state machine cho Direction Outbound/Return. Event có actor phía server và thời gian UTC, location/note, event ID unique; không nhận actor giả từ client. Legacy event giữ được đọc, không gán bừa actor lịch sử.
- Cập nhật query “cần hành động”, claimability, UI, log/thông báo/realtime khi thêm state. Không biến ReturnedToSeller thành Delivered buyer.

**Nghiệm thu:** không nhảy bước, lịch không tiêu lượt, lượt ba bị chặn; chỉ shipper được gán thao tác; event lặp không tăng lượt; tracking return không tăng số đơn giao thành công cho buyer.

## 5. RT1 — Deadline dùng chung và return demo 5 phút

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
