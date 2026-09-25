# Chạy bản MVP checkout & giao hàng (nhóm 4)

MVP chạy hai ứng dụng .NET 8: API ở `http://localhost:5251` và giao diện ở `http://localhost:5131`. Demo tự tạo một buyer, một seller, 5 sản phẩm, 2 địa chỉ và coupon `G4SAVE10` khi mở giao diện lần đầu. Các vai buyer/seller là vai **giả lập trong Development**, không phải đăng nhập thật.

## Chạy nhanh không cần SQL Server

Trong hai cửa sổ PowerShell tại thư mục gốc dự án:

```powershell
# Cửa sổ API
$env:ASPNETCORE_ENVIRONMENT='Development'
$env:Demo__UseInMemory='true'
dotnet run --project backend --urls http://localhost:5251
```

```powershell
# Cửa sổ giao diện
$env:ASPNETCORE_ENVIRONMENT='Development'
dotnet run --project frontend --urls http://localhost:5131
```

Mở `http://localhost:5131`. Dữ liệu InMemory mất khi dừng API. Chế độ này chỉ để thử UI và nghiệp vụ; kiểm thử SQL Server vẫn cần chạy riêng.

## Chạy với SQL Server

1. Tạo một database **trống** bằng [database/baseline.sql](../database/baseline.sql) trong SSMS hoặc `sqlcmd`. Script này chứa `CREATE DATABASE CloneEbayDB`; **không chạy lại** trên database đã có bảng/dữ liệu.
2. Cấu hình `ConnectionStrings__DefaultConnection` trong môi trường theo SQL Server của bạn. Mẫu cấu hình trong `backend/appsettings.json` dùng SQL Server Docker qua cổng `14333`; SQL Server cài trực tiếp thường có server/cổng khác.
3. Từ thư mục gốc chạy `dotnet ef database update --project backend --startup-project backend`. Migration cũ `InitialCreate` không tạo bảng; bảng nền phải đến từ bước 1. Migration MVP thêm cột, index và bảng mới. Có thể xem [database/mvp-migration.sql](../database/mvp-migration.sql) nếu muốn duyệt SQL trước.
4. Chạy API và giao diện như trên nhưng **không đặt** `Demo__UseInMemory=true`.

Nếu đã có `CloneEbayDB` từ trước, kiểm tra các bảng nền có khớp [database/baseline.sql](../database/baseline.sql) trước khi chạy migration; sao lưu dữ liệu trước khi thay schema.

## PayPal Sandbox và dữ liệu thử

Đặt `PayPal__ClientId` và `PayPal__ClientSecret` từ ứng dụng **Sandbox** của PayPal bằng biến môi trường hoặc .NET User Secrets. Không ghi secret vào source. Tài khoản buyer sandbox dùng ở trang PayPal khi được chuyển hướng. Nếu chưa có credential, nút PayPal sẽ báo chưa cấu hình; các luồng thẻ và giao hàng vẫn chạy được.

Credit Card ở đây chỉ là mô phỏng: `4111111111111111` + `12/30` thành công; `4000000000000002` + `12/30` bị từ chối; tháng/năm đã qua trả về hết hạn. Không nhập thẻ thật. Seller có thể tạo vận đơn, phát các mốc tracking, duyệt trả hàng và hoàn tiền trong tab **Seller & Tracking**. Tab **Email demo** hiển thị thông báo đã lưu; nếu đặt `Mail__Host` và tùy chọn `Mail__Port` thì API gửi tới SMTP/mail catcher cục bộ.

## Kiểm thử

```powershell
dotnet build G4_Project.sln
dotnet run --project tests/G4.Mvp.Tests
./tests/smoke-demo.ps1
```

`smoke-demo.ps1` yêu cầu cả API và giao diện đang chạy và tạo đơn thử mới. Luồng PayPal thật cần credential sandbox và buyer test account nên không có trong smoke tự động; unit test dùng gateway giả để kiểm tra timeout/đối soát mà không gọi PayPal.

