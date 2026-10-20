# PAY1 — Rút tiền ra ngân hàng giả lập cho mọi level

Đã triển khai/nghiệm thu source ngày 20/10/2026 theo [đặc tả §9](BUSINESS_REQUIREMENTS.md#9-phí-nền-tảng-seller-balance-và-payout). Migration mới chỉ áp QA riêng, **chưa áp database chính**.

## Ví và quy trình ngân hàng

Level 3 vẫn nhận Net sau phí/bù nợ vào Available ngay khi payment thành công. Payout là seller chủ động chuyển từ ví ra ngân hàng giả lập; không tự tạo payout khi tiền vào ví.

Mọi level tạo **Created**, CompletedAt/BankReferenceId ban đầu null. Worker 5 giây/lần chuyển một bước mỗi lượt:

```text
Created → InProgress → FundsSent → Completed
                              ↘ Returned
```

FundsSent có bank reference nhưng chưa phải ngân hàng xác nhận hoàn tất. Bước kế tiếp là xác nhận ngân hàng **mô phỏng**: Completed nếu thành công, Returned khi bật SimulateFailure. Không gọi ngân hàng production, không sử dụng tiền/tài khoản thật. Không còn shortcut Level 3 Completed tại request.

Ví dụ Available 87,70 USD, rút 50 USD: Available 37,70, PayoutPending 50, PaidOut chưa tăng. Completed làm PayoutPending về 0 và PaidOut tăng 50. Returned trả hệ thống đúng một lần rồi bù Negative nếu có; chỉ phần dư còn Available. Ví dụ Available đã bị sử dụng hết, Negative 20 và ngân hàng trả 50 thì bù nợ 20, còn Available 30.

PayoutPending/PaidOut là tổng truy vấn **tất cả** SellerPayout của seller, không cắt theo trang/20 dòng summary. Không tạo bucket balance mới hoặc sửa cách ghi nhận tiền bán hàng. Số đang chuyển không còn nằm trong Available để rút lần nữa. History/state mới không thay thế sổ FinancialTransaction hoặc hoàn thành task LED1.

## Transaction, actor và lịch sử

- API chỉ cho seller yêu cầu/xem payout của mình, lấy sellerId từ tài khoản xác thực. Kiểm tra số tiền, Available, Negative = 0 và trạng thái hạn chế dưới seller gate/transaction.
- Request cùng key/amount/SimulateFailure trả payout đã có; khác nội dung trả 409. Request trùng/concurrent chỉ trừ ví và ghi ledger một lần. UI khóa form khi đang gửi, giữ cùng key/payload sau lỗi mạng/5xx chưa rõ kết quả; 4xx cho sửa và gửi lại. Không đổi amount/lựa chọn khi yêu cầu trước còn chưa rõ kết quả.
- `SellerPayout.WorkflowVersion = 1` cho payout mới. `SellerPayoutEvent` lưu FromStatus, ToStatus, actor, UTC/time và bank reference snapshot. Created có actor seller; worker có actor system, user ID null. Unique PayoutId/ToStatus chống ghi bước trùng; DbContext chặn sửa/xóa lịch sử payout.
- Worker khóa seller, đọc lại payout/account và chỉ xử lý Created/InProgress/FundsSent. Terminal Completed/Returned không được chạy lại; state không biết không bị tự coi Completed. Returned/ledger/debt recovery/history/outbox/realtime cùng transaction; rollback không tạo tiền hoặc phát thông báo giả.
- PayoutCompleted/PayoutReturned chỉ phát khi có kết quả terminal đã commit, giữ cơ chế EventKey/RecipientUserId của N1. Created/InProgress/FundsSent không phát email hoàn tất. Realtime finance cập nhật theo SellerPayout kể cả bước không thay Available.

## API và UI

Các endpoint cũ giữ quyền và nội dung request:

- `POST /api/seller/payouts`: `{amount, key, simulateFailure}`.
- `GET /api/seller/finance`: thêm `payoutPending`, `paidOut`; lịch sử payout trong summary cũng có history/version.
- `GET /api/seller/finance/payouts/page`: phân trang theo payout; mỗi dòng có `history`, `workflowVersion`. History chỉ chứa bước đã ghi thật, timestamp UTC/Z. Không nhận actor hoặc status do client chọn.

Trang Seller thêm số **Đang chuyển ngân hàng**, **Đã chi ra ngân hàng**, lịch sử từng bước trong từng payout; FundsSent ghi rõ chờ xác nhận. Level 3 có giải thích riêng tiền vào ví và thao tác rút ra ngân hàng. Nội dung được escape; desktop/mobile và retry đã kiểm tra.

## Legacy và migration

Migration **20261020162647_UnifyPayoutWorkflowAndHistory**, sau RT1 **20261020162229_SnapshotReturnWindowDeadlines**. [SQL để duyệt](../database/payout-workflow-history-migration.sql) chỉ thêm WorkflowVersion mặc định 0, bảng event/index/FK; không sửa balance, ledger, CompletedAt/BankReferenceId hoặc trạng thái payout cũ.

Payout Completed/Returned cũ giữ terminal và không chạy lại, kể cả Level 3 đã hoàn ngay trước nâng cấp. Không backfill actor hoặc các bước chưa có bằng chứng. Payout cũ còn pending tiếp tục từ trạng thái thực tế, ghi các bước mới với actor system; không trừ Available lại. UI giải thích lịch sử legacy có thể thiếu bước trước nâng cấp.

```powershell
dotnet ef database update 20261020162647_UnifyPayoutWorkflowAndHistory --project backend/G4.Infrastructure --startup-project backend/G4.Api
```

Đợt này chỉ áp trên **G4_ReturnWindowPayout_Test_20261020**, **chưa áp database chính**. Nâng migration tăng dần rồi restart API/frontend và tải lại trang, không chạy lại baseline/seed trên database có dữ liệu.

## Kiểm thử 20/10

- Solution build output `artifacts/return-window-payout-validation`: 0 warning/error; backend mặc định (gồm PAY1/RT1/finance/debt/N1/SMTP loopback) đạt.
- `--payout-workflow`: Level 1/2/3 đủ từng bước, số dư 87,70 → 37,70 + 50 đang chuyển, chỉ terminal có email; retry không nhân debit/history/mail; Returned/bù nợ một lần; guard sửa/xóa history; tổng không cắt theo trang; legacy terminal giữ nguyên, pending tiếp tục không bịa bước cũ hoặc trừ tiền lại. FinancePolicyChecks tiếp tục xác nhận Level 3 vào ví ngay, không tự tạo payout.
- `--return-payout-sql`: migration giữ tiền/payout legacy, hai request cùng key chỉ tạo một payout, hai key tranh Available chỉ một đủ tiền; nhiều worker ghi đủ bước/đúng history; SQL query summary/paging; lỗi sau SaveChanges ở request/Completed/Returned rollback ví, state/history/outbox/realtime và retry thành công.
- Node UI 125/125 đạt cho cả đợt. Chrome fixture dùng Seller Razor/CSS/JS thật, desktop 1440px/mobile 390px: tạo yêu cầu, pending/paid-out, refresh từng bước, history/actor, Completed/Returned và không tràn ngang. Ảnh `artifacts/return-window-payout-ui/` đã xem lại. Đây là API fixture, chưa E2E HTTP nối SQL hoặc gọi ngân hàng thật.

```powershell
dotnet run --project tests/G4.Commerce.Tests -- --payout-workflow
$env:G4_RETURN_PAYOUT_SQL_CONNECTION='Server=localhost;Database=G4_ReturnWindowPayout_Test_20261020;Integrated Security=True;TrustServerCertificate=True'
dotnet run --project tests/G4.Commerce.Tests -- --return-payout-sql
node --test tests/*.test.cjs
node tests/return-payout-ui.e2e.cjs
```

SQL chỉ nhận đúng tên database QA; browser test cần Playwright/Chrome (NODE_PATH/CHROME_PATH nếu dùng runtime ngoài project). LED1 bảo vệ/đối soát toàn bộ FinancialTransaction vẫn là task riêng.
