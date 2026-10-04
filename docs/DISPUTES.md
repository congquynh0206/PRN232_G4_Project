# Thương lượng và giải quyết tranh chấp

SQL Server lưu hồ sơ `Dispute`, lịch sử `DisputeEntry`, khoản giữ tiền và kết quả hoàn tiền. Buyer/seller chỉ xem và cập nhật hồ sơ của đơn thuộc mình. Shipper không tham gia tranh chấp.

## Quy trình

1. Trong chi tiết đơn đã giao, buyer chọn **Mở yêu cầu giải quyết**, nhập mô tả và từ 1–10 link bằng chứng HTTP/HTTPS. Đơn còn trong cửa sổ 7 ngày từ khi giao, chưa hoàn tiền thành công mới được mở. Mỗi đơn chỉ có một yêu cầu đang mở.
2. Hệ thống giữ khoản tiền của đúng đơn, gửi thông báo cho cả hai bên, gắn trạng thái vào dòng đơn và tạo hồ sơ **Chờ người bán phản hồi**.
3. Seller gửi phương án **Hoàn tiền toàn bộ**, **Trả hàng để hoàn tiền** hoặc **Giữ nguyên đơn hàng**, kèm mô tả và link bằng chứng. Buyer/seller được gửi bổ sung bằng chứng trong lúc thương lượng hoặc chờ admin; mỗi lần gửi bắt buộc có mô tả và link.
4. Buyer chấp nhận thì hệ thống thực hiện phương án. Giữ nguyên đơn: đóng hồ sơ và giải phóng khoản giữ theo điều kiện tài chính. Hoàn tiền/trả hàng: hồ sơ tiếp tục mở đến khi hoàn tiền thành công. Lỗi cổng thanh toán vẫn giữ tiền và worker thử lại.
5. Buyer không đồng ý thì nhập lý do và chuyển hồ sơ cho admin. Seller không gửi phương án đúng hạn cũng tự chuyển admin. Chỉ bổ sung bằng chứng không được xem là đã gửi phương án.
6. Buyer không trả lời phương án đúng hạn: tự đóng với kết quả **Đã đóng do người mua không phản hồi**, giải phóng khoản giữ theo lịch tài chính. Không tự chấp nhận phương án, không tự hoàn tiền.
7. Admin chỉ thấy hồ sơ đã chuyển lên, đọc lịch sử của cả hai bên và đưa quyết định kèm lý do. Buyer thắng: hoàn tiền; seller thắng: giải phóng khoản giữ. Kết quả còn lưu trong bộ lọc **Đã giải quyết**.

Bằng chứng/lịch sử chỉ được thêm mới. Không có API sửa/xóa; DbContext từ chối cập nhật/xóa `DisputeEntry`. Giao diện nhắc **Bằng chứng không thể sửa hoặc xóa sau khi gửi** trước khi xác nhận. Bổ sung bằng chứng không kéo dài thời hạn; seller không được gửi lại phương án để đặt lại hạn buyer.

## Thời hạn demo

`backend/G4.Api/appsettings.Development.json` đặt cả ba khóa dưới đây là **45 giây**:

| Cấu hình | Ý nghĩa | Mặc định khi không cấu hình |
| --- | --- | --- |
| `Disputes:SellerResponseSeconds` | Seller gửi phương án sau khi buyer mở yêu cầu | 3 ngày làm việc, bỏ thứ Bảy/Chủ nhật |
| `Disputes:BuyerResponseSeconds` | Buyer trả lời sau khi seller gửi phương án | 72 giờ |
| `Disputes:ReturnReceiptSeconds` | Seller xác nhận hàng trả sau tracking Delivered | 48 giờ |

Worker chạy mỗi **5 giây**; giao diện kiểm tra cập nhật mỗi **4 giây**. Hết hạn có thể chờ thêm một chu kỳ worker trước khi nhìn thấy kết quả. Worker xử lý deadline trước khi gọi vận chuyển/hoàn tiền; tổng thời gian một đợt thử thực hiện thỏa thuận được giới hạn bằng `Disputes:ExecutionTimeoutSeconds` (Development: 3 giây, mặc định: 5 giây, tối đa: 10 giây) để cổng ngoài bị treo không chặn xử lý deadline và thông báo. Thời gian lưu UTC. Ngày lễ không được tính riêng trong mặc định ngày làm việc. Các thời hạn thanh toán/cửa sổ trả hàng hiện có vẫn dùng quy tắc hiện tại.

Với thỏa thuận trả hàng, seller có thể xác nhận nhận hàng sớm trong chi tiết đơn. Nếu chưa xác nhận sau 45 giây từ tracking chiều trả Delivered, hệ thống tự xác nhận và hoàn tiền. Yêu cầu được mở hợp lệ trong cửa sổ trả hàng vẫn tiếp tục được xử lý sau khi cửa sổ đó hết.

## Giao diện

- Buyer/seller: tab **Tranh chấp** riêng, số yêu cầu đang mở, lọc đang mở/đã giải quyết/tất cả, phân trang 10 mục.
- Admin: cùng tab và bộ lọc nhưng chỉ chứa hồ sơ đã chuyển lên.
- Chi tiết, gửi bằng chứng, gửi phương án và quyết định đều dùng popup. Lưu chỉ cập nhật các dòng liên quan và popup, giữ vị trí cuộn.
- Dòng đơn có nhãn trạng thái có thể bấm để mở hồ sơ. Lịch sử ghi người gửi, mô tả, link và thời gian. Đếm ngược hiện thời hạn thực tế.
- Mỗi lần bổ sung/đổi trạng thái tạo thông báo cho buyer và seller trong `NotificationOutbox`. Không cấu hình SMTP thì thư ở trạng thái ghi nhận, không gửi ra ngoài.

## Endpoint

| Endpoint | Vai trò / nội dung |
| --- | --- |
| `GET /api/disputes/page?page=1&pageSize=10&filter=open` | Buyer/seller/admin; `filter`: `open`, `closed`, `all` |
| `GET /api/disputes/{id}` | Chi tiết và lịch sử, kiểm tra quyền sở hữu/phạm vi admin |
| `POST /api/orders/{id}/disputes` | Buyer: `description`, `evidenceLinks` (mảng URL) |
| `POST /api/disputes/{id}/evidence` | Buyer/seller: `description`, `evidenceLinks` |
| `POST /api/disputes/{id}/proposal` | Seller: `proposal` (`Refund`, `ReturnRefund`, `KeepOrder`), `description`, `evidenceLinks` |
| `POST /api/disputes/{id}/response` | Buyer: `accept`, `description` (bắt buộc khi không đồng ý) |
| `POST /api/disputes/{id}/resolution` | Admin: `buyerWins`, `reason` |

Endpoint cũ `fund-hold` vẫn tồn tại nhưng chuyển qua cùng quy trình: mở cần `reason` và `evidenceLinks`; giải quyết cần `releaseToSeller` và `reason`, chỉ khi hồ sơ đã chuyển admin. Không dùng endpoint cũ để bỏ qua thương lượng.

## Giữ tiền và nâng cấp dữ liệu

Khóa giữ tiền mới có dạng `order:{orderId}:case:{disputeId}:hold`; mỗi hồ sơ có khóa giải quyết riêng. Đóng một hồ sơ không làm mất lịch sử, mở lại trong thời hạn tạo hồ sơ và khoản giữ mới. Tài chính lấy khoản giữ gần nhất của đúng đơn.

Khoản `OnHold`/`RefundPending` có trước nâng cấp mà chưa có hồ sơ mới được worker nhập thành hồ sơ chuyển admin/đang hoàn tiền, trừ khoản giữ của yêu cầu trả hàng thường đang xử lý. Giữ nguyên giao dịch tài chính và các bản ghi tranh chấp cũ; lịch sử mới ghi rõ hồ sơ nhập từ trước nâng cấp.

Seller báo vấn đề sau khi hàng trả giao tới mình và trước deadline sẽ tạo/chuyển hồ sơ lên admin, giữ tiền và dừng tự refund. Buyer không mở thêm hồ sơ song song khi trả hàng thường đang hoạt động. Xem [RETURNS_AND_SHIPPING.md](RETURNS_AND_SHIPPING.md).

Migration `AddDisputeWorkflow` thêm trường trạng thái/thời gian, `rowversion`, bảng lịch sử và chỉ mục duy nhất cho một hồ sơ đang mở trên mỗi đơn. Chạy từ thư mục gốc sau khi dừng API đang chạy:

```powershell
dotnet ef database update --project backend/G4.Infrastructure --startup-project backend/G4.Api
```

Có thể duyệt SQL tại [dispute-workflow-migration.sql](../database/dispute-workflow-migration.sql). Script này nâng từ `AddOrderUpdatedAt`; dùng lệnh EF phía trên nếu database còn thiếu các migration trước đó. Sau migration, khởi động lại API và frontend.

Nếu trước đó migration lỗi `Cannot drop the index 'Dispute.IX_Dispute_orderId'`, dùng mã migration đã sửa rồi chạy lại lệnh EF. Schema nền không tạo chỉ mục này, nên bước xóa hiện kiểm tra tồn tại trước. Không cần xóa database, tạo lại dữ liệu hay xóa lịch sử migration.

## Kiểm thử

```powershell
dotnet run --project tests/G4.Commerce.Tests
node --test tests/ui-navigation.test.cjs tests/dispute-ui.test.cjs
./tests/smoke-commerce.ps1
```

Console tests kiểm tra deadline bằng đồng hồ giả, kiểm tra quyền, bằng chứng bất biến, thương lượng, hoàn tiền lỗi/thử lại, tự xác nhận hàng trả, mở lại hồ sơ và hàng chờ admin không che mất deadline. Smoke cần ứng dụng chạy với SQL Server đã migration và seed, tạo dữ liệu thử mới. InMemory tests không thay thế việc chạy smoke trên SQL Server.
