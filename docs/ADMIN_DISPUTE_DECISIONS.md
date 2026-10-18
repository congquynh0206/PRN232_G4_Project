# Phán quyết admin theo trách nhiệm và khoản hoàn

**Căn cứ mới 18/10/2026:** [đặc tả §8](BUSINESS_REQUIREMENTS.md#8-tranh-chấp), [task A2](IMPLEMENTATION_BACKLOG.md#11-a2--admin-yêu-cầu-trả-trước-kết-luận) và FR1. Ba phán quyết cuối bên dưới đã có; câu “không tạo vận đơn mới” chỉ nói phiên bản hiện tại. A2 phải bổ sung nhánh yêu cầu trả trước kết luận, dùng lại/tạo return hợp lệ, giữ hold và chặn auto-refund thường. Nhánh này chưa phải phán quyết cuối; giá phải tôn trọng policy free returns snapshot mới khi triển khai.

Admin chỉ quyết định hồ sơ `Escalated` đã chuyển lên, đang mở và chưa có phán quyết. Không tùy chọn ai chịu phí một cách độc lập: admin kết luận trách nhiệm Seller/Buyer từ hồ sơ, chọn căn cứ trong lịch sử và bắt buộc ghi nhận định. Hệ thống tự tính phần phí và khoản hoàn tương ứng.

## Giao diện và kết quả

Trong chi tiết tranh chấp, admin xem Trao đổi/bằng chứng và Đơn hàng/payment/tracking hai chiều/báo giá trả ban đầu, rồi chọn **Xem xét và đưa ra phán quyết**. Form có ba kết quả:

- **SellerWins:** không hoàn thêm; trách nhiệm phải là Buyer. Giải phóng hold theo lịch gốc, không thu thêm phí từ buyer. Chi phí nhãn đã ghi trước đó không bị xóa hoặc ghi thêm.
- **FullRefund:** hoàn toàn bộ khoản đủ điều kiện theo kết luận trách nhiệm; không có trường tiền tự nhập.
- **PartialRefund:** admin nhập khoản buyer thực nhận sau phí trả hàng, lớn hơn 0 và nhỏ hơn mức tối đa, tối đa hai chữ số thập phân.

Phải chọn ít nhất một căn cứ thuộc chính hồ sơ và nhập lý do, sau đó xem dự kiến tài chính trước khi xác nhận. Thay kết luận/khoản hoàn thì dự kiến cũ mất hiệu lực. Form khóa trong khi lưu, giữ key khi retry phản hồi không chắc chắn. Bằng chứng thêm vào hoặc tiền/trạng thái thay đổi sau preview khiến server từ chối báo giá cũ.

Phán quyết này không tạo vận đơn mới. Khi đã có vận đơn trả, refund chỉ được quyết định sau khi hàng trả Delivered về seller; nếu chưa có vận đơn, admin có thể giải quyết bằng hoàn tiền và buyer giữ hàng. Chỉ định bắt buộc trả hàng mới từ admin vẫn là phần mở rộng sau.

## Tính tiền

Gọi `P` là payment đã thu, `S` là ship buyer đã trả, `A` là ship đã hoàn chênh lệch, `R` là chi phí chiều trả đã được ghi và chưa được bù, `U` là tài trợ nền tảng chưa đảo:

| Trách nhiệm | Mức cash tối đa | Phí chiều trả |
| --- | --- | --- |
| Seller | `P - A` | Seller chịu R, không trừ vào cash buyer |
| Buyer | `P - S - R` | R được trừ một lần khỏi khoản hoàn |

Không có nhãn/chi phí đã ghi thì `R = 0`; không đoán phí hoặc tạo thêm phí. Nếu R vượt tiền hàng được hoàn, kết luận Buyer bị chặn. FullRefund bằng 0 hợp lệ khi đủ điều kiện, chỉ ghi nội bộ; không gọi Card/PayPal. Đơn đã có refund khác hoặc shipping adjustment chưa hoàn tất phải đối soát trước, không tự tạo phán quyết chồng lên refund đang chạy.

Với full refund, đảo toàn bộ U còn lại. Với partial refund, tài trợ được đảo theo phần giá trị hoàn **trước** deduction R: Seller dùng `cash / (P - A)`, Buyer dùng `(cash + R) / (P - S)`. Phần cash và funding được lưu riêng, không cộng coupon vào tiền buyer nhận và không khôi phục lượt dùng coupon. Buyer-funded R được bù toàn bộ trong phán quyết refund, không thu thêm R trên số dư seller lần nữa.

`SellerGrossRefund = cash + subsidy reversal + buyer postage recovery`. Fee credit dùng tỷ lệ Gross bị đảo trên Gross gốc, có xét phần đã đảo/credit trước đó, dùng fee snapshot của settlement. Seller debit thêm lúc refund thành công là `GrossRefund - FeeCredit - PostageRecovery`; nếu âm thì credit bù Negative trước. Chi phí R đã được ứng khi nhãn thành công là khoản riêng, không thu lại trong bước refund.

Ví dụ payment 95, hàng buyer đã trả 90, coupon nền tảng 10, Gross 105, phí gốc 12,90 và nhãn trả đã ứng 5 USD. Admin kết luận Buyer và hoàn cash 40: thu hồi tài trợ 5, bù postage 5, Gross đảo 50, fee credit 6,14, debit thêm seller 38,86. Từ Net 92,10 đã ứng nhãn 5, seller còn 48,24 USD. FullRefund theo cùng kết luận trả cash 85 và seller còn 4,39. Nếu kết luận Seller và full cash 95, seller chịu chi phí R 5 (có thể thành Negative).

## Quyết định, refund và hold

`DisputeDecision` là snapshot chỉ thêm mới: admin/ngày/kết quả/trách nhiệm/căn cứ/lý do/key/fingerprint và các khoản tiền. Một hồ sơ có một quyết định, key duy nhất; replay đúng payload trả về quyết định cũ, đổi payload/admin bị từ chối. DbContext chặn sửa/xóa quyết định. ReturnRequest và báo giá buyer đã xác nhận giữ nguyên; refund liên kết quyết định qua `DisputeDecisionId`.

Quyết định được commit trước gọi gateway. Hoàn tiền lỗi giữ `ExecutingAgreement`/`RefundPending`, giữ tiền và worker retry đúng cash/key. Chỉ khi Succeeded mới hiện số thực nhận và đóng hồ sơ. Phần hold còn lại được giải phóng về Processing hoặc Available theo lịch settlement gốc; Instant vào ví, không tạo payout. Thiếu tiền tạo Negative, hold của order khác không bị sử dụng. Lịch sử và thông báo buyer/seller chứa nhận định và khoản hoàn; API chi tiết của các bên có cả phán quyết và trạng thái refund thực tế.

Hồ sơ/quyết định/refund trước migration giữ nguyên, không tạo phán quyết giá mới hồi tố. Hàm `ResolveAsync(bool)` còn phục vụ xử lý tương thích nội bộ và regression của dữ liệu cũ; HTTP endpoint quyết định chỉ nhận body mới bên dưới, không nhận `buyerWins`/`releaseToSeller` để bỏ qua preview/căn cứ.

## API, migration và kiểm thử

- `POST /api/disputes/{id}/decision-preview`: admin, `{ outcome, responsibility, refundAmount }`.
- `POST /api/disputes/{id}/resolution`: admin, các trường trên cộng `reason`, `evidenceEntryIds`, `key`, `pricingFingerprint`.
- `outcome`: SellerWins/FullRefund/PartialRefund; `responsibility`: Seller/Buyer; `refundAmount` null trừ PartialRefund.
- Endpoint tương thích `/api/orders/{id}/fund-hold/resolve` dùng cùng body mới và cùng kiểm tra. Chi tiết tranh chấp trả thêm `decision`; chi tiết order thêm `refund.DisputeDecisionId`.

Migration `20261014091619_AddAdminDisputeDecisions` chỉ tạo bảng/cột/index/FK. SQL: [admin-dispute-decisions-migration.sql](../database/admin-dispute-decisions-migration.sql). Dừng API, backup database, chạy migration rồi khởi động lại API/frontend.

```powershell
dotnet ef database update --project backend/G4.Infrastructure --startup-project backend/G4.Api
dotnet build G4_Project.sln -c Release --no-restore -m:1 -p:UseSharedCompilation=false -p:UseAppHost=false
dotnet tests/G4.Commerce.Tests/bin/Release/net8.0/G4.Commerce.Tests.dll
node --test tests/*.test.cjs
```

Đặt `G4_DECISION_SQL_TEST_CONNECTION` trỏ đúng **G4_AdminDecision_Test_20261014** và chạy DLL với `--decision-sql`. Test guard database QA, bootstrap baseline/migrations/seed chỉ tại đó; kiểm tra submit cùng key/worker đồng thời, hai hồ sơ cùng seller, tiền/fee/postage không trùng và báo giá gốc giữ nguyên.

Ngày 14/10/2026, SQL QA và API QA đã xác nhận refund thẻ mô phỏng 40 USD với báo giá gốc 95 USD, replay không tạo refund mới, buyer/seller thấy cùng quyết định và role buyer/seller/shipper bị 403 khi preview/lưu quyết định với payload hợp lệ. Email QA dùng Capture, không gọi PayPal Sandbox. Kiểm tra trình duyệt desktop/mobile chưa chạy được trong đợt này vì công cụ browser lỗi khởi tạo; kiểm tra JavaScript bao gồm preview, stale response, escaping, validation và key retry.

Đã áp dụng migration trên database local `CloneEbayDB` ngày 14/10/2026 sau backup COPY_ONLY/CHECKSUM và VERIFYONLY tại `artifacts/admin-decisions/CloneEbayDB-before-admin-decisions-20261014.bak`. Đối chiếu trước/sau: số return/refund/dispute/history, tổng tiền refund, sáu settlement (Gross 1.322,50 USD, phí 160,50 USD, Net 1.162,00 USD) và toàn bộ số dư ví giữ nguyên. Không tạo quyết định hồi tố. Database khác cần chạy migration; khởi động lại API/frontend để dùng code mới.
