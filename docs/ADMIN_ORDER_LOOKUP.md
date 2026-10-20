# Admin tra cứu đơn và giao dịch

Tab **Tra cứu đơn & giao dịch** trên `/Admin` tải các đơn mới nhất khi được mở. API chỉ cho role `admin` truy cập danh sách và chi tiết tại `GET /api/admin/order-lookup` và `GET /api/admin/order-lookup/{orderId}`. Chức năng chỉ đọc. Bản A1 gốc không cần migration riêng; từ SH1 ngày 20/10 cần migration tracking `20261020150408_AddShipmentReattemptSchedulesAndActors` trước khi chạy binary mới.

**SH1 ngày 20/10:** timeline mỗi vận đơn thêm `actorUserId`, `actorRole`, `reattemptAt`, `note`. UI hiển thị người thực hiện (system/shipper/dữ liệu cũ chưa ghi nhận), lịch dự kiến và lý do/ghi chú shipper đã escape. Legacy không được gán actor giả. Lịch và trạng thái giữ đúng Direction, không hiểu chuyển hoàn/trả hàng là buyer nhận hàng. Mã provider/tracking vẫn được che, quyền admin vẫn chỉ đọc. Migration chỉ áp QA riêng, chưa áp database chính; kiểm thử backend/UI chung và [SH1 SQL/browser fixture](RETURNS_AND_SHIPPING.md#sh1--lên-lịch-giao-lại-và-actor-20102026) đạt.

Tìm kiếm có lựa chọn **Tìm theo** và API nhận `SearchType=Auto/Order/Payment/Refund/Provider` (mặc định Auto):

| Cách nhập | Kết quả |
| --- | --- |
| `125`, `#125` hoặc `order:125` trong Auto | Chỉ đơn 125, không so với ID payment/refund |
| Chọn Mã thanh toán nội bộ + `125`, hoặc Auto + `payment:125` | Đơn chứa Payment 125 |
| Chọn Mã hoàn tiền nội bộ + `125`, hoặc Auto + `refund:125` | Đơn chứa Refund 125 |
| Mã provider order/capture/refund hoặc tham chiếu tích hợp đầy đủ | So khớp chính xác, không tìm bằng chuỗi con |

Nếu mã provider chỉ gồm chữ số, chọn loại Provider để không bị hiểu là mã đơn. Tiền tố không khớp loại đã chọn, ID không dương hoặc mã dài hơn 100 ký tự trả HTTP 400. `OrderId` vẫn phục vụ đọc chi tiết chính xác. Buyer/seller nhận bộ lọc theo tên, email hoặc ID; có khoảng tạo đơn, trạng thái đơn, lịch sử payment/refund; các lọc có thể kết hợp. Kết quả phân trang theo đơn nên nhiều payment/refund không nhân bản dòng. **Có thanh toán lỗi** lọc mọi đơn có ít nhất một Payment `Failed`, kể cả khi có lần `Succeeded` sau đó. Tổng thu và hoàn chỉ tính trạng thái `Succeeded`.

Giao diện 19/10: tìm mã là thao tác chính; bộ lọc nâng cao thu gọn mặc định. Điều kiện đã áp dụng hiện thành nhãn có nút bỏ riêng, kèm Xóa tất cả. Tìm kiếm/áp dụng lọc về trang 1; phân trang, làm mới và đóng chi tiết giữ điều kiện đã áp dụng, không lấy các ô đang gõ dở. Bộ lọc nhanh có Tất cả đơn; khi không có kết quả có gợi ý bỏ lọc, lỗi tải có nút Thử lại. Trạng thái đã thanh toán hiển thị riêng với ghi chú từng có lần lỗi. Điện thoại dùng thẻ đơn thay cho bảng ngang.

Chi tiết có ba số tiền toàn vòng đời và bốn tab Tổng quan / Thanh toán / Hoàn tiền / Vận chuyển. Hiển thị snapshot sản phẩm, mọi lần payment/refund và timeline từng vận đơn theo `Direction`, liên kết nhật ký tích hợp và hồ sơ dispute đã chuyển admin. Các tab hỗ trợ phím mũi tên/Home/End; phản hồi chi tiết đến muộn không mở lại cửa sổ đã đóng hoặc thay đơn đang xem. Hoàn chênh lệch ship giữ mô phỏng hiện có và ghi rõ Giả lập nội bộ; không thêm gọi PayPal refund trong task này. Email được che khi không có username; mã provider và tracking chỉ trả bốn ký tự cuối. Không trả địa chỉ, điện thoại, token, secret, idempotency key, số thẻ hay nội dung lỗi tự do. Danh sách/chi tiết không cung cấp thao tác sửa tiền, payment, tracking hoặc phán quyết.

**Thời gian:** giá trị SQL UTC có `Kind=Unspecified` được gắn UTC trong DTO chỉ đọc trước serialize, trả hậu tố `Z` cho order/payment/refund/shipment/event/dispute; không sửa dữ liệu đã lưu. UI chấp nhận payload cũ thiếu timezone như UTC, giữ nguyên `Z` và offset đã có, rồi hiển thị theo múi giờ trình duyệt. Bộ lọc datetime-local được đổi sang UTC trước gửi. Múi giờ và ý nghĩa ngày tạo đơn được ghi trên màn hình.

Ghi nhận ban đầu 19/10/2026: source được bổ sung, build và JavaScript syntax đã kiểm tra; chưa QA nghiệp vụ ở lượt ban đầu.

Đợt sửa tìm mã/UTC và thiết kế lại ngày 19/10/2026:

- `dotnet build G4_Project.sln --artifacts-path artifacts/admin-lookup-validation -m:1 --disable-build-servers -p:NuGetAudit=false`: pass, 0 warning/error. Dùng output riêng vì API/frontend đang chạy giữ khóa bin hiện tại.
- `dotnet run --project tests/G4.Commerce.Tests --no-build -- --admin-order-lookup`: pass. Bao phủ ID trùng giữa các bảng, tiền tố/loại mã, exact provider/reference, đầu vào lỗi, lịch sử retry, tổng Succeeded, phân trang ổn định, UTC JSON và SQL Server translation qua interceptor không kết nối DB.
- `node --test tests/*.test.cjs`: 113/113 pass, gồm 7 test mới cho UTC/offset, điều kiện đã áp dụng, race kết quả/chi tiết, đóng chi tiết, lỗi/thử lại, nội dung encode và lọc nhanh.
- `dotnet artifacts/admin-lookup-validation/bin/G4.Commerce.Tests/debug/G4.Commerce.Tests.dll`: bộ backend mặc định pass, gồm regression commerce/finance/dispute và kiểm thử A1 mới; SMTP loopback chạy ngoài sandbox do yêu cầu socket localhost.
- QA Chrome headless bằng HTML/CSS/JS thật của module với API fixture, desktop 1440px và mobile 390px: pass. Kiểm tra tab bàn phím, áp dụng/làm mới lọc, không tràn ngang hoặc lỗi JavaScript; ảnh tại `artifacts/admin-lookup-ui/`. Đây không phải E2E trên database chính.
- Không thay schema, không phát sinh hoặc áp migration; không thay số liệu tài chính. Cần khởi động lại API/frontend để chạy binary/Razor mới trên môi trường đang mở.
