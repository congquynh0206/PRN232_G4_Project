# Chạy dự án checkout & giao hàng (nhóm 4)

Dự án chạy hai ứng dụng .NET 8: API ở `http://localhost:5251` và giao diện ở `http://localhost:5131`. Chức năng khởi tạo dữ liệu tạo một buyer, một seller, 5 sản phẩm, 2 địa chỉ và coupon `G4SAVE10` khi mở giao diện lần đầu. Các vai buyer/seller hiện là vai thử nghiệm trong Development, chưa phải đăng nhập thật.

Xem cách chia các project và quy tắc đặt file tại [BACKEND_ARCHITECTURE.md](BACKEND_ARCHITECTURE.md).

## Chạy nhanh không cần SQL Server

Trong hai cửa sổ PowerShell tại thư mục gốc dự án:

```powershell
# Cửa sổ API
$env:ASPNETCORE_ENVIRONMENT='Development'
$env:Storage__UseInMemory='true'
dotnet run --project backend/G4.Api --urls http://localhost:5251
```

```powershell
# Cửa sổ giao diện
$env:ASPNETCORE_ENVIRONMENT='Development'
dotnet run --project frontend --urls http://localhost:5131
```

Mở `http://localhost:5131`. Dữ liệu InMemory mất khi dừng API. Chế độ này chỉ để thử UI và nghiệp vụ; kiểm thử SQL Server vẫn cần chạy riêng.

## Chạy với SQL Server

1. Tạo một database **trống** bằng [database/baseline.sql](../database/baseline.sql) trong SSMS hoặc `sqlcmd`. Script này chứa `CREATE DATABASE CloneEbayDB`; **không chạy lại** trên database đã có bảng/dữ liệu.
2. Cấu hình `ConnectionStrings__DefaultConnection` trong môi trường theo SQL Server của bạn. Mẫu cấu hình trong `backend/G4.Api/appsettings.json` dùng SQL Server Docker qua cổng `14333`; SQL Server cài trực tiếp thường có server/cổng khác.
3. Từ thư mục gốc chạy `dotnet ef database update --project backend/G4.Infrastructure --startup-project backend/G4.Api`. Migration cũ `InitialCreate` không tạo bảng; bảng nền phải đến từ bước 1. Migration checkout và giao hàng thêm cột, index và bảng mới. Có thể xem [database/checkout-shipping-migration.sql](../database/checkout-shipping-migration.sql) nếu muốn duyệt SQL trước.
4. Chạy API và giao diện như trên nhưng **không đặt** `Storage__UseInMemory=true`.

Nếu đã có `CloneEbayDB` từ trước, kiểm tra các bảng nền có khớp [database/baseline.sql](../database/baseline.sql) trước khi chạy migration; sao lưu dữ liệu trước khi thay schema.

## Chạy bằng Visual Studio

1. Mở `G4_Project.sln` và đợi Visual Studio restore các project.
2. Chọn **Configure Startup Projects** → **Multiple startup projects**.
3. Đặt `G4.Api` và `frontend` thành **Start**, chọn profile `http` cho cả hai. Các project Domain/Application/Contracts/Infrastructure để **None**.
4. Nếu dùng SQL Server, mở **Manage User Secrets** của project `G4.Api` và đặt `ConnectionStrings:DefaultConnection`. Nếu dùng InMemory, đặt `Storage:UseInMemory` thành `true` trong User Secrets hoặc biến môi trường.
5. Nhấn Start. Giao diện mở tại `http://localhost:5131`, Swagger ở `http://localhost:5251/swagger`.

Sau khi đổi cấu trúc, project startup backend là `backend/G4.Api/G4.Api.csproj`; cấu hình trỏ tới `backend/backend.csproj` cũ không còn hợp lệ.

## PayPal Sandbox và dữ liệu thử

Đặt `PayPal__ClientId` và `PayPal__ClientSecret` từ ứng dụng **Sandbox** của PayPal bằng biến môi trường hoặc .NET User Secrets của project `G4.Api`. `UserSecretsId` cũ được giữ nguyên nên secret đã lưu trên máy vẫn dùng được. Không ghi secret vào source. Tài khoản buyer sandbox dùng ở trang PayPal khi được chuyển hướng. Nếu chưa có credential, nút PayPal sẽ báo chưa cấu hình; các luồng thẻ và giao hàng vẫn chạy được.

Credit Card ở đây chỉ là mô phỏng: `4111111111111111` + `12/30` thành công; `4000000000000002` + `12/30` bị từ chối; tháng/năm đã qua trả về hết hạn. Không nhập thẻ thật. Seller có thể tạo vận đơn, phát các mốc tracking, duyệt trả hàng và hoàn tiền trong tab **Seller & Tracking**. Tab **Email** hiển thị thông báo đã lưu; nếu đặt `Mail__Host` và tùy chọn `Mail__Port` thì API gửi tới SMTP/mail catcher cục bộ.

## Tracking hai chiều

Trong **My Orders** và **Seller & Tracking**, mở **Xem chi tiết** để xem các khối vận đơn:

- **Giao tới buyer (Seller → Buyer):** mã vận đơn chiều đi, trạng thái và timeline chỉ chứa sự kiện của vận đơn này. Chưa có vận đơn thì hiển thị trạng thái chờ tạo. `Delivered` được ghi là **Buyer đã nhận hàng**.
- **Trả về seller (Buyer → Seller):** xuất hiện khi đã có vận đơn trả sau khi seller duyệt yêu cầu. Có mã, trạng thái và timeline riêng; `Delivered` được ghi là **Seller đã nhận hàng trả**. Chưa yêu cầu trả hoặc chưa có vận đơn trả thì không xuất hiện khối này.
- **Giao thất bại và chuyển hoàn:** nằm trong khối giao tới buyer, với các mốc **Đang chuyển hoàn về seller → Seller đã nhận hàng chuyển hoàn**. Đây là hàng chưa giao thành công, khác với buyer đã nhận rồi yêu cầu trả.

Seller thao tác tạo vận đơn, phát mốc tracking và xác nhận nhận hàng trả ngay trong khối vận đơn tương ứng. Quyết định duyệt/từ chối yêu cầu, hủy đơn và hoàn tiền nằm ở **Thao tác đơn hàng**. Kết quả hoàn tiền hiển thị riêng, không được tính là một mốc vận chuyển.

Nếu tạo vận đơn trả thất bại, seller bấm **Thử tạo vận đơn trả lại** trong khối trả về seller. API `POST /api/seller/returns/{id}/ship` chỉ xử lý yêu cầu đã duyệt, dùng lại vận đơn và khóa tạo nhãn cũ; không tạo lại vận đơn chiều đi.

Thay đổi này dùng `ShippingInfo.Direction` và `ShippingEvent.ShippingInfoId` đã có, **không cần migration database mới**. Các giá trị trạng thái lưu trong database giữ nguyên; giao diện dịch nhãn theo hướng vận chuyển.

Kiểm tra giao diện bằng các kịch bản:

1. Đặt và thanh toán bằng thẻ giả lập, seller tạo vận đơn: chỉ có khối giao tới buyer. Phát tracking đến `Delivered` và kiểm tra nhãn **Buyer đã nhận hàng**.
2. Buyer yêu cầu trả, seller duyệt: xuất hiện khối trả về seller. Phát sự kiện chiều trả và kiểm tra timeline chiều đi không thay đổi. Sau `Delivered`, seller xác nhận nhận hàng rồi hoàn tiền; hai timeline và kết quả hoàn tiền vẫn hiển thị riêng.
3. Với một đơn mới, mô phỏng `DeliveryFailed → ReturningToSender → ReturnedToSeller`: chỉ có timeline chiều đi kèm giải thích chuyển hoàn, không sinh khối yêu cầu trả hàng.
4. Đổi giữa buyer và seller: buyer không có nút phát sự kiện; mỗi nút tracking của seller nằm trong đúng khối vận đơn. Kiểm tra cả màn hình điện thoại và desktop.

## Kiểm thử

```powershell
dotnet build G4_Project.sln
dotnet run --project tests/G4.Commerce.Tests
./tests/smoke-commerce.ps1
```

`smoke-commerce.ps1` yêu cầu cả API và giao diện đang chạy và tạo đơn thử mới. Luồng PayPal thật cần credential sandbox và buyer test account nên không có trong smoke tự động; unit test dùng gateway giả để kiểm tra timeout/đối soát mà không gọi PayPal.

