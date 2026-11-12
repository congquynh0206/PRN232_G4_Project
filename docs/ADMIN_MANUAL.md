# Hướng dẫn quản trị (Administrator Manual)

Dự án G4_Project, PRN232, nhóm 4. Tài liệu dành cho vai trò **Admin**: người xử lý tranh
chấp, tra cứu giao dịch, quản lý chính sách phí, khuyến mãi và theo dõi seller. Cách cài đặt
hệ thống xem [Installation & Deployment Guide](INSTALLATION_DEPLOYMENT_GUIDE.md); cách dùng của
buyer/seller/shipper xem [End-user Manual](END_USER_MANUAL.md).

## Mục lục

1. [Đăng nhập và giao diện](#1-đăng-nhập-và-giao-diện)
2. [Nguyên tắc của admin](#2-nguyên-tắc-của-admin)
3. [Tranh chấp](#3-tranh-chấp)
4. [Tra cứu đơn](#4-tra-cứu-đơn)
5. [Cấp người bán](#5-cấp-người-bán)
6. [Khoản nợ](#6-khoản-nợ)
7. [Nợ khó thu / Danh sách chặn](#7-nợ-khó-thu--danh-sách-chặn)
8. [Khuyến mãi](#8-khuyến-mãi)
9. [Phí nền tảng](#9-phí-nền-tảng)
10. [Thông báo và Nhật ký tích hợp](#10-thông-báo-và-nhật-ký-tích-hợp)
11. [Vận hành hệ thống](#11-vận-hành-hệ-thống)

---

## 1. Đăng nhập và giao diện

1. Mở trang web (mặc định `http://localhost:5131`), đăng nhập bằng tài khoản admin
   (`admin@example.test`, hoặc `admin@demo.test` với dữ liệu demo; mật khẩu `G4@123456`).
2. Hệ thống chuyển tới trang Admin. Menu bên trái chia nhóm:

| Nhóm | Mục |
| --- | --- |
| Xử lý đơn | **Tranh chấp**, **Tra cứu đơn** |
| Người bán | **Cấp người bán**, **Khoản nợ**, **Nợ khó thu / Danh sách chặn** |
| Chính sách | **Khuyến mãi**, **Phí nền tảng** |
| Hệ thống | **Thông báo**, **Nhật ký tích hợp** |

Trên màn hình nhỏ, bấm **Menu** để mở danh sách. Chuông thông báo ở góc trên báo các việc mới
(ví dụ tranh chấp vừa được chuyển lên admin).

## 2. Nguyên tắc của admin

- Các thao tác chốt quyết định (phán quyết tranh chấp, đổi phí nền tảng, tạo/sửa/đình chỉ
  khuyến mãi) đều yêu cầu **nhập lại mật khẩu** của chính tài khoản admin.
- Admin **không** sửa tay số dư, khoản thanh toán, hoàn tiền hay tracking. Mọi thay đổi tiền đi
  qua quy trình của hệ thống (phán quyết, worker nền).
- Admin **không** tự nhập cấp seller; cấp do hệ thống xét theo số liệu thực tế.
- Tra cứu đơn chỉ để đọc; dữ liệu nhạy cảm (mã cổng thanh toán, tracking) chỉ hiện 4 ký tự cuối.

---

## 3. Tranh chấp

Admin chỉ xử lý hồ sơ **đã được buyer chuyển lên** (buyer và seller không tự thống nhất được).

1. Menu **Tranh chấp** → chọn hồ sơ. Xem lịch sử trao đổi, bằng chứng của hai bên, thông tin
   đơn, thanh toán và vận đơn hai chiều.
2. Bấm **Xem xét và đưa ra phán quyết**. Trong hộp **Phán quyết tranh chấp**:
   - **Kết luận trách nhiệm:** **Thuộc trách nhiệm người bán**, hoặc **Đổi ý hoặc thuộc trách
     nhiệm người mua**.
   - **Kết quả xử lý:**
     - **Seller thắng, không hoàn thêm** (chỉ khi trách nhiệm thuộc buyer).
     - **Hoàn toàn bộ khoản được nhận theo kết luận**.
     - **Hoàn một phần**: nhập **Tiền buyer thực nhận sau phí trả hàng (USD)**.
   - **Căn cứ quyết định:** chọn ít nhất một bằng chứng trong hồ sơ.
   - **Lý do và nhận định từ bằng chứng:** bắt buộc.
3. Bấm **Xem dự kiến tài chính** để xem số tiền buyer nhận, phần seller chịu, phí trả hàng.
   Hệ thống tự tính, admin không nhập phí.
4. Bấm **Xác nhận và lưu phán quyết** → nhập lại mật khẩu.

Lưu ý:

- Đổi kết luận hoặc số tiền sau khi xem dự kiến thì phải xem lại. Nếu hồ sơ có bằng chứng mới
  hoặc tiền thay đổi sau lúc xem, hệ thống từ chối và yêu cầu xem lại.
- Phán quyết đã lưu không sửa/xóa được. Nếu hoàn tiền tạm lỗi, hệ thống tự thử lại; hồ sơ chỉ
  đóng khi hoàn tiền thành công.
- Nếu đã có vận đơn trả hàng, chỉ phán quyết hoàn tiền sau khi hàng đã về tới seller.

## 4. Tra cứu đơn

Dùng để hỗ trợ khách hàng: xem toàn bộ thanh toán, hoàn tiền, vận chuyển, email của một đơn.

1. Menu **Tra cứu đơn**. Danh sách đơn mới nhất tự tải.
2. Nhập mã vào ô **Tìm kiếm**, chọn **Tìm theo**:
   - **Tự nhận diện mã**: `125` hoặc `#125` là mã đơn; `payment:125`, `refund:125` là mã nội bộ.
   - **Mã đơn hàng**, **Mã thanh toán nội bộ**, **Mã hoàn tiền nội bộ**.
   - **Mã giao dịch cổng thanh toán**: phải nhập đầy đủ, không tìm một phần.
3. **Bộ lọc nâng cao**: người mua/người bán, khoảng ngày, trạng thái đơn, thanh toán, hoàn tiền.
   Bấm **Áp dụng bộ lọc**; **Xóa tất cả bộ lọc** để bỏ.
4. Lọc nhanh: **Tất cả đơn**, **Chờ thanh toán**, **Có thanh toán lỗi**, **Có tranh chấp**...
5. Bấm vào đơn để xem chi tiết gồm các tab Tổng quan / Thanh toán / Hoàn tiền / Vận chuyển.

Tổng thu, tổng hoàn chỉ tính các giao dịch thành công. Thời gian hiển thị theo múi giờ máy.

## 5. Cấp người bán

Menu **Cấp người bán** hiển thị cấp hiện tại của từng seller, số liệu (đơn hoàn tất, đánh giá
tích cực, giao đúng hạn, tỷ lệ hủy/trả/tranh chấp) và lịch sử xét cấp.

| Cấp | Hạn mức doanh số tháng | Thời gian giữ tiền sau giao |
| --- | --- | --- |
| Cấp 1 | 5.000 USD | 21 ngày |
| Cấp 2 | 10.000 USD | 7 ngày |
| Cấp 3 · Instant | 1.000.000 USD | Không giữ, vào ví ngay |

- Lọc theo cấp hoặc seller; bấm vào seller để xem chi tiết tiêu chí.
- Hệ thống tự xét định kỳ (mặc định mỗi giờ). Seller đang nợ quá hạn tự về cấp 1.
- Mục **Thử các tình huống xét cấp · Development** cho phép **Chạy mô phỏng** với các tình huống
  mẫu; mô phỏng không ghi dữ liệu.

## 6. Khoản nợ

Menu **Khoản nợ** theo dõi seller có số dư âm (thường do hoàn tiền vượt số dư).

1. Lọc theo **Đang nợ**, **Quá hạn**, **Khóa bán vĩnh viễn**, hoặc **Tất cả**.
2. Bấm vào seller để xem **Chi tiết khoản nợ**: các kỳ nợ, hạn trả, cảnh báo và hạn chế,
   **Biên nhận trả nợ**, **Sổ cái tài chính**.

Admin chỉ theo dõi; seller tự trả nợ. Nợ quá hạn khiến seller bị hạn chế bán và rút tiền.

## 7. Nợ khó thu / Danh sách chặn

Menu **Nợ khó thu / Danh sách chặn** gồm:

- **Quyết định nợ khó thu:** các khoản nợ quá lâu bị hệ thống tự xóa nợ (write-off). Seller bị
  write-off bị **khóa bán vĩnh viễn**, kể cả khi trả hết nợ sau đó.
- **Blacklist định danh:** định danh của seller bị write-off. Đăng ký seller mới bằng định danh
  này sẽ bị từ chối.

Write-off do hệ thống thực hiện (**Giám sát tự động**), admin không bấm tay.

## 8. Khuyến mãi

Menu **Khuyến mãi** hiển thị cả chương trình của shop và của nền tảng.

**Tạo coupon toàn sàn (nền tảng tài trợ):**

1. Bấm **Tạo khuyến mãi**, chọn loại **Mã giảm giá**.
2. Nhập mã, thời gian hiệu lực, shop/sản phẩm áp dụng, đơn tối thiểu, mức giảm, trần giảm, tổng
   lượt dùng, lượt mỗi buyer, ngân sách. Có thể bật **Hiển thị mã cho Buyer**.
3. Bấm **Lưu khuyến mãi** → nhập lại mật khẩu.

Admin có thể sửa, bật/tắt coupon của nền tảng.

**Chương trình của shop:** admin chỉ xem, **Đình chỉ chương trình** hoặc **Gỡ đình chỉ** kèm lý do.
Gỡ đình chỉ không tự bật lại; seller phải tự kích hoạt.

Bộ lọc: **Loại**, **Nguồn tài trợ** (Shop / Nền tảng), tìm theo tên hoặc mã. Mỗi chương trình có
**Lịch sử khuyến mãi**. Thay đổi không áp dụng hồi tố cho đơn đã thanh toán.

## 9. Phí nền tảng

Phí mặc định: **12% SellerGross + 0,30 USD** mỗi đơn.

1. Menu **Phí nền tảng** → **Thiết lập mức phí mới**.
2. Nhập **Phí theo phần trăm (%)**, **Phí cố định mỗi đơn (USD)**, **Lý do thay đổi**.
3. Chọn **Thời điểm áp dụng**: **Ngay khi lưu thành công**, hoặc **Đặt lịch hiệu lực** và nhập
   **Thời điểm hiệu lực (giờ địa phương)**.
4. Bấm **Lưu mức phí** → nhập lại mật khẩu.

**Lịch sử cấu hình** ghi lại mọi lần đổi phí và người đổi. Đơn đã thanh toán giữ mức phí lúc mua.

## 10. Thông báo và Nhật ký tích hợp

- **Thông báo:** danh sách email hệ thống đã gửi (thanh toán, hoàn tiền, trả hàng, nợ, rút
  tiền...). Lọc theo loại và trạng thái (**Đã gửi**, **Chờ gửi / thử lại**, **Lỗi, cần kiểm
  tra**). Email lỗi có nút **Thử gửi lại**.
- **Nhật ký tích hợp:** lịch sử gọi dịch vụ ngoài (**Thẻ giả lập**, **PayPal**, **Vận chuyển**,
  **Email**) theo đơn, kết quả **Thành công**, **Lỗi** hoặc **Chưa xác định kết quả**. Dùng để
  điều tra khi khách báo thanh toán/giao hàng có vấn đề.

---

## 11. Vận hành hệ thống

Phần này dành cho người quản trị kỹ thuật. Chi tiết lệnh ở
[Installation & Deployment Guide](INSTALLATION_DEPLOYMENT_GUIDE.md).

| Việc | Cách làm |
| --- | --- |
| Khởi động / dừng | Chạy lại API và frontend (`dotnet run`, Visual Studio hoặc `docker compose`) |
| Xem hộp thư email thử | MailHog `http://localhost:8025` |
| Sao lưu database | `BACKUP DATABASE CloneEbayDB TO DISK = N'...'` trước mỗi lần nâng cấp |
| Nâng cấp phiên bản | Dừng API → sao lưu → `dotnet ef database update` → khởi động lại |
| Cấu hình thời hạn, phí, rate limit | `backend/G4.Api/appsettings*.json` hoặc biến môi trường |
| Khóa bảo mật | `Authentication:JwtKey`, `SellerIdentity:HmacKey`; giữ nguyên khóa HMAC cùng database |
| Worker nền | Bật mặc định (`Workers:Enabled`); xử lý hết hạn, đối soát thanh toán, mở khóa tiền, xét cấp, write-off |
| Tạo tài khoản admin mới | Chưa có giao diện; thêm qua script SQL theo mẫu `database/seed-data.sql` |

Sự cố thường gặp:

| Hiện tượng | Xử lý |
| --- | --- |
| Không gửi được email | Kiểm tra MailHog đang chạy hoặc đặt `Mail:Mode = Capture` |
| Thanh toán treo ở `Verifying` | Worker tự đối soát; kiểm tra **Nhật ký tích hợp** của đơn |
| Người dùng báo bị chặn "thao tác quá nhanh" | Do rate limit; chờ hết thời gian báo, hoặc tăng `RateLimiting` trong cấu hình |
| Seller không đăng ký được | Định danh nằm trong **Blacklist định danh** |
