# Kiểm thử tải bằng Apache JMeter

Hai kịch bản dùng cùng API chỉ đọc `GET /api/catalog/random?count=3` để tách độ
trễ truy vấn catalog khỏi việc kiểm tra phản hồi `429`. Đích mặc định là API
Development tại `http://localhost:5257` dùng database demo riêng có sản phẩm.

## Chuẩn bị

1. Cài **JDK 21** và giải nén [Apache JMeter 5.6.3](https://jmeter.apache.org/download_jmeter.cgi). JMeter 5.6.3 không chạy được với Java 25; đặt `JAVA_HOME` trỏ tới JDK 21. `Test-Infra.ps1` tự ưu tiên JDK 21 trong `C:\Program Files\Microsoft`, `Eclipse Adoptium` hoặc `Java` khi chạy JMeter.
   Kiểm tra `java -version` và chạy `bin/jmeter.bat --version`.
2. Khởi động API theo [hướng dẫn database demo](DEMO_DATABASE.md). Dùng tài khoản
   buyer trên database demo hoặc QA. Chạy các lệnh dưới đây từ thư mục gốc repo.
3. Đăng nhập một lần ngoài bài đo. Token chỉ được truyền qua biến môi trường của
   PowerShell hiện tại và tiến trình con JMeter.

```powershell
$jmeter = 'C:\Tools\apache-jmeter-5.6.3\bin\jmeter.bat' # Đổi theo vị trí đã giải nén.
$apiHost = 'localhost'
$apiPort = 5257
$apiProtocol = 'http'
$baseUrl = "${apiProtocol}://${apiHost}:${apiPort}"
$target = @("-Jprotocol=$apiProtocol", "-Jhost=$apiHost", "-Jport=$apiPort")
$buyer = Get-Credential -UserName 'buyer@demo.test' -Message 'Nhập tài khoản buyer demo'
$body = @{
    email = $buyer.UserName
    password = $buyer.GetNetworkCredential().Password
} | ConvertTo-Json
$login = Invoke-RestMethod "$baseUrl/api/auth/login" -Method Post `
    -ContentType 'application/json' -Body $body
if ($login.role -ne 'buyer' -or -not $login.token) {
    throw 'API phải trả token hợp lệ của buyer'
}
$env:G4_JMETER_BUYER_TOKEN = $login.token
Remove-Variable buyer, body, login
```

## Đo tải catalog

Mặc định chạy 4 luồng trong 30 giây, tăng dần trong 3 giây. Timer nhắm **60
request/phút tổng cộng** cho một token, dưới quota mặc định 120 request/phút.
Số luồng là mức đồng thời có thể tạo request, còn tốc độ thực tế xem trong báo
cáo. Mọi request phải trả HTTP 200 với dữ liệu sản phẩm.

```powershell
$run = "artifacts/jmeter/catalog-$(Get-Date -Format 'yyyyMMdd-HHmmss-fff')"
New-Item -ItemType Directory -Path $run | Out-Null
& $jmeter -n -t tests/jmeter/catalog-load.jmx @target `
    '-Jthreads=4' '-Jramp_seconds=3' '-Jduration_seconds=30' '-Jrequests_per_minute=60' `
    -l "$run/results.jtl" -j "$run/jmeter.log" -e -o "$run/report"
```

Mở `report/index.html` để xem throughput, P95/P99 và tỷ lệ lỗi. Kiểm tra có
sample và tỷ lệ lỗi bằng 0 trước khi dùng số liệu. HTTP 429 trong bài đo này là
lỗi và cho thấy quota hoặc traffic khác cùng buyer đã ảnh hưởng đến kết quả.
Khi so sánh trước/sau Nginx, giữ nguyên dữ liệu, cấu hình quota và tham số tải,
rồi đổi đích `$target`. Với mặc định, kết quả dùng làm baseline cho một buyer
và một truy vấn. Muốn đo tải cao hơn, cấu hình quota phù hợp riêng cho QA hoặc
thiết kế bài nhiều buyer, đồng thời theo dõi tài nguyên API và SQL Server.

## Kiểm tra rate limiting

Chờ ít nhất một cửa sổ quota (mặc định 60 giây) kể từ request cuối cùng của buyer
đó. Chạy trên một API instance với quota `read` mặc định. Kịch bản gửi 150 request
liên tiếp bằng cùng một token và yêu cầu thấy cả 200 lẫn 429. Mỗi 429 phải có
`Retry-After` dương, khớp `retryAfterSeconds`, và JSON có code `RateLimitExceeded`,
group `read`. Bài thử dừng khi đủ số mẫu hoặc hết 15 giây. Nếu đổi quota, điều
chỉnh `-Jrequests` để vượt ngưỡng trong cùng cửa sổ. Tránh dùng buyer đó cho các
thao tác khác trong lúc chạy.

```powershell
$run = "artifacts/jmeter/rate-limit-$(Get-Date -Format 'yyyyMMdd-HHmmss-fff')"
New-Item -ItemType Directory -Path $run | Out-Null
& $jmeter -n -t tests/jmeter/catalog-rate-limit.jmx @target '-Jrequests=150' `
    -l "$run/results.jtl" -j "$run/jmeter.log" -e -o "$run/report"
$rows = Import-Csv "$run/results.jtl"
$rows | Group-Object responseCode, success | Select-Object Name, Count
Remove-Item Env:G4_JMETER_BUYER_TOKEN
```

Kết quả đạt khi có đủ 150 mẫu, có cả nhóm `200, true` và `429, true`, và không
có mẫu `false`. JMeter ghi 429 hợp lệ là mẫu thành công vì đó là phản hồi mong
đợi. Kiểm tra JTL/dashboard thay vì chỉ dựa vào mã thoát của JMeter. Báo cáo
bài 429 đánh giá hành vi quota, còn số liệu tải lấy từ bài đo catalog.
Nếu chỉ chạy bài đo tải, dọn token cuối phiên bằng `Remove-Item Env:G4_JMETER_BUYER_TOKEN`.
