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
`api-a`/`api-b` dùng cùng image và một SQL Server demo riêng. Nginx nghe trên
`http://127.0.0.1:8088`. SQL Server và hai API chỉ có mạng nội bộ Docker.

- `GET /api/catalog/random?count=3` đi luân phiên qua hai API. Header
  `X-G4-Upstream` và log Nginx ghi địa chỉ upstream. Nếu thử lại sau lỗi kết nối,
  địa chỉ cuối cùng trong header là instance trả phản hồi.
- `POST /api/auth/login` đi riêng tới `api-a` để lấy JWT buyer. Nginx gửi request
  đăng nhập một lần. Cả hai API dùng cùng khóa JWT demo.
- Các route còn lại trả 404. Compose đặt `Workers__Enabled=false` để hai tiến
  trình demo không chạy worker bảo trì và phát thông báo trên database dùng chung.
  Cấu hình API mặc định bật hai worker.

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
Nhập database một lần khi volume còn mới. Bộ SQL sẽ báo lỗi nếu `G4_Demo` đã tồn
tại. Nếu đã nhập trước đó, dùng lại database và bỏ qua hai lệnh nhập.

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

## Kiểm tra chia tải

Dùng bước đăng nhập PowerShell trong [hướng dẫn JMeter](JMETER.md#chuẩn-bị), đặt
`$apiHost = '127.0.0.1'` và `$apiPort = 8088` trước khi tạo `$baseUrl` và `$target`.

```powershell
$headers = @{ Authorization = "Bearer $env:G4_JMETER_BUYER_TOKEN" }
1..6 | ForEach-Object {
    $response = Invoke-WebRequest "$baseUrl/api/catalog/random?count=3" -Headers $headers
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

Chạy [bài đo catalog](JMETER.md#đo-tải-catalog) với `$target` trỏ vào 8088. Giữ
nguyên dữ liệu và tham số để so sánh với bài đo một instance. Bài JMeter kiểm tra
quota dùng đích một instance vì mỗi API có bộ đếm riêng. Demo này chỉ kiểm chứng
chia tải catalog, đăng nhập demo và khả năng đọc khi mất một upstream. Việc đo
sức tải tối đa và đánh giá phiên đăng nhập MVC, SignalR, thanh toán hay cập nhật
không gián đoạn cần kịch bản riêng.
