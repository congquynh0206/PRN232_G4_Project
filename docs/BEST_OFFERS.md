# Best Offer (trả giá)

## Trạng thái

Đã có giao diện Buyer/Seller, API và xử lý server cho trả giá, counteroffer, ngưỡng tự động,
hết hạn và chốt order thanh toán thẻ mô phỏng. Migration mới nhất là
`20261108124914_AddBestOffers`; migration đã được tạo nhưng **chưa áp lên database nào trong phiên này**.
Solution build thành công. Chưa chạy kiểm thử nghiệp vụ hoặc browser E2E.

## Cách dùng

1. Áp migration vào database phát triển có baseline và các migration trước đó:

   ```powershell
   dotnet ef database update --project backend/G4.Infrastructure --startup-project backend/G4.Api
   ```

   Lệnh này áp tất cả migration đang pending trong connection string hiện tại. Kiểm tra
   `ConnectionStrings__DefaultConnection` trước khi chạy. Không chạy baseline trên database có dữ liệu.
2. Chạy API và frontend theo [PROJECT_RUN.md](PROJECT_RUN.md); đăng nhập bằng tài khoản Buyer và Seller.
3. Seller mở **Best Offer**, bật “Cho phép người mua trả giá” trên sản phẩm giá cố định. Có thể
   bỏ trống hai ngưỡng; nếu cấu hình cả hai, ngưỡng từ chối phải thấp hơn ngưỡng chấp nhận.
4. Buyer mở **Trả giá**, chọn sản phẩm, mức giá, địa chỉ giao hàng và thẻ mô phỏng, rồi gửi.
   Nếu chưa có địa chỉ, bấm **Quản lý địa chỉ** ngay tại trang này và thêm địa chỉ; danh sách
   sẽ tự tải lại khi đóng hộp thoại.
   Phiên hiển thị lịch sử lượt, lượt buyer đã dùng và hạn phản hồi 24 giờ.
5. Seller chấp nhận/từ chối/trả giá lại; buyer có thể chấp nhận/từ chối/trả giá lại. Khi buyer
   chấp nhận counteroffer, hoặc seller chấp nhận, hệ thống thử thu tiền và chỉ tạo order `Paid`
   nếu mọi bước ghi payment/settlement thành công.

Thẻ thử `4111…1111` thành công; `4000…0002` bị từ chối. Đây là lựa chọn hồ sơ mô phỏng, không phải
nhập hoặc lưu thông tin thẻ thật. Buyer cần có địa chỉ; seller cần có địa chỉ lấy hàng mặc định.
Auto-accept cũng thử thanh toán ngay khi buyer gửi giá đạt ngưỡng. Auto-decline đóng phiên ngay.

## Phạm vi thanh toán và giới hạn

Best Offer hiện nhận thẻ mô phỏng. PayPal checkout hiện có là capture sau thao tác xác nhận của
buyer, không phải quyền thu tiền được cấp trước; vì vậy chưa dùng PayPal hoặc Klarna cho Best Offer.
Muốn mở PayPal cần bổ sung luồng authorization/vault được buyer chấp thuận trước, sau đó capture
đúng authorization khi offer được accept và reconcile kết quả provider. Không lưu PAN/CVV.

Worker kiểm tra hạn phiên mỗi 5 giây theo cấu hình hiện tại, deadline 24 giờ. Mỗi phiên có tối đa
5 lượt của buyer (lượt mở đầu tính là lượt thứ nhất). Product/order/payment/settlement được xử lý
trong transaction; lỗi thanh toán rollback order và trừ tồn. Chưa chạy SQL QA đồng thời hoặc E2E
trên database/provider thật trong lần triển khai này.
