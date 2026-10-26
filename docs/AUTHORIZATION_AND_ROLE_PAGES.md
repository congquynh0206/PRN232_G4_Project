# Đăng nhập, phân quyền và trang theo role

Hệ thống có bốn role độc lập. Người dùng đăng nhập tại `/Account/Login`; API kiểm tra mật khẩu hash trong SQL Server, cấp JWT 8 giờ, frontend giữ JWT bên trong cookie đăng nhập `HttpOnly`. Mọi request frontend gửi qua proxy đều được gắn Bearer token. Header `X-Actor-Role` và dropdown tự đổi role đã bị loại bỏ.

## Tài khoản phát triển

| Role | Email | Mật khẩu | Trang sau đăng nhập |
| --- | --- | --- | --- |
| Buyer | `buyer@example.test` | `G4@123456` | `/Buyer` |
| Seller | `seller@example.test` | `G4@123456` | `/Seller` |
| Shipper | `shipper@example.test` | `G4@123456` | `/Shipper` |
| Admin | `admin@example.test` | `G4@123456` | `/Admin` |

`database/seed-data.sql` tạo hoặc cập nhật bốn tài khoản và lưu PBKDF2 hash, không lưu mật khẩu rõ trong database. Đây là credential dùng cho Development; môi trường khác phải đổi mật khẩu và cấu hình khóa JWT riêng.

## Phân chia chức năng

### Điều hướng ngày 21/10/2026

- Buyer: Giỏ hàng, Đơn hàng, Tranh chấp, Thông báo. Thanh toán mở từ **Tiếp tục thanh toán**, có **Quay lại giỏ hàng**; khi thanh toán, mục Giỏ hàng vẫn được đánh dấu. Desktop menu ngang; mobile ≤760px dùng thanh dưới với khoảng đệm cuối trang/safe area để cuộn tới các nút hành động.
- Seller: sidebar 230px chia **Đơn hàng** (đơn, trả/hoàn, tranh chấp), **Tài chính** (ví/rút tiền, báo cáo), **Cửa hàng** (khuyến mãi, đánh giá, giao hàng), **Tài khoản** (cấp, định danh, thông báo, nhật ký).
- Admin: sidebar chia **Xử lý đơn** (tranh chấp, tra cứu), **Người bán** (cấp, nợ, nợ khó thu/danh sách chặn), **Chính sách** (khuyến mãi, phí), **Hệ thống** (thông báo, nhật ký).
- Seller/Admin ≤1000px mở menu qua nút Menu; dialog native giữ focus bàn phím, Escape/Đóng trả focus về nút mở; chọn mục đóng menu và đưa focus tới tiêu đề nội dung. Khi resize, cùng một nav chuyển giữa sidebar/drawer nên không nhân ID badge hoặc handler.
- Fragment URL như `/Seller#finance-report`, `/Admin#order-lookup`, `/Buyer#checkout` giữ mục khi tải lại và hỗ trợ Back/Forward. Link cũ `?tab=finance`, `?tab=finance-report`, `?orderId=...` và PayPal return vẫn được xử lý; fragment hợp lệ ưu tiên khi đã chuyển mục. Fragment lạ/khác role quay về mặc định, không mở loader ngoài role.
- Shipper giữ nguyên layout/menu/CSS/JS. Menu mới không đổi quyền server, endpoint, tiền hoặc trạng thái dữ liệu.

Build frontend 0 warning/error, Node 128/128 và Chrome fixture Razor thật ở 320/390/768/1000/1024/1440px đạt. Kiểm tra đủ 4/12/9 mục, badge duy nhất, history/reload/query link, checkout/quay lại, keyboard/Escape/focus, resize drawer, không tràn ngang hoặc gọi API ghi dữ liệu. Fixture RT1/PAY1 hồi quy đạt. Ảnh `artifacts/role-navigation-ui/`. Login/API browser là fixture; chưa E2E nối SQL/provider hoặc xác nhận dữ liệu thật. Không có migration mới, không seed/migrate database nào trong đợt UI này.

### Chức năng theo role

- **Buyer:** random giỏ, checkout, Credit Card/PayPal, xem đơn và lịch sử payment/refund của chính mình, tracking, hủy, trả hàng; tab Tranh chấp để mở yêu cầu có bằng chứng, bổ sung và chấp nhận/từ chối phương án.
- **Seller:** chỉ xem đơn bán của mình; chuẩn bị hàng, tạo vận đơn, xử lý hủy/trả/refund, xem balance, level, ledger và payout; tab Tranh chấp để gửi bằng chứng và phương án. Seller không có quyền phát tracking event hoặc quyết định thay admin.
- **Shipper:** xem vận đơn chờ nhận, nhận riêng chiều đi/chiều trả rồi cập nhật tracking của mình. Chi tiết đơn chỉ chứa vận đơn và lịch sử đang sở hữu; payment, refund và balance không trả về cho shipper. Xem [RETURNS_AND_SHIPPING.md](RETURNS_AND_SHIPPING.md).
- **Admin:** tra cứu đơn/payment/refund và tracking ở chế độ chỉ đọc, xem nhật ký tích hợp, chỉ mở hồ sơ tranh chấp đã chuyển lên (seller hết hạn hoặc buyer từ chối phương án), đưa ra quyết định theo workflow; không sửa tay tiền/payment/tracking. Tra cứu order không yêu cầu có dispute.

API còn kiểm tra quyền sở hữu dữ liệu: buyer chỉ truy cập order có `BuyerId` của mình, seller chỉ truy cập order có `SellerId` của mình. Có token đúng role nhưng dùng ID của người khác vẫn nhận `403`.

## Cấu hình xác thực

- `Authentication:Issuer` và `Authentication:Audience` nằm trong `backend/G4.Api/appsettings.json`.
- `Authentication:JwtKey` hiện chỉ nằm trong `appsettings.Development.json` để dự án chạy ngay ở Development.
- Khi deploy, đặt khóa bằng environment hoặc User Secrets, ví dụ `Authentication__JwtKey`, và không đưa khóa production vào Git.
- Frontend dùng ASP.NET Core Cookie Authentication; cookie chứa claims và JWT đã được Data Protection mã hóa, có `HttpOnly` và thời hạn 8 giờ.

## Endpoint chính

- `POST /api/auth/login`: nhận email/mật khẩu và cấp JWT.
- `GET /api/orders`: tự lọc theo buyer hoặc seller đang đăng nhập.
- `GET /api/buyer/transactions` và `GET /api/buyer/transactions/{type}/{id}`: lịch sử giao dịch, tổng kỳ và chi tiết chỉ đọc; luôn ràng buộc theo BuyerId trong JWT.
- `GET /api/shipper/shipments`: danh sách công việc của shipper.
- `GET /api/disputes/page`: danh sách phân trang theo quyền; admin chỉ thấy hồ sơ đã chuyển lên.
- `GET /api/disputes/{id}` và các thao tác: xem [DISPUTES.md](DISPUTES.md).
- `GET /api/admin/fund-holds`: endpoint tương thích, chỉ liệt kê khoản giữ thuộc hồ sơ đã chuyển admin.
- `GET/POST /api/admin/platform-fees`: chỉ admin đọc/cập nhật mức phí có thời điểm hiệu lực và lịch sử; buyer/seller/shipper nhận 403.
- `GET /api/admin/order-lookup` và `GET /api/admin/order-lookup/{orderId}`: chỉ admin; tra cứu order/payment/refund toàn cục và trả dữ liệu nhạy cảm đã che. API chỉ đọc.
- `POST /api/orders/{id}/fund-hold/resolve`: endpoint tương thích, vẫn kiểm tra hồ sơ đã chuyển admin và lý do quyết định.

Các controller API dùng fallback policy yêu cầu đăng nhập. Carrier simulator vẫn dùng `X-Carrier-Key` riêng và được phép gọi ẩn danh vì đây là tích hợp machine-to-machine cục bộ.
