# Kubernetes: tối đa 4 API, countdown và ngân sách

## Phạm vi và kết quả kiểm tra 12/11/2026

Người dùng xác nhận **4 bản API**, không phải 4 máy chủ hay tổng 4 Pod.
Source trước đợt này chỉ có Compose/Nginx demo catalog với 2 API; chưa có K8s.
Countdown đã lấy deadline từ database/API, đồng bộ giờ server và đếm tại browser.

Bản triển khai trong `deploy/k8s` dùng JSON manifest chuẩn Kubernetes:

| Thành phần | Số bản | Vai trò |
| --- | ---: | --- |
| api-main | 1 | Tất cả giao dịch, login, worker, SignalR |
| api-read | 1–3 | GET /api/catalog/random qua gateway; worker tắt |
| api-gateway | 1 | Nginx điều phối, nằm ở namespace riêng |
| Frontend, SQL, hạ tầng cluster | Tính riêng | Không nằm trong giới hạn 4 API |

Tổng API bình thường 2, khi tải catalog cao tối đa 4. Đây là chia tải catalog,
**không phải 4 replica xử lý mọi chức năng**. Không bật worker trên cả 4 process:
chưa nghiệm thu distributed worker locking, SignalR backplane và quota dùng chung.
API chính là điểm nghẽn/điểm lỗi đơn; mất nó thì giao dịch và realtime gián đoạn
đến khi Kubernetes khởi động lại. Không tự chuyển ghi sang read replica.

## Giới hạn và xử lý quá tải

- HPA CPU 65%, read min 1/max 3; tăng tối đa 1 Pod mỗi 60 giây.
- Scale-down stabilization 300 giây, giảm tối đa 1 Pod mỗi 60 giây. Đây là cửa sổ
  chống dao động, **không bảo đảm đúng 300 giây là Pod bị xóa**. HPA cần Metrics Server.
- Namespace `g4-api` chỉ chứa API. ResourceQuota `pods=4` chặn cả scale thủ công
  và Pod phát sinh khi rollout, kể cả lúc Pod cũ còn Terminating. Không đặt Job
  migrate, frontend hay gateway vào namespace này.
- API read rollout `maxSurge=0`, `maxUnavailable=1`; main dùng Recreate để tránh
  hai worker main chạy chồng khi cập nhật. Có gián đoạn main khi rollout.
- Mỗi API request 0,5 CPU/512 MiB, limit 1 CPU/1 GiB; quota tổng request 2 CPU/2 GiB,
  limit 4 CPU/4 GiB. Đây là điểm khởi đầu đo tải, không phải capacity đã benchmark.
- Gateway giới hạn tổng catalog 100 request/giây, burst 20; vượt trả 429 và
  Retry-After: 5. Đây là ngưỡng thử nghiệm cần chỉnh theo JMeter/DB, không phải SLA.
  API tiếp tục áp quota hiện có. Không tự retry POST/thanh toán ở proxy.
- Read quota trong API vẫn theo process, không phải quota toàn cluster. Gateway
  giới hạn tổng tải chứ không thay quota theo UserId. Gateway xóa X-Forwarded-For
  từ đầu vào; login quota IP có thể gộp theo gateway cho tới khi cấu hình proxy tin cậy.
- Services là ClusterIP; chỉ expose gateway qua frontend/ingress được quản lý.
  Không expose trực tiếp api-read: image vẫn có route ghi, không phải image chỉ đọc.

## Chạy trên cluster đã có

Điều kiện: kubectl, cluster Linux, Metrics Server, SQL có schema hiện hành,
image API truy cập được từ node. Máy thực hiện thay đổi này chưa có kubectl/Docker;
chưa deploy hay xác minh cluster thật. TCP probes chỉ kiểm tra process lắng nghe,
không chứng minh SQL/provider hoạt động.

```powershell
docker build -f backend/G4.Api/Dockerfile -t g4-api:local .
# Load image vào cluster local, hoặc push registry và sửa image trong api.json.
kubectl create namespace g4-api
```

Tạo file env riêng ngoài git, chứa `ConnectionStrings__DefaultConnection`,
`Authentication__JwtKey`, `SellerIdentity__HmacKey`, `Frontend__PublicBaseUrl`,
`Realtime__AllowedOrigins__0` và credential Sandbox nếu cần. Các API dùng cùng
SQL và JWT key. Không dùng localhost cho SQL trong Pod. Dùng đúng khóa HMAC hiện
có khi nối database cũ để bảo toàn đối chiếu định danh. Không in secrets vào log.

```powershell
kubectl -n g4-api create secret generic api-secrets --from-env-file=C:/secure/g4-api.env
kubectl apply --dry-run=server -f deploy/k8s/api.json
kubectl apply -f deploy/k8s/api.json
kubectl -n g4-api rollout status deployment/api-main
kubectl -n g4-api rollout status deployment/api-read
kubectl apply --dry-run=server -f deploy/k8s/gateway.json
kubectl apply -f deploy/k8s/gateway.json
kubectl -n g4-edge rollout status deployment/api-gateway
kubectl -n g4-edge port-forward service/api-gateway 8088:8080
```

Frontend local đặt `ApiSettings__BaseUrl=http://localhost:8088`; nếu frontend trong
cluster dùng `http://api-gateway.g4-edge.svc.cluster.local:8080`. Cấu hình URL realtime
public và CORS theo [COMMERCE_REFRESH.md](COMMERCE_REFRESH.md). Bản manifest dùng
Production, thời hạn nghiệp vụ theo appsettings Production; không tự rút thành
thời gian demo. Mail Capture, không gửi SMTP thật. Không có migration/seed tự chạy.
TLS public và SQL backup thuộc hạ tầng riêng, cần tính trong ngân sách.

Khi sửa gateway.conf phải đồng bộ data.nginx.conf trong gateway.json, apply rồi
rollout restart gateway (ConfigMap subPath không tự cập nhật container đang chạy).

## Ngân sách: phải nhập đơn giá thực tế

Giới hạn Pod không đồng nghĩa giới hạn hóa đơn cloud. Nếu nhà cung cấp tính theo
VM/node thì scale-down Pod không giảm tiền khi node vẫn chạy. Phải giới hạn node
pool/autoscaler, dùng billing alert của nhà cung cấp và tính cả SQL, frontend,
gateway, disk/snapshot, log, egress, load balancer, control plane và thuế.

Với mô hình có chi phí theo API-giờ:

`Chi phí = chi phí khác + đơn giá API/giờ × tổng số API-giờ`

`Số API cho phép = min(4, floor((ngân sách × (1 - dự phòng) - chi phí khác) / (giá giờ × số giờ)))`

```powershell
# Số minh họa, cùng một đơn vị tiền; không phải báo giá cloud.
node scripts/api-budget.cjs 1000 100 0.25 730 0.1
```

Ví dụ trên: dành 100 dự phòng; 4 API cả tháng + chi phí khác = 830, còn 70 trong
ngân sách sử dụng 900. Với ngân sách 800 chỉ cho phép 3 API: sửa HPA maxReplicas
thành 2 (trừ 1 main), đồng thời hạ quota pods thành 3 trước triển khai. Nếu đang
chạy, hạ HPA và đợi số Pod giảm trước khi hạ quota. Script trả exit 2 khi không đủ
2 API tối thiểu; không được triển khai cấu hình mặc định trong trường hợp đó.
Script chỉ tính, không tự chỉnh cluster hay chặn hóa đơn. Người dùng chưa cung
cấp ngân sách/nhà cung cấp nên chưa thể xác nhận một mức tiền thực tế.

## Countdown và demo nghiệm thu

Countdown nghiệp vụ giữ `PaymentExpiresAt`/deadline đã lưu; không tạo 1 Pod/timer
cho mỗi đơn và không gọi server mỗi giây. Refresh/chuyển instance không được reset
hạn. Hết giờ chỉ đọc lại trạng thái; không tự refund, không tự coi payment thành công.
Worker main tiếp tục xử lý theo database khi khởi động lại. Không thay deadline
nghiệp vụ bằng thời gian chờ HPA. Node của cluster cần đồng bộ NTP.

1. `kubectl -n g4-api get pods,hpa,resourcequota`: ban đầu 2 API.
2. Chạy bài catalog [JMETER.md](JMETER.md) qua port 8088; theo dõi
   `kubectl -n g4-api get hpa,pods -w`. Token quota có thể chặn trước ngưỡng CPU;
   cần nhiều user test hợp lệ để đo autoscale, không tắt bảo vệ tiền.
3. Khi đủ tải, tối đa 1 main + 3 read. Thử scale read lên 4: Pod thứ 5 phải bị
   FailedCreate do quota; HPA có thể đưa desired về 3. Xem events/describe replicaSet.
4. Dừng tải, theo dõi recommendation qua cửa sổ 300 giây và số read giảm về 1.
5. Trên database QA, mở đơn còn hạn, ghi deadline, restart main, reload và đối chiếu
   deadline không đổi; kiểm tra reconcile/worker không ghi tiền trùng. Không dùng DB chính.
6. Khi 4 API vẫn quá tải, chứng minh 429/Retry-After; không có API thứ 5.
7. Ghi P95, tỷ lệ lỗi, CPU/RAM, SQL load và chi phí dự kiến trước khi nghiệm thu.

Kiểm thử local: `node --test tests/api-budget.test.cjs tests/countdowns-ui.test.cjs`.
Chưa kiểm chứng rollout, HPA, quota enforcement, Nginx runtime hoặc SQL concurrency
trên cluster thật; các bước trên là checklist cần chạy, không phải kết quả đã đạt.

Nguồn Kubernetes: [HPA](https://kubernetes.io/docs/concepts/workloads/autoscaling/horizontal-pod-autoscale/),
[Quota](https://kubernetes.io/docs/concepts/policy/resource-quotas/),
[Deployment](https://kubernetes.io/docs/concepts/workloads/controllers/deployment/).
