# RT1 — Cửa sổ trả hàng từ giao thành công

**Đồng hồ 23/10/2026:** Buyer xem đếm ngược ReturnWindowClosesAt trong chi tiết đơn và báo giá return; receipt hiển thị riêng theo hạn nhận hàng trả. Không khởi động lại thời hạn khi reload và không cắt hồ sơ đã gửi đúng hạn. [Hướng dẫn chung](DEMO_COUNTDOWNS.md).

Đã triển khai và kiểm thử source ngày 20/10/2026. [Đặc tả §7](BUSINESS_REQUIREMENTS.md#7-trả-hàng-và-refund), [backlog RT1](IMPLEMENTATION_BACKLOG.md#5-rt1--deadline-dùng-chung-và-return-demo-5-phút). Migration chỉ áp QA riêng, **chưa áp database chính**.

## Deadline dùng chung

`Returns:WindowSeconds` mặc định 604800 giây (7 ngày); Development 300 giây (5 phút). Khi event **Outbound Delivered** thành công, cùng transaction lưu trên order:

- `ReturnWindowPolicyVersion = 1`;
- `ReturnWindowSeconds`, `ReturnWindowStartedAt = DeliveredAt` UTC;
- `ReturnWindowClosesAt = DeliveredAt + WindowSeconds`.

Đổi cấu hình sau đó không thay deadline đã snapshot. Chiều trả Delivered, event lặp hoặc retry không reset thời hạn. `ReturnWindowPolicy` dùng chung cho quote, API return hiện tại/cũ, dispute thông thường và worker đóng đơn. Cửa sổ mở từ Delivered đến **trước** deadline; tại đúng deadline không mở request mới.

Request mới lưu ReturnDeadline bằng deadline đã chốt từ giao thành công, thay cho cách cũ “thời điểm gửi + 7 ngày”. Request đã nhận đúng hạn tiếp tục qua seller duyệt, tạo nhãn, receipt/refund hoặc tranh chấp sau khi cửa sổ mở request hết. Replay đúng key/payload sau hạn trả cùng request, không tạo hồ sơ mới.

**Receipt vẫn riêng:** Returns:ReceiptSeconds mặc định 172800 giây/Development 45 giây từ **Return Delivered**. Không đổi khóa này thành 300. RT1 không thay hold seller. Theo cập nhật demo 23/10/2026, hold Development Level 1/2 là 300/180 giây (5/3 phút); Level 3 vẫn Instant.

## Worker, quyền phản đối và UI

Worker lấy ứng viên hết hạn rồi khóa/đọc lại order, deadline và hồ sơ liên quan trước khi Closed. Return chưa Closed hoặc dispute còn mở chặn đóng; cạnh tranh request/worker không được vừa chấp nhận return vừa đóng đơn. Worker lặp không đóng hai lần hoặc tạo refund.

Return bị từ chối nhưng đã mở đúng cửa sổ vẫn được giữ quyền mở dispute sau cửa sổ, không bị worker RT1 đóng mất quyền. Đây là bảo vệ chuyển tiếp: **RT2 chưa có deadline phản đối riêng từ RejectedAt hoặc tự đóng rejection**; RT1 không tự đoán hạn phản đối hoặc đóng các hồ sơ đó. INR1 vẫn là task riêng, không được coi đã có do sửa gate dispute thường.

Chi tiết order trả `returnWindowClosesAt` UTC/Z, `returnWindowPolicyVersion`, `returnWindowSeconds`, `canRequestReturn`, `canOpenDispute`. Buyer hiển thị deadline theo múi giờ thiết bị, ẩn thao tác không đủ điều kiện. Quote trả deadline/version; dialog tự vô hiệu quote hết hạn và không gửi request bằng báo giá hết hạn. Server luôn kiểm tra lại, kể cả khi UI/client cũ vẫn gửi.

## Dữ liệu cũ và migration

Migration **20261020162229_SnapshotReturnWindowDeadlines**, sau AD1 **20261020153620_AddAddressChangeConfirmations**; [SQL để duyệt](../database/return-window-deadlines-migration.sql).

Order đã Delivered trước nâng cấp giữ **7 ngày** theo hành vi cũ: policy version 0, seconds 604800, anchor từ Outbound DeliveredAt. Không suy diễn đơn cũ là demo 5 phút theo môi trường hiện tại. Order cũ chưa giao sẽ snapshot policy hiện hành khi giao thành công sau nâng cấp. Dữ liệu import chưa có snapshot nhưng có DeliveredAt cũng fallback 7 ngày, không đọc cấu hình mới để rút ngắn quyền.

Migration không sửa ReturnRequest.ReturnDeadline cũ (có thể từng mang ý nghĩa “CreatedAt + 7 ngày”), CreatedAt, tiền, refund hoặc hồ sơ đang xử lý. Deadline order là nguồn cho các yêu cầu mới; request cũ đã hợp lệ không bị hết hiệu lực do backfill. Không chạy baseline/seed lại trên database có dữ liệu.

```powershell
dotnet ef database update 20261020162229_SnapshotReturnWindowDeadlines --project backend/G4.Infrastructure --startup-project backend/G4.Api
```

Đợt RT1/PAY1 chỉ bootstrap và áp migration lên **G4_ReturnWindowPayout_Test_20261020**, chưa áp database chính. Khi triển khai, migrate đúng database rồi khởi động lại API/frontend và tải lại trang. PAY1 nằm ở migration kế tiếp, [xem hướng dẫn](PAYOUT_WORKFLOW.md).

## Bằng chứng kiểm thử 20/10

- Solution build output `artifacts/return-window-payout-validation`: 0 warning/error; backend mặc định đạt, gồm SMTP loopback.
- `--return-window`: mốc 299/300 giây nhất quán quote/API cũ/API mới/dispute/close; quote trước hạn gửi sau hạn bị chặn, request đã nhận replay sau hạn được giữ; đổi cấu hình không đổi quyền; legacy 7 ngày; rejection/dispute không bị đóng; return delivery không reset cửa sổ và receipt vẫn 45 giây.
- `--return-payout-sql`: database QA riêng, nâng từ schema trước RT1; giữ legacy deadline/request/payout/tiền; request/close cạnh tranh không ghi kết quả xung đột, snapshot/order/event rollback cùng delivery rồi retry thành công.
- UI Node 125/125 đạt cho cả đợt. Chrome fixture desktop 1440px/mobile 390px dùng dialog/CSS/JS thật, kiểm tra deadline, quote hết hạn và vô hiệu submit; ảnh `artifacts/return-window-payout-ui/` đã xem lại. Đây là API fixture, chưa E2E HTTP nối SQL hoặc carrier/provider thật.

```powershell
dotnet run --project tests/G4.Commerce.Tests -- --return-window
$env:G4_RETURN_PAYOUT_SQL_CONNECTION='Server=localhost;Database=G4_ReturnWindowPayout_Test_20261020;Integrated Security=True;TrustServerCertificate=True'
dotnet run --project tests/G4.Commerce.Tests -- --return-payout-sql
node --test tests/*.test.cjs
node tests/return-payout-ui.e2e.cjs
```

SQL test chỉ chấp nhận đúng tên QA trên và bootstrap khi trống. Browser test cần Playwright/Chrome (NODE_PATH trỏ package, CHROME_PATH trỏ Chrome nếu dùng runtime ngoài project).
