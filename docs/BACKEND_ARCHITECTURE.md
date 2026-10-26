# Cấu trúc backend

Backend được chia thành năm project .NET 8 trong cùng solution. Khi chạy ứng dụng chỉ khởi động `G4.Api`; bốn project còn lại là thư viện được nạp vào API.

## Sơ đồ tổng quan từ source ngày 25/10/2026

[Mở sơ đồ HTML tương tác](../.archify/architecture-g4-overview-20261025-155434/g4-overview.html)
hoặc sửa [nguồn JSON Archify](../.archify/architecture-g4-overview-20261025-155434/candidate.json).
Sơ đồ ghim source tại commit `e7997b03bd86bb5160332dd94bd5dc853b441963`;
các dấu `SRC` dẫn đến file và dòng tương ứng. Mở HTML bằng trình duyệt nếu trình xem
Markdown chỉ hiển thị source HTML.

Sơ đồ thể hiện đường đi browser → frontend MVC/proxy → API JWT/role → nghiệp vụ/worker
trong Infrastructure → SQL Server, cùng adapter PayPal Sandbox, carrier giả lập và SMTP.
SignalR nối trực tiếp browser với API. Các node nghiệp vụ, adapter, worker, SignalR và
carrier giả lập là vai trò trong cùng tiến trình API; Application/Contracts/Domain được
giải thích trong thẻ chi tiết. Carrier mặc định gọi về chính API và chỉ tạo nhãn ở
Development; SMTP chỉ gửi khi `Mail:Mode = Smtp`.

Kiểm tra tự động bằng Archify `finalize`: schema/bố cục, dẫn chứng Git, render, kiểm tra
HTML/SVG và Chrome ở 1440×900, 1600×1000, 1920×1080, 2048×1320 đều đạt. Chưa đánh giá
ảnh bằng mắt; một số điều khiển viewer dùng tiếng Anh. Chỉ tạo tài liệu và artifact;
không tạo/áp migration, không chạy ứng dụng hoặc kiểm thử database/provider thật.

## Cây thư mục backend

```text
backend/
├── G4.Api/
│   ├── Controllers/
│   │   ├── AuthenticationController.cs
│   │   └── FinanceController.cs
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
    ├── Authentication/
    ├── Payments/
    ├── Finance/
    ├── Shipping/
    ├── Returns/
    ├── Disputes/
    ├── Notifications/
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
| `G4.Infrastructure` | EF Core, migration, triển khai service, PayPal, carrier, refund và worker | Razor/UI và xử lý HTTP controller |

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

`G4.Domain`, `G4.Contracts` không tham chiếu Infrastructure hay Api. Controller nhận các interface như `ICheckoutService`, `IShipmentService`, `IReturnService`, `IPayPalPaymentService` và `ISellerFinanceService`; `Program.cs` nối interface với implementation tương ứng.

## Cách chia controller

Controller cũ chứa toàn bộ endpoint đã được tách theo chức năng:

| Controller | Trách nhiệm |
|---|---|
| `CatalogController` | Sản phẩm ngẫu nhiên và địa chỉ buyer |
| `CheckoutController` | Báo giá và tạo đơn |
| `OrdersController` | Danh sách, chi tiết, hủy và chuẩn bị đơn |
| `PaymentsController` | Credit, PayPal và refund |
| `ShippingController` | Tạo vận đơn, sự kiện tracking và mô phỏng lỗi carrier |
| `ReturnsController` | Yêu cầu, duyệt, từ chối và nhận hàng trả |
| `DisputesController` | Thương lượng, bằng chứng bất biến, phản hồi, quyết định và danh sách theo quyền |
| `NotificationsController` | Hộp thư thông báo |
| `FinanceController` | Số dư seller, level, payout và giữ/giải phóng tiền |
| `AuthenticationController` | Kiểm tra credential và cấp JWT chứa user ID/role |
| `CarrierSimulatorController` | API carrier cục bộ có kiểm tra key |

Các endpoint backend bắt đầu bằng `/api`. Frontend chuyển tiếp request qua `/api/proxy/{path}` và gắn JWT của phiên đăng nhập. API dùng role claim cùng user ID để kiểm tra quyền và quyền sở hữu order; client không thể tự đổi role bằng header.

## Quy tắc đặt tên

- Tên file/lớp mô tả trách nhiệm: `CheckoutService`, `CommerceMaintenanceWorker`, `CardPaymentSimulator`.
- Không dùng tên giai đoạn phát triển trong tên file/lớp, ví dụ `Mvp`, `Demo`, `Temp`, `New`.
- Dữ liệu hoặc dịch vụ giả lập phải được gọi đúng bản chất bằng `Simulator`, `Sandbox`, `Seed` hoặc `Test`.
- Mỗi controller/service chỉ xử lý một nhóm nghiệp vụ. Nếu file tăng quá lớn, tách theo use case thay vì thêm hậu tố phiên bản.
- Migration đã áp dụng không đổi migration ID. Chuỗi lịch sử `20260924112602_AddCheckoutShippingMvp` vẫn được giữ trong metadata và script SQL để database cũ không chạy lại migration; tên file và tên class đã đổi thành `AddCheckoutShipping`.

## Trình bày mã nguồn

- Dùng 4 dấu cách cho C#/Razor/project XML, 2 dấu cách cho JavaScript/CSS. Mục tiêu 100 ký tự/dòng; tách tham số, chuỗi gọi hàm, điều kiện dài và thuộc tính HTML ra nhiều dòng. Không dồn nhiều câu lệnh hoặc cả khối xử lý trên một dòng.
- `.editorconfig` lưu quy tắc chung; CSharpier 1.3.0 dùng `.csharpierrc.json`, Prettier 3.6.2 dùng `.prettierrc.json`. Giữ nguyên giá trị chuỗi/template literal khi format, kể cả khi chúng dài hơn mục tiêu; không sửa logic hoặc nội dung hiển thị để rút ngắn dòng.
- Chỉ format mã tự viết; bỏ qua `bin`, `obj`, `artifacts`, thư viện bên thứ ba và migration đã có. Razor cần giữ nguyên biểu thức, thuộc tính, nội dung và phần tử rỗng (tránh ảnh hưởng selector `:empty`).
- Sau format, kiểm tra diff, build và chạy bộ test liên quan. Đợt 23/10/2026: solution build sang thư mục riêng đạt 0 warning/error, backend mặc định và Node 128/128 đạt; không tạo hoặc áp migration. Chi tiết phạm vi ở mục FORMAT của backlog.

## Chạy và tạo migration

```powershell
dotnet run --project backend/G4.Api
dotnet ef database update --project backend/G4.Infrastructure --startup-project backend/G4.Api
dotnet ef migrations add TenMigration --project backend/G4.Infrastructure --startup-project backend/G4.Api --output-dir Persistence/Migrations
```

User Secrets tiếp tục nằm ở `G4.Api` và giữ nguyên `UserSecretsId`, nên PayPal Client ID/Secret đã cấu hình trên máy hiện tại vẫn được đọc. Khi dùng Visual Studio, đặt `G4.Api` và `frontend` làm multiple startup projects.

## Lưu ý chuyển tiếp

Việc refactor cấu trúc không tự thay schema. Gói tài chính có migration riêng `AddSellerFinance` vì bổ sung `SellerAccount`, `SellerSettlement`, `FinancialTransaction` và `SellerPayout`. `Finance/SellerFinanceService.cs` là nơi duy nhất điều phối số dư và ledger; controller không tự cộng/trừ tiền.

Quy trình tranh chấp dùng `Disputes/DisputeService.cs` và interface `IDisputeService`. Service điều phối trạng thái, quyền sở hữu và deadline; giao dịch giữ/giải phóng tiền đi qua `ISellerFinanceService`, trả hàng/hoàn tiền qua `IReturnService`. `TimeProvider` cho phép kiểm thử deadline không chờ thật. Migration `AddDisputeWorkflow` tạo lịch sử `DisputeEntry`, thêm `rowversion` và chỉ mục một hồ sơ đang mở/đơn. Xem [DISPUTES.md](DISPUTES.md).
