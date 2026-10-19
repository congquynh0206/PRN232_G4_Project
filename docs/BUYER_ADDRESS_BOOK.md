# Sổ địa chỉ buyer

Buyer mở **Quản lý địa chỉ** trong checkout để thêm/sửa/xóa/đặt mặc định. API `/api/addresses` chỉ nhận buyer hiện đăng nhập, kiểm tra quyền theo UserId ở mọi thao tác. Dùng schema Address hiện có, không cần migration mới. Tối đa 10 địa chỉ đang trong sổ; địa chỉ đầu tiên tự mặc định, xóa mặc định chọn địa chỉ còn lại có ID nhỏ nhất. Đổi mặc định không tự thay lựa chọn giao hàng trong checkout đang mở.

Chọn/sửa/xóa địa chỉ đang giao làm mất báo giá cũ và tính lại từng nhóm seller. Nút đặt hàng khóa trong lúc lưu địa chỉ/lấy báo giá; phản hồi báo giá cũ đến muộn bị bỏ qua. Lưu địa chỉ khóa Lưu/Hủy/Đóng và Escape đến khi request kết thúc; lỗi giữ dữ liệu để thử lại.

## Bảo vệ đơn cũ và retry

Địa chỉ chưa dùng trong đơn có thể xóa vật lý. Địa chỉ đã được OrderTable tham chiếu giữ lại làm dữ liệu lịch sử: tách UserId và bỏ cờ mặc định để không còn nằm trong sổ buyer. `OrderTable.AddressId`, snapshot địa chỉ và tiền giữ nguyên. Buyer không thể đọc/sửa/chọn địa chỉ lưu trữ qua API sổ hoặc dùng tạo đơn mới. Không nới kiểm tra cùng checkout key khác request; retry đúng yêu cầu cũ vẫn trả đơn ban đầu.

Việc lưu trữ dùng các cột nullable hiện có, không thêm schema hoặc chuyển dữ liệu cũ. Các đơn đã bị phiên bản cũ đặt AddressId=null trước đợt sửa không được suy diễn/backfill lại tham chiếu khi không có bằng chứng.

## Kiểm thử khi hợp nhất ngày 19/10/2026

- Build solution: pass, 0 warning/error trên worktree kiểm tra riêng.
- `dotnet run --project tests/G4.Commerce.Tests --no-build -- --buyer-address-book`: pass. Kiểm tra quyền chéo, chuẩn hóa/mặc định, xóa/lưu trữ, snapshot và tiền không đổi, checkout retry sau xóa không trùng đơn, địa chỉ lưu trữ bị chặn cho đơn mới, khác request cùng key bị từ chối.
- Bộ backend mặc định: pass, gồm hồi quy payment/finance/return/dispute và admin lookup.
- `node --test tests/*.test.cjs`: 115/115 pass. Harness giỏ hàng nạp module địa chỉ thật; kiểm tra quote theo seller, khóa checkout, retry cùng key, thẻ/PayPal và khóa form khi lưu/thất bại.
- Trình duyệt Chrome desktop/mobile cho admin lookup sau hợp nhất: pass bằng API fixture. Chưa có SQL Server QA/E2E thật cho CRUD địa chỉ ở đợt này.
- Không áp migration, không chạy seed hoặc thay dữ liệu database chính.
