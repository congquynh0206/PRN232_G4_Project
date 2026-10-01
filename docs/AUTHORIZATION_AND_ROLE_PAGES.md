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

- **Buyer:** random giỏ, checkout, Credit Card/PayPal, xem đơn của chính mình, tracking, hủy, trả hàng và mở tranh chấp.
- **Seller:** chỉ xem đơn bán của mình; chuẩn bị hàng, tạo vận đơn, xử lý hủy/trả/refund, xem balance, level, ledger và payout. Seller không còn quyền phát tracking event.
- **Shipper:** xem các vận đơn đã được seller tạo và cập nhật mốc vận chuyển chiều đi/chiều trả. Shipper không thanh toán hoặc xử lý refund.
- **Admin:** xem các settlement `OnHold`, quyết định seller thắng/buyer thắng và xem nhật ký email hệ thống.

API còn kiểm tra quyền sở hữu dữ liệu: buyer chỉ truy cập order có `BuyerId` của mình, seller chỉ truy cập order có `SellerId` của mình. Có token đúng role nhưng dùng ID của người khác vẫn nhận `403`.

## Cấu hình xác thực

- `Authentication:Issuer` và `Authentication:Audience` nằm trong `backend/G4.Api/appsettings.json`.
- `Authentication:JwtKey` hiện chỉ nằm trong `appsettings.Development.json` để dự án chạy ngay ở Development.
- Khi deploy, đặt khóa bằng environment hoặc User Secrets, ví dụ `Authentication__JwtKey`, và không đưa khóa production vào Git.
- Frontend dùng ASP.NET Core Cookie Authentication; cookie chứa claims và JWT đã được Data Protection mã hóa, có `HttpOnly` và thời hạn 8 giờ.

## Endpoint chính

- `POST /api/auth/login`: nhận email/mật khẩu và cấp JWT.
- `GET /api/orders`: tự lọc theo buyer hoặc seller đang đăng nhập.
- `GET /api/shipper/shipments`: danh sách công việc của shipper.
- `GET /api/admin/fund-holds`: các khoản cần admin xử lý.
- `POST /api/orders/{id}/fund-hold/resolve`: chỉ admin.

Các controller API dùng fallback policy yêu cầu đăng nhập. Carrier simulator vẫn dùng `X-Carrier-Key` riêng và được phép gọi ẩn danh vì đây là tích hợp machine-to-machine cục bộ.
