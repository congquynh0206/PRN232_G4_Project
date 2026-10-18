# Làm việc với G4_Project

## Đọc trước khi code

1. Đọc `docs/README.md` để xác định tài liệu liên quan.
2. Đọc `docs/BUSINESS_REQUIREMENTS.md`: đặc tả mục tiêu phiên bản 2.0, chốt ngày 18/10/2026.
3. Đọc `docs/IMPLEMENTATION_BACKLOG.md`: task, khoảng cách với source, phụ thuộc và nghiệm thu.
4. Đọc hướng dẫn module liên quan và `docs/PROJECT_RUN.md` để biết API/schema/test/cách chạy hiện tại.

Yêu cầu mới nhất của người dùng ưu tiên hơn tài liệu. Nếu tài liệu module, kế hoạch cũ hoặc code khác đặc tả mục tiêu, đó là khoảng cách cần xử lý, không tự đổi lại nghiệp vụ đã chốt theo code cũ. Phạm vi 17/10 và bốn task ưu tiên còn áp dụng khi không bị đặc tả 18/10 thay thế. Không coi toàn bộ backlog là lệnh phải làm trong một task; làm đúng yêu cầu đang được giao.

## Những điểm dễ nhầm

- Ship theo kg lẻ; phí mặc định 12% SellerGross + 0,30 USD; doanh số tháng UTC.
- Một lần đổi địa chỉ thành công; chỉ áp dụng sau đủ xác nhận cần thiết. Chưa có shipper thì không chờ shipper; vượt cap seller thì từ chối, không thu thêm buyer.
- Return window: 7 ngày / Development 300 giây từ Delivered chiều đi. Receipt: 48 giờ / 45 giây từ Delivered chiều trả. Phản đối từ RejectedAt và INR là deadline riêng.
- Giao lại phải qua ReattemptScheduled; hai chiều dùng chung trạng thái và Direction.
- Free shipping chiều đi khác free returns theo mặt hàng. Free shipping đã có loại/cờ; tái sử dụng.
- Level 3 vào Available ngay sau payment và bù nợ; payout ngân hàng ảo là workflow riêng cho mọi level. Admin chỉnh hạn mức riêng, không nhập level tùy ý.
- Write-off do worker; capture + reconcile là luồng chính; webhook và kế toán kép đầy đủ chưa bắt buộc.

## Khi triển khai và hoàn tất

- Giữ một seller/order, cart tối đa hai seller, return toàn bộ, một kiện mỗi chiều; không tự thêm chat/COD/catalog đầy đủ.
- Dùng migration giữ dữ liệu, snapshot và lịch sử cũ; không chạy lại baseline trên database có dữ liệu. Không suy diễn trạng thái database chính từ QA/source.
- Mọi thay đổi tiền phải kiểm tra quyền server, idempotency, transaction, tính đồng thời và retry/rollback. Không giả Succeeded khi provider còn pending/Verifying.
- Cập nhật đặc tả nếu có quyết định mới; cập nhật trạng thái/kiểm thử ở backlog và hướng dẫn module sau khi làm. Chỉ ghi hoàn thành khi có bằng chứng kiểm thử phù hợp; nêu migration đã/chưa áp dụng.
- Tôn trọng thay đổi chưa commit đang có; không hoàn tác hoặc ghi đè phần việc không thuộc task.
