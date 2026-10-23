# Nghiệp vụ tài chính người bán

**Căn cứ mới 18/10/2026:** [đặc tả §9–10](BUSINESS_REQUIREMENTS.md) và [LIM1/PAY1/LED1](IMPLEMENTATION_BACKLOG.md). Phí 12% + 0,30 USD, kg lẻ, hold từ Delivered và Instant vào ví giữ nguyên. Cần thêm hạn mức riêng do admin đặt, payout đủ bước cho mọi level và bảo vệ/đối soát ledger. PAY1 đã hoàn thành source ngày 20/10, bỏ shortcut Level 3 Completed tại request; cần migration `20261020162647_UnifyPayoutWorkflowAndHistory`. [Hướng dẫn PAY1](PAYOUT_WORKFLOW.md) ghi API/tổng pending/paid-out/lịch sử và kiểm thử. Chỉ áp QA riêng, chưa áp database chính. Không nhận ledger hiện có là kế toán kép đầy đủ.

Gói tài chính mô phỏng cách marketplace thu tiền buyer, trừ phí, giữ tiền trước khi cho seller rút và xử lý trường hợp seller không còn đủ tiền để hoàn lại. Tất cả số tiền đều là dữ liệu thử nghiệm; PayPal Sandbox cũng không chuyển tiền thật.

## Luồng tiền

**Giao diện 23/10/2026:** Ví Seller có **Tiền giữ theo từng đơn**, phân trang và đồng hồ riêng theo ReleaseAt/HoldSecondsSnapshot. Hiển thị Processing và OnHold còn lại, chưa giao thì chưa bắt đầu đếm, có return/dispute thì nêu lý do chặn. Hết giờ không tự coi tiền đã vào ví. API đọc `seller/finance/funds/page` chỉ cho chính seller, không sửa số dư/ledger hoặc cần migration. [Chi tiết](DEMO_COUNTDOWNS.md).

1. Buyer thanh toán thành công. **SellerGross** bằng tiền buyer trả cộng phần coupon nền tảng tài trợ; giảm giá seller đã được trừ khỏi giá hàng.
2. Mức phí mặc định **12% SellerGross + 0,30 USD/đơn**, không vượt SellerGross. Admin thay đổi trong tab **Phí nền tảng**, có hiệu lực ngay hoặc theo lịch. Settlement chọn mức theo `Payment.PaidAt` và lưu `FeePercentSnapshot`, `FixedFeeAmount`, `PlatformFeeAmount`, `FeePolicyId`; refund vẫn dùng phí gốc.
3. Phần còn lại là **Net**, dùng bù Negative trước. Level 1/2 giữ phần dư trong `Processing`; Level 3 tự cộng phần dư vào **ví Available ngay khi thanh toán thành công**, không cần Delivered hoặc tạo payout.
4. Level 1/2 tính thời gian giữ từ `DeliveredAt` chiều đi. Worker chỉ chuyển tiền vào ví khi hết hạn và không có return/dispute đang mở; giao chiều trả không bắt đầu hoặc đặt lại lịch này.
5. Seller chỉ payout từ `Available`. Payout lần lượt đi qua `Created → InProgress → FundsSent → Completed`. Nếu giả lập ngân hàng từ chối, payout thành `Returned`, tiền trả về hệ thống một lần rồi bù Negative nếu có, phần dư vào `Available`. Tất cả level qua cùng workflow; FundsSent chưa phải hoàn tất. Lịch sử từng bước/actor/UTC trong SellerPayoutEvent, không backfill bước cũ chưa biết. PayoutPending/PaidOut tính trên toàn bộ payout, không cắt theo trang.

Development dùng `Finance:Levels:1:HoldSeconds = 300`, `Finance:Levels:2:HoldSeconds = 180`; Level 3 luôn 0 giây từ payment thành công. Môi trường khác bỏ các khóa HoldSeconds của Level 1/2 và dùng 21/7 ngày từ giao thành công. Level và thời gian giữ được snapshot lúc tạo settlement, không đọc lại level khi mở khóa. Trước Delivered, Level 1/2 có `ReleaseAt = null`.

**Ví và payout:** Available là tiền nằm trong ví hệ thống. Payout chuyển từ ví ra ngân hàng giả lập và vẫn là tính năng riêng. Nhận tiền Instant vào ví không tạo SellerPayout hoặc PaidOut. Tiền Instant vẫn được giữ khi return/dispute và được thu hồi khi refund.

**Cập nhật demo 23/10/2026:** Level 1 giữ 5 phút, Level 2 giữ 3 phút từ Outbound Delivered. Cấu hình mới áp dụng khi tạo settlement mới; settlement đã có giữ nguyên `HoldSecondsSnapshot` và `ReleaseAt`, kể cả chưa giao hàng. Không sửa tiền/lịch sử hoặc cần migration; khởi động lại API để chắc chắn nạp cấu hình mới.

## Các số dư

| Số dư | Ý nghĩa |
| --- | --- |
| `Processing` | Tiền bán hàng đã trừ phí nhưng còn chờ giao hàng/hết hạn giữ. |
| `Available` | Tiền seller được phép payout. |
| `OnHold` | Tiền bị khóa vì tranh chấp hoặc trả hàng đã được duyệt, seller không thể rút. |
| `Negative` | Nghĩa vụ seller còn nợ hệ thống sau refund/chargeback khi các số dư khác không đủ. |

`FinancialTransaction` là sổ cái chỉ thêm mới. Mỗi giao dịch có `EntryKey` duy nhất để retry không ghi phí, refund hay payout hai lần. `SellerSettlement` là bảng đối soát theo từng order/payment; `SellerAccount` giữ số dư hiện tại; `SellerPayout` giữ lịch sử rút tiền.

## Level và giới hạn

| Level | Doanh số tối đa/tháng | Giữ tiền | Điều kiện lên cấp |
| --- | ---: | ---: | --- |
| 1 | 5.000 USD | 21 ngày từ Delivered; demo 5 phút (300 giây) | Mặc định |
| 2 | 10.000 USD | 7 ngày từ Delivered; demo 3 phút (180 giây) | 20 đơn hoàn tất, feedback dương ≥ 90%, giao đúng hạn ≥ 90% |
| 3 (`Instant`) | 1.000.000 USD | 0 giây từ payment thành công, tự vào ví | 100 đơn hoàn tất, feedback dương ≥ 97%, giao đúng hạn ≥ 95% |

Doanh số được tính theo tháng UTC và tự về 0 khi sang tháng. Backend kiểm tra giới hạn trước khi gọi cổng thanh toán và kiểm tra lại lúc ghi nhận thanh toán. Nút **Đánh giá lại cấp** trên tab **Seller Finance** tính level từ dữ liệu đơn, feedback và lần giao hàng hiện có.

Từ bản sửa G3 ngày 19/10/2026, PayPal một đơn cũng commit reservation SellerGross cùng trạng thái Verifying trước capture, dùng chung phần hạn mức đang giữ với PayPal cả giỏ và thẻ. Ví dụ còn 150 USD, đơn 100 USD đang chờ PayPal thì chỉ còn 50 USD cho payment khác. Thất bại đã xác định giải phóng phần giữ; timeout/request bị hủy/sang tháng không tự giải phóng khoản chưa rõ kết quả. Settlement tôn trọng reservation hợp lệ kể cả hạn mức vừa giảm, retry/reconcile không cộng doanh số hai lần. Luồng cũ thiếu reservation được query để đối soát, không tự cấp quyền capture mới. Chi tiết kiểm thử InMemory/SQL và dữ liệu cũ ở [COMMERCE_REFRESH.md](COMMERCE_REFRESH.md#g3--reservation-paypal-một-đơn-sửa-ngày-19102026). Không có migration mới; chỉ áp chuỗi hiện có trên QA riêng, chưa áp lên database chính trong đợt này.

## Hoàn tiền và số dư âm

Khi refund thành công, hệ thống hoàn lại phần phí nền tảng theo tỷ lệ số tiền refund. Nghĩa vụ còn lại được trừ lần lượt từ tiền của settlement đang `Processing`, khoản `OnHold` của đúng đơn, rồi `Available`. Phần vẫn còn thiếu trở thành `Negative`.

Với yêu cầu trả mới (`PricingPolicyVersion = 1`), phí trả được ghi một lần khi tạo nhãn thành công vào `ReturnShippingCost`. Nếu buyer chịu phí, khoản bị trừ khỏi tiền hoàn được bù vào chi phí đã ứng qua `ReturnShippingRecovery`, tránh thu hai lần. Refund tách tiền mặt buyer, phần tài trợ coupon của nền tảng và Gross seller phải hoàn; fee credit dựa trên Gross được hoàn. Phần tiền giữ còn lại được giải phóng theo lịch settlement sau khi refund thành công. Seller Instant đã rút tiền có thể phát sinh số dư âm. Công thức và ví dụ ở [RETURN_PRICING.md](RETURN_PRICING.md); yêu cầu cũ phiên bản 0 giữ cách tính trước nâng cấp.

Trả hàng thường đã duyệt giữ tiền đến khi receipt/refund hoặc admin giải quyết vấn đề hàng trả. Worker không nhập khoản giữ này thành hồ sơ tranh chấp cũ và không mở khóa tiền khi còn yêu cầu trả đang xử lý. Xem [RETURNS_AND_SHIPPING.md](RETURNS_AND_SHIPPING.md).

Seller có `Negative > 0` không được tạo payout. Khi có giao dịch bán mới, hệ thống tự dùng Net mới để bù `Negative` trước; phần dư mới vào `Processing`. Vì vậy seller bỏ tài khoản không làm khoản nợ biến mất trong dữ liệu. Việc khóa danh tính seller hoặc thu nợ thật nằm ngoài phạm vi mô phỏng.

## Giữ tiền khi tranh chấp

Buyer mở yêu cầu qua **Mở yêu cầu giải quyết** trong chi tiết đơn đã giao; mô tả và link bằng chứng bắt buộc. Hệ thống giữ tiền của đúng đơn và chờ hai bên thương lượng trước. Xem [DISPUTES.md](DISPUTES.md) cho toàn bộ quy trình và các thời hạn demo 45 giây.

Khóa giữ tiền theo order và hồ sơ tranh chấp giúp mở lại sau khi đóng mà không dùng lại khoản giữ đã giải quyết. Endpoint cũ `POST /api/orders/{orderId}/fund-hold` chuyển qua quy trình mới, nhận `reason` và `evidenceLinks`; không bỏ qua bằng chứng hoặc thương lượng.

Admin chỉ quyết định hồ sơ đã chuyển lên do seller hết hạn hoặc buyer không đồng ý phương án. Endpoint tương thích `POST /api/orders/{orderId}/fund-hold/resolve` chỉ nhận JWT `admin`, dùng body phán quyết mới có trách nhiệm, kết quả, khoản hoàn, lý do, căn cứ, key và fingerprint; xem [ADMIN_DISPUTE_DECISIONS.md](ADMIN_DISPUTE_DECISIONS.md):

- `SellerWins`: seller thắng, tiền từ `OnHold` trở lại `Available` nếu đã giao và hết hạn giữ; nếu chưa đủ điều kiện thì trở lại `Processing`.
- `FullRefund`/`PartialRefund`: tiền tiếp tục nằm trong `OnHold` và API khởi tạo refund đúng khoản đã chốt theo trách nhiệm. Nếu cổng refund lỗi, settlement ở `RefundPending`, hồ sơ ở `ExecutingAgreement`; worker tự retry. Chỉ khi refund thành công, hold của **đúng order đó** mới được trừ và ghi fee credit đúng một lần; hold còn lại theo lịch ban đầu. Nếu seller thắng trước khi đủ điều kiện mở khóa, tiền trở lại `Processing`.

Nếu buyer không phản hồi phương án đúng hạn, yêu cầu tự đóng và khoản giữ được giải phóng theo điều kiện trên; không tự chấp nhận phương án seller. Thỏa thuận hoàn tiền hoặc trả hàng vẫn giữ hồ sơ mở đến khi hoàn tiền thành công.

## API và giao diện

- `GET /api/seller/finance`: số dư, level, tiến độ, ledger và payout.
- `POST /api/seller/finance/evaluate-level`: tính lại level.
- `POST /api/seller/payouts`: tạo payout với `amount`, `key`, `simulateFailure`.
- `GET /api/orders/{id}`: có thêm `settlement` để seller xem Gross, phí, Net và thời điểm mở khóa.
- Trang **Seller** có khu vực tài chính hiển thị toàn bộ dữ liệu trên và cho tạo payout.

Worker `CommerceMaintenanceWorker` chạy định kỳ để backfill các payment/refund cũ, mở khóa khoản đến hạn và tiến payout. Backfill dùng khóa duy nhất nên có thể chạy lại an toàn.

## Database và cấu hình

Migration `AddSellerFinance` tạo bốn bảng `SellerAccount`, `SellerSettlement`, `FinancialTransaction`, `SellerPayout`. File SQL để duyệt hoặc chạy thủ công là [seller-finance-migration.sql](../database/seller-finance-migration.sql). Không thêm các bảng này trực tiếp vào `baseline.sql`, vì baseline là schema gốc và migration chịu trách nhiệm nâng database hiện có.

Hạn mức/ngày giữ mặc định nằm ở `Finance:Levels` trong `backend/G4.Api/appsettings.json`; Development ghi đè HoldSeconds trong `appsettings.Development.json`. Phí do bảng `PlatformFeePolicy` quản lý, không còn chỉnh bằng appsettings.

## Cấu hình phí admin và migration ngày 14/10/2026

- `GET /api/admin/platform-fees?page=1&pageSize=10`: mức hiện tại và lịch sử phân trang.
- `POST /api/admin/platform-fees`: `feePercent`, `fixedFee`, `effectiveAt` (UTC hoặc null để áp dụng khi lưu), `reason`, `requestKey`, `expectedLatestPolicyId`.
- Chỉ JWT admin được truy cập. Phần trăm 0–100 và phí cố định 0–10.000 USD, tối đa 2 chữ số thập phân; không đặt lịch về quá khứ. Retry cùng requestKey dùng lại bản ghi; phiên cũ nhận 409 và phải tải lại.
- Lịch sử lưu giá trị cũ/mới, người sửa, ngày lưu, ngày hiệu lực và lý do; không có API sửa/xóa và DbContext chặn sửa/xóa. Giá trị cũ là mức tại thời điểm hiệu lực được chọn khi tạo cấu hình. Cấu hình đặt lịch xuất hiện với nhãn Chờ hiệu lực.
- Migration `20261014051607_AddPlatformFeePoliciesAndDeliveryHolds` bổ sung bảng phí và các snapshot vào SellerSettlement, cho phép ReleaseAt null. **Giữ nguyên phí, lịch và tiền của settlement đã tồn tại** (`HoldPolicyVersion = 0`); settlement mới dùng phiên bản 1. Không chạy lại baseline hoặc reset dữ liệu.

```powershell
dotnet ef database update --project backend/G4.Infrastructure --startup-project backend/G4.Api
```

SQL để duyệt/chạy thủ công: [platform-fees-delivery-holds-migration.sql](../database/platform-fees-delivery-holds-migration.sql). Dừng API cũ trước khi cập nhật schema và khởi động lại API/frontend mới.

Kiểm thử: build Release; commerce checks (bao gồm FinancePolicyChecks); Node `tests/platform-fees-ui.test.cjs`; SQL QA riêng qua `--finance-sql`. Các tiêu chí level mở rộng và nợ xấu/blacklist vẫn chưa thuộc thay đổi này.

SQL QA tạo/khởi tạo baseline **chỉ** trên database được guard theo tên `G4_Finance_Test_20261014`, sau đó chạy migration. Đặt `G4_FINANCE_SQL_TEST_CONNECTION` trỏ tới database này rồi chạy:

```powershell
dotnet tests/G4.Commerce.Tests/bin/Release/net8.0/G4.Commerce.Tests.dll --finance-sql
```

Ngày 14/10/2026 đã kiểm tra SQL QA: cùng payment retry đồng thời chỉ có một settlement/release; hai đơn cùng seller không mất cập nhật; lưu phí đồng thời chống trùng và phát hiện phiên cũ; worker khôi phục lịch từ DeliveredAt và không mở khóa sớm. API QA xác minh admin GET 200, buyer/seller/shipper GET/POST 403. Trình duyệt kiểm tra lưu phí, lịch sử, lỗi lịch quá khứ có focus, và form desktop/mobile 390px không tràn ngang.

Đã áp dụng migration trên database local `CloneEbayDB` sau backup COPY_ONLY tại `artifacts/finance-policy/CloneEbayDB-before-finance-20261014.bak`. Sáu settlement cũ giữ nguyên tổng Gross 1.322,50 USD, phí 160,50 USD và Net 1.162,00 USD; giữ nguyên lịch cũ. Các database khác vẫn cần chạy migration. Khởi động lại API/frontend để dùng code mới.
## Quản lý nợ và trả nợ

Kỳ nợ liên tục, trả nợ ngân hàng giả lập, đình chỉ do quá hạn và dashboard admin được mô tả tại [SELLER_DEBT.md](SELLER_DEBT.md). Cần migration `20261014102533_AddSellerDebtManagement` trước khi chạy API mới.

## Báo cáo phát sinh theo kỳ (S1)

Tab Báo cáo tài chính riêng, cách tính và test ở [SELLER_FINANCE_REPORT.md](SELLER_FINANCE_REPORT.md). Đây là read-only projection từ payment/refund thành công và bút toán nghiệp vụ; không thay số dư ví hoặc lịch sử payout. Source/test đã thêm trên nhánh S1, còn cần QA SQL Server tích hợp.
