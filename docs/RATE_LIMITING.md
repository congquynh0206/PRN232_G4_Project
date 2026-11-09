# Rate limiting theo nhóm chức năng

## Kế hoạch triển khai ngày 28/10/2026

1. Dùng middleware ASP.NET Core và Sliding Window, queue bằng 0, policy dùng chung
   theo nhóm. Login/registration theo IP; API đã xác thực theo UserId từ JWT.
2. Xử lý IP qua MVC bằng allowlist proxy rõ ràng, không tin header từ Internet.
   Local dùng loopback; Docker resolve đúng service frontend. Không giới hạn
   carrier nội bộ và worker theo quota người dùng.
3. Trả HTTP 429, Retry-After, JSON tiếng Việt; MVC hiển thị lỗi đăng nhập riêng,
   proxy giữ header. Giao diện không tự gửi lại mutation, giữ payload/key chưa rõ kết quả.
4. Kiểm thử middleware HTTP, giới hạn đồng thời/nhóm/quyền/IP/khôi phục, request bị
   chặn không gọi nghiệp vụ; kiểm thử UI/login/proxy, build và hồi quy phù hợp.

Các bước trên đã triển khai và kiểm thử ngày 28/10/2026. Không cần migration.

## Policy và cấu hình

**Bổ sung 31/10/2026:** request tới action có `RequirePassword` nhưng chưa gửi header
xác nhận chỉ nhận challenge 428, không tiêu quota nghiệp vụ. Request có header vẫn
qua quota nhóm và bộ đếm mật khẩu riêng 5 lần/60 giây theo UserId, gồm cả thành công.
429 từ bộ đếm này có group `password-confirmation`. [Chi tiết](PASSWORD_CONFIRMATION.md).


| Policy | Mặc định | Bộ đếm |
| --- | --- | --- |
| `login` | 10 request / 60 giây | IP đã resolve |
| `registration` | 5 request / 600 giây | IP đã resolve |
| `read` | 120 request / 60 giây | UserId xác thực; fallback IP cho anonymous |
| `write` | 60 request / 60 giây | UserId xác thực; fallback IP |
| `payment` | 10 request / 60 giây | UserId xác thực |
| `finance` | 5 request / 60 giây | UserId xác thực |

Cấu hình trong `backend/G4.Api/appsettings.json` dưới
`RateLimiting:{policy}:PermitLimit/WindowSeconds/SegmentsPerWindow`. Frontend có
`login` và `registration` trong `frontend/appsettings.json`. Mặc định chia sáu đoạn,
queue bằng 0; thiếu cấu hình dùng mặc định, số không hợp lệ làm startup thất bại.
Khởi động lại ứng dụng sau khi đổi cấu hình; chỉnh cùng policy ở cả hai ứng dụng
khi muốn cùng hạn mức. Request lỗi và replay cũng tiêu lượt khi đã qua limiter.

`CommerceRateLimitConvention` gắn metadata policy cho mọi action API người dùng:
GET/HEAD/OPTIONS dùng `read`, các thao tác ghi chưa phân riêng dùng `write`.
Tạo order một seller/cả giỏ, payment thẻ và PayPal một đơn/cả giỏ cùng `payment`.
Refund, payout, trả nợ, quyết định hủy của seller, duyệt/xác nhận return có thể
refund, quyết định dispute và buyer chấp nhận phương án dùng `finance`.
Preview/quote dùng policy theo HTTP method. Group dùng chung bộ đếm giữa các
endpoint/order ID, không tính một quota riêng theo URL hoặc theo key thanh toán.

Carrier simulator dùng key machine-to-machine riêng, không gắn quota người dùng;
worker gọi service trực tiếp nên không đi qua middleware. SignalR không bị tính
quota này cho từng message; kết nối/auth của hub giữ nguyên. Chưa thêm giới hạn
carrier/hub, Redis, Nginx hoặc thay đổi idempotency/quyền/tài chính của nghiệp vụ.

## UserId và IP qua proxy

- API lấy NameIdentifier từ principal do JWT authentication xác thực. ID/role/IP
  tùy ý trong body/header không thay được quota UserId. IPv4-mapped IPv6 được
  chuẩn hóa để không tạo hai quota cho cùng IPv4.
- Frontend login/register có quota IP tại MVC, trước controller. API cũng có quota
  cho các endpoint auth để không thể bỏ qua bằng cách gọi thẳng API.
- MVC tạo `X-Forwarded-For` mới từ `Connection.RemoteIpAddress` đã resolve, không
  sao chép header của browser. Các request proxy đã đăng nhập vẫn dùng JWT UserId.
- `TrustedProxies:Addresses` và `TrustedProxies:Hosts` là allowlist, forward tối đa
  một hop và chỉ đọc X-Forwarded-For. Không có allowlist thì không bật forwarding,
  không bao giờ coi danh sách rỗng là tin mọi peer. Đây không phải config chuyển
  tiếp scheme/HTTPS; nếu thêm reverse proxy TLS cần cấu hình hosting riêng.
- Local: API tin `127.0.0.1` và `::1`, frontend không tin proxy nào mặc định.
  Chỉ các tiến trình local tin cậy được coi là proxy; đây không phải ranh giới
  bảo vệ khỏi một tiến trình độc hại đã chạy trên cùng máy.
- Docker Compose: API thêm `TrustedProxies__Hosts__0=frontend`; resolve IP service
  tại startup, không tin cả subnet Docker. Frontend khởi động trước backend để DNS
  service có sẵn; frontend không gọi API lúc startup. Nếu container frontend đổi
  IP, restart backend để resolve lại. Khi dùng Nginx phía trước MVC, thêm đúng IP
  hoặc hostname Nginx vào allowlist frontend; không bật trust-all.

`UseRateLimiter` nằm sau routing/authentication và trước authorization/controller.
Không lấy password, JWT hoặc key carrier làm partition/log. Log cảnh báo chỉ ghi
group và thời gian chờ, không ghi payload/tài khoản/token.

## HTTP 429 và giao diện

JSON có `code=RateLimitExceeded`, `group`, `retryAfterSeconds` và `error` tiếng Việt.
HTTP trả `Retry-After` (giây) và `Cache-Control: no-store`. Dùng metadata limiter
khi có; nếu không có, dùng toàn bộ window làm thời gian chờ bảo thủ. Timer UI
chỉ hướng dẫn thời gian thử lại, request tiếp theo vẫn do server quyết định.

- MVC trả trang Login với status 429 cho cả limiter local và 429 từ API, không
  báo sai mật khẩu. Nút có countdown và bật lại để user tự gửi; không tự POST.
- API proxy giữ status/body/Retry-After. Common API helper tạo lỗi có status 429,
  group/thời gian chờ và thông báo tiếng Việt; không tự retry. Polling cùng method
  và đường dẫn bỏ query bị chặn phía client trong thời gian chờ để không spam API.
  Server vẫn là nơi kiểm tra quota theo group giữa mọi đường dẫn.
- Đăng ký giữ payload/key khi bị 429 sau một lần gửi chưa rõ kết quả. Không báo
  đăng ký thành công hoặc buộc tạo key mới. Lỗi hiện ngay ở form và nhận focus.
- PayPal return vẫn chuyển về đơn/kết quả cả giỏ, hiển thị thông báo bị giới hạn
  và thời gian chờ; không báo thất bại thu tiền chắc chắn, không capture lại tự động.

Bộ đếm nằm trong RAM từng process và reset khi restart. Đây là bản cho một API
instance; nhiều replica cần limiter phân tán với thao tác atomic (ví dụ Redis),
không chỉ thêm IDistributedCache. Rate limit không thay idempotency, auth/owner,
transaction hoặc reconcile payment/refund.

## Kiểm thử và phạm vi bằng chứng ngày 28/10/2026

- Solution build đầu ra `artifacts/rate-limit-validation/build/` đạt 0 warning/error;
  NuGet vulnerability audit không chạy do môi trường không truy cập được nuget.org.
  Không thêm package bên thứ ba; `G4.WebCommon` chỉ dùng framework ASP.NET Core.
- Backend `--rate-limits` đạt qua Kestrel/HttpClient thật: metadata của toàn bộ MVC
  action, quota chung nhiều route, JWT hợp lệ/giả và hai tài khoản cùng IP, XFF từ
  proxy tin cậy/không tin cậy, chuẩn hóa IPv4, 12 request cạnh tranh chỉ hai qua,
  429/Retry-After và hết window dùng lại được.
- Request tạo đơn/thẻ được cho phép đã chạy controller/service thật với InMemory.
  Sau khi quota hết, create/card/PayPal/payout/refund bị chặn và snapshot order,
  tồn kho, payment, refund, payout, ví, ledger và outbox không đổi.
- Backend mặc định đạt, gồm kiểm thử tiền và SMTP loopback. Node 140/140 đạt, gồm
  thông báo 429, HTTP-date/fallback, cooldown polling, không tự replay thanh toán,
  giữ key đăng ký sau kết quả chưa rõ và countdown login.
- Chrome fixture đạt MVC/Razor thật: 429 từ API và limiter MVC, không gọi backend
  khi MVC chặn, XFF giả từ browser không được chuyển tiếp, header Retry-After qua
  proxy, giữ key thanh toán và không tự retry. Desktop 1280px/mobile 390px; đã xem
  ảnh `artifacts/rate-limit-ui/`, không lỗi JS hoặc tràn ngang trong kịch bản mới.
- Hồi quy Chrome đăng nhập bốn role và điều hướng Buyer/Seller/Admin ở 320–1440px
  đạt. `docker compose config --quiet`, CSharpier check và `git diff --check` đạt;
  chưa khởi động/build Docker trong đợt này.
- Không chạy SQL opt-in, database chính, PayPal/carrier/ngân hàng thật hoặc Docker
  trong đợt này. Không tạo/áp migration, không seed và không thay dữ liệu chính.

Chạy riêng sau build:

```powershell
dotnet run --project tests/G4.Commerce.Tests -- --rate-limits
node --test tests/rate-limit-ui.test.cjs tests/seller-registration-ui.test.cjs
# Build frontend trước; dùng FRONTEND_DLL nếu output không ở artifacts như mặc định script.
node tests/rate-limit.e2e.cjs
```

HTTP/SMTP/Chrome cần quyền loopback; browser dùng NODE_PATH trỏ tới Playwright và
CHROME_PATH tới Chrome có sẵn. Log kiểm thử ở `artifacts/rate-limit-validation/`.
