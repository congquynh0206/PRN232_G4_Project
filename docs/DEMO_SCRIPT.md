================================================================================
KỊCH BẢN DEMO BÁO CÁO G4 (10/11/2026)
================================================================================

Ý tưởng chính: Theo chân một đơn hàng từ lúc buyer mua -> seller giao -> shipper giao ->
buyer trả hàng/tranh chấp -> admin phán quyết -> seller nhận và rút tiền. Mỗi bước dùng đơn
đã chuẩn bị sẵn trong database demo nên không phải chờ đồng hồ, không bị loạn.

Thời lượng: khoảng 15-18 phút. Phần đánh dấu (★) có thể cắt nếu thiếu thời gian.
Tài liệu gốc: database/demo/HUONG_DAN_DEMO.md


--------------------------------------------------------------------------------
0. CHUẨN BỊ
--------------------------------------------------------------------------------
G4_Demo.sql đã có đủ schema (mọi migration) và dữ liệu, kể cả Best Offer, Buy It Now và
Unpaid Item. Mỗi lần muốn demo chỉ cần:

1. Tắt API demo nếu đang chạy.
2. Mở database/demo/G4_Demo.sql trong SSMS -> Execute (F5). File tự xóa G4_Demo cũ và tạo lại từ đầu.
3. Mở 2 cửa sổ PowerShell tại repo:
   Cửa sổ 1: powershell -ExecutionPolicy Bypass -File .\database\demo\Run-Demo.ps1 -App Api -SqlServer localhost
   Cửa sổ 2: powershell -ExecutionPolicy Bypass -File .\database\demo\Run-Demo.ps1 -App Frontend
4. Mở http://localhost:5137.

Mốc thời gian tính từ lúc chạy SQL; trạng thái chuẩn bị sẵn giữ đúng khoảng 4 giờ.
Tối nay chạy một lượt để tập, sáng mai chạy lại file SQL trong vòng 1-2 giờ trước giờ báo cáo.

MỞ SẴN 4 CỬA SỔ TRÌNH DUYỆT (Đăng nhập trước khi lên báo cáo, Mật khẩu: G4@123456):
- Chrome thường: buyer@demo.test (Vai trò: Buyer)
- Chrome ẩn danh: seller.l1@demo.test (Vai trò: Seller cấp 1, phần lớn đơn demo thuộc shop này)
- Edge thường: shipper@demo.test (Vai trò: Shipper)
- Edge InPrivate: admin@demo.test (Vai trò: Admin)

Thẻ mô phỏng: 4111111111111111 (thành công), 4000000000000002 (bị từ chối), hạn 12/30.


--------------------------------------------------------------------------------
1. MỞ ĐẦU (1 phút)
--------------------------------------------------------------------------------
Nói ngắn:
- Sàn thương mại điện tử kiểu eBay, 4 vai trò: Buyer, Seller, Shipper, Admin.
- Kiến trúc: ASP.NET Core 8 Web API + frontend Razor MVC, SQL Server, EF Core, SignalR, worker nền.
- Hôm nay đi theo vòng đời một đơn hàng, từ lúc mua đến lúc seller rút được tiền.


--------------------------------------------------------------------------------
2. MUA HÀNG — BUYER (3 phút)
--------------------------------------------------------------------------------
Cửa sổ Buyer:
1. Giỏ hàng -> Tạo giỏ từ 2 seller -> Tiếp tục thanh toán.
   (Nói: giỏ có bao nhiêu seller cũng được, mỗi seller thành một đơn riêng; có thể trả riêng từng seller).
2. Chọn địa chỉ, nhập mã SAN5 hoặc DEMO10, bấm Cập nhật báo giá.
   (Nói: phí ship tính theo kg, giảm giá/miễn ship do seller cấu hình).
3. Chọn thẻ 4000000000000002 -> Đặt hàng & thanh toán -> Bị từ chối, đơn vẫn chờ thanh toán.
4. Đổi sang thẻ 4111111111111111 -> Thanh toán thành công -> Đơn hàng thấy 2 đơn mới.
5. (★) Mở chi tiết đơn trong Đơn hàng: xem trạng thái thanh toán và số tiền đã thu.
6. Quay sang cửa sổ Seller: chuông thông báo có badge mới (realtime qua SignalR).

*Dự phòng nếu lỗi: mở đơn #242 (PAY-FAILED) trong tab Đơn hàng của buyer, thanh toán lại.


--------------------------------------------------------------------------------
3. GIAO HÀNG — SELLER VÀ SHIPPER (3 phút)
--------------------------------------------------------------------------------
Dùng đơn #247 (đã thanh toán, chưa có vận đơn):
1. Seller -> Đơn hàng -> đơn #247 -> Chuẩn bị hàng -> Tạo vận đơn.
2. Shipper -> Quản lý vận đơn -> tab chờ nhận -> Nhận vận đơn của #247.
3. Shipper chọn Thao tác tiếp theo -> Lưu cập nhật lần lượt: PickedUp -> InTransit -> Bắt đầu giao hàng -> Giao thành công.
   (Nói: mỗi bước có lịch sử và người thao tác; seller/buyer thấy trạng thái cập nhật).
4. (★) Giao thất bại: mở đơn #253 -> Lên lịch giao lại (nhập lý do, thời gian).
   (Nói: lên lịch không tăng lượt giao, chỉ khi bấm bắt đầu giao lại mới tăng lượt).

*Dự phòng: Nếu #247 lỗi tạo vận đơn, dùng #249 cho shipper nhận, và #252 để bấm Giao thành công.

(★) Hạn bàn giao (SH2, cần migration AddOrderHandlingDeadline): dùng đơn VỪA thanh toán ở mục 2,
không dùng #247 (đơn có sẵn trong SQL demo là dữ liệu cũ, không có hạn nên không hiện đồng hồ).
1. Seller -> Đơn hàng: thẻ đơn mới có "Hạn bàn giao cho vận chuyển còn 01:59..." (Development 2 phút).
   (Nói: thật là 2 ngày, chỉnh Shipping:HandlingSeconds; hạn tính từ lúc thanh toán thành công).
2. Mở chi tiết đơn, bấm F5: đồng hồ chạy tiếp, không reset (deadline lưu ở server).
3. Tạo vận đơn khi còn giờ -> chi tiết đơn ghi "Bàn giao đúng hạn" kèm giờ tạo vận đơn và hạn.
4. Tracking chiều đi hiện "Giao dự kiến trước" = hạn bàn giao + 5 phút (Shipping:TransitSeconds).
   (Nói: để quá 2 phút rồi mới tạo vận đơn vẫn được, đơn ghi "Bàn giao trễ hạn").


--------------------------------------------------------------------------------
4. SAU KHI NHẬN HÀNG — ĐÁNH GIÁ VÀ TRẢ HÀNG (3 phút)
--------------------------------------------------------------------------------
1. Buyer -> Đơn hàng -> đơn #247 (vừa giao) hoặc #256:
   - Gửi đánh giá sản phẩm.
   - Bấm Yêu cầu trả hàng, chọn lý do -> xem popup báo giá (Lỗi seller = Seller chịu ship, Đổi ý = Trừ tiền ship). Gửi yêu cầu.
2. Seller -> Trả hàng / Hoàn tiền -> đơn #261 (hàng sai mô tả) -> Duyệt trả hàng.
   (Nói: duyệt xong hệ thống tạo vận đơn chiều trả riêng).
3. (★) Seller -> đơn #264 (hàng trả đã về) -> Xác nhận nhận hàng hợp lệ -> Hoàn tiền.
4. Mở đơn #265 (đã hoàn tiền) để xem kết quả hoàn.
5. (★) Seller -> Đánh giá sản phẩm -> đơn #266 -> Trả lời review.


--------------------------------------------------------------------------------
5. TRANH CHẤP VÀ ADMIN (2,5 phút)
--------------------------------------------------------------------------------
1. (★) Buyer -> đơn #262 (seller từ chối trả hàng) -> Mở tranh chấp.
   (Nói: bị từ chối thì không tự hoàn tiền, buyer phải mở tranh chấp).
2. Admin -> Tranh chấp -> hồ sơ của đơn #269 -> Xem lịch sử -> Xem trước số tiền -> Xác nhận và lưu phán quyết.
   (Popup bắt nhập lại mật khẩu vì liên quan đến tiền).
3. Admin -> Tra cứu đơn -> nhập 265 -> Xem toàn bộ thanh toán, vận đơn, email ở một chỗ.


--------------------------------------------------------------------------------
6. TIỀN CỦA SELLER (3 phút)
--------------------------------------------------------------------------------
1. Đăng nhập seller.l3@demo.test -> Ví & Rút tiền:
   - Cấp 3 nhận tiền ngay; Cấp 1/2 bị giữ tiền một thời gian. Xem bảng "Tiền giữ theo từng đơn".
   - Nhập số tiền -> Rút tiền -> Nhập lại mật khẩu.
   - (★) Xem payout #6 bị ngân hàng trả lại: tiền tự động cộng lại vào ví.
2. (★) Báo cáo tài chính: Doanh thu, phí sàn (12% + 0.3 USD), hoàn tiền.
3. Đăng nhập seller.up2@demo.test -> Cấp người bán -> Đánh giá lại cấp của tôi -> Lên cấp 2.
4. Admin -> Khoản nợ và Nợ khó thu / Danh sách chặn:
   - Quá hạn bị hạn chế bán/rút. Quá lâu bị write-off và vào danh sách chặn.
5. (★) Mở cửa sổ ẩn danh -> Đăng ký seller -> dùng nút mẫu định danh bị chặn (999100000008) -> Bị từ chối.


--------------------------------------------------------------------------------
7. TÍNH NĂNG MỚI: BEST OFFER, BUY IT NOW, UNPAID ITEM (3 phút)
--------------------------------------------------------------------------------
(Dữ liệu chuẩn bị: 3 sản phẩm đầu của seller.l1 đã bật trả giá).
1. Seller (seller.l1) -> Best Offer -> phiên #2: buyer đề nghị 100$ cho món 120$ -> Trả giá lại 110$.
2. Buyer -> Trả giá -> phiên #3: seller đã counter 130$ -> Chấp nhận (nhập mật khẩu) -> Đơn chờ thanh toán được tạo.
3. Buyer -> Trả giá -> "[L1] Wireless Headphones" (100$): Gửi 40$ tự bị từ chối; Gửi 96$ tự được chấp nhận tạo đơn.
4. (★) Buyer -> Đơn hàng -> đơn #273 (tạo từ Best Offer, 85$) -> Thanh toán thẻ.
5. Buy It Now (Mua ngay): Buyer -> Sản phẩm -> "[L1] Buy It Now..." -> Trả thẻ ngay. Đơn Paid lập tức.
   (★) 2 người cùng lúc thanh toán món cuối cùng -> Chỉ 1 người mua được, người kia được hoàn tiền giữ.
6. Unpaid Item: Seller -> Đơn hàng -> đơn #243 (quá hạn) -> Hủy đơn quá hạn -> Hoàn kho, buyer bị 1 điểm phạt.
   (★) Đơn #245 (buyer.other) hủy tương tự -> buyer này đủ 2 điểm phạt -> Bị khóa tài khoản không cho đăng nhập.


--------------------------------------------------------------------------------
8. KẾT (30 giây)
--------------------------------------------------------------------------------
Nói nhanh phần kỹ thuật:
- Rate limit theo user/IP, chia tải Nginx, test tải JMeter, CI.
- Chống gửi trùng (idempotency key), kiểm tra quyền bằng JWT.
- Email và log xem ở Nhật ký tích hợp.


================================================================================
MẸO ĐỂ KHÔNG BỊ LOẠN
================================================================================
- Đi theo thứ tự: Buyer -> Seller -> Shipper -> Buyer -> Seller -> Admin -> Seller.
- Ghi giấy mã đơn: 247, 253, 256, 261, 264, 265, 266, 262, 269, 273, 243, 245; phiên Best Offer #2, #3.
- Nếu một bước lỗi: Nói "bước này em chuyển sang đơn đã chuẩn bị sẵn" và mở đơn dự phòng, không debug trước lớp.
- Không demo PayPal/Klarna live (chỉ nói là có hỗ trợ).


================================================================================
CÂU HỎI GIẢNG VIÊN HAY HỎI
================================================================================
Hỏi: Sao seller không nhận tiền ngay?
Đáp: Giữ tiền để còn hoàn cho buyer khi trả hàng/tranh chấp. Cấp 3 thì được nhận ngay.

Hỏi: Bấm thanh toán 2 lần thì sao?
Đáp: Mỗi thao tác tiền có gửi kèm key (idempotency). Trùng key thì trả về kết quả cũ, không thu hai lần.

Hỏi: Hai người mua món cuối cùng cùng lúc?
Đáp: Buy It Now giữ tiền trước, chỉ 1 người thu tiền và tạo đơn thành công, người kia tự động được hủy giữ tiền.

Hỏi: Buyer không trả tiền?
Đáp: Quá hạn seller hủy đơn, kho trả lại, buyer bị cộng 1 điểm phạt. 2 điểm / 30 ngày = Khóa tài khoản.

Hỏi: Seller nợ không trả?
Đáp: Quá hạn bị cấm bán/rút tiền. Quá lâu bị write-off và chặn định danh vĩnh viễn.

Hỏi: Thông báo realtime làm thế nào?
Đáp: SignalR đẩy tín hiệu, dữ liệu lưu trong bảng thông báo theo tài khoản.
================================================================================