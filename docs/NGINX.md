# Demo chia tải API catalog bằng Nginx

## Chạy toàn bộ kiểm tra bằng một lệnh

[`scripts/Test-Infra.ps1`](../scripts/Test-Infra.ps1) dựng stack bên dưới (đợi SQL
Server, import `G4_Demo` nếu chưa có, build image), rồi kiểm tra lần lượt: chia tải
qua 2 API, failover khi tắt `api-b`, JMeter đo tải và JMeter 429, rate limit login
và run CI GitHub Actions mới nhất trên `master`. Cuối script in bảng PASS/FAIL/SKIP;
có FAIL thì exit code 1. Jira và K8s không nằm trong script.

```powershell
powershell -ExecutionPolicy Bypass -File scripts\Test-Infra.ps1
```

Tùy chọn: `-SkipBuild`, `-SkipJMeter`, `-SkipCi`, `-LocalCi` (chạy lại restore,
build Release và test như CI), `-Down` (tắt stack khi xong), `-JMeterPath`. Script
dùng `buyer@demo.test` của database demo và tạm tắt `api-b` khi đo 429 vì mỗi API
đếm quota riêng; sau đó tự bật lại. Bước login sai 12 lần làm IP bị chặn login
khoảng 60 giây. Báo cáo JMeter nằm trong `artifacts/jmeter/infra-<thời gian>/`.

Stack [Docker Compose](../docker-compose.nginx.yml) chạy Nginx, hai bản API
`api-a`/`api-b` dùng cùng image, Redis SignalR backplane và một SQL Server demo
riêng. Nginx nghe trên `http://127.0.0.1:8088`. SQL Server, Redis và hai API chỉ
có mạng nội bộ Docker.

- `GET /api/catalog/random?count=3` đi luân phiên qua hai API. Header
  `X-G4-Upstream` và log Nginx ghi địa chỉ upstream. Nếu thử lại sau lỗi kết nối,
  địa chỉ cuối cùng trong header là instance trả phản hồi.
- `POST /api/auth/login` đi riêng tới `api-a` để lấy JWT buyer. Nginx gửi request
  đăng nhập một lần. Cả hai API dùng cùng khóa JWT demo.
- `/api/` và `/hubs/commerce` đi qua cả hai API. Nginx hỗ trợ WebSocket cho hub;
  `hub_backends` dùng `ip_hash` giữ negotiate/WebSocket của một IP ở cùng API;
  Redis chuyển tiếp thông điệp SignalR giữa các instance.
- Chỉ `api-a` chạy `CommerceChangeWorker` qua cấu hình riêng
  `Realtime:DispatcherEnabled`; cả hai API vẫn tắt worker bảo trì. `api-b` không
  chạy dispatcher để tránh hai tiến trình cùng phát một outbox event. Nếu `api-a` dừng, realtime dispatcher
  tạm dừng tới khi `api-a` chạy lại. Đây là cấu hình demo một dispatcher, chưa
  phải worker HA cho production/Kubernetes.
- Redis chỉ là backplane truyền thông điệp, không phải nguồn dữ liệu nghiệp vụ.
  `CommerceChange` và inbox vẫn nằm trong SQL; client tải bù qua API sau reconnect.

## Chạy lần đầu trên Docker Desktop

Chạy PowerShell tại thư mục gốc repo. Docker Desktop cần dùng Linux containers.

```powershell
docker compose -f docker-compose.nginx.yml up -d sqlserver

$demoPassword = 'G4@123456Strong!'
$sql = @(
    '-T', '-e', "SQLCMDPASSWORD=$demoPassword", 'sqlserver',
    '/opt/mssql-tools18/bin/sqlcmd', '-S', 'localhost', '-U', 'sa', '-C', '-I', '-b'
)
docker compose -f docker-compose.nginx.yml exec @sql -Q 'SELECT 1'
```

Chờ SQL Server sẵn sàng và chạy lại `SELECT 1` nếu kết nối đầu tiên báo lỗi.
Kiểm tra database trước khi import. Chỉ nhập một lần nếu truy vấn không trả về
`G4_Demo`; database nằm trong volume Compose riêng, không phải database local khác.

```powershell
docker compose -f docker-compose.nginx.yml exec @sql `
    -Q "SELECT name FROM sys.databases WHERE name = 'G4_Demo'"
```

Nếu chưa có `G4_Demo`, chạy hai lệnh import bên dưới. Nếu đã có thì bỏ qua chúng.

```powershell
docker compose -f docker-compose.nginx.yml cp database/demo/G4_Demo.sql sqlserver:/tmp/G4_Demo.sql
docker compose -f docker-compose.nginx.yml exec @sql -f 65001 -i /tmp/G4_Demo.sql
docker compose -f docker-compose.nginx.yml build api-a
docker compose -f docker-compose.nginx.yml up -d
docker compose -f docker-compose.nginx.yml exec nginx nginx -t
```

Project Compose riêng dùng volume `g4-nginx-demo_catalog_sql` và tài khoản demo.
Dừng stack bằng `docker compose -f docker-compose.nginx.yml down` để giữ dữ liệu
cho lần demo sau.

## Chạy giao diện để demo SignalR

Sau khi Compose đã chạy, mở PowerShell **thứ hai** tại thư mục repo. Trỏ API proxy
và URL hub của frontend tới Nginx public, rồi chạy frontend:

```powershell
Set-Location D:\PRN232_G4_Project
$env:ApiSettings__BaseUrl = 'http://127.0.0.1:8088'
$env:Realtime__PublicApiBaseUrl = 'http://127.0.0.1:8088'
dotnet run --project frontend --urls http://localhost:5131
```

Mở `http://localhost:5131` và đăng nhập `buyer@demo.test` với mật khẩu demo
`G4@123456`. Nếu đã có phiên đăng nhập thì dùng lại. CORS Development đã cho
phép origin frontend `http://localhost:5131`. Giữ cửa sổ frontend mở trong lúc demo.

Để quan sát sự kiện trên UI, mở buyer trong hai tab cùng tài khoản và mở chuông
thông báo ở cả hai. Ở một tab, đánh dấu một thông báo chưa đọc; tab còn lại cần
tự cập nhật trạng thái/badge mà không reload. Có thể mở DevTools → Network → WS →
`/hubs/commerce` → Messages/Frames để xem frame `CommerceChanged`. Hub dùng
`ip_hash`, nên cùng máy sẽ vào cùng API instance; dispatcher của demo chỉ chạy
trên `api-a`, còn Redis chuyển sự kiện tới API giữ hub nếu đó là `api-b`.

## Kiểm tra chia tải

Dùng bước đăng nhập PowerShell trong [hướng dẫn JMeter](JMETER.md#chuẩn-bị), đặt
`$apiHost = '127.0.0.1'` và `$apiPort = 8088` trước khi tạo `$baseUrl` và `$target`.

```powershell
$headers = @{ Authorization = "Bearer $env:G4_JMETER_BUYER_TOKEN" }
1..6 | ForEach-Object {
    $response = Invoke-WebRequest "$baseUrl/api/catalog/random?count=3" `
        -Headers $headers -UseBasicParsing
    [PSCustomObject]@{
        Status = $response.StatusCode
        Upstream = $response.Headers['X-G4-Upstream'] -join ', '
    }
}
docker compose -f docker-compose.nginx.yml logs --tail 20 nginx
```

Kỳ vọng có HTTP 200 từ hai địa chỉ upstream khác nhau. Có thể dừng `api-b`,
gửi lại request catalog với token đã lấy rồi bật lại `api-b`. Nginx thử instance
còn lại khi gặp lỗi kết nối upstream trên route đọc. Login vẫn đi qua `api-a`.

```powershell
docker compose -f docker-compose.nginx.yml stop api-b
# Gửi lại GET catalog, sau đó bật lại instance đã dừng.
docker compose -f docker-compose.nginx.yml start api-b
```

## Kiểm tra realtime xuyên instance

Đăng nhập qua `/api/auth/login`, kết nối SignalR bằng JWT tới
`http://127.0.0.1:8088/hubs/commerce`, rồi gửi một thao tác nghiệp vụ hợp lệ
qua API. Xác nhận thao tác được xử lý ở instance khác với instance đang giữ hub
(dùng log Nginx/API trong QA), client nhận `CommerceChanged`, rồi tải lại dữ liệu
qua API. Kiểm tra buyer/seller liên quan nhận sự kiện và tài khoản khác không
nhận sự kiện riêng.

Ngắt kết nối client hoặc dừng một API, khôi phục rồi xác nhận client reconnect
và tải bù trạng thái. Redis Pub/Sub không phải hàng đợi bền vững; `CommerceChange`
và inbox trong SQL là nguồn để đối soát/tải lại. Bài này chưa đo sức tải tối đa,
thanh toán hay HA dispatcher.

### Kết quả demo 11/11/2026

Người dùng đã build API bằng Docker, chạy đủ năm service và xác nhận `nginx -t`
thành công. Sáu request catalog trả HTTP 200, luân phiên giữa hai upstream
`172.18.0.4:8080` và `172.18.0.5:8080`. Browser hiển thị trạng thái SignalR đang
kết nối; Nginx ghi negotiate HTTP 200 và WebSocket HTTP 101 tới api-b. Khi
dispatcher chỉ chạy ở api-a, `CommerceChange` ID 2653 (`notification-inbox`) có
`DispatchedAt` sau `CreatedAt` khoảng 0,1 giây. Đây là bằng chứng dispatcher xử
lý và phát sự kiện qua cấu hình backplane; chưa có log xác nhận callback đã tới
browser. Chưa kiểm tra mất instance, reconnect hoặc dispatcher HA.

Chạy [bài đo catalog](JMETER.md#đo-tải-catalog) với `$target` trỏ vào 8088. Giữ
nguyên dữ liệu và tham số để so sánh với bài đo một instance. Bài JMeter kiểm tra
quota dùng đích một instance vì mỗi API có bộ đếm riêng. Demo realtime hiện dùng
hai API và một dispatcher; chưa đo sức tải tối đa, HA dispatcher, hoặc thanh toán
qua nhiều instance.
