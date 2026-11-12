# Kế hoạch kiểm thử tổng thể — Hiệp (QA / tải / realtime)

**Phạm vi:** kiểm thử tích hợp và hệ thống; không thay thế unit/business tests của từng thành viên.
**Môi trường bắt buộc:** branch cần kiểm thử, SQL Server demo, Docker Desktop (Linux containers), .NET 8 SDK, Java, Apache JMeter 5.6.3.

## 1. Cổng chất lượng trước khi demo/merge

| Mức | Hạng mục | Điều kiện đạt | Bằng chứng |
|---|---|---|---|
| P0 | Build + regression | `dotnet restore`, build Release, toàn bộ `G4.Commerce.Tests`, toàn bộ JS tests đều pass | log lệnh/CI |
| P0 | Dữ liệu và phân quyền | buyer/seller/admin/shipper chỉ thấy và sửa dữ liệu thuộc quyền; không lộ token/mật khẩu/PII | test API + UI |
| P0 | Tiền và đơn hàng | checkout/payment/refund/payout không nhân đôi khi retry; số tiền, tồn kho, outbox nhất quán | case nghiệp vụ + SQL đối chiếu |
| P0 | Nginx/load balancing | 2 API cùng trả catalog; dừng 1 API, route đọc vẫn HTTP 200 | `Test-Infra.ps1` + Nginx logs |
| P0 | Realtime xuyên instance | `api-a` dispatcher, hub đi `api-b`, 2 tab buyer nhận `notification-inbox`, seller không liên quan không nhận | `Test-RealtimeCrossInstance.ps1` + Nginx logs |
| P1 | Rate limit | traffic bình thường 200; vượt quota trả 429 + `Retry-After`, không làm thay đổi dữ liệu | JMeter rate-limit JTL |
| P1 | Hiệu năng | catalog read có error rate 0%; ghi P95/P99/throughput và thông số máy | JMeter HTML report |
| P1 | Khôi phục | client reconnect rồi reload state; api-b restart không làm rơi dữ liệu; dispatcher restart xử lý outbox còn lại | UI/DB/log |
| P1 | Bảo mật phụ thuộc | không còn package NuGet bị advisory High/Critical trước khi merge release | `dotnet list package --vulnerable` |

**Không được kết luận “đã chịu tải” chỉ vì một test catalog 30 giây pass.** Bài catalog là baseline/read-only; checkout, payment, refund và payout phải có case chức năng/đối chiếu riêng vì chúng là tác vụ cần chắc chắn thành công.

## 2. Bộ test theo tầng

| Tầng | Cần test | Ví dụ kiểm tra |
|---|---|---|
| Unit/domain | công thức fee/refund/hold, retry/idempotency, validation | cùng request key không tạo thêm payment/refund/order |
| API/integration | auth/role/ownership, response 4xx, transaction/outbox | seller không đọc order buyer khác; token sai trả 401/403 |
| Database | migration, FK/index, snapshot, số dư, duplicate prevention | payment/refund/payout đối chiếu tổng; `CommerceChange` có `DispatchedAt` |
| UI/E2E | flow người dùng và error state | login → cart → checkout; refund → buyer history/seller finance cập nhật |
| Realtime/HA | WebSocket, Redis backplane, reconnect/failover | event đúng audience qua `api-a` → Redis → `api-b`; không gửi sự kiện riêng sang seller khác |
| Load | read baseline, rate limit, soak nhỏ | 4 threads/60 rpm; 150 request nhìn 200 + 429; tăng dần chỉ trên QA |

## 3. Script và cách chạy chuẩn

### A. Toàn bộ stack/infrastructure

```powershell
powershell -ExecutionPolicy Bypass -File scripts\Test-Infra.ps1 -LocalCi
```

Script tự kiểm: Docker stack, Nginx config, login, chia tải, failover, realtime cross-instance, JMeter (nếu đã có), login rate limit và regression local. Dùng `-SkipJMeter` hoặc `-SkipRealtimeE2E` **chỉ khi ghi rõ lý do** trong báo cáo QA.

### B. Realtime xuyên hai API (test mới, tự động)

```powershell
powershell -ExecutionPolicy Bypass -File scripts\Test-RealtimeCrossInstance.ps1
```

Script chỉ làm việc với **G4_Demo**: đưa một notification demo của buyer về unread, gọi API thật `POST /api/me/notifications/{id}/read`, rồi kiểm:

1. hai client buyer SignalR qua Nginx nhận topic `notification-inbox` với version mới;
2. seller không liên quan không nhận event;
3. `CommerceChange.DispatchedAt` đã được worker cập nhật;
4. Nginx log chứng minh hub traffic đi `api-b`, trong khi Compose chỉ bật dispatcher tại `api-a` — đây là bằng chứng Redis backplane xuyên instance.

Nếu log không cho thấy `api-b`, **không đánh dấu pass cross-instance**; chạy lại từ source IP/client hash vào `api-b` và lưu log.

### C. JMeter: gửi gì và đọc gì

- **Load baseline:** `GET /api/catalog/random?count=3`, buyer JWT trong biến môi trường `G4_JMETER_BUYER_TOKEN`. Đây là read-only, không phá dữ liệu.
- **Rate limit:** cùng endpoint nhưng 150 requests trên **một** instance. Kỳ vọng có cả `200` và `429` hợp lệ; `429` phải có `Retry-After` và body `RateLimitExceeded`.
- Chạy theo `docs/JMETER.md`; lưu cả `.jtl`, `jmeter.log`, `report/index.html` và cấu hình threads/ramp/duration.
- Khi tăng tải: dùng nhiều buyer QA hoặc quota QA riêng; không dùng token production/demo chung để kết luận số liệu.

## 4. Kịch bản demo trước giảng viên

1. Mở Docker stack, chạy `Test-Infra.ps1 -SkipJMeter` và chỉ ra PASS build/login/load balance/failover/realtime.
2. Mở Nginx log: hai upstream catalog; sau đó stop `api-b`, request catalog vẫn thành công.
3. Mở hai tab buyer và một seller khác; đánh dấu notification buyer đã đọc. Hai buyer cập nhật, seller không đổi. Nêu rõ `api-a` dispatch và Redis chuyển cho hub ở `api-b`.
4. Mở JMeter HTML report: threads/ramp/time, sample count, error rate, P95/P99, throughput. Chạy riêng rate-limit report để cho thấy `429` là hành vi mong đợi khi vượt quota.
5. Với checkout/payment/refund: dùng case đã có sẵn và SQL/API evidence để giải thích idempotency, outbox, retry và không cộng tiền hai lần.

## 5. Trạng thái kiểm tra ngày 12/11/2026

**Đã pass trong worktree QA (branch `qa/realtime-load-verification`):**

- restore + `dotnet build G4_Project.sln --configuration Release --no-restore`: pass;
- test domain/API `tests/G4.Commerce.Tests`: pass (bao gồm Klarna HPP, refund, finance, notification, rate limit, address, checkout…);
- `node --test tests/*.test.cjs`: **164 pass, 0 fail**;
- `docker compose -f docker-compose.nginx.yml config --quiet`: pass;
- XML của 2 file JMeter và PowerShell syntax của scripts: pass.

**Chưa thể kết luận pass runtime trên máy hiện tại:** Docker Desktop daemon đang tắt và Apache JMeter chưa được cài/đặt tại path của project. Vì vậy chưa có kết quả container, Nginx runtime, Redis SignalR, JMeter HTML report hoặc reconnect runtime cho lần QA này. Đây là blocker môi trường, không phải kết quả pass/fail của code.

**Sửa advisory dependency:** audit ban đầu phát hiện `Microsoft.AspNetCore.SignalR.StackExchangeRedis 8.0.31` mức **High** và transitive `MessagePack 2.5.302` mức **Moderate**. Nhánh QA đã nâng package .NET 8 và Docker image lên **8.0.34**; `dotnet list ... --vulnerable --include-transitive` sau đó không còn báo package vulnerable, đồng thời MessagePack resolve **2.5.303**. Máy local hiện vẫn có runtime 8.0.31, nên cần cập nhật runtime/SDK lên 8.0.34 hoặc mới hơn trước khi chạy API local và lặp lại regression/infrastructure.
