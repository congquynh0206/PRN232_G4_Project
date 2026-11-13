# Hướng dẫn sử dụng cho người dùng (End-user Manual)

Dự án G4_Project, PRN232, nhóm 4. Tài liệu dành cho ba vai trò **Buyer** (người mua),
**Seller** (người bán) và **Shipper** (người giao hàng). Vai trò Admin xem
[Administrator Manual](ADMIN_MANUAL.md).

> Hệ thống là bản mô phỏng: thanh toán, thẻ, ngân hàng và định danh đều giả lập. Không nhập
> thông tin thẻ hay tài khoản thật.

## Mục lục

1. [Bắt đầu](#1-bắt-đầu)
2. [Buyer](#2-buyer)
3. [Seller](#3-seller)
4. [Shipper](#4-shipper)
5. [Câu hỏi thường gặp](#5-câu-hỏi-thường-gặp)

---

## 1. Bắt đầu

### 1.1. Đăng nhập

1. Mở địa chỉ trang web (mặc định `http://localhost:5131`).
2. Nhập **Địa chỉ thư điện tử** và **Mật khẩu**, bấm **Đăng nhập**.
3. Hệ thống tự chuyển tới trang của vai trò tương ứng.

Tài khoản thử có mật khẩu chung `G4@123456`, ví dụ `buyer@example.test`,
`seller@example.test`, `shipper@example.test` (dữ liệu demo dùng đuôi `@demo.test`).

Đăng nhập sai quá 10 lần trong 1 phút sẽ bị tạm chặn khoảng 1 phút.

### 1.2. Đăng ký tài khoản

- **Người mua:** ở trang đăng nhập bấm **Đăng ký người mua**, nhập Tên, Họ, Email, Mật khẩu
  (8–128 ký tự, có cả chữ và số), bấm **Tạo tài khoản**. Hệ thống tự đăng nhập.
- **Người bán:** bấm **Đăng ký người bán thử nghiệm**, nhập thông tin cửa hàng và định danh
  giả lập (có nút điền dữ liệu mẫu). Định danh đã bị chặn sẽ bị từ chối.

### 1.3. Giao diện chung

- **Menu:** Buyer dùng thanh menu ngang (trên điện thoại là thanh dưới màn hình). Seller dùng
  thanh bên trái chia nhóm; màn hình nhỏ bấm nút **Menu**.
- **Chuông thông báo:** góc trên, hiện số thông báo chưa đọc (tối đa "9+"). Thông báo cập nhật
  ngay khi có thay đổi, không cần tải lại trang.
- **Đồng hồ đếm ngược:** hiện ở các thao tác có hạn (thanh toán, trả hàng, tranh chấp, đổi
  địa chỉ...). Hết giờ nghĩa là đến hạn; trạng thái chỉ đổi khi hệ thống xử lý xong.
- **Nhập lại mật khẩu:** các thao tác quan trọng (hoàn tiền, rút tiền, chấp nhận giá, duyệt
  trả hàng...) sẽ mở hộp thoại yêu cầu nhập lại mật khẩu trước khi thực hiện.
- **Đăng xuất:** nút **Đăng xuất** ở góc trên.

---

## 2. Buyer

Menu Buyer: **Sản phẩm**, **Giỏ hàng**, **Ưu đãi**, **Trả giá**, **Đơn hàng**,
**Tranh chấp**, **Thông báo**.

### 2.1. Quản lý địa chỉ

1. Trong trang Thanh toán, bấm **Quản lý địa chỉ** → **Thêm địa chỉ**.
2. Nhập tên người nhận, số điện thoại, quốc gia, tỉnh/thành, phường/xã, đường và số nhà.
   Với địa chỉ Việt Nam có ô gợi ý; chọn gợi ý để tự điền.
3. Bấm **Lưu địa chỉ**. Có thể **Sửa**, đặt mặc định hoặc xóa. Đơn đã đặt giữ nguyên địa chỉ
   lúc mua.

### 2.2. Mua hàng qua giỏ

1. **Giỏ hàng** → **Tạo giỏ từ 2 seller** để lấy ngẫu nhiên sản phẩm (hoặc thêm từ trang
   **Sản phẩm**). Có thể sửa số lượng, xóa sản phẩm.
2. Bấm **Tiếp tục thanh toán**.
3. Chọn địa chỉ **Giao đến**. Mỗi seller hiện một nhóm riêng với giá, phí ship, giảm giá và
   tổng tiền. Có thể nhập/chọn mã giảm giá rồi bấm **Cập nhật báo giá**.
4. Chọn cách thanh toán:
   - **Credit card thử nghiệm:** `4111111111111111` (thành công) hoặc `4000000000000002`
     (bị từ chối), hạn `12/30`.
   - **PayPal** (Sandbox) hoặc **Klarna** (trả sau), nếu hệ thống đã được cấu hình.
5. Bấm **Thanh toán cả giỏ**, hoặc **Chỉ thanh toán người bán này** để trả riêng một seller.

Mỗi seller tạo thành một đơn riêng. Nếu thanh toán bị từ chối, đơn vẫn chờ trong hạn thanh
toán và có thể thử lại bằng thẻ khác. Đơn không trả tiền đúng hạn sẽ bị seller hủy và buyer bị
cộng **1 điểm phạt**; đủ **2 điểm trong 30 ngày** thì tài khoản bị khóa.

### 2.3. Mua ngay (Buy It Now)

1. Menu **Sản phẩm** → chọn sản phẩm có nhãn **Yêu cầu thanh toán ngay**.
2. Xem báo giá, chọn địa chỉ, nhập thẻ, bấm **Xác nhận và thanh toán**.
3. Đơn được tạo ở trạng thái đã thanh toán ngay. Nếu kết quả chưa rõ, bấm **Kiểm tra kết quả**.

### 2.4. Ưu đãi

Menu **Ưu đãi** liệt kê chương trình giảm giá đang chạy. Mã coupon có thể sao chép và dùng ở
bước thanh toán. Mỗi đơn dùng tối đa một coupon.

### 2.5. Trả giá (Best Offer)

1. Menu **Trả giá** → **Sản phẩm đang nhận trả giá** → chọn sản phẩm.
2. Nhập giá, chọn địa chỉ và cách thanh toán khi được chấp nhận:
   - **Thanh toán sau** (mặc định): tạo đơn chờ thanh toán.
   - **Auto-Pay**: tự thu tiền bằng thẻ ngay khi được chấp nhận.
3. Bấm **Gửi đề nghị**. Seller có thể chấp nhận, từ chối hoặc trả giá lại.
4. Khi seller trả giá lại, mục **Phiên thương lượng của bạn** hiện **Đến lượt bạn**: bấm
   **Chấp nhận** (nhập lại mật khẩu), **Từ chối** hoặc nhập giá mới rồi **Gửi giá**.

Mỗi sản phẩm tối đa 5 lượt đề nghị; mỗi lượt chờ tối đa 24 giờ.

### 2.6. Theo dõi đơn hàng

Menu **Đơn hàng** → bấm vào đơn để xem chi tiết: sản phẩm, tiền đã thu/đã hoàn, vận đơn và
lịch sử giao hàng. Đơn có hai khối vận đơn riêng: **Giao tới buyer** và **Trả về seller**
(khi có trả hàng).

Các thao tác trong chi tiết đơn (chỉ hiện khi được phép):

| Nút | Khi nào |
| --- | --- |
| **Thanh toán** | Đơn đang chờ thanh toán |
| **Hủy đơn** | Đơn chưa thanh toán: hủy ngay |
| **Yêu cầu hủy** | Đơn đã thanh toán nhưng chưa giao cho shipper: chờ seller duyệt, duyệt thì hoàn tiền |
| **Yêu cầu đổi địa chỉ** | Trước khi giao; xem báo giá phí chênh lệch, xác nhận rồi chờ seller (và shipper nếu đã nhận đơn) đồng ý. Mỗi đơn đổi thành công một lần |
| **Yêu cầu trả hàng** | Sau khi nhận hàng, trong hạn trả hàng |
| **Đánh giá** sản phẩm / **Đánh giá seller** | Sau khi nhận hàng |
| **Mở tranh chấp** | Khi seller từ chối trả hàng hoặc có vấn đề cần xử lý |

### 2.7. Trả hàng

1. Trong chi tiết đơn đã giao, bấm **Yêu cầu trả hàng**.
2. **Chọn lý do**. Hệ thống hiện số tiền được hoàn:
   - Lỗi của seller (hàng sai mô tả, hỏng...): hoàn toàn bộ, seller chịu phí trả.
   - Đổi ý: không hoàn phí ship chiều đi, phí ship chiều về trừ vào tiền hoàn.
3. Xác nhận gửi. Khi seller duyệt, hệ thống tạo vận đơn trả. Sau khi seller nhận và xác nhận
   hàng hợp lệ, tiền được hoàn (hoặc tự hoàn khi seller không xử lý đúng hạn).

### 2.8. Tranh chấp

1. Trong chi tiết đơn bấm **Mở tranh chấp**, nhập mô tả và **Link bằng chứng** (bắt buộc).
2. Theo dõi ở menu **Tranh chấp**. Seller sẽ gửi phương án giải quyết.
3. Bấm **Đồng ý phương án** hoặc **Yêu cầu admin xử lý** để chuyển admin phán quyết.

Nội dung và bằng chứng đã gửi không sửa/xóa được.

---

## 3. Seller

Menu Seller chia nhóm:

| Nhóm | Mục |
| --- | --- |
| Đơn hàng | **Đơn hàng**, **Trả hàng / Hoàn tiền**, **Tranh chấp** |
| Tài chính | **Ví & Rút tiền**, **Báo cáo tài chính** |
| Cửa hàng | **Best Offer**, **Khuyến mãi**, **Đánh giá sản phẩm**, **Thiết lập giao hàng** |
| Tài khoản | **Cấp người bán**, **Định danh**, **Thông báo**, **Nhật ký tích hợp** |

### 3.1. Đăng sản phẩm

1. Ở trang Seller bấm liên kết **Sản phẩm · Cấu hình thanh toán**.
2. Nhập **Tên sản phẩm**, **Mô tả**, **Giá USD**, **Tồn kho**, bấm **Đăng sản phẩm**.
3. Trong **Sản phẩm của bạn**, chọn cách thanh toán cho từng sản phẩm: **Normal Payment**
   (vào giỏ như thường) hoặc **Immediate Payment** (Buy It Now, buyer phải trả ngay).
4. Khai báo khối lượng ở **Thiết lập giao hàng**; bật trả giá ở **Best Offer**. Sản phẩm thiếu
   khối lượng sẽ không checkout được.

### 3.2. Xử lý đơn hàng

1. Menu **Đơn hàng** → mở đơn đã thanh toán.
2. Bấm **Chuẩn bị hàng**, sau đó **Tạo vận đơn** để tạo vận đơn chiều đi.
3. Chờ shipper nhận và giao. Trạng thái cập nhật tự động.

Các thao tác khác trong đơn:

| Nút | Khi nào |
| --- | --- |
| **Chấp nhận hủy** / **Từ chối hủy** | Buyer yêu cầu hủy đơn đã thanh toán (chấp nhận thì hệ thống hoàn tiền) |
| **Hủy đơn quá hạn thanh toán** | Buyer không trả tiền đúng hạn; trả lại kho và cộng điểm phạt cho buyer |
| **Duyệt địa chỉ mới** / **Từ chối đổi địa chỉ** | Buyer yêu cầu đổi địa chỉ |
| **Hoàn tiền** | Hoàn tiền chủ động cho buyer |

Các thao tác liên quan đến tiền đều yêu cầu nhập lại mật khẩu.

### 3.3. Trả hàng / Hoàn tiền

1. Menu **Trả hàng / Hoàn tiền** → chọn yêu cầu, xem lý do và báo giá tiền hoàn.
2. Bấm **Duyệt trả hàng** (hệ thống tạo vận đơn chiều trả) hoặc **Từ chối trả hàng** kèm lý do.
3. Khi hàng trả về tới nơi: bấm **Xác nhận hàng hợp lệ** để hoàn tiền, hoặc **Báo vấn đề**
   kèm bằng chứng nếu hàng không đúng.
4. Nếu tạo vận đơn trả bị lỗi, bấm **Tạo lại vận đơn trả** trong chi tiết đơn.

Nếu không xác nhận trong hạn sau khi hàng về, hệ thống tự hoàn tiền cho buyer.

### 3.4. Tranh chấp

Menu **Tranh chấp** → mở hồ sơ → nhập **Phương án giải quyết** và bằng chứng → gửi (nhập lại
mật khẩu). Buyer đồng ý thì kết thúc; buyer không đồng ý thì admin phán quyết.

### 3.5. Ví & Rút tiền

- Ví chia các khoản: **khả dụng**, **đang xử lý**, **đang giữ**, **số dư âm** (nợ).
- Bảng **Tiền giữ theo từng đơn** cho biết thời điểm mở khóa từng khoản.
- Tiền vào ví theo cấp: **cấp 1** giữ 21 ngày, **cấp 2** giữ 7 ngày sau khi giao; **cấp 3** vào
  khả dụng ngay khi buyer thanh toán.
- Phí sàn mặc định **12% + 0,30 USD** mỗi đơn, trừ trước khi vào ví.
- Rút tiền: nhập số tiền → **Rút tiền** → nhập lại mật khẩu. Trạng thái chạy qua
  Created → InProgress → FundsSent → Completed về ngân hàng giả lập.

**Báo cáo tài chính:** chọn khoảng thời gian để xem doanh thu, phí sàn, hoàn tiền theo kỳ.

### 3.6. Nợ

Khi hoàn tiền làm ví âm, seller có khoản nợ kèm hạn trả. Vào **Ví & Rút tiền** → mục
**Khoản nợ người bán** → nhập **Số tiền trả nợ (USD)** (hoặc **Điền toàn bộ số nợ**) → **Trả nợ**.
Nợ quá hạn bị hạn chế bán và rút tiền; quá lâu sẽ bị xóa nợ (write-off) và **khóa bán vĩnh
viễn**.

### 3.7. Cửa hàng

- **Best Offer:** bật trả giá cho sản phẩm, đặt ngưỡng tự chấp nhận và tự từ chối (ngưỡng từ
  chối thấp hơn ngưỡng chấp nhận). Với từng phiên: **Chấp nhận**, **Từ chối** hoặc
  **Hoặc trả giá lại** → **Gửi giá**.
- **Khuyến mãi:** **Tạo khuyến mãi** loại giảm giá theo thời gian, giảm theo số lượng, giảm theo
  đơn hoặc coupon; có thể bật/tắt. Chương trình bị admin đình chỉ phải chờ admin gỡ, sau đó
  seller tự bật lại.
- **Đánh giá sản phẩm:** xem đánh giá theo sản phẩm, lọc theo số sao và trả lời.
- **Thiết lập giao hàng:** khai báo khối lượng sản phẩm, **Lưu mức chịu phí** đổi địa chỉ (phần
  chênh lệch seller chịu thay buyer), **Lưu địa chỉ lấy hàng**.

### 3.8. Tài khoản

- **Cấp người bán:** xem cấp hiện tại, tiêu chí và lịch sử. Bấm **Đánh giá lại cấp của tôi**
  để hệ thống xét lại (dựa trên số đơn, giao đúng hạn, đánh giá của buyer, tỷ lệ hủy/trả/tranh
  chấp). Mỗi cấp có hạn mức doanh số tháng riêng.
- **Định danh:** xem/sửa thông tin định danh giả lập (đã che bớt).
- **Nhật ký tích hợp:** xem lịch sử gọi dịch vụ ngoài (thanh toán, vận chuyển, email) của đơn.

---

## 4. Shipper

Trang **Quản lý vận đơn** có hai loại: **Giao hàng** (chiều đi) và **Trả hàng** (chiều về). Có
bộ lọc theo loại, trạng thái và ô **Tìm vận đơn**.

### 4.1. Nhận vận đơn

Ở danh sách vận đơn đang chờ, bấm **Nhận vận đơn**. Mỗi vận đơn chỉ một shipper nhận được.
Chỉ shipper đã nhận mới được cập nhật vận đơn đó.

### 4.2. Cập nhật hành trình

Mở vận đơn → chọn **Thao tác tiếp theo** → **Lưu cập nhật**, theo thứ tự:

```text
Đã lấy hàng → Đang vận chuyển → Bắt đầu giao hàng → Giao thành công
(PickedUp)    (InTransit)       (OutForDelivery)    (Delivered)
```

Có thể nhập địa điểm và ghi chú cho mỗi mốc. Buyer và seller thấy cập nhật ngay.

### 4.3. Giao thất bại và giao lại

1. Khi đang giao mà không giao được, chọn **Giao thất bại** và nhập **Lý do giao thất bại**.
2. **Lên lịch giao lại**: chọn thời gian trong tương lai và lý do.
3. Khi thực sự đi giao lại, chọn **Bắt đầu giao hàng** (lúc này mới tính thêm một lượt giao).
4. Tối đa 2 lượt giao. Hết lượt hoặc không thể giao thì **Chuyển hoàn về nơi gửi**, và chọn
   **Đã hoàn về nơi gửi** khi hàng đã thực sự về tới seller.

### 4.4. Đổi địa chỉ

Nếu buyer xin đổi địa chỉ cho vận đơn mình đã nhận, shipper thấy yêu cầu trong vận đơn và chọn
đồng ý hoặc từ chối khả năng giao tới địa chỉ mới. Trong lúc chờ, không bắt đầu giao được.

---

## 5. Câu hỏi thường gặp

| Câu hỏi | Trả lời |
| --- | --- |
| Thanh toán báo "Verifying" | Hệ thống đang chờ kết quả từ cổng thanh toán. Bấm **Kiểm tra kết quả**; không thanh toán lại vội để tránh trùng. |
| Bấm thanh toán hai lần có bị trừ tiền hai lần không? | Không. Mỗi lần thanh toán có mã chống trùng. |
| Vì sao không đăng nhập được? | Sai mật khẩu, bị tạm chặn do đăng nhập sai nhiều lần, hoặc tài khoản bị khóa (điểm phạt, nợ). |
| Báo "Bạn thao tác quá nhanh" | Vượt giới hạn số lần thao tác; đợi số giây được báo rồi thử lại. |
| Sao không thấy nút hủy/trả hàng? | Nút chỉ hiện khi đơn ở trạng thái cho phép và còn trong hạn. |
| Seller sao chưa nhận được tiền? | Tiền được giữ theo cấp seller để còn hoàn cho buyer khi có trả hàng/tranh chấp. |
