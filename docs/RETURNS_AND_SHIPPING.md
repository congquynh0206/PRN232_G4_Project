# Trả hàng tự hoàn tiền, phí ship theo kg và shipper nhận vận đơn

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
2. Buyer bấm **Yêu cầu trả hàng** trong vòng 7 ngày từ giao thành công. Trạng thái `Requested` chỉ chờ seller, chưa tự hoàn tiền.
3. Seller duyệt. Tiền seller chuyển sang `OnHold`; hệ thống tạo vận đơn chiều trả. Nếu carrier thất bại, bấm **Thử tạo vận đơn trả lại**.
4. Shipper nhận riêng vận đơn chiều trả, cập nhật `PickedUp → InTransit → OutForDelivery → Delivered`.
5. Từ thời điểm chiều trả `Delivered`, seller có 45 giây ở Development để kiểm tra hàng. Seller bấm **Xác nhận hàng hợp lệ và hoàn tiền** thì hoàn ngay. Nếu seller không phản hồi, worker tự xác nhận và hoàn tiền ở lần kiểm tra sau hạn.
6. Kiểm tra yêu cầu `Refunded`, đơn `Closed`, một refund `Succeeded`, settlement `Refunded`. Không cần bấm thêm nút hoàn tiền. Gateway lỗi giữ khoản tiền và trạng thái có thể thử lại; worker dùng lại refund và idempotency key.

Nếu hàng trả có vấn đề, seller bấm **Báo vấn đề hàng trả** trước hạn và nhập mô tả cùng 1–10 link bằng chứng hợp lệ. Yêu cầu trả thành `Disputed`, hồ sơ chuyển admin (`Escalated`), tiền tiếp tục giữ và worker dừng tự hoàn. Admin quyết định buyer thắng thì refund; seller thắng thì đóng yêu cầu trả và giải phóng khoản giữ theo điều kiện tài chính.

Buyer không mở một hồ sơ tranh chấp song song trong khi yêu cầu trả thường đang xử lý. Trả hàng theo phương án `ReturnRefund` đã được hai bên thống nhất tiếp tục dùng workflow tranh chấp riêng; cả hai luồng dùng cùng cơ chế receipt/refund để tránh hoàn hai lần.

Phần này giữ chính sách hoàn toàn bộ số tiền thanh toán hiện tại. Chia phí ship trả theo lý do, không hoàn phí ship chiều đi khi buyer đổi ý và hoàn một phần chưa thuộc thay đổi này.

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
