# Trả hàng tự hoàn tiền, phí ship theo kg và shipper nhận vận đơn

**Căn cứ mới 18/10/2026:** [đặc tả §6–8](BUSINESS_REQUIREMENTS.md) và [SH1/RT1/RT2/INR1/FR1](IMPLEMENTATION_BACKLOG.md). SH1 đã triển khai/nghiệm thu source ngày 20/10, hướng dẫn dưới đây; cần migration mới trước khi chạy. Các phần return khác vẫn mô tả source hiện tại: mục tiêu RT1 là return 7 ngày/Development 300 giây từ chiều đi Delivered; receipt 48 giờ/45 giây từ chiều trả Delivered; phản đối từ chối/INR là deadline riêng. RT1/RT2/INR1 và free returns theo item chưa hoàn thành, không chạy được chỉ bằng thay cấu hình receipt.

## SH1 — Lên lịch giao lại và actor 20/10/2026

Áp dụng chung cho `Direction = Outbound` và `Return`:

```text
LabelCreated → PickedUp → InTransit → OutForDelivery → Delivered
                                          ↓
                                    DeliveryFailed
                                      ↙       ↘
                         ReattemptScheduled   ReturningToSender → ReturnedToSeller
                                ↓
                          OutForDelivery
```

- Sau thất bại không được bắt đầu giao lại trực tiếp. Chọn **Lên lịch giao lại**, nhập thời gian dự kiến trong tương lai và lý do tối đa 1000 ký tự; địa điểm tùy chọn tối đa 100 ký tự, không tự lấy cả địa chỉ dài làm địa điểm event. Form dùng giờ thiết bị, gửi ISO có timezone; server chuẩn hóa UTC vào `ShippingInfo.ReattemptAt`, `ReattemptReason` và snapshot `ShippingEvent.ReattemptAt`/`Note`.
- Lên lịch không tăng `DeliveryAttempts`. **Bắt đầu giao hàng** mới tăng, tối đa hai lượt/vận đơn. Lịch là dự kiến: không chặn bắt đầu sớm nếu thực tế shipper đã xuất phát, không có worker tự bắt đầu khi đến giờ. Lịch vẫn lưu để tra cứu sau khi bắt đầu/kết thúc.
- Sau thất bại lần hai không lên lịch/lượt ba. Chuyển hoàn cần hai event thực tế `ReturningToSender → ReturnedToSeller`; không tự coi đã giao hoặc đã về nơi gửi khi hết lượt.
- Chỉ shipper đang được gán được cập nhật. `ActorUserId` lấy từ tài khoản xác thực và `ActorRole = shipper`; client không được chọn actor. Event tạo nhãn tự động có role `system`, không gán user giả. Event cũ giữ actor null và UI ghi **Chưa ghi nhận người thực hiện (dữ liệu cũ)**.
- `OccurredAt`/`ReceivedAt` dùng giờ UTC phía server. Event ID unique, cùng shipment/actor/payload lặp lại là no-op (`applied=false`) kể cả thời gian dự kiến đã qua; dùng lại ID cho payload khác trả 409. Thiếu lịch/lý do, lịch quá khứ, note/location quá dài hoặc gửi lịch cho trạng thái khác trả 400; sai quyền trả 403. Chuyển sai bước/hết lượt trả `applied=false` và không ghi event.
- UI giữ nguyên event ID/payload sau lỗi mạng/5xx để bấm thử lại; lỗi 4xx cho nhập lại. Buyer/seller có timeline riêng theo chiều, shipper/admin xem actor/lịch/note đã escape. Bộ lọc có ReattemptScheduled; vận đơn đang xử lý chưa được gán vẫn có thể nhận. Email buyer/seller lưu cùng transaction theo từng shipment/event/người nhận, có chiều, lịch UTC và lý do; worker gửi riêng. Realtime phát thay đổi đã commit.
- `Delivered` chiều trả chỉ bắt đầu receipt deadline, không đánh dấu giao thành công cho buyer hoặc phát email Delivered chiều đi. ReturnedToSeller nghĩa là về nơi gửi ban đầu (seller ở chiều đi; buyer ở chiều trả).

API `POST /api/shipments/{id}/events` thêm trường tùy chọn `reattemptAt`; ví dụ lên lịch (timestamp ví dụ cần đổi thành tương lai khi thử):

```json
{
  "eventId": "unique-event-id",
  "status": "ReattemptScheduled",
  "location": "Điểm nhận",
  "note": "Người nhận hẹn giao lại",
  "reattemptAt": "2026-10-21T09:00:00+07:00"
}
```

Endpoint danh sách/chi tiết shipment trả thêm `reattemptAt`, `reattemptReason`; event chi tiết trả `actorUserId`, `actorRole`, `reattemptAt` cùng note/location và timestamp hiện có. Admin lookup bổ sung actor/lịch/note cho timeline, giữ quyền chỉ đọc.

**Migration:** `20261020150408_AddShipmentReattemptSchedulesAndActors`, sau `20261017050657_CompleteBusinessNotifications`; [SQL để duyệt](../database/shipment-reattempt-actors-migration.sql). Chỉ thêm cột nullable, actor index/FK; không backfill actor/lịch giả, không viết lại snapshot/tiền/lịch sử cũ. Trên database hiện có chạy migration tăng dần, không chạy lại baseline:

```powershell
dotnet ef database update 20261020150408_AddShipmentReattemptSchedulesAndActors --project backend/G4.Infrastructure --startup-project backend/G4.Api
```

Build rồi áp migration đúng môi trường trước khi chạy API/frontend mới. Đợt này migration **chỉ được áp QA** `G4_ShippingReattempt_Test_20261020`, **chưa áp database chính**.

**Kiểm thử 20/10:** solution build output `artifacts/shipping-reattempt-validation` đạt 0 warning/error; backend mặc định (gồm SH1) đạt; `node --test tests/*.test.cjs` 117/117 đạt. Lệnh riêng:

```powershell
dotnet run --project tests/G4.Commerce.Tests -- --shipping-reattempts
$env:G4_SHIPPING_REATTEMPT_SQL_CONNECTION='Server=localhost;Database=G4_ShippingReattempt_Test_20261020;Integrated Security=True;TrustServerCertificate=True'
dotnet run --project tests/G4.Commerce.Tests -- --shipping-reattempts-sql
node tests/shipping-reattempt-ui.e2e.cjs
```

SQL test chỉ chấp nhận tên QA trên, tạo/bootstrap database nếu trống, nâng từ schema trước SH1 và kiểm tra giữ actor null/note/time/lượt giao legacy. Hai chiều đều kiểm tra request đồng thời chỉ ghi một schedule/start, counter, email đúng người nhận, claim/filter và quyền; lỗi sau SaveChanges trước commit rollback shipment/event/outbox/realtime. Không dùng connection database chính.

Browser test cần Playwright và Chrome (`NODE_PATH` trỏ nơi đã cài package nếu cần, `CHROME_PATH` trỏ Chrome; mặc định dùng Chromium của Playwright). Dùng view/dialog/CSS/JS thật và API fixture; đạt desktop 1440px/mobile 390px cả hai chiều, validation, UTC, retry cùng key, escape/timeline/actor, thao tác bắt đầu và không tràn ngang. Ảnh tại `artifacts/shipping-reattempt-ui/`, đã xem lại layout. Chưa E2E HTTP nối SQL hoặc carrier thật.

## Chuẩn bị

Áp dụng migration bổ sung `20261003151225_AddReturnWeightAndShipmentClaims` trên database hiện có:

```powershell
dotnet ef database update --project backend/G4.Infrastructure --startup-project backend/G4.Api
```

Có thể xem SQL tương đương tại `database/return-weight-shipment-claims-migration.sql`. Migration không điền khối lượng giả cho sản phẩm cũ, không tính lại phí của đơn cũ và không gán shipper cho vận đơn cũ. Vận đơn cũ đang vận chuyển vẫn có thể được nhận để tiếp tục tracking; vận đơn đã kết thúc không được nhận mới.

Chạy `database/seed-data.sql` sau migration nếu cần dữ liệu demo. Script chỉ điền khối lượng còn thiếu cho năm sản phẩm mẫu của `seller@example.test`, thêm địa chỉ lấy hàng mặc định nếu chưa có và giữ nguyên tồn kho. Khối lượng demo: Headphones 0,3 kg; Camera 0,6 kg; Watch 0,2 kg; Case 0,05 kg; Charger 0,25 kg.

Khởi động lại API và frontend sau khi build. API Development dùng `Returns:ReceiptSeconds = 45`, worker kiểm tra mỗi 5 giây. Môi trường không có cấu hình này dùng 48 giờ. Giao diện chi tiết buyer/seller đang mở tự cập nhật mỗi 5 giây.

## Trả hàng thường

1. Buyer đặt đơn, thanh toán, seller tạo vận đơn; shipper nhận vận đơn chiều đi và tracking đến `Delivered`.
2. Buyer bấm **Yêu cầu trả hàng** trong vòng 7 ngày từ giao thành công, chọn lý do và xác nhận báo giá gồm refund/phí chiều về trước khi gửi. Trạng thái `Requested` chỉ chờ seller, chưa tự hoàn tiền. Xem [RETURN_PRICING.md](RETURN_PRICING.md).
3. Seller duyệt. Tiền seller chuyển sang `OnHold`; hệ thống tạo vận đơn chiều trả. Nếu carrier thất bại, bấm **Thử tạo vận đơn trả lại**.
4. Shipper nhận riêng vận đơn chiều trả, cập nhật `PickedUp → InTransit → OutForDelivery → Delivered`.
5. Từ thời điểm chiều trả `Delivered`, seller có 45 giây ở Development để kiểm tra hàng. Seller bấm **Xác nhận hàng hợp lệ và hoàn tiền** thì hoàn ngay. Nếu seller không phản hồi, worker tự xác nhận và hoàn tiền ở lần kiểm tra sau hạn.
6. Kiểm tra yêu cầu `Refunded`, đơn `Closed`, một refund `Succeeded` đúng báo giá. Với lỗi seller, phần SellerGross bị hoàn toàn bộ; với buyer đổi ý, tiền ship chiều đi còn lại được giải phóng theo lịch gốc nên settlement có thể về Processing/Available. Không cần bấm thêm nút hoàn tiền. Gateway lỗi giữ khoản tiền và trạng thái có thể thử lại; worker dùng lại refund và idempotency key.

Nếu hàng trả có vấn đề, seller bấm **Báo vấn đề hàng trả** trước hạn và nhập mô tả cùng 1–10 link bằng chứng hợp lệ. Yêu cầu trả thành `Disputed`, hồ sơ chuyển admin (`Escalated`), tiền tiếp tục giữ và worker dừng tự hoàn. Admin quyết định buyer thắng thì refund; seller thắng thì đóng yêu cầu trả và giải phóng khoản giữ theo điều kiện tài chính.

Buyer không mở một hồ sơ tranh chấp song song trong khi yêu cầu trả thường đang xử lý. Trả hàng theo phương án `ReturnRefund` đã được hai bên thống nhất tiếp tục dùng workflow tranh chấp riêng; cả hai luồng dùng cùng cơ chế receipt/refund để tránh hoàn hai lần.

Từ `AddReasonBasedReturnPricing`, yêu cầu mới chia phí chiều về theo lý do, không hoàn phí chiều đi khi buyer đổi ý, và lưu báo giá buyer đã xác nhận. Yêu cầu cũ giữ chính sách cũ; xem [RETURN_PRICING.md](RETURN_PRICING.md). Admin chọn hoàn một phần hoặc tùy chỉnh bên chịu phí trong phán quyết là phần còn lại cần triển khai.

## Phí ship theo khối lượng

Seller mở tab **Thiết lập giao hàng**, lưu địa chỉ lấy hàng và khối lượng từng sản phẩm (kg, 0,001–10.000, tối đa 3 chữ số thập phân). Sản phẩm thiếu hoặc có khối lượng không hợp lệ, hoặc seller thiếu địa chỉ lấy hàng mặc định, chưa được checkout.

Tổng kg bằng tổng `khối lượng mỗi sản phẩm × số lượng`. Dùng kg thực tế, không làm tròn lên nguyên kg:

| Tuyến | USD |
| --- | --- |
| Cả hai địa chỉ ở Hà Nội, Việt Nam | `2 + max(0, kg - 1) × 0,5` |
| Các tuyến khác | `5 + max(0, kg - 1)` |

Làm tròn phí cuối đến 2 chữ số thập phân. Ví dụ hai Camera demo tổng 1,2 kg: Hà Nội → Hà Nội $2,10; Hà Nội → HCM $5,20. Cùng ở HCM vẫn áp dụng tuyến khác. Tỉnh/quốc gia được chuẩn hóa dấu, hoa thường và khoảng trắng.

Buyer thấy kg từng sản phẩm và tổng kg ở checkout. Đơn lưu `UnitWeightKgSnapshot`, `TotalWeightKg`, địa chỉ lấy/giao; sửa sản phẩm hoặc địa chỉ sau đó không đổi đơn đã tạo. Đơn cũ thiếu kg hiển thị **Chưa có dữ liệu** và giữ phí đã lưu.

## Shipper nhận vận đơn

Trang Shipper có **Chờ nhận** và **Vận đơn của tôi**, phân trang theo từng vận đơn. Thẻ cho biết mã đơn, chiều vận chuyển, kg, địa chỉ lấy/giao và trạng thái.

- **Chờ nhận:** bấm **Nhận vận đơn**, xác nhận rồi chuyển sang danh sách của mình.
- **Vận đơn của tôi:** mở chi tiết và cập nhật mốc tracking hợp lệ. Chi tiết chỉ chứa vận đơn của shipper đó và lịch sử tương ứng.
- Hai shipper nhận cùng một vận đơn: SQL conditional UPDATE chỉ cho một người nhận. Người còn lại nhận `409`; người đang sở hữu bấm lại không đổi thời điểm nhận.
- Chưa nhận hoặc vận đơn của shipper khác: tracking và chi tiết đơn trả `403`. Dữ liệu payment, refund và balance không nằm trong chi tiết shipper.

Endpoint chính:

| Endpoint | Vai trò |
| --- | --- |
| `GET /api/seller/shipping/settings` | Seller đọc sản phẩm/địa chỉ của mình |
| `POST /api/seller/shipping/products/{id}/weight` | Seller lưu `{ "weightKg": 0.6 }` |
| `POST /api/seller/shipping/pickup` | Seller lưu địa chỉ |
| `POST /api/seller/returns/{id}/receive` | Xác nhận hàng hợp lệ và hoàn tiền |
| `POST /api/seller/returns/{id}/issue` | Báo vấn đề với mô tả/link bằng chứng |
| `GET /api/shipper/shipments/page?filter=pending` | Vận đơn chờ nhận |
| `GET /api/shipper/shipments/page?filter=mine` | Vận đơn của tài khoản đang đăng nhập |
| `POST /api/shipper/shipments/{id}/claim` | Nhận riêng từng vận đơn |
| `POST /api/shipments/{id}/events` | Chỉ shipper đang sở hữu cập nhật |

## Kiểm thử

```powershell
dotnet run --project tests/G4.Commerce.Tests --configuration Release -p:UseAppHost=false
node --test tests/ui-navigation.test.cjs tests/dispute-ui.test.cjs
./tests/smoke-commerce.ps1
```

Smoke tạo đơn thử trên database demo, kiểm tra cả trả hàng theo thỏa thuận và trả thường tự hoàn sau 45 giây. API/frontend phải chạy ở Development với seed đã cập nhật; PayPal thật không nằm trong smoke này. Bộ commerce kiểm tra ranh giới 44/45 giây, seller issue, refund lỗi/thử lại, thao tác lặp, worker chạy đồng thời với receipt hoặc hold, kg thập phân, snapshot, quyền shipper và SQL query translation.
