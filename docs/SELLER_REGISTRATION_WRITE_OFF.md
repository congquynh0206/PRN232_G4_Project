# Đăng ký seller, blacklist và write-off tự động

Đây là luồng mô phỏng trong Development, không phải dịch vụ KYC hoặc xác minh ngân hàng thật. Trang `/Account/RegisterSeller` được mở từ liên kết **Đăng ký người bán thử nghiệm** ở trang đăng nhập.

## Đăng ký và xác minh

Luồng: nhập tài khoản/shop/định danh → chuẩn hóa → xác minh định dạng giả lập → kiểm tra blacklist và quyền sở hữu định danh → tạo tài khoản.

- CCCD: 12 chữ số ASCII; giữ nguyên số 0 đầu, bỏ khoảng trắng.
- Passport: 1–3 chữ cái và 6–9 chữ số; viết hoa, bỏ khoảng trắng.
- Ngân hàng: mã 2–12 chữ/số và tài khoản 6–30 chữ số. Mã ngân hàng tham gia đối chiếu để hai ngân hàng có cùng số tài khoản không bị nhầm.
- Email chuẩn hóa chữ thường, duy nhất. Mật khẩu 8–128 ký tự, gồm chữ và số; lưu PBKDF2 SHA-256, salt ngẫu nhiên, 210.000 vòng.
- Định danh hợp lệ, chưa thuộc tài khoản khác và không nằm trong blacklist: tạo đồng thời `User` role seller, `Store`, `SellerIdentity`, `SellerRegistration` và `SellerAccount` cấp 1, trạng thái `Active`, số dư 0. Không có bước chờ admin duyệt.
- Định danh không hợp lệ: 400, không tạo tài khoản. Trùng blacklist hoặc đã thuộc seller khác: 403 với thông báo chung, không tiết lộ định danh nào trùng. Tài khoản mới hợp lệ không bị ban vì mới đăng ký.
- Cùng key và nội dung nhận lại kết quả đăng ký gốc; cùng key nhưng thay nội dung bị từ chối. Giao diện giữ payload trong bộ nhớ để thử lại lỗi mạng; không lưu mật khẩu vào session/local storage. Đăng ký thành công xóa trường mật khẩu và hướng dẫn đăng nhập.

Ba nút điền mẫu: seller mới hợp lệ, định danh không hợp lệ, trùng blacklist. Giá trị `999000000002` được dành riêng cho blacklist giả lập Development, có nhãn `IsDemo`; không gán cho seller và không phải kết quả xử phạt tài khoản mới. Các mẫu khác được tạo ngẫu nhiên. Không nhập CCCD/Passport/tài khoản ngân hàng thật.

Seller có tab **Định danh** để bổ sung/cập nhật hồ sơ. Thông tin mới phải xác minh và kiểm tra blacklist lại, có kiểm tra version và key chống gửi trùng. Lịch sử đã xác minh bất biến; chỉ con trỏ định danh hiện tại thay đổi. Seller cũ chưa có tài khoản tài chính được khởi tạo khi cập nhật. Seller bị khóa bán vĩnh viễn không được đổi định danh để vượt khóa.

Payout mới hiển thị ngân hàng đã xác minh gần nhất và bốn số cuối của tài khoản nếu seller đã khai báo loại định danh ngân hàng; payout cũ giữ snapshot điểm nhận cũ. Seller chỉ khai báo CCCD/Passport tiếp tục dùng điểm nhận ngân hàng giả lập mặc định.

## HMAC và quyền truy cập

Chỉ lưu loại, phần đã che, HMAC và mã khóa; không lưu giá trị định danh thô. HMAC dùng SHA-256 và khóa tối thiểu 32 byte. Đối chiếu cả định danh hiện tại và lịch sử đã xác minh; một định danh không được gán cho hai seller.

Khóa có thể cung cấp qua `SellerIdentity__HmacKey` (Base64) và `SellerIdentity__KeyId` hoặc User Secrets. Nếu Development chưa cấu hình, API tự sinh khóa ngẫu nhiên và giữ trong `backend/G4.Api/.local-security/seller-identity.key`, nằm ngoài database và bị Git ignore. Giữ nguyên khóa này khi khởi động lại hoặc chuyển database sang máy khác. Database chỉ lưu dấu kiểm tra khóa; đổi/mất khóa không âm thầm bỏ qua blacklist mà trả lỗi yêu cầu khôi phục đúng khóa. Chưa hỗ trợ tự động xoay khóa.

API đăng ký/mẫu chỉ mở trong Development. Hồ sơ seller chỉ trả dữ liệu đã che cho chính seller và admin; API blacklist và báo cáo write-off chỉ cho admin, không có thao tác sửa/xóa trực tiếp.

## Worker tự động và tiêu chí

`CommerceMaintenanceWorker` chạy mỗi 5 giây. Sau đối soát payment/refund, giải phóng tiền và xử lý payout, worker đánh giá write-off. Admin không cần bấm quyết định.

| Cấu hình | Mặc định | Development |
| --- | ---: | ---: |
| `Finance:Debt:DueSeconds` | 7 ngày | 60 giây |
| `Finance:WriteOff:AfterSeconds` | 30 ngày | 300 giây |
| `Finance:WriteOff:InactivitySeconds` | 7 ngày | 60 giây |
| `Finance:WriteOff:Enabled` | true | true |

Điều kiện đồng thời: số nợ còn dương, kỳ nợ đã đủ tuổi write-off, đã đến hạn trả, đủ thời gian không có khoản thu hồi và chưa bị khóa bán vĩnh viễn. `AfterSeconds` phải lớn hơn hạn nợ; `0 < InactivitySeconds <= AfterSeconds`.

Ngày đủ tuổi write-off và thời gian không thu hồi được chốt cho từng kỳ. Trả một phần không đổi hạn trả hoặc tuổi kỳ nợ, nhưng cập nhật mốc thu hồi gần nhất nên có thể trì hoãn write-off. Khoản bù từ doanh thu, tiền được giải phóng hoặc payout bị trả về cũng tính là thu hồi, không chỉ trả ngân hàng. Kỳ cũ khi nâng cấp được khởi tạo đầy đủ thời gian ân hạn không thu hồi tại lần quan sát đầu tiên, không đổi ngày bắt đầu/hạn trả hoặc các số dư cũ.

Tab tài chính seller hiển thị mốc sớm nhất có thể bị khóa bán vĩnh viễn nếu không có khoản thu hồi mới. Mốc này là giá trị lớn nhất của ngày đủ tuổi nợ, hạn trả và thời điểm thu hồi gần nhất cộng thời gian không thu hồi; có thu hồi mới thì tính lại. Khi tắt xử lý tự động hoặc đã khóa vĩnh viễn, không hiển thị dự báo này.

Ngay trước quyết định, worker khóa tài chính seller và đọc lại nợ. Trả hết trước khi worker lấy khóa thì không có quyết định. Worker lấy khóa trước và đã đủ tiêu chí thì quyết định có hiệu lực; khoản trả sau đó được ghi nhận thu hồi. Nhiều worker chỉ tạo một quyết định cho một kỳ.

## Quyết định và thu hồi sau write-off

Trong cùng transaction: ghi quyết định bất biến (số nợ gốc, kỳ, thời điểm, mốc thu hồi, chính sách, lý do, actor `BackgroundWorker`), đặt `PermanentSalesBlockedAt`, đưa toàn bộ định danh đã xác minh của seller vào blacklist, ghi bút toán audit 0 USD và đưa thông báo vào outbox.

Write-off không xóa/giảm nợ hoặc làm thay đổi tiền trong ví. Seller cũ không có định danh vẫn bị phân loại/khóa khi đủ tiêu chí; quyết định ghi rõ số định danh đưa blacklist bằng 0. Không tạo định danh thay thế.

Khóa vĩnh viễn độc lập với `Status` và `DebtSuspended`, giữ các quyết định khác. Chặn đăng bán, nhận đơn mới và tạo payout mới; vẫn cho đăng nhập, trả nợ và xử lý đơn cũ. Đơn cũ còn hạn thanh toán được tiếp tục. Trả hết sau write-off không phát thông báo khôi phục bán hàng và không gỡ khóa vĩnh viễn.

Khoản giảm nợ sau quyết định tạo `SellerWriteOffRecovery`, được giới hạn ở số nợ gốc chưa thu hồi. Phân bổ vào khoản nợ cũ trước; chi phí phát sinh thêm không tăng ngược số nợ gốc trong quyết định. Báo cáo tách **nợ lúc write-off**, **đã thu hồi**, **còn trong khoản write-off** và **nợ hiện tại seller** để không nhầm chi phí mới với khoản đã phân loại. Retry không ghi thu hồi hai lần.

## API và màn hình

- Public Development: `POST /api/auth/register-seller`, `GET /api/auth/seller-registration-samples`.
- Seller: `GET/POST /api/seller/identity`; POST nhận `{ identity: { kind, value, bankCode }, expectedVersion, key }`.
- Admin: `GET /api/admin/seller-identities/{sellerId}`, `GET /api/admin/seller-blacklist?page=1&pageSize=10&sellerId=...`, `GET /api/admin/seller-write-offs?page=1&pageSize=10&sellerId=...`.
- Dashboard nợ có bộ lọc **Khóa bán vĩnh viễn** và định danh đã che trong chi tiết seller. Tab **Nợ khó thu / Blacklist** hiển thị chính sách, tổng tiền, quyết định có tiêu chí/thời điểm và blacklist có phân trang.
- Thông báo write-off và thu hồi sau write-off dùng outbox hiện có, không yêu cầu email gửi thành công để lưu giao dịch.

## Migration và kiểm thử

Dừng API, áp dụng migration `20261015041522_AddSellerRegistrationAndAutomaticWriteOff`, rồi khởi động API/frontend mới. Migration chỉ thêm cột/bảng/chỉ mục/FK; không đặt lại số dư hoặc `Status` cũ. SQL để duyệt: [seller-registration-writeoff-migration.sql](../database/seller-registration-writeoff-migration.sql).

```powershell
dotnet ef database update --project backend/G4.Infrastructure --startup-project backend/G4.Api
dotnet build G4_Project.sln -m:1 -p:UseSharedCompilation=false
dotnet run --project tests/G4.Commerce.Tests --no-build
node --test tests/*.test.cjs
```

Kiểm thử SQL tự bootstrap đúng database QA riêng và từ chối tên database khác:

```powershell
$env:G4_REGISTRATION_SQL_TEST_CONNECTION = 'Server=localhost;Database=G4_SellerRegistration_Test_20261015;Integrated Security=True;TrustServerCertificate=True;'
dotnet run --project tests/G4.Commerce.Tests --no-build -- --registration-sql
```

`tests/seller-registration.e2e.cjs` là kiểm thử opt-in cho frontend QA `127.0.0.1:5133`, API QA cổng 5253, database `G4_SellerRegistration_Test_20261015`. Dùng khóa HMAC test trong `SellerRegistrationChecks.Configuration()`, email Capture và chính sách QA rút xuống 90 giây/20 giây, hạn nợ 15 giây/cảnh báo trước 5 giây. Kiểm thử tạo dữ liệu mẫu và khoản nợ 40 USD trong QA, chờ worker thật, trả hết, thử đăng ký lại, xem báo cáo admin và kiểm tra bố cục 390px. Chỉ chạy khi đã chủ động khởi động ứng dụng QA với cấu hình này; không dùng database chính.
