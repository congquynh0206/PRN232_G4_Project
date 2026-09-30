# Cấu trúc backend

Backend được chia thành năm project .NET 8 trong cùng solution. Khi chạy ứng dụng chỉ khởi động `G4.Api`; bốn project còn lại là thư viện được nạp vào API.

```text
backend/
├── G4.Api/
│   ├── Controllers/
│   ├── Middleware/
│   ├── Properties/
│   ├── Program.cs
│   └── appsettings*.json
├── G4.Contracts/
│   └── Checkout/
├── G4.Application/
│   ├── Integrations/
│   └── Services/
├── G4.Domain/
│   ├── Entities/
│   └── Rules/
└── G4.Infrastructure/
    ├── Checkout/
    ├── Payments/
    ├── Shipping/
    ├── Returns/
    ├── Notifications/
    ├── Seeding/
    ├── Integrations/
    │   ├── PayPal/
    │   ├── Refunds/
    │   └── Shipping/
    └── Persistence/
        └── Migrations/
```

## Trách nhiệm của từng project

| Project | Chứa gì | Không nên chứa |
|---|---|---|
| `G4.Api` | Controller, HTTP route, kiểm tra role ở biên API, ánh xạ lỗi HTTP, cấu hình khởi động | Luật tính tiền, gọi PayPal trực tiếp, câu lệnh truy cập dịch vụ bên ngoài |
| `G4.Contracts` | Request/response dùng giữa API và client | Entity EF, DbContext, xử lý nghiệp vụ |
| `G4.Application` | Interface mô tả use case và cổng tích hợp | Chi tiết SQL Server, SMTP, PayPal HTTP |
| `G4.Domain` | Entity và quy tắc thuần như giá đơn, trạng thái đơn/vận chuyển | Controller, IConfiguration, HttpClient, DbContext |
| `G4.Infrastructure` | EF Core, migration, triển khai service, PayPal, carrier, refund, worker và seed data | Razor/UI và xử lý HTTP controller |

Chiều phụ thuộc được giữ như sau:

```text
G4.Api ────────────────┐
   │                   │
   ▼                   ▼
G4.Application     G4.Infrastructure
   │                   │
   ├── G4.Contracts ◄──┤
   └── G4.Domain    ◄──┘
```

`G4.Domain`, `G4.Contracts` không tham chiếu Infrastructure hay Api. Controller nhận các interface như `ICheckoutService`, `IShipmentService`, `IReturnService` và `IPayPalPaymentService`; `Program.cs` nối interface với implementation tương ứng.

## Cách chia controller

Controller cũ chứa toàn bộ endpoint đã được tách theo chức năng:

| Controller | Trách nhiệm |
|---|---|
| `SetupController` | Khởi tạo dữ liệu thử trong Development |
| `CatalogController` | Sản phẩm ngẫu nhiên và địa chỉ buyer |
| `CheckoutController` | Báo giá và tạo đơn |
| `OrdersController` | Danh sách, chi tiết, hủy và chuẩn bị đơn |
| `PaymentsController` | Credit, PayPal và refund |
| `ShippingController` | Tạo vận đơn, sự kiện tracking và mô phỏng lỗi carrier |
| `ReturnsController` | Yêu cầu, duyệt, từ chối và nhận hàng trả |
| `NotificationsController` | Hộp thư thông báo |
| `CarrierSimulatorController` | API carrier cục bộ có kiểm tra key |

Các endpoint backend hiện bắt đầu bằng `/api`, không còn đoạn `/api/demo`. Frontend chuyển tiếp request qua `/api/proxy/{path}` và dùng header `X-Actor-Role` cho hai vai thử nghiệm.

## Quy tắc đặt tên

- Tên file/lớp mô tả trách nhiệm: `CheckoutService`, `ApplicationDataSeeder`, `CommerceMaintenanceWorker`, `CardPaymentSimulator`.
- Không dùng tên giai đoạn phát triển trong tên file/lớp, ví dụ `Mvp`, `Demo`, `Temp`, `New`.
- Dữ liệu hoặc dịch vụ giả lập phải được gọi đúng bản chất bằng `Simulator`, `Sandbox`, `Seed` hoặc `Test`.
- Mỗi controller/service chỉ xử lý một nhóm nghiệp vụ. Nếu file tăng quá lớn, tách theo use case thay vì thêm hậu tố phiên bản.
- Migration đã áp dụng không đổi migration ID. Chuỗi lịch sử `20260924112602_AddCheckoutShippingMvp` vẫn được giữ trong metadata và script SQL để database cũ không chạy lại migration; tên file và tên class đã đổi thành `AddCheckoutShipping`.

## Chạy và tạo migration

```powershell
dotnet run --project backend/G4.Api
dotnet ef database update --project backend/G4.Infrastructure --startup-project backend/G4.Api
dotnet ef migrations add TenMigration --project backend/G4.Infrastructure --startup-project backend/G4.Api --output-dir Persistence/Migrations
```

User Secrets tiếp tục nằm ở `G4.Api` và giữ nguyên `UserSecretsId`, nên PayPal Client ID/Secret đã cấu hình trên máy hiện tại vẫn được đọc. Khi dùng Visual Studio, đặt `G4.Api` và `frontend` làm multiple startup projects.

## Lưu ý chuyển tiếp

Đợt refactor này giữ nguyên bảng SQL và quy tắc nghiệp vụ. Không có migration mới chỉ vì chuyển project. Một số implementation trong Infrastructure vẫn dùng `ApplicationDbContext` trực tiếp; khi bổ sung email và nhật ký tích hợp, truy vấn dùng chung sẽ được đưa dần vào service/repository thay vì quay lại controller lớn.
