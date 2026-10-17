# Feedback sản phẩm và phản hồi seller

## Kế hoạch S2

- [x] Thêm nguồn đánh giá sản phẩm theo đơn đã giao và phản hồi seller, migration nâng cấp giữ Review cũ.
- [x] API seller tổng hợp theo sản phẩm, lọc, phân trang, trả lời đúng quyền và chống gửi trùng.
- [x] Giao diện seller tổng hợp, xem đánh giá và trả lời; buyer đánh giá sản phẩm đã mua và xem phản hồi.
- [x] Kiểm thử nghiệp vụ, SQL/migration/đồng thời, UI và demo desktop/mobile.

Đánh giá sản phẩm dùng Review; không chuyển SellerOrderFeedback thành đánh giá sản phẩm và không thay cách xét cấp seller. Mỗi sản phẩm trong một đơn đủ điều kiện có một đánh giá. Seller có một phản hồi công khai, bất biến cho mỗi đánh giá, retry cùng nội dung/key trả kết quả cũ. Buyer xem phản hồi trong phần đánh giá sản phẩm của đơn. Không thêm quản lý sản phẩm hoặc chat.

Danh sách seller chỉ nhóm các sản phẩm có đánh giá hợp lệ (1–5 sao), có bộ lọc tên/mã sản phẩm, số sao, đã/chưa trả lời, khoảng ngày UTC và phân trang server. Các tổng hợp áp dụng cùng bộ lọc trên toàn bộ tập kết quả. Review cũ được giữ nguyên, không tự gắn đơn hoặc nhãn đã mua hàng.

## Cách sử dụng

1. Buyer mở chi tiết đơn đã thanh toán, chọn **Đánh giá sản phẩm & phản hồi**. Sau khi đơn Delivered/Closed, mỗi sản phẩm có form 1–5 sao và nhận xét tối đa 2.000 ký tự. Một order/product chỉ được đánh giá một lần; không phải mỗi lần tải lại một đánh giá.
2. Seller mở tab **Đánh giá sản phẩm**. Bốn thẻ hiển thị số sản phẩm có đánh giá, tổng đánh giá, điểm trung bình và số chưa trả lời trong phạm vi bộ lọc. Chọn sản phẩm bên trái để đọc đánh giá bên phải.
3. Lọc theo tên/mã sản phẩm, số sao, đã/chưa trả lời hoặc ngày gửi. Khoảng ngày người dùng chọn là ngày địa phương; frontend gửi UTC, đến ngày dùng đầu ngày kế tiếp làm cận trên loại trừ. Sản phẩm và đánh giá có phân trang riêng.
4. Seller nhập câu trả lời tại đánh giá chưa có phản hồi. Phản hồi tối đa 2.000 ký tự, một lần, không sửa/xóa sau gửi. Chưa rõ kết quả mạng thì giữ nguyên nội dung/key để thử lại; server không tạo hai câu trả lời.
5. Buyer mở lại mục đánh giá sản phẩm hoặc bấm **Làm mới phản hồi** để xem câu trả lời. Không hiển thị nội dung đánh giá như HTML.

Realtime dùng topic `product-feedback` và các audience hiện có; dữ liệu nội dung vẫn chỉ được đọc qua API kiểm tra quyền. Không ghi dữ liệu vào seller level hoặc ledger. Bản nháp seller không bị xóa bởi cập nhật nền. Dialog buyer có nút làm mới để xem phản hồi khi đang mở, theo quy tắc hoãn cập nhật lúc mở dialog hiện tại.

## API

Các API tiếp tục giới hạn Development theo dự án hiện tại.

| API | Quyền và mục đích |
| --- | --- |
| `GET /api/seller/product-feedback/products` | Seller: summary toàn bộ kết quả và danh sách sản phẩm của mình. |
| `GET /api/seller/product-feedback/products/{id}/reviews` | Seller: đọc đánh giá của đúng sản phẩm thuộc mình. |
| `POST /api/seller/product-feedback/reviews/{id}/reply` | Seller: `{ comment, key }`, ownership và idempotency phía server. |
| `GET /api/orders/{id}/product-reviews` | Buyer chủ đơn: các sản phẩm đã mua, đánh giá của mình và câu trả lời của shop. |
| `POST /api/orders/{id}/products/{productId}/review` | Buyer chủ đơn: `{ rating, comment, key }`, kiểm tra đã trả tiền, trạng thái giao và sản phẩm thuộc đơn. |

GET seller hỗ trợ `search`, `rating`, `reply=all|answered|unanswered`, `from`, `to`, `page`, `pageSize` (tối đa 50). `from` bao gồm, `to` loại trừ. Mỗi nhóm sản phẩm tính số đánh giá, điểm trung bình và số chưa trả lời trên cùng bộ lọc, không chỉ trang hiện tại. Review legacy thiếu order không có nhãn Đã mua hàng.

## Migration

Migration mới: **20261017042643_AddProductFeedbackReplies**, kế tiếp **20261015161703_AddPayPalCartAndRealtime**. SQL để duyệt: [product-feedback-migration.sql](../database/product-feedback-migration.sql).

Thêm hai cột nullable `Review.OrderId`/`RequestKey` và bảng `ProductReviewReply`. Giữ nguyên Review cũ, đơn, tiền và đánh giá seller theo đơn. Unique index bảo vệ một review/order/product, một reply/review và các key theo actor; SQL app lock bảo vệ retry đồng thời. Migration có kiểm tra tồn tại trước khi thay index Review vì database tạo từ baseline có thể chưa có index EF ghi trong snapshot.

Chỉ áp dụng tự động trong đợt này lên **G4_ProductFeedback_Test_20261017**. Database chính chưa được cập nhật. Khi chuẩn bị nâng database đang dùng, dừng ứng dụng cũ, kiểm tra đúng connection và chạy:

```powershell
dotnet ef database update --project backend/G4.Infrastructure --startup-project backend/G4.Api
```

Khởi động lại cả API/frontend sau build và migration; không chạy lại baseline trên database có dữ liệu.

## Kiểm thử đã thực hiện

- Build solution Release: đạt, 0 lỗi/cảnh báo.
- Bộ console commerce đầy đủ: đạt, bao gồm regression return/dispute/finance/PayPal và SMTP loopback.
- Bộ UI Node: đạt, gồm sáu test mới về XSS, retry cùng payload/key, validation, lọc ngày, request trả về sai thứ tự và buyer xem phản hồi.
- SQL QA: giữ Review cũ sau migration; LINQ group/filter/paging chạy thực tế; hai submit/reply đồng thời chỉ lưu một bản; unique constraint chặn duplicate; rollback không lưu reply; kiểm tra ownership.
- Browser E2E: buyer đánh giá sản phẩm thực trong đơn QA; seller lọc 5 sao/chưa trả lời; abort HTTP sau khi server lưu rồi retry; buyer thấy phản hồi; role/owner không hợp lệ bị từ chối; draft được giữ khi push; desktop 1440px và mobile 390px không overflow/JS error. Ảnh QA trong `artifacts/product-feedback-qa/`.

Lệnh chạy lại từ gốc repo:

```powershell
dotnet build G4_Project.sln -c Release --no-restore -m:1 -p:UseSharedCompilation=false -p:UseAppHost=false
dotnet tests/G4.Commerce.Tests/bin/Release/net8.0/G4.Commerce.Tests.dll --product-feedback
node --test tests/product-feedback-ui.test.cjs

$env:G4_PRODUCT_FEEDBACK_SQL_CONNECTION='Server=localhost;Database=G4_ProductFeedback_Test_20261017;Integrated Security=True;TrustServerCertificate=True'
dotnet tests/G4.Commerce.Tests/bin/Release/net8.0/G4.Commerce.Tests.dll --product-feedback-sql
```

SQL QA chỉ chấp nhận đúng tên database trên, không reset dữ liệu đã có; mỗi lần chạy tạo fixture riêng và ghi thông tin tài khoản tổng hợp giả vào `artifacts/product-feedback-qa/fixture.json`. Mật khẩu riêng QA trong script: `FeedbackDemo123!`.

Browser QA cần Playwright và hai ứng dụng QA chạy riêng: API 5257, frontend 5137, API dùng database QA trên; frontend `ApiSettings__BaseUrl`/`Realtime__PublicApiBaseUrl=http://127.0.0.1:5257`; API `Realtime__AllowedOrigins__0=http://127.0.0.1:5137`, `Mail__Mode=Capture`, `Finance__WriteOff__Enabled=false`. Sau khi tạo fixture SQL, chạy:

```powershell
$env:G4_PRODUCT_FEEDBACK_E2E_URL='http://127.0.0.1:5137'
node tests/product-feedback.e2e.cjs
```

Script E2E chỉ chấp nhận frontend QA trên. Chạy SQL fixture lại trước mỗi lần E2E để có sản phẩm chưa được buyer QA đánh giá; không xóa đánh giá bất biến đã dùng lần trước.
