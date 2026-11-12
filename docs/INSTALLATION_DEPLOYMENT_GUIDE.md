# Hướng dẫn cài đặt và triển khai (Installation & Deployment Guide)

**Dự án:** G4_Project, mô phỏng sàn thương mại điện tử (checkout, giao hàng, trả hàng,
tranh chấp, tài chính seller). Môn PRN232, nhóm 4.

**Phiên bản tài liệu:** 12/11/2026, đối chiếu với source nhánh `master` (commit `2fe7bae`)
và migration mới nhất `20261111034557_AddDeliveryPhoneAndAddressChangeFields`.

Tài liệu này hướng dẫn cách dựng hệ thống từ source code đến khi đăng nhập được bằng
bốn vai trò. Cách dùng chức năng nằm ở End-user Manual và Administrator Manual.

---

## Mục lục

1. [Tổng quan kiến trúc triển khai](#1-tổng-quan-kiến-trúc-triển-khai)
2. [Yêu cầu hệ thống](#2-yêu-cầu-hệ-thống)
3. [Lấy source code và build](#3-lấy-source-code-và-build)
4. [Chọn cách cài đặt](#4-chọn-cách-cài-đặt)
5. [Cách A: Database demo có sẵn dữ liệu (khuyến nghị để chấm)](#5-cách-a-database-demo-có-sẵn-dữ-liệu)
6. [Cách B: Database mới từ đầu](#6-cách-b-database-mới-từ-đầu)
7. [Cách C: Chạy toàn bộ bằng Docker Compose](#7-cách-c-chạy-toàn-bộ-bằng-docker-compose)
8. [Cách D: Demo cân bằng tải bằng Nginx](#8-cách-d-demo-cân-bằng-tải-bằng-nginx)
9. [Cấu hình ứng dụng](#9-cấu-hình-ứng-dụng)
10. [Tích hợp bên ngoài (tùy chọn)](#10-tích-hợp-bên-ngoài-tùy-chọn)
11. [Kiểm tra sau cài đặt](#11-kiểm-tra-sau-cài-đặt)
12. [Nâng cấp phiên bản và migration](#12-nâng-cấp-phiên-bản-và-migration)
13. [Giới hạn của môi trường triển khai](#13-giới-hạn-của-môi-trường-triển-khai)
14. [Xử lý sự cố](#14-xử-lý-sự-cố)
15. [Tài liệu liên quan](#15-tài-liệu-liên-quan)

---

## 1. Tổng quan kiến trúc triển khai

```text
 Trình duyệt
     │  HTTP (cookie G4.Auth)
     ▼
 ┌──────────────────────┐   HTTP + JWT Bearer   ┌──────────────────────┐
 │ frontend (ASP.NET    │ ────────────────────▶ │ G4.Api (ASP.NET Core │
 │ Core MVC, Razor)     │                       │ Web API + SignalR +  │
 │ cổng 5131            │ ◀──── SignalR ──────  │ worker nền) cổng 5251│
 └──────────────────────┘   /hubs/commerce      └──────────┬───────────┘
                                                           │ EF Core 8
                         ┌─────────────────┬───────────────┼──────────────────┐
                         ▼                 ▼               ▼                  ▼
                   SQL Server        MailHog SMTP    PayPal Sandbox /   VietMap
                   (CloneEbayDB      1025, web 8025  Klarna Playground  Autocomplete
                    hoặc G4_Demo)                    (tùy chọn)         (tùy chọn)
```

| Thành phần | Project / công nghệ | Vai trò |
| --- | --- | --- |
| Frontend | `frontend/frontend.csproj`, ASP.NET Core 8 MVC | Giao diện bốn vai trò, đăng nhập cookie, chuyển tiếp lời gọi API |
| API | `backend/G4.Api/G4.Api.csproj`, ASP.NET Core 8 Web API | Nghiệp vụ, JWT, SignalR, rate limit, Swagger, worker nền |
| Lớp nghiệp vụ | `G4.Application`, `G4.Domain`, `G4.Contracts` | Quy tắc nghiệp vụ, model, DTO |
| Hạ tầng | `G4.Infrastructure` | EF Core SQL Server, migration, cổng thanh toán, email |
| Dùng chung | `shared/G4.WebCommon` | Rate limit, trusted proxy dùng chung cho API và frontend |
| Database | SQL Server 2019/2022 | Nguồn dữ liệu duy nhất khi chạy ứng dụng |
| Hộp thư thử | MailHog v1.0.1 | Nhận email thông báo, không gửi ra Internet |

### Bảng cổng mặc định

| Dịch vụ | Chạy trực tiếp (dotnet run) | Chạy demo (`Run-Demo.ps1`) | Docker Compose |
| --- | --- | --- | --- |
| Frontend | `http://localhost:5131` (HTTPS `7018`) | `http://localhost:5137` | `http://localhost:5000` |
| API | `http://localhost:5251` (HTTPS `7087`) | `http://localhost:5257` | `http://localhost:8080` |
| Swagger | `http://localhost:5251/swagger` | `http://localhost:5257/swagger` | `http://localhost:8080/swagger` |
| SQL Server | theo máy (thường `localhost,1433`) | theo máy | `localhost,14333` |
| MailHog | SMTP `1025`, web `http://localhost:8025` | không dùng (Mail Capture) | SMTP `1025`, web `8025` |
| Nginx (demo tải) | — | — | `http://127.0.0.1:8088` |

---

## 2. Yêu cầu hệ thống

### Phần cứng tối thiểu

| Hạng mục | Tối thiểu | Khuyến nghị |
| --- | --- | --- |
| CPU | 2 nhân | 4 nhân |
| RAM | 8 GB (chạy SQL Server cùng máy) | 16 GB nếu dùng Docker |
| Ổ đĩa trống | 5 GB | 10 GB (image Docker SQL Server khoảng 1,5 GB) |

### Phần mềm

| Phần mềm | Phiên bản | Bắt buộc? | Ghi chú |
| --- | --- | --- | --- |
| Windows 10/11 hoặc Linux/macOS | — | Có | Script helper (`.ps1`, `.cmd`) viết cho Windows PowerShell |
| .NET SDK | 8.0.x | Có | Kiểm tra: `dotnet --list-sdks`. Có thêm SDK 9 cũng được |
| SQL Server | 2019 hoặc 2022 (Express/Developer) | Có, trừ khi dùng Docker | Hoặc SQL Server qua Docker (cách C) |
| SSMS hoặc `sqlcmd` | SSMS 19+, sqlcmd 16+ | Có | Để chạy script `.sql` |
| Công cụ EF Core `dotnet-ef` | 8.0.x | Chỉ cách B/C | Áp migration lên database mới |
| Git | bất kỳ | Có | Lấy source code |
| Docker Desktop | 4.x, Linux containers | Chỉ cách C/D | |
| Visual Studio 2022 | 17.8+ | Không | Có thể chạy hoàn toàn bằng dòng lệnh |
| Node.js | 22 | Không | Chỉ để chạy bộ test JavaScript |

Cài công cụ EF Core (chỉ một lần trên máy):

```powershell
dotnet tool install --global dotnet-ef --version 8.0.*
```

### Mạng

- Cần Internet ở lần build đầu để `dotnet restore` tải gói NuGet.
- Lần đầu chạy `start-mailhog.cmd` sẽ tải MailHog v1.0.1 từ GitHub.
- PayPal Sandbox, Klarna Playground và VietMap chỉ cần khi bật các tích hợp đó.

---

## 3. Lấy source code và build

```powershell
git clone <địa-chỉ-repo-của-nhóm> G4_Project
cd G4_Project
dotnet restore G4_Project.sln
dotnet build G4_Project.sln
```

Build thành công khi dòng cuối báo `Build succeeded` và `0 Error(s)`.

Cấu trúc thư mục chính:

| Thư mục | Nội dung |
| --- | --- |
| `backend/` | API và các lớp Application, Domain, Contracts, Infrastructure |
| `frontend/` | Ứng dụng MVC (Views, `wwwroot/js`, `wwwroot/css`) |
| `shared/` | Thư viện dùng chung `G4.WebCommon` |
| `database/` | `baseline.sql`, `seed-data.sql`, SQL migration để duyệt, thư mục `demo/` |
| `deploy/nginx/` | Cấu hình Nginx cho demo chia tải |
| `scripts/` | `start-mailhog.ps1`, `Test-Infra.ps1` |
| `tests/` | Test backend (console), test JavaScript, E2E, JMeter |
| `docs/` | Tài liệu nghiệp vụ và kỹ thuật |

---

## 4. Chọn cách cài đặt

| Cách | Khi nào dùng | Database | Thời gian |
| --- | --- | --- | --- |
| **A. Database demo** | Chấm bài, trình diễn: có sẵn tài khoản, đơn hàng ở mọi trạng thái, ví, nợ, tranh chấp | `G4_Demo` | ~10 phút |
| **B. Database mới** | Cài sạch, phát triển tiếp | `CloneEbayDB` | ~15 phút |
| **C. Docker Compose** | Không muốn cài SQL Server lên máy | `CloneEbayDB` trong container | ~20 phút |
| **D. Nginx** | Trình diễn cân bằng tải và failover API catalog | `G4_Demo` trong container | ~20 phút |

> **Lưu ý quan trọng:** hệ thống hiện chỉ chạy ở môi trường `Development`
> (`ASPNETCORE_ENVIRONMENT=Development`). Ngoài Development, các API nghiệp vụ trả
> HTTP 403 và các trang đăng ký bị ẩn. Mọi cách cài đặt bên dưới đã đặt sẵn biến này.
> Xem [mục 13](#13-giới-hạn-của-môi-trường-triển-khai).

---

## 5. Cách A: Database demo có sẵn dữ liệu

Bộ `database/demo/G4_Demo.sql` tạo database `G4_Demo` với **đủ schema** (baseline và toàn
bộ migration đến `20261111034557`) cùng dữ liệu trình diễn. **Không** cần chạy
`baseline.sql`, `seed-data.sql` hay `dotnet ef database update`.

### Bước 1: Tạo database `G4_Demo`

**Bằng SSMS:** mở `database/demo/G4_Demo.sql` → kết nối SQL Server → Execute (F5).

**Bằng sqlcmd** (Windows Authentication, chạy tại thư mục gốc repo):

```powershell
sqlcmd -S localhost -E -C -f 65001 -i database/demo/G4_Demo.sql
```

- `-f 65001` đọc file ở dạng UTF-8 để giữ đúng tiếng Việt.
- Nếu SQL Server dùng instance riêng, thay `localhost` bằng tên đó, ví dụ `.\SQLEXPRESS`.
- Script **xóa `G4_Demo` cũ rồi tạo lại** (biến `@ReplaceExisting = 1`). Script chỉ nhận
  tên bắt đầu bằng `G4_Demo` nên không chạm vào `CloneEbayDB`. Tắt API demo trước khi
  chạy lại.

### Bước 2: Chạy API và frontend

Mở **hai** cửa sổ PowerShell tại thư mục gốc repo:

```powershell
powershell -ExecutionPolicy Bypass -File .\database\demo\Run-Demo.ps1 -App Api -SqlServer localhost
```

```powershell
powershell -ExecutionPolicy Bypass -File .\database\demo\Run-Demo.ps1 -App Frontend
```

Helper chỉ đặt biến môi trường cho tiến trình đang chạy, không sửa `appsettings` hay
User Secrets. Nó dùng:

- Database `G4_Demo` qua Windows Authentication.
- Khóa HMAC demo cố định khớp với định danh seller giả lập trong dữ liệu.
- `Mail:Mode = Capture`: email chỉ lưu trong ứng dụng, không cần MailHog.
- Bộ đếm thời gian demo dài hơn (cửa sổ trả hàng, tranh chấp 6 giờ) để kịp trình diễn.

Nếu SQL Server dùng instance khác, đổi `-SqlServer`. Nếu chạy script từ ngoài repo, thêm
`-ProjectRoot 'D:\đường\dẫn\G4_Project'`.

### Bước 3: Đăng nhập

Mở `http://localhost:5137`. Mật khẩu chung cho mọi tài khoản demo: **`G4@123456`**.

| Email | Vai trò |
| --- | --- |
| `buyer@demo.test` | Buyer |
| `seller.l1@demo.test` | Seller cấp 1 (có đơn hàng, trả hàng, tranh chấp) |
| `seller.l3@demo.test` | Seller cấp 3 (tiền vào ví ngay) |
| `shipper@demo.test` | Shipper |
| `admin@demo.test` | Admin |

Danh sách đầy đủ 16 tài khoản và các kịch bản dựng sẵn (mã đơn, thao tác, kết quả)
nằm trong [database/demo/HUONG_DAN_DEMO.md](../database/demo/HUONG_DAN_DEMO.md).

---

## 6. Cách B: Database mới từ đầu

### Bước 1: Tạo schema nền

Chạy [database/baseline.sql](../database/baseline.sql) trên database **trống**. Script chứa
`CREATE DATABASE CloneEbayDB`.

```powershell
sqlcmd -S localhost -E -C -f 65001 -i database/baseline.sql
```

> **Không chạy lại `baseline.sql`** trên database đã có bảng hoặc dữ liệu.

### Bước 2: Khai báo connection string

Giá trị mẫu trong `backend/G4.Api/appsettings.json` trỏ tới SQL Server Docker cổng `14333`.
Với SQL Server cài trên máy, đặt connection string bằng User Secrets để không ghi mật khẩu
vào source:

```powershell
# Windows Authentication
dotnet user-secrets set "ConnectionStrings:DefaultConnection" "Server=localhost;Database=CloneEbayDB;Integrated Security=True;TrustServerCertificate=True;" --project backend/G4.Api

# Hoặc SQL Authentication
dotnet user-secrets set "ConnectionStrings:DefaultConnection" "Server=localhost;Database=CloneEbayDB;User Id=sa;Password=<mật-khẩu>;TrustServerCertificate=True;" --project backend/G4.Api
```

Trong Visual Studio: chuột phải project `G4.Api` → **Manage User Secrets**.

### Bước 3: Áp migration

```powershell
dotnet ef database update --project backend/G4.Infrastructure --startup-project backend/G4.Api
```

Lệnh áp lần lượt 32 migration trong `backend/G4.Infrastructure/Persistence/Migrations`.
Migration đầu `InitialCreate` không tạo bảng; bảng nền đến từ bước 1. Kiểm tra trạng thái:

```powershell
dotnet ef migrations list --project backend/G4.Infrastructure --startup-project backend/G4.Api
```

Nếu muốn duyệt SQL trước khi áp, xem các file `database/*-migration.sql`.

### Bước 4: Nạp dữ liệu tham chiếu

```powershell
sqlcmd -S localhost -E -C -f 65001 -i database/seed-data.sql
```

Script thêm hoặc cập nhật tài khoản bốn vai trò, cửa hàng, sản phẩm, tồn kho, địa chỉ và
coupon `G4SAVE10`. Chạy lại an toàn: không tạo bản ghi trùng, không đặt lại tồn kho đã bị
đơn trừ.

### Bước 5: Bật MailHog (hộp thư thử)

Chọn **một** trong hai cách:

- Bấm đúp `start-mailhog.cmd` ở thư mục gốc (Windows, tự mở `http://localhost:8025`).
- Hoặc dùng Docker: `docker compose up -d --no-deps mailhog`.

Nếu không muốn chạy MailHog, đặt `Mail:Mode` thành `Capture` (xem [mục 9](#9-cấu-hình-ứng-dụng)).

### Bước 6: Chạy API và frontend

Hai cửa sổ dòng lệnh khác nhau, tại thư mục gốc repo:

```powershell
dotnet run --project backend/G4.Api --urls http://localhost:5251
```

```powershell
dotnet run --project frontend --urls http://localhost:5131
```

Mở `http://localhost:5131` và đăng nhập. Mật khẩu chung: **`G4@123456`**.

| Email | Vai trò |
| --- | --- |
| `buyer@example.test` | Buyer |
| `seller@example.test`, `seller2@example.test` | Seller (giỏ hàng random cần hai seller) |
| `shipper@example.test` | Shipper |
| `admin@example.test` | Admin |

### Chạy bằng Visual Studio 2022 (thay cho bước 6)

1. Mở `G4_Project.sln`, đợi restore xong.
2. Chuột phải Solution → **Configure Startup Projects** → **Multiple startup projects**.
3. Đặt `G4.Api` và `frontend` là **Start**, các project khác để **None**.
4. Chọn profile **`http`** cho cả hai project.
5. Nhấn **Start**. Frontend mở `http://localhost:5131`, Swagger mở `http://localhost:5251/swagger`.

> Nên dùng profile `http`. Frontend gọi API qua `ApiSettings:BaseUrl = http://localhost:5251`.
> Nếu API chạy profile `https`, API tự chuyển hướng sang HTTPS và trình gọi HTTP bỏ header
> `Authorization`, làm các lời gọi bị từ chối. Muốn dùng HTTPS thì phải đổi
> `ApiSettings:BaseUrl` của frontend thành `https://localhost:7087`.

---

## 7. Cách C: Chạy toàn bộ bằng Docker Compose

File [docker-compose.yml](../docker-compose.yml) dựng bốn container trong mạng `g4-net`:

| Container | Image | Cổng trên máy |
| --- | --- | --- |
| `g4-sql` | `mcr.microsoft.com/mssql/server:2022-latest` | `14333 → 1433` |
| `g4-mailhog` | `mailhog/mailhog:v1.0.1` | `127.0.0.1:1025`, `127.0.0.1:8025` |
| `g4-api` | build từ `backend/G4.Api/Dockerfile` | `8080` |
| `g4-mvc` | build từ `frontend/Dockerfile` | `5000 → 8080` |

Compose **không tự tạo database**, nên phải chuẩn bị database trước khi bật API.

### Bước 1: Bật SQL Server và MailHog

```powershell
docker compose up -d sqlserver mailhog
```

Đợi khoảng 20–30 giây cho SQL Server sẵn sàng.

### Bước 2: Tạo schema và dữ liệu

Từ máy host (cần `sqlcmd` và `dotnet-ef`). Connection string mặc định trong
`appsettings.json` đã trỏ đúng `localhost,14333` với tài khoản `sa`:

```powershell
sqlcmd -S localhost,14333 -U sa -P "G4@123456Strong!" -C -f 65001 -i database/baseline.sql
dotnet ef database update --project backend/G4.Infrastructure --startup-project backend/G4.Api
sqlcmd -S localhost,14333 -U sa -P "G4@123456Strong!" -C -f 65001 -i database/seed-data.sql
```

> Nếu đã đặt User Secrets `ConnectionStrings:DefaultConnection` ở cách B, secret đó sẽ
> được ưu tiên. Khi đó thêm tham số
> `--connection "Server=localhost,14333;Database=CloneEbayDB;User Id=sa;Password=G4@123456Strong!;TrustServerCertificate=True;"`
> vào lệnh `dotnet ef database update`.

### Bước 3: Build và chạy API, frontend

```powershell
docker compose up -d --build
docker compose ps
```

Mở `http://localhost:5000` để dùng giao diện, `http://localhost:8080/swagger` để xem API và
`http://localhost:8025` để xem email. Tài khoản giống cách B.

### Lệnh vận hành thường dùng

```powershell
docker compose logs -f backend      # xem log API
docker compose restart backend      # khởi động lại API
docker compose down                 # dừng, giữ dữ liệu SQL trong volume sql_data
docker compose down -v              # dừng và XÓA dữ liệu SQL (không hoàn tác được)
```

Các biến môi trường quan trọng trong Compose: `ConnectionStrings__DefaultConnection` trỏ
`sqlserver,1433`, `Mail__Host=mailhog`, `ApiSettings__BaseUrl=http://backend:8080` (frontend
gọi API trong mạng Docker), `Realtime__PublicApiBaseUrl=http://localhost:8080` (trình duyệt
kết nối SignalR) và `TrustedProxies__Hosts__0=frontend`.

> Container API tự sinh khóa HMAC định danh seller trong thư mục `.local-security` của
> container. Khóa mất khi container bị tạo lại, làm dữ liệu định danh seller cũ không còn
> khớp. Nếu cần giữ lâu dài, truyền `SellerIdentity__HmacKey` cố định qua biến môi trường.

---

## 8. Cách D: Demo cân bằng tải bằng Nginx

File [docker-compose.nginx.yml](../docker-compose.nginx.yml) dựng một SQL Server riêng, hai
bản API `api-a`/`api-b` cùng image và Nginx nghe tại `http://127.0.0.1:8088`.

- `GET /api/catalog/random` chia luân phiên cho hai API; header `X-G4-Upstream` cho biết
  instance trả lời. Khi tắt một API, Nginx tự chuyển sang API còn lại.
- `POST /api/auth/login` chỉ đi tới `api-a`. Các route khác trả 404.
- Worker nền bị tắt (`Workers__Enabled=false`) để hai API không xử lý trùng.

Chạy toàn bộ dựng stack và kiểm tra bằng một lệnh:

```powershell
powershell -ExecutionPolicy Bypass -File scripts\Test-Infra.ps1
```

Script đợi SQL Server, nhập `G4_Demo` nếu chưa có, build image, rồi kiểm tra chia tải,
failover, JMeter và rate limit, cuối cùng in bảng PASS/FAIL. Các bước làm tay nằm ở
[NGINX.md](NGINX.md).

---

## 9. Cấu hình ứng dụng

### Thứ tự ưu tiên cấu hình

ASP.NET Core đọc cấu hình theo thứ tự sau, nguồn sau ghi đè nguồn trước:

1. `appsettings.json`
2. `appsettings.Development.json`
3. User Secrets (chỉ khi môi trường Development)
4. Biến môi trường. Dấu `:` thay bằng `__`, ví dụ `Mail__Mode=Capture`
5. Tham số dòng lệnh, ví dụ `--urls`

> **Không ghi mật khẩu, client secret hay API key vào file trong repo.** Dùng User Secrets
> hoặc biến môi trường.

### Cấu hình API (`backend/G4.Api`)

| Khóa | Mặc định | Ý nghĩa |
| --- | --- | --- |
| `ConnectionStrings:DefaultConnection` | SQL Docker `localhost,14333`, `CloneEbayDB` | Kết nối SQL Server |
| `Authentication:JwtKey` | khóa Development trong `appsettings.Development.json` | Khóa ký JWT. **Bắt buộc**; thiếu thì API không khởi động |
| `Authentication:Issuer` / `Audience` | `G4.Api` / `G4.Frontend` | Thông tin kiểm tra JWT |
| `SellerIdentity:HmacKey`, `KeyId` | tự sinh trong `backend/G4.Api/.local-security/` | Khóa băm số định danh seller. Phải giữ nguyên cùng database |
| `Frontend:PublicBaseUrl` | `http://localhost:5131` | URL frontend dùng trong email và trang trả về của PayPal/Klarna |
| `Realtime:AllowedOrigins` | `5131`, `7018`, `127.0.0.1:5131` | Origin được phép kết nối SignalR (CORS) |
| `Mail:Mode` | `Smtp` | `Smtp` gửi tới MailHog; `Capture` chỉ lưu trong ứng dụng |
| `Mail:Host`, `Port`, `EnableSsl`, `From` | `127.0.0.1`, `1025`, `false`, `g4@example.test` | Máy chủ SMTP |
| `Carrier:BaseUrl`, `Carrier:Key` | `http://localhost:5251`, `local-carrier-key` | Bộ giả lập hãng vận chuyển nằm trong API |
| `Workers:Enabled` | `true` | Bật worker nền: hết hạn, đối soát, giải phóng tiền giữ, xét cấp, write-off |
| `TrustedProxies:Addresses`, `Hosts` | `127.0.0.1`, `::1` | Proxy được tin header `X-Forwarded-For` |
| `RateLimiting:*` | xem `appsettings.json` | Giới hạn theo nhóm login, registration, read, write, payment, finance |
| `PasswordConfirmation:PermitLimit`, `WindowSeconds` | `5`, `60` | Số lần nhập lại mật khẩu cho thao tác quan trọng |
| `PayPal:ClientId`, `ClientSecret` | trống | Thông tin ứng dụng PayPal **Sandbox** |
| `Klarna:Username`, `Password` | trống | Thông tin Klarna **Playground** |
| `VietMap:ApiKey` | trống | Khóa gợi ý địa chỉ Việt Nam |

### Cấu hình frontend (`frontend`)

| Khóa | Mặc định | Ý nghĩa |
| --- | --- | --- |
| `ApiSettings:BaseUrl` | `http://localhost:5251` | Địa chỉ frontend gọi API (phía server) |
| `Realtime:PublicApiBaseUrl` | `http://localhost:5251` | Địa chỉ trình duyệt kết nối SignalR |
| `TrustedProxies:*`, `RateLimiting:*` | | Giới hạn đăng nhập/đăng ký theo IP |

Khi đổi cổng API, phải sửa đồng thời `ApiSettings:BaseUrl`, `Realtime:PublicApiBaseUrl` của
frontend và `Carrier:BaseUrl` của API. Khi đổi cổng frontend, sửa `Frontend:PublicBaseUrl` và
`Realtime:AllowedOrigins` của API.

### Thời hạn nghiệp vụ theo môi trường

`appsettings.Development.json` rút ngắn thời hạn để trình diễn nhanh:

| Khóa | Development | Mặc định (`appsettings.json` / code) |
| --- | --- | --- |
| `Checkout:PaymentHoldMinutes` | 5 phút | 5760 phút (4 ngày) |
| `Returns:WindowSeconds` | 300 giây | 7 ngày |
| `Returns:ReceiptSeconds` | 45 giây | 48 giờ |
| `Disputes:SellerResponseSeconds` | 45 giây | 3 ngày làm việc |
| `Disputes:BuyerResponseSeconds` | 45 giây | 3 ngày |
| `AddressChanges:ConfirmationSeconds` | 120 giây | 24 giờ |
| `Finance:Levels:1/2:HoldSeconds` | 300 / 180 giây | 21 / 7 ngày |
| `Finance:Debt:DueSeconds` | 60 giây | 7 ngày |
| `Finance:LevelPromotion:EvaluationSeconds` | 60 giây | 3600 giây |

`Run-Demo.ps1` (cách A) dùng bộ thời hạn riêng khoảng 6 giờ, khớp với dữ liệu `G4_Demo`.

---

## 10. Tích hợp bên ngoài (tùy chọn)

Các tích hợp dưới đây đều không bắt buộc. Thiếu cấu hình thì nút tương ứng báo chưa cấu hình,
các luồng còn lại vẫn chạy bằng thẻ giả lập.

### Thẻ giả lập (luôn có sẵn)

| Số thẻ | Hạn | Kết quả |
| --- | --- | --- |
| `4111111111111111` | `12/30` | Thành công |
| `4000000000000002` | `12/30` | Bị từ chối |
| bất kỳ số trên | tháng/năm đã qua | Hết hạn |

Đây chỉ là mô phỏng. **Không nhập thẻ thật.**

### PayPal Sandbox

Tạo ứng dụng tại PayPal Developer Dashboard (chế độ Sandbox), rồi:

```powershell
dotnet user-secrets set "PayPal:ClientId" "<sandbox-client-id>" --project backend/G4.Api
dotnet user-secrets set "PayPal:ClientSecret" "<sandbox-client-secret>" --project backend/G4.Api
```

Khi thanh toán, đăng nhập bằng tài khoản buyer sandbox của PayPal.

### Klarna Playground

```powershell
dotnet user-secrets set "Klarna:Username" "<playground-username>" --project backend/G4.Api
dotnet user-secrets set "Klarna:Password" "<playground-password>" --project backend/G4.Api
```

Chi tiết luồng thanh toán ở [KLARNA_PAY_LATER.md](KLARNA_PAY_LATER.md).

### VietMap (gợi ý địa chỉ Việt Nam)

```powershell
dotnet user-secrets set "VietMap:ApiKey" "<key>" --project backend/G4.Api
```

Gói thử cho 500 lượt Autocomplete mỗi ngày. Thiếu khóa thì ô tìm địa chỉ bị ẩn, người dùng
nhập tay.

Sau khi đổi secret, khởi động lại API.

---

## 11. Kiểm tra sau cài đặt

### Danh sách kiểm tra nhanh

| # | Kiểm tra | Kết quả mong đợi |
| --- | --- | --- |
| 1 | Mở Swagger của API | Trang Swagger UI hiện danh sách endpoint |
| 2 | Mở frontend | Trang đăng nhập hiện ra |
| 3 | Đăng nhập lần lượt buyer, seller, shipper, admin | Mỗi tài khoản vào đúng trang của vai trò, không thấy trang vai trò khác |
| 4 | Buyer random giỏ hàng, thanh toán bằng thẻ `4111111111111111` | Đơn chuyển sang đã thanh toán |
| 5 | Mở MailHog `http://localhost:8025` (cách B/C) | Có email "Payment succeeded" của đơn vừa trả |
| 6 | Seller tạo vận đơn, shipper nhận và cập nhật tracking | Timeline đơn cập nhật ngay trên trang buyer (SignalR) |

### Kiểm thử tự động

```powershell
dotnet build G4_Project.sln
dotnet run --project tests/G4.Commerce.Tests      # test backend, dùng EF InMemory
node --test tests/*.test.cjs                      # test giao diện JavaScript (cần Node 22)
./tests/smoke-commerce.ps1                         # cần API + frontend đang chạy trên database có seed-data
```

Pipeline CI (`.github/workflows/ci.yml`) tự chạy restore, build Release, test backend và test
JavaScript cho mỗi pull request và lần push vào `master`.

---

## 12. Nâng cấp phiên bản và migration

Khi lấy source mới về một máy đã có database:

1. **Sao lưu database** trước khi đổi schema:

   ```sql
   BACKUP DATABASE CloneEbayDB TO DISK = N'D:\Backup\CloneEbayDB.bak' WITH INIT, COMPRESSION;
   ```

   (SQL Server Express không hỗ trợ `COMPRESSION`; bỏ tùy chọn này.)
2. Dừng API và frontend.
3. `git pull`, rồi `dotnet build G4_Project.sln`.
4. Áp các migration còn thiếu:

   ```powershell
   dotnet ef database update --project backend/G4.Infrastructure --startup-project backend/G4.Api
   ```

5. Khởi động lại API, frontend và tải lại trang trên trình duyệt.

Nguyên tắc:

- **Không chạy lại `baseline.sql`** trên database có dữ liệu.
- Không khởi động API mới trên schema cũ. API sẽ lỗi khi truy vấn cột chưa tồn tại.
- Migration chỉ thêm cột/bảng/index và giữ dữ liệu, snapshot, lịch sử cũ. Muốn duyệt SQL
  trước khi áp, xem file `database/<tên>-migration.sql` tương ứng.
- Với `G4_Demo`, chỉ cần chạy lại `G4_Demo.sql` để có bộ dữ liệu sạch theo schema mới nhất.

Khôi phục khi cần quay lại:

```sql
RESTORE DATABASE CloneEbayDB FROM DISK = N'D:\Backup\CloneEbayDB.bak' WITH REPLACE;
```

---

## 13. Giới hạn của môi trường triển khai

Đây là hệ thống đồ án mô phỏng, chưa sẵn sàng cho môi trường production thật:

- **Chỉ chạy ở môi trường Development.** API nghiệp vụ trả 403 ngoài Development; đăng ký
  buyer/seller, Swagger và bộ giả lập hãng vận chuyển cũng chỉ có ở Development.
- **Tiền và thanh toán đều giả lập:** thẻ mô phỏng, PayPal Sandbox, Klarna Playground, ngân
  hàng payout giả lập. Không dùng thẻ, tài khoản hay tiền thật.
- **Khóa bảo mật Development có trong repo** (`Authentication:JwtKey`, khóa HMAC demo). Nếu
  đưa lên máy chủ dùng chung, phải thay bằng khóa riêng qua biến môi trường.
- **Rate limit đếm trong bộ nhớ từng instance.** Chạy nhiều instance API thì mỗi instance có
  quota riêng (đã thấy trong demo Nginx).
- **Worker nền nên chỉ bật trên một instance** khi nhiều API dùng chung database
  (`Workers__Enabled=false` cho các instance còn lại).
- **Khóa Data Protection của frontend** lưu trong `frontend/.data-protection-keys/`. Xóa thư
  mục này thì mọi phiên đăng nhập cũ bị hủy, người dùng chỉ cần đăng nhập lại.

---

## 14. Xử lý sự cố

| Hiện tượng | Nguyên nhân thường gặp | Cách xử lý |
| --- | --- | --- |
| API dừng ngay với lỗi `Authentication:JwtKey must be configured` | Không chạy ở Development nên không đọc `appsettings.Development.json` | Đặt `ASPNETCORE_ENVIRONMENT=Development` hoặc cấp `Authentication__JwtKey` |
| `A network-related or instance-specific error` khi API khởi động hoặc chạy EF | Sai server/cổng trong connection string, SQL Server chưa bật | Kiểm tra dịch vụ SQL Server, tên instance, cổng; với Docker dùng `localhost,14333` |
| `The certificate chain was issued by an authority that is not trusted` | Thiếu tùy chọn tin chứng chỉ | Thêm `TrustServerCertificate=True;` vào connection string, thêm `-C` cho `sqlcmd` |
| `Invalid column name ...` hoặc `Invalid object name ...` | Database thiếu migration | Chạy `dotnet ef database update` (mục 12) |
| `There is already an object named ...` khi chạy `baseline.sql` | Chạy baseline trên database đã có bảng | Không chạy lại baseline; chỉ áp migration |
| `dotnet ef` báo không tìm thấy lệnh | Chưa cài công cụ EF | `dotnet tool install --global dotnet-ef --version 8.0.*` |
| Đăng nhập được nhưng các trang báo lỗi 401/403 | API chạy HTTPS profile trong khi frontend gọi HTTP, hoặc API không ở Development | Dùng profile `http` cho cả hai (mục 6) |
| Không thấy email | MailHog chưa bật | Chạy `start-mailhog.cmd`, hoặc đặt `Mail__Mode=Capture` để xem email trong ứng dụng |
| Thông báo không cập nhật theo thời gian thực | Origin frontend không có trong `Realtime:AllowedOrigins` hoặc sai `Realtime:PublicApiBaseUrl` | Cập nhật hai khóa này theo cổng thực tế rồi khởi động lại |
| Đăng nhập báo HTTP 429 | Vượt giới hạn đăng nhập (10 lần/60 giây theo IP) | Đợi khoảng 1 phút |
| Định danh seller cũ báo không khớp | Khóa HMAC bị đổi hoặc mất thư mục `.local-security` | Khôi phục khóa cũ; với `G4_Demo` luôn chạy qua `Run-Demo.ps1` |
| `Address already in use` / cổng bị chiếm | Một tiến trình khác dùng cổng 5131/5251 | Tắt tiến trình đó hoặc chạy với `--urls` cổng khác và cập nhật cấu hình (mục 9) |
| `Run-Demo.ps1` báo không chạy được script | Execution Policy của PowerShell | Chạy kèm `powershell -ExecutionPolicy Bypass -File ...` như hướng dẫn |

---

## 15. Tài liệu liên quan

| Tài liệu | Nội dung |
| --- | --- |
| [PROJECT_RUN.md](PROJECT_RUN.md) | Nhật ký chạy dự án theo từng đợt thay đổi và migration |
| [DEMO_DATABASE.md](DEMO_DATABASE.md), [HUONG_DAN_DEMO.md](../database/demo/HUONG_DAN_DEMO.md) | Dữ liệu demo, tài khoản, kịch bản |
| [BACKEND_ARCHITECTURE.md](BACKEND_ARCHITECTURE.md) | Phân lớp project và nơi đặt code |
| [AUTHORIZATION_AND_ROLE_PAGES.md](AUTHORIZATION_AND_ROLE_PAGES.md) | Đăng nhập, JWT, phân quyền bốn vai trò |
| [RATE_LIMITING.md](RATE_LIMITING.md) | Cấu hình rate limit và proxy tin cậy |
| [NGINX.md](NGINX.md), [JMETER.md](JMETER.md) | Demo chia tải và kiểm thử tải |
| [EMAIL_INTEGRATION_TEST_GUIDE.md](EMAIL_INTEGRATION_TEST_GUIDE.md) | Email, MailHog, nhật ký tích hợp |
| [COMMERCE_REFRESH.md](COMMERCE_REFRESH.md) | PayPal giỏ hàng, SignalR, cấu hình URL/CORS |
