# Buyer — Lịch sử giao dịch (B2)

Buyer mở tab **Lịch sử giao dịch** để xem mọi lần thanh toán và refund, có thể lọc theo mã đơn, thời gian tạo, loại và trạng thái. Danh sách sắp xếp mới nhất trước, phân trang 20 dòng; làm mới chỉ đọc dữ liệu hiện có. Mã đơn mở chi tiết; nút trong chi tiết đơn mở tab này với bộ lọc đơn.

API `GET /api/buyer/transactions` chỉ nhận role buyer và ràng buộc mọi truy vấn qua `OrderTable.BuyerId == JWT user id`. Query hỗ trợ `page`, `pageSize` (tối đa 100), `orderId`, `from`, `to` (to exclusive), `type` và `status`. `paidFrom/paidTo` giới hạn tổng theo `Payment.PaidAt`; `completedFrom/completedTo` giới hạn tổng refund theo `Refund.CompletedAt`. Không tự thay timestamp thiếu bằng ngày tạo. Tổng vòng đời từng đơn trả về cùng response, độc lập trang, filter và kỳ; chỉ Payment/Refund `Succeeded` được tính. `GET /api/buyer/transactions/{type}/{id}` trả chi tiết riêng và vẫn kiểm tra chủ đơn. Không có thao tác tạo/capture/refund.

Phương thức hiển thị Credit Card là thẻ giả lập và PayPal là Sandbox. Refund chênh lệch ship được gắn nhãn giả lập. Không có migration: dùng các bảng Payment/Refund hiện hữu. Database chính không được nâng cấp trong thay đổi này.

## Kế hoạch B2 và triển khai ngày 22/10/2026

- Kế hoạch: (1) UI tab, preset 30 ngày/tất cả/tùy chọn; (2) API chỉ đọc theo BuyerId, danh sách mới nhất/phân trang và chi tiết; (3) tách filter ngày tạo khỏi tổng theo PaidAt/CompletedAt; (4) luôn tổng vòng đời đơn độc lập filter/trang; (5) hồi quy failed→success, refund partial/pending, buyer khác, và không ghi giao dịch khi refresh; (6) build/QA browser, cập nhật docs và migration state.
- Đã hoàn thành source/API/UI, endpoint chi tiết, bộ lọc preset, tổng theo kỳ và vòng đời đơn. Thêm regression backend `--buyer-transactions` và `tests/buyer-transactions-ui.test.cjs`.
- Build API/frontend đạt 0 lỗi; regression backend `--buyer-transactions` pass; regression UI 3/3 pass; `node --check` và `git diff --check` đạt. Có cảnh báo NU1900 do không truy cập được vulnerability feed nuget.org; project test còn MSB3277 do tham chiếu API để kiểm tra controller.
- `tests/role-navigation.e2e.cjs` chạy Chrome fixture trên Razor thật: Buyer/Seller/Admin đạt, Buyer có 5 tab và tab giao dịch tải thành công, 320–1440px, Back/Forward/reload/query link, không có request ghi hoặc lỗi JS.
- Backend regression dùng EF InMemory và xác minh thất bại + thành công, refund Pending không cộng, refund 20 thành công trên payment 100 cho còn 80, tổng theo PaidAt/CompletedAt không fallback CreatedAt, filter, pagination, buyer isolation và read-only. Không có SQL QA hoặc provider thật; database chính không được chạm. Không có migration.
- B2 source và QA fixture đạt; kịch bản SQL/provider demo trên môi trường QA còn mở trước khi đóng nghiệm thu tích hợp.

## Cần nghiệm thu

- Chạy `dotnet run --project tests/G4.Commerce.Tests -- --buyer-transactions` và `node --test tests/buyer-transactions-ui.test.cjs`.
- Bổ sung browser fixture riêng cho dữ liệu dòng để nghiệm thu copy, modal chi tiết, lọc, link đơn và thể hiện totals; E2E điều hướng đã đạt.
- Kịch bản SQL/payment gateway thực tế chưa chạy; không tự áp migration hoặc tạo payment/refund trên DB chính để demo.
