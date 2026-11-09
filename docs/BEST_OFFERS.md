# Best Offer (trả giá)

## Trạng thái

Đã có giao diện Buyer/Seller, API và xử lý server cho trả giá, counteroffer, ngưỡng tự động,
hết hạn, chốt đơn **thanh toán sau** (đơn chờ thanh toán) hoặc **Auto-Pay** bằng thẻ mô phỏng.
Migration của tính năng là `20261108124914_AddBestOffers`; bản sửa ngày 09/11/2026 **không thêm
migration**. Migration chưa áp lên database nào trong các phiên này.

Ngày 09/11/2026 đã sửa: giới hạn 5 lượt theo sản phẩm, luồng thanh toán sau, khoảng giá counter,
chống gửi trùng khi thử lại, nhập lại mật khẩu và rate limit cho thao tác chấp nhận. Kiểm thử
`--best-offers` (InMemory), `--password-confirmation`, `--rate-limits`, bộ backend mặc định và Node
154/154 đạt. Chưa chạy SQL QA đồng thời hoặc browser E2E.

## Quy tắc nghiệp vụ

| Quy tắc | Hành vi |
| --- | --- |
| Giới hạn lượt | Mỗi buyer tối đa **5 lượt cho một sản phẩm**, tính gộp mọi phiên: giá mở đầu và mỗi lần buyer counter là một lượt. Phiên bị từ chối/hết hạn vẫn giữ lượt đã dùng. Lượt thứ 6 bị từ chối. |
| Hạn phản hồi | Mỗi lượt chờ tối đa 24 giờ; worker chuyển phiên đang chờ thành `Expired`. |
| Counter của seller | Lớn hơn giá buyer vừa đưa và nhỏ hơn giá niêm yết. |
| Counter của buyer | Lớn hơn giá buyer đưa lần trước và nhỏ hơn giá seller vừa counter. |
| Ngưỡng tự động | Dưới ngưỡng từ chối thì đóng phiên; đạt ngưỡng chấp nhận thì chốt ngay theo cách thanh toán buyer đã chọn. |
| Thanh toán sau (`PaymentMethod = PayLater`, mặc định) | Khi được chấp nhận: tạo đơn `AwaitingPayment` một sản phẩm theo giá đã chốt, giữ tồn ngay, hạn `Checkout:PaymentHoldMinutes` (4 ngày; Development 5 phút). Buyer trả trong tab Đơn hàng bằng thẻ thử, PayPal hoặc Klarna. Quá hạn: seller dùng **Hủy đơn quá hạn thanh toán**, buyer bị cộng điểm phạt ([UNPAID_ITEMS.md](UNPAID_ITEMS.md)). Buyer đã bị khóa không được tạo đơn này. |
| Auto-Pay (`PaymentMethod = Card`) | Buyer chọn thẻ mô phỏng lúc gửi giá. Khi được chấp nhận: thu tiền ngay, tạo đơn `Paid`, ghi payment/settlement trong transaction. Thẻ lỗi thì rollback, không chốt đơn. |
| Chống gửi trùng | Chấp nhận, từ chối, counter và tạo phiên đều gửi `key`. Gửi lại cùng `key` trả về đúng kết quả đã lưu, không tạo lượt/đơn/payment mới. `key` đã dùng cho phiên hoặc người khác bị từ chối. |
| Thông báo | Chuông chỉ gửi cho **bên còn lại**, không gửi cho người thao tác: buyer gửi giá/counter/chấp nhận/từ chối thì seller nhận; seller counter/chấp nhận/từ chối thì buyer nhận; tự động chấp nhận theo ngưỡng thì seller nhận. Hết hạn (không ai thao tác) thì cả hai nhận. Ngoài ra có tín hiệu realtime không kèm nội dung để màn hình cả hai bên tự làm mới. Thông báo chung "Cập nhật thanh toán" khi Auto-Pay ghi payment vẫn theo cơ chế đơn hàng chung (gửi buyer và seller của đơn). |
| Bảo vệ thao tác | `seller/accept` và `buyer/accept` bắt nhập lại mật khẩu (header `X-Action-Password`). Tạo phiên và hai thao tác chấp nhận dùng rate limit nhóm `payment`; các thao tác khác dùng nhóm `write`. |

Đơn tạo từ Best Offer chụp đủ địa chỉ giao hàng (tên, đường, thành phố, bang, ZIP, quốc gia) để
dùng được với Klarna.

## API

| Method | Đường dẫn | Ghi chú |
| --- | --- | --- |
| GET | `api/best-offers/catalog` | Sản phẩm nhận trả giá, kèm `buyerOffersUsed`/`buyerOfferLimit` của buyer. |
| GET | `api/best-offers/buyer`, `api/best-offers/seller` | Danh sách phiên; `buyerOfferCount` là lượt đã dùng cho sản phẩm, `paymentMethod` là cách thanh toán. |
| POST | `api/best-offers` | `{ productId, amount, addressId, paymentMethod: "PayLater" \| "Card", cardProfile?, cardExpiry?, key }` |
| POST | `api/best-offers/{id}/seller/accept`, `.../buyer/accept` | `{ key }`; cần nhập lại mật khẩu. |
| POST | `api/best-offers/{id}/seller/decline`, `.../buyer/decline` | `{ key }` |
| POST | `api/best-offers/{id}/seller/counter`, `.../buyer/counter` | `{ amount, key }` |
| POST | `api/best-offers/{id}/buyer/payment-profile` | Đổi thẻ mô phỏng; chỉ cho phiên Auto-Pay. |
| POST | `api/best-offers/seller/settings` | Bật/tắt trả giá và hai ngưỡng tự động. |

## Cách dùng

1. Áp migration vào database phát triển có baseline và các migration trước đó (sao lưu trước):

   ```powershell
   dotnet ef database update --project backend/G4.Infrastructure --startup-project backend/G4.Api
   ```

   Lệnh này áp tất cả migration đang pending trong connection string hiện tại. Không chạy
   baseline trên database có dữ liệu.
2. Chạy API và frontend theo [PROJECT_RUN.md](PROJECT_RUN.md); đăng nhập bằng tài khoản Buyer và
   Seller.
3. Seller mở **Best Offer**, bật “Cho phép người mua trả giá” trên sản phẩm giá cố định. Có thể
   bỏ trống hai ngưỡng; nếu cấu hình cả hai, ngưỡng từ chối phải thấp hơn ngưỡng chấp nhận.
4. Buyer mở **Trả giá**, chọn sản phẩm, mức giá, địa chỉ và cách thanh toán khi được chấp nhận
   (Thanh toán sau hoặc Auto-Pay; Auto-Pay chọn thêm thẻ mô phỏng), rồi gửi. Form hiện số lượt
   đã dùng cho sản phẩm.
5. Seller chấp nhận (nhập lại mật khẩu), từ chối hoặc trả giá lại trong khoảng giá hiển thị.
   Buyer cũng vậy khi seller counter.
6. Thanh toán sau: mở tab Đơn hàng để trả trước hạn. Auto-Pay: đơn đã `Paid` ngay.

Thẻ thử `4111…1111` thành công; `4000…0002` bị từ chối. Đây là hồ sơ mô phỏng, không nhập hoặc
lưu thông tin thẻ thật. Buyer cần có địa chỉ; seller cần có địa chỉ lấy hàng mặc định.

## Kiểm thử

```powershell
dotnet run --project tests/G4.Commerce.Tests -- --best-offers
dotnet run --project tests/G4.Commerce.Tests -- --password-confirmation
dotnet run --project tests/G4.Commerce.Tests -- --rate-limits
```

`--best-offers` kiểm tra: giới hạn 5 lượt qua nhiều phiên, khoảng giá counter hai chiều, đơn
thanh toán sau giữ tồn và theo hạn cấu hình, seller hủy quá hạn và cộng điểm phạt, buyer bị khóa
không được tạo đơn, Auto-Pay thẻ lỗi/thành công, chỉ đổi thẻ cho phiên Auto-Pay, gửi lại cùng
`key` cho chấp nhận/từ chối/counter.

## Giới hạn còn lại

- Auto-Pay chỉ dùng thẻ mô phỏng. PayPal/Klarna cần luồng authorization/vault được buyer chấp
  thuận trước khi seller accept; chưa có trong source. Không lưu PAN/CVV.
- Auto-Pay chưa chặn buyer đã bị khóa (chỉ luồng thanh toán sau chặn).
- Chưa có SQL QA đồng thời; giới hạn lượt dựa trên transaction `Serializable` khi chạy SQL Server.
