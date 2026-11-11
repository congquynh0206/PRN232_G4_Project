# Kịch bản demo báo cáo G4 (10/11/2026)

Ý tưởng chính: **theo chân một đơn hàng** từ lúc buyer mua → seller giao → shipper giao →
buyer trả hàng/tranh chấp → admin phán quyết → seller nhận và rút tiền. Mỗi bước dùng đơn
đã chuẩn bị sẵn trong database demo nên không phải chờ đồng hồ, không bị loạn.

Thời lượng: khoảng **15–18 phút**. Phần đánh dấu ★ có thể cắt nếu thiếu thời gian.

Tài liệu gốc: [database/demo/HUONG_DAN_DEMO.md](../database/demo/HUONG_DAN_DEMO.md)
(bảng kịch bản và tài khoản).

---

## 0. Chuẩn bị

`G4_Demo.sql` đã có đủ schema (mọi migration) và dữ liệu, kể cả Best Offer, Buy It Now và
Unpaid Item. Mỗi lần muốn demo chỉ cần:

1. Tắt API demo nếu đang chạy.
2. Mở `database/demo/G4_Demo.sql` trong SSMS → **Execute**. File tự xóa `G4_Demo` cũ (nếu có)
   và tạo lại từ đầu.
3. Mở hai PowerShell tại repo:

   ```powershell
   powershell -ExecutionPolicy Bypass -File .\database\demo\Run-Demo.ps1 -App Api -SqlServer localhost
   ```

   ```powershell
   powershell -ExecutionPolicy Bypass -File .\database\demo\Run-Demo.ps1 -App Frontend
   ```

   `-ExecutionPolicy Bypass` tránh lỗi "running scripts is disabled" của Windows, chỉ áp dụng
   cho lệnh đó.

4. Mở `http://localhost:5137`.

Mốc thời gian tính từ **lúc chạy SQL**; trạng thái chuẩn bị sẵn giữ đúng khoảng **4 giờ**.
Tối nay chạy một lượt để tập, sáng mai chạy lại file SQL trong vòng 1–2 giờ trước giờ báo cáo.
Phải chạy API bằng `Run-Demo.ps1` (không chạy bằng Visual Studio) để dùng đúng database,
khóa định danh và thời hạn demo.

### Mở sẵn 4 cửa sổ trình duyệt, mỗi role một cửa sổ

Đăng nhập sẵn **trước** khi lên báo cáo (đăng nhập giới hạn 10 lần/phút/IP, đổi tài khoản
liên tục dễ bị chặn 429). Mật khẩu chung: `G4@123456`.

| Cửa sổ | Tài khoản | Ghi chú |
| --- | --- | --- |
| Chrome thường | `buyer@demo.test` | Buyer |
| Chrome ẩn danh | `seller.l1@demo.test` | Seller cấp 1, phần lớn đơn demo thuộc shop này |
| Edge thường | `shipper@demo.test` | Shipper |
| Edge InPrivate | `admin@demo.test` | Admin |

Các tài khoản seller khác (`seller.l3`, `seller.up2`) đăng nhập lại trong cửa sổ seller khi
tới phần tài chính. Để zoom trình duyệt khoảng 90% cho dễ nhìn trên máy chiếu.

Thẻ mô phỏng: `4111111111111111` (thành công), `4000000000000002` (bị từ chối), hạn `12/30`.

---

## 1. Mở đầu (1 phút)

Nói ngắn:

- Sàn thương mại điện tử kiểu eBay, 4 vai trò: Buyer, Seller, Shipper, Admin.
- Kiến trúc: ASP.NET Core 8 Web API + frontend Razor MVC, SQL Server, EF Core,
  SignalR cho thông báo realtime, worker nền xử lý hạn/tiền.
- Hôm nay đi theo vòng đời một đơn hàng, từ lúc mua đến lúc seller rút được tiền.

## 2. Mua hàng — Buyer (3 phút)

Cửa sổ **Buyer**.

1. **Giỏ hàng → Tạo giỏ từ 2 seller → Tiếp tục thanh toán.**
   Nói: giỏ tối đa 2 seller, khi đặt sẽ tách thành 2 đơn riêng.
2. Ở trang Thanh toán: chọn địa chỉ, nhập mã `SAN5` (mã sàn) hoặc `DEMO10` (mã shop),
   bấm **Cập nhật báo giá**.
   Nói: phí ship tính theo kg, giảm giá/miễn ship do seller cấu hình.
3. Chọn thẻ `4000000000000002` → **Đặt hàng & thanh toán** → bị từ chối, đơn vẫn chờ
   thanh toán (không mất đơn, không trừ tiền).
4. Đổi sang thẻ `4111111111111111` → thanh toán thành công → **Đơn hàng** thấy 2 đơn mới.
5. ★ Mở chi tiết đơn trong **Đơn hàng**: xem trạng thái thanh toán và số tiền đã thu.
6. Quay sang cửa sổ **Seller**: chuông thông báo có badge mới (realtime qua SignalR).

Dự phòng nếu lỗi: mở đơn **#242** (PAY-FAILED) trong tab Đơn hàng của buyer, thanh toán lại.

## 3. Giao hàng — Seller và Shipper (3 phút)

Dùng đơn **#247** (đã thanh toán, chưa có vận đơn) để chạy liên tục một đơn.

1. **Seller** → Đơn hàng → đơn #247 → **Chuẩn bị hàng** → **Tạo vận đơn**.
2. **Shipper** → Quản lý vận đơn → tab chờ nhận → **Nhận vận đơn** của #247.
3. Shipper chọn **Thao tác tiếp theo** → **Lưu cập nhật** lần lượt: PickedUp → InTransit →
   **Bắt đầu giao hàng** (OutForDelivery) → **Giao thành công** (Delivered).
   Nói: mỗi bước có lịch sử và người thao tác; seller/buyer thấy trạng thái cập nhật.
4. ★ Giao thất bại: mở đơn **#253** → **Lên lịch giao lại** (nhập lý do, thời gian).
   Nói: lên lịch không tăng lượt giao, chỉ khi bấm bắt đầu giao lại mới tăng lượt.

Dự phòng: nếu #247 lỗi khi tạo vận đơn, dùng **#249** (đã có vận đơn) cho shipper nhận, và
**#252** (đang giao) để bấm Giao thành công.

## 4. Sau khi nhận hàng — đánh giá và trả hàng (3 phút)

1. **Buyer** → Đơn hàng → đơn #247 (vừa giao) hoặc **#256**:
   - **Gửi đánh giá sản phẩm** (số sao + nhận xét).
   - Bấm **Yêu cầu trả hàng**, chọn lý do → xem popup báo giá: phí trả hàng thay đổi theo
     lý do (đổi ý thì buyer chịu ship chiều về, hàng sai mô tả thì seller chịu).
     Chỉ ra đồng hồ đếm ngược hạn trả hàng. Có thể gửi luôn.
2. **Seller** → **Trả hàng / Hoàn tiền** → đơn **#261** (hàng không đúng mô tả) →
   **Duyệt trả hàng**. Nói: duyệt xong hệ thống tạo vận đơn chiều trả riêng.
3. ★ **Seller** → đơn **#264** (hàng trả đã về) → xác nhận nhận hàng hợp lệ → hoàn tiền.
4. Mở đơn **#265** (đã hoàn tiền) để xem kết quả: số tiền hoàn, phí trả hàng, bút toán.
5. ★ **Seller** → **Đánh giá sản phẩm** → đơn **#266** → trả lời review.

## 5. Tranh chấp và Admin (2,5 phút)

1. ★ **Buyer** → đơn **#262** (seller từ chối trả hàng) → **Mở tranh chấp**.
   Nói: bị từ chối thì không tự hoàn tiền, buyer phải mở tranh chấp.
2. **Admin** → **Tranh chấp** → hồ sơ của đơn **#269** → xem lịch sử, bằng chứng hai bên
   → chọn kết luận, xem trước số tiền → **Xác nhận và lưu phán quyết**.
   Popup bắt **nhập lại mật khẩu** (thao tác quan trọng về tiền).
3. **Admin** → **Tra cứu đơn** → nhập `265` → xem toàn bộ thanh toán, vận đơn, hoàn tiền,
   email của một đơn ở một chỗ.

## 6. Tiền của seller (3 phút)

1. Cửa sổ Seller đăng nhập `seller.l3@demo.test` → **Ví & Rút tiền**:
   - Seller cấp 3 nhận tiền vào Available ngay sau thanh toán; cấp 1/2 phải giữ tiền một
     thời gian sau khi giao (demo: 4/2 giờ). Chỉ bảng **Tiền giữ theo từng đơn**.
   - Nhập số tiền → **Rút tiền** (nếu có popup thì nhập lại mật khẩu). Trạng thái tự chạy qua
     Created → InProgress → FundsSent → Completed do worker nền.
   - ★ Xem payout **#6** bị ngân hàng trả lại: tiền vào lại ví đúng một lần.
2. ★ **Báo cáo tài chính**: doanh thu, phí sàn (12% + 0,30 USD), hoàn tiền theo kỳ.
3. Đăng nhập `seller.up2@demo.test` → **Cấp người bán** → **Đánh giá lại cấp của tôi**
   → lên cấp 2. Nói: hệ thống xét dựa trên số đơn, giao đúng hạn, feedback thật.
4. **Admin** → **Khoản nợ** và **Nợ khó thu / Danh sách chặn**:
   - Seller nợ quá hạn bị hạn chế bán/rút.
   - Seller bị write-off nằm trong danh sách chặn.
5. ★ Mở cửa sổ ẩn danh mới → trang đăng nhập → **Đăng ký seller** → dùng nút mẫu định danh
   bị chặn (hoặc nhập `999100000008`) → bị từ chối vì định danh đã bị blacklist.

## 7. Tính năng mới: Best Offer, Buy It Now, Unpaid Item (3 phút)

Dữ liệu đã chuẩn bị sẵn: 3 sản phẩm đầu của `seller.l1` đã bật trả giá, có sẵn 3 phiên.

1. **Seller** (`seller.l1`) → **Best Offer** → phiên **#2**: buyer đề nghị 100 USD cho món
   120 USD → **trả giá lại** 110 USD (hoặc chấp nhận, nhập lại mật khẩu).
2. **Buyer** → **Trả giá** → phiên **#3**: seller đã counter 130 USD → **chấp nhận**
   (nhập lại mật khẩu) → đơn chờ thanh toán được tạo theo giá đã chốt.
3. **Buyer** → **Trả giá** → "[L1] Wireless Headphones" (100 USD, ngưỡng tự động):
   gửi 40 USD → tự bị từ chối; gửi 96 USD → tự được chấp nhận và tạo đơn.
   Nói: tối đa 5 lượt/sản phẩm, mỗi lượt hết hạn sau 24 giờ.
4. ★ **Buyer** → **Đơn hàng** → đơn **#273** (tạo từ Best Offer, 85 USD) → thanh toán thẻ.
5. **Buy It Now**: Buyer vào `/Buyer/Products` → "[L1] Buy It Now - Portable Charger
   (trả ngay)" → xem báo giá → **Pay Now** thẻ `4111…` → đơn Paid ngay. Nói: tiền được giữ
   (authorize) trước, chỉ thu khi chắc chắn còn hàng.
   ★ Món "[L1] Buy It Now - Compact Camera (chỉ còn 1 cái)": hai cửa sổ buyer cùng Pay Now
   → chỉ một người mua được, người kia được hủy giữ tiền.
6. **Unpaid Item**: **Seller** → Đơn hàng → đơn **#243** (quá hạn thanh toán) →
   **Hủy đơn quá hạn thanh toán** (nhập lại mật khẩu) → hoàn kho, buyer bị 1 điểm phạt.
   ★ Đơn **#245** của `buyer.other`: hủy tương tự → buyer này đủ 2 điểm, đăng nhập
   `buyer.other@demo.test` bị từ chối vì tài khoản bị khóa.

## 8. Kết (30 giây)

Kể nhanh phần kỹ thuật không hiện trên giao diện:

- Rate limit theo user/IP, test tải bằng JMeter, chia tải bằng Nginx, CI.
- Mọi thao tác tiền có kiểm tra quyền phía server, chống gửi trùng (idempotency key),
  transaction và nhập lại mật khẩu.
- Email và log tích hợp xem ở **Nhật ký tích hợp**.

---

## Mẹo để không bị loạn

- **Đi theo thứ tự cửa sổ**: Buyer → Seller → Shipper → Buyer → Seller → Admin → Seller.
  Mỗi lần chuyển cửa sổ nói một câu "giờ sang vai trò ...".
- **Ghi giấy mã đơn** cho từng bước: 247, 253, 256, 261, 264, 265, 266, 262, 269, 273,
  243, 245; phiên Best Offer #2, #3. Bảng đầy đủ ở `database/demo/HUONG_DAN_DEMO.md`.
- Mã đơn chỉ đúng khi database được tạo từ file `G4_Demo.sql` hiện tại và chưa ai thao tác.
  Nếu đã tập dượt trên database đó, chạy lại file SQL trước giờ báo cáo.
- Nếu một bước lỗi: nói "bước này em chuyển sang đơn đã chuẩn bị sẵn" và mở đơn dự phòng,
  không debug trước lớp.
- Không demo PayPal/Klarna live (cần tài khoản sandbox và mạng). Chỉ nói là có hỗ trợ.
- Nếu nhóm chia người trình bày: mỗi người phụ trách một phần 2–7 theo chức năng mình làm.

## Câu hỏi giảng viên hay hỏi

| Câu hỏi | Trả lời ngắn |
| --- | --- |
| Sao seller không nhận tiền ngay? | Giữ tiền để còn hoàn tiền nếu buyer trả hàng/tranh chấp; cấp càng cao giữ càng ngắn, cấp 3 nhận ngay. |
| Bấm thanh toán 2 lần thì sao? | Mỗi thao tác tiền gửi kèm key; gửi lại cùng key trả về kết quả cũ, không thu hai lần. |
| Hai người mua món cuối cùng cùng lúc? | Buy It Now giữ tiền trước, chỉ một người được thu tiền và tạo đơn, người kia được hủy giữ tiền. |
| Buyer không trả tiền? | Hàng được giữ đến hạn thanh toán; quá hạn seller hủy đơn, kho được trả lại, buyer bị 1 điểm phạt, 2 điểm trong 30 ngày thì bị khóa. |
| Seller nợ không trả? | Quá hạn bị hạn chế bán/rút; quá lâu thì write-off và chặn định danh vĩnh viễn. |
| Thông báo realtime làm thế nào? | SignalR đẩy tín hiệu, dữ liệu lưu trong bảng thông báo theo tài khoản. |
