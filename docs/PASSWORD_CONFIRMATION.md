# Xác nhận mật khẩu cho quyết định admin/seller

## Kế hoạch và phạm vi chốt ngày 31/10/2026

1. Bảo vệ API trước khi chạy nghiệp vụ bằng mật khẩu hiện tại của UserId/role từ JWT.
2. Dùng popup chung tại bước xác nhận cuối, hiển thị quyết định/khoản tiền, giữ nguyên
   payload và idempotency key trong lúc nhập. Hủy hoặc sai mật khẩu không chạy nghiệp vụ.
3. Kiểm thử HTTP thật với InMemory, Chrome với Razor/proxy thật và API fixture;
   hồi quy backend/frontend, cập nhật đặc tả và hướng dẫn. Không đổi schema/worker.

## Các thao tác đang được bảo vệ

| Vai trò | Thao tác / route POST dưới `/api` |
| --- | --- |
| Admin | Phán quyết: `disputes/{id}/resolution`, alias `orders/{id}/fund-hold/resolve` |
| Admin | Đổi phí ngay/đặt lịch: `admin/platform-fees` |
| Admin/Seller | Tạo/sửa/bật/tắt khuyến mãi: `{role}/promotions`, `/{id}/edit`, `/{id}/state` |
| Admin | Đình chỉ/gỡ đình chỉ khuyến mãi seller: `admin/promotions/{id}/suspension` |
| Seller | Rút tiền: `seller/payouts`; trả nợ: `seller/debt/repayments` |
| Seller | Thay định danh: `seller/identity` |
| Seller | Quyết định hủy: `seller/orders/{id}/cancel/decision` |
| Seller | Duyệt/từ chối/nhận hàng/báo lỗi hàng trả: `seller/returns/{id}/approve`, `/reject`, `/receive`, `/issue` |
| Seller | Duyệt kèm giả lập lỗi nhãn: `seller/returns/{id}/approve/simulate-failure` |
| Seller | Hoàn tiền riêng: `seller/orders/{id}/refund` |
| Seller | Gửi phương án tranh chấp: `disputes/{id}/proposal` |
| Seller | Quyết định đổi địa chỉ: `seller/address-changes/{id}/decision` |
| Seller | Đổi cap/địa chỉ lấy: `seller/shipping/address-change-cap`, `seller/shipping/pickup` |

Tổng cộng 21 action MVC hiện có; các action promotion dùng được cho cả hai role
theo quyền tài trợ/quản lý hiện hành. Preview, xem dữ liệu, chuẩn bị hàng, tạo nhãn
thông thường, retry nhãn đã duyệt, bổ sung bằng chứng, trả lời đánh giá và đổi khối
lượng không yêu cầu mật khẩu. Xác nhận mật khẩu không mở rộng quyền nghiệp vụ.

**Còn phụ thuộc backlog:** admin yêu cầu trả hàng trước phán quyết (A2), đặt/đổi/kết
thúc hạn mức riêng (LIM1), seller thay policy free returns (FR1). Khi bổ sung API/UI
các nhánh này phải gắn `RequirePassword` và dùng common API helper. Task này không
đánh dấu A2/LIM1/FR1 hoàn thành. Worker refund, payout, xét cấp, write-off/blacklist
không đợi mật khẩu. Không thêm quyền sửa level, số dư hay blacklist thủ công.

## Cơ chế API và popup

- `RequirePasswordAttribute` là authorization filter, chạy trước model binding và
  controller: thiếu header trả 428 với `PasswordConfirmationRequired`, action tiếng
  Việt; sai mật khẩu trả 403 với `PasswordConfirmationInvalid`. Sai role vẫn 403,
  anonymous vẫn qua JWT authentication/authorization. Phản hồi dùng `no-store`.
- Common helper nhận challenge và mở dialog cho admin/seller. Khi người dùng bấm
  xác nhận, gửi lại đúng JSON đã đóng băng với header `X-Action-Password`, giá trị
  là `encodeURIComponent(password)`. API giải mã một lần và kiểm PBKDF2 của chính
  user/role hiện tại trong database; không nhận email/UserId xác minh từ body.
  Encoding chỉ để truyền Unicode qua header; kết nối triển khai phải dùng HTTPS.
- Proxy chỉ chuyển tiếp header xác nhận đã chỉ định, cùng JWT của cookie hiện tại.
  Không ghi giá trị mật khẩu vào log, URL, localStorage, sessionStorage hoặc database;
  không tạo token xác nhận hay lưu khoảng thời gian miễn nhập lại mật khẩu.
- Sai mật khẩu giữ popup, xóa ô nhập và đưa focus về ô có lỗi. Hủy/Escape trước gửi
  trả lỗi client `PasswordConfirmationCancelled`/status 428 để các form giải phóng
  draft chắc chắn chưa thực thi. Khi đang gửi thì chặn hủy và gửi trùng. Lỗi mạng/
  nghiệp vụ đóng popup và trả về form; không tự retry quyết định có kết quả chưa rõ.
- Native dialog nằm trên form/chi tiết đang mở, giữ focus bàn phím; nhãn hiện rõ,
  có autocomplete current-password, cho paste/password manager. Nội dung quyết định
  dùng textContent để không render HTML của lý do/bằng chứng.

## Giới hạn thử và cấu hình

`PasswordConfirmation:PermitLimit = 5`, `WindowSeconds = 60` trong API appsettings.
Sliding Window sáu đoạn, queue bằng 0, bộ đếm dùng chung cho mọi action của một
UserId, tính cả xác nhận thành công. Quá lượt trả 429/Retry-After và group
`password-confirmation`. Popup đếm ngược rồi chỉ cho thử lại thủ công; không tự POST.

Request chưa có header chỉ mở challenge, được miễn quota nghiệp vụ vì filter chắc
chắn dừng trước controller. Request có header vẫn qua quota finance/write hiện có
và bộ đếm mật khẩu riêng, nên đổi URL/role/body/key không né được quota UserId.
Các bộ đếm nằm trong RAM một API instance, reset khi restart; nhiều instance cần
bộ đếm phân tán. Không đổi transaction, ownership, idempotency hoặc worker retry.

## Chạy và xác minh

Build/restart cả API và frontend, tải lại trang admin/seller để dùng UI mới. Không
có migration mới, không migrate/seed hoặc cập nhật database chính trong task này.

```powershell
dotnet build G4_Project.sln --no-restore -m:1 -p:UseAppHost=false -p:UseSharedCompilation=false -p:OutputPath=D:/HocTap/Semester/Ky8/PRN232/G4_Project/artifacts/password-confirmation-validation/build/
dotnet artifacts/password-confirmation-validation/build/G4.Commerce.Tests.dll --password-confirmation
dotnet artifacts/password-confirmation-validation/build/G4.Commerce.Tests.dll
node --test tests/*.test.cjs
node tests/password-confirmation.e2e.cjs
```

Chrome fixture cần `NODE_PATH` trỏ tới Playwright có sẵn và `CHROME_PATH` nếu dùng
Chrome cài trên máy. Fixture khởi động frontend/API local trên port riêng rồi đóng,
không dùng SQL/provider thật. Ảnh lưu tại `artifacts/password-confirmation-ui/`.
Kết quả kiểm thử cuối được ghi tại backlog, không suy ra migration database chính.
