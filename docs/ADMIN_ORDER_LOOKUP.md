# Admin tra cứu đơn và giao dịch

Tab **Tra cứu đơn & giao dịch** trên `/Admin` tải các đơn mới nhất khi được mở. API chỉ cho role `admin` truy cập danh sách và chi tiết tại `GET /api/admin/order-lookup` và `GET /api/admin/order-lookup/{orderId}`. Chức năng chỉ đọc, dùng schema hiện tại và không cần migration mới.

Tìm kiếm nhận mã đơn (có hoặc không có `#`), ID payment/refund và chuỗi mã provider order/capture/refund hoặc mã tham chiếu tích hợp. Buyer/seller nhận bộ lọc theo tên, email hoặc ID; có khoảng tạo đơn, trạng thái đơn, payment và refund; các lọc có thể kết hợp. Kết quả phân trang theo đơn nên nhiều payment/refund không nhân bản dòng. **Có thanh toán lỗi** lọc mọi đơn có ít nhất một Payment `Failed`, kể cả khi có lần `Succeeded` sau đó. Tổng thu và hoàn chỉ tính trạng thái `Succeeded`.

Chi tiết hiển thị snapshot sản phẩm, các số đã thu/đã hoàn/còn lại, mọi payment/refund, timeline từng vận đơn theo `Direction`, nhật ký tích hợp theo order và hồ sơ dispute đã chuyển admin. Email được che khi không có username; mã provider và tracking chỉ trả bốn ký tự cuối. Không trả địa chỉ, điện thoại, token, secret, idempotency key, số thẻ hay nội dung lỗi tự do. Danh sách/chi tiết không cung cấp thao tác sửa tiền, payment, tracking hoặc phán quyết.

Trạng thái triển khai: source đã bổ sung ngày 19/10/2026; `dotnet build G4_Project.sln -m:1 -p:UseSharedCompilation=false` thành công, JavaScript qua `node --check`; chưa chạy QA/test nghiệp vụ trong lượt này. Không có migration phát sinh.
