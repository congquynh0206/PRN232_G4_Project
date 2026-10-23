# Cấp seller tự động và đánh giá đơn mua

**Căn cứ mới 18/10/2026:** [đặc tả §10](BUSINESS_REQUIREMENTS.md#10-seller-level-và-hạn-mức) và [LIM1](IMPLEMENTATION_BACKLOG.md#9-lim1--admin-hạn-mức-riêng-seller). Giữ tự xét cấp và các ngưỡng hiện có; bổ sung admin override hạn mức riêng seller có lịch sử/hiệu lực. Quyền mới không cho nhập level tùy ý, không reset hold/fee settlement cũ và không tự xóa override khi đổi cấp. UI cấu hình override chưa có chỉ từ tài liệu này.

Seller và admin có tab **Cấp seller**. Hệ thống tự xét theo dữ liệu đơn; seller có thể yêu cầu xét lại, admin chỉ xem tiêu chí, kết quả và lịch sử. Worker kiểm tra mỗi 5 giây và chỉ xét seller đến hạn: mặc định mỗi **3.600 giây**, Development mỗi **60 giây (1 phút)**, tối đa 50 seller mỗi lượt. Seller mới sạch giữ cấp 1, Active và không bị khóa chỉ vì chưa có dữ liệu.

## Buyer đánh giá seller

Giao diện 23/10/2026 hiển thị đếm ngược tới NextEvaluationAt trên chi tiết cấp seller của Seller/Admin; tới hạn ghi đang chờ hệ thống đánh giá, chỉ dữ liệu mới từ server mới thay level/kết quả. Không đếm lại từ đầu khi tải trang và không đặt deadline giả nếu chưa có lượt xét đầu tiên. [Hướng dẫn chung](DEMO_COUNTDOWNS.md).

Trong chi tiết đơn trên trang Buyer, nút **Đánh giá seller** xuất hiện khi đơn đã thanh toán thành công và đang Delivered hoặc Closed. Chỉ buyer của đơn được gửi; cấm tự đánh giá. Chọn 1–5 sao, nhận xét không bắt buộc, tối đa 2.000 ký tự. Mỗi đơn có một đánh giá bất biến; 4–5 sao là tích cực, 1–3 sao không tích cực. Không tính đơn chưa được đánh giá thành phản hồi xấu.

`SellerOrderFeedback` là dữ liệu đánh giá đơn thực tế. Không dùng số tổng hợp `Feedback.PositiveRate` hoặc review sản phẩm cũ để tự nâng cấp seller. API nhận rating/comment/key; seller/buyer/order được xác định từ tài khoản và đơn trong DB. Retry cùng key và nội dung đã chuẩn hóa trả bản ghi cũ; đổi key hoặc nội dung sau khi đã đánh giá trả 409. Khóa theo buyer, khóa đơn và hai unique index bảo vệ gửi đồng thời. Giao diện giữ nguyên payload/key sau lỗi mạng chưa rõ kết quả.

## Tiêu chí mặc định có cấu hình

Các mức dưới đây là giá trị mặc định cho bộ tiêu chí mở rộng, có thể thay ở `Finance:LevelPromotion`. Không phải cam kết chính sách của eBay.

| Tiêu chí | Cấp 2 | Cấp 3 / Instant |
| --- | ---: | ---: |
| Đơn giao thành công cộng dồn | ≥20 | ≥100 |
| Phản hồi tích cực | ≥90% | ≥97% |
| Giao đúng hạn | ≥90% | ≥95% |
| Ngày hoạt động | ≥30 | ≥90 |
| Tỷ lệ hủy đơn đã thanh toán | ≤5% | ≤2% |
| Tỷ lệ trả hàng được chấp nhận | ≤10% | ≤5% |
| Tỷ lệ đơn có tranh chấp | ≤2% | ≤1% |
| Nợ âm / hạn chế | Không có | Không có |

- Cửa sổ mặc định `WindowDays=90`; mẫu tối thiểu `MinimumPaidOrders=20`. Cohort là các order riêng biệt có payment Succeeded, dùng PaidAt (fallback CreatedAt) trong cửa sổ UTC bao gồm hai biên. Nhiều payment không nhân đôi mẫu.
- Dưới mẫu tối thiểu: giữ cấp hiện tại. Nợ âm hoặc hạn chế tài khoản vẫn đưa về cấp 1, không phụ thuộc số mẫu.
- Số đơn hoàn tất tính toàn bộ lịch sử đã thanh toán: trạng thái Delivered/Closed hoặc có bằng chứng DeliveredAt chiều đi. Đơn đã giao rồi phát sinh return không mất lần giao thành công; tỷ lệ trả hàng phản ánh riêng sự kiện đó.
- Phản hồi và đúng hạn chỉ dùng các đơn trong cohort có dữ liệu tương ứng. Đúng hạn so sánh `DeliveredAt <= EstimatedArrival` của vận đơn Outbound cuối cùng; không suy ra từ số lần giao. Chiều Return và đơn thiếu hạn/thời điểm giao bị loại khỏi mẫu đúng hạn.
- Thiếu phản hồi hoặc dữ liệu hạn giao hiện **Chưa có dữ liệu**. Không đủ bằng chứng để lên cấp; cũng không dùng dữ liệu thiếu làm lý do hạ cấp hiện tại khi các tiêu chí khác đều đạt.
- Hủy: order Cancelled hoặc có refund Succeeded lý do `cancel`. Trả: yêu cầu được chấp nhận ở Approved, ReturnShipping, ReceivedBySeller, RefundPending/RefundFailed/Refunded; Requested/Rejected không tính. Tranh chấp: có Dispute WorkflowEnabled, kể cả đã đóng. Mỗi loại chỉ đếm một lần trên mỗi order.
- Tuổi hoạt động lấy mốc sớm nhất có bằng chứng: đăng ký seller, payment thành công đầu tiên, hoặc mốc hệ thống bắt đầu theo dõi. Seller cũ không có cả ba bắt đầu theo dõi ở lần xét đầu, không đoán tuổi tài khoản từ ID hay UpdatedAt.
- Xét bằng tỷ lệ chưa làm tròn, chỉ làm tròn để hiển thị. Có thể lên thẳng cấp 3 nếu đủ toàn bộ tiêu chí. Hạ cấp do hiệu quả bán hàng không ban tài khoản.

## Tài chính, đồng thời và lịch sử

Cấp 1/2/3 mặc định có hạn mức tháng 5.000/10.000/1.000.000 USD và giữ tiền 21/7/0 ngày; Development 300/180/0 giây (5 phút/3 phút/tức thời), cập nhật ngày 23/10/2026. Đổi cấp chỉ cập nhật chính sách tài khoản dùng cho settlement **tạo mới**. Không đổi LevelSnapshot, HoldSecondsSnapshot, ReleaseAt, phí, số dư, doanh số đã ghi nhận hoặc payout của giao dịch cũ. Nếu hạ cấp làm doanh số đã dùng vượt hạn mức mới, seller phải chờ tháng mới hoặc đủ điều kiện nâng cấp trước khi nhận thêm doanh số.

Worker và thao tác seller cùng khóa tài chính với thanh toán, đọc lại cấp/nợ/hạn chế trước khi lưu. Worker kiểm tra lại thời điểm xét dưới khóa nên hai worker không ghi hai lần cho cùng lượt đến hạn. Đọc dữ liệu hiệu suất trước khi lấy khóa tài chính để tránh đảo thứ tự khóa với payment. Dữ liệu nhiều truy vấn có thể thay đổi khi có đơn/feedback đồng thời; lần xét kế tiếp cập nhật các sự kiện mới. Nợ/hạn chế quyết định tại lúc ghi luôn được đọc lại dưới khóa.

Mỗi lần xét lưu `SellerLevelEvaluation`: nguồn Worker/SellerRequest, cấp trước/sau, thời điểm, lý do, toàn bộ metrics và chính sách tiêu chí. EF chặn sửa/xóa đánh giá và lịch sử đã lưu. Giao diện hiển thị 20 lần gần nhất, mỗi lần mở ra xem số liệu/chính sách lúc xét. Preview hiện tại tách khỏi kết quả đã lưu. Thao tác xét lại không thay đổi quyết định khóa nợ/khóa vĩnh viễn.

## Màn test Development

Mở tab **Cấp seller → Thử các tình huống xét cấp** trên Seller hoặc Admin. Có sáu mẫu: đủ cấp 2, đủ cấp 3, tỷ lệ hủy cao bị hạ cấp, thiếu mẫu giữ cấp, đang nợ về cấp 1, thiếu phản hồi/hạn giao. Có thể sửa từng số liệu và chạy lại. Đây là mô phỏng dùng **cùng `SellerLevelRules` và cấu hình của worker**, không tạo đơn, không sửa cấp và không thay đổi ví. Route không khả dụng ngoài Development; buyer/shipper không được gọi.

## API

- `POST /api/orders/{id}/seller-feedback`: buyer, body `{ rating, comment, key }`.
- `GET /api/seller/level`: seller xem preview và lịch sử của mình.
- `POST /api/seller/finance/evaluate-level`: seller yêu cầu xét thật.
- `GET /api/admin/seller-levels?page=1&pageSize=10&sellerId=&level=`: danh sách, chính sách và bộ lọc admin.
- `GET /api/admin/seller-levels/{sellerId}`: admin xem preview/lịch sử, không có endpoint gán cấp tùy ý.
- `POST /api/dev/seller-levels/simulate`: seller/admin Development, body `{ currentLevel, metrics }`, không ghi DB.

Các route ứng dụng tiếp tục theo giới hạn môi trường Development hiện có của dự án.

## Migration và chạy kiểm thử

Migration `20261015102627_AddSellerLevelsAndOrderFeedback` chỉ thêm hai cột nullable vào SellerAccount và hai bảng, FK, index/check constraint; giữ dữ liệu, số dư và settlement cũ. SQL có thể duyệt tại [seller-levels-feedback-migration.sql](../database/seller-levels-feedback-migration.sql). Dừng API trước khi cập nhật schema, rồi khởi động lại API/frontend:

```powershell
dotnet ef database update --project backend/G4.Infrastructure --startup-project backend/G4.Api
dotnet run --project tests/G4.Commerce.Tests -- --levels
node --test tests/*.test.cjs
```

Kiểm thử SQL từ chối mọi DB ngoài tên QA riêng, không chạy trên CloneEbayDB:

```powershell
$env:G4_LEVEL_SQL_TEST_CONNECTION='Server=localhost;Database=G4_SellerLevels_Test_20261015;Integrated Security=True;TrustServerCertificate=True'
dotnet run --project tests/G4.Commerce.Tests -- --levels-sql
```

SQL test tạo baseline nếu thiếu, kiểm tra migration giữ tiền/trạng thái cũ, retry đánh giá đồng thời, worker trùng lượt và race worker/payment. Test sinh dữ liệu giả lập mới mỗi lần, không xóa dữ liệu có sẵn. Fixture ghi trong `artifacts/seller-levels-qa/fixture.json` chỉ chứa mã/email test; mật khẩu test là `LevelDemo123!`.

E2E: chạy API QA ở `http://127.0.0.1:5254`, kết nối đúng database QA trên; frontend Development ở `http://127.0.0.1:5134` trỏ API QA. Dùng `Mail__Mode=Capture`, tắt write-off trong QA bằng `Finance__WriteOff__Enabled=false`. Cần Playwright với Chromium/Edge:

```powershell
$env:G4_LEVEL_E2E_URL='http://127.0.0.1:5134'
node tests/seller-levels.e2e.cjs
```

E2E kiểm tra buyer đánh giá một lần/nội dung an toàn, seller xét thật/lịch sử, sáu mẫu mô phỏng, admin chỉ xem, phân quyền và mobile không tràn ngang. Ảnh kiểm tra lưu tại `artifacts/seller-levels-qa/`.
