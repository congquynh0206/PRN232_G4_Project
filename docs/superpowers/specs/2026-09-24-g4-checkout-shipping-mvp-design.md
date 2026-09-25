# Thiết kế MVP: Thanh toán và giao hàng eBay Clone - Nhóm 4

**Trạng thái:** Bản để nhóm đọc và góp ý, chưa triển khai code  
**Ngày:** 24/09/2026  
**Phạm vi:** Demo độc lập của nhóm 4, một seller cho mỗi đơn hàng

## 1. Mục tiêu và căn cứ

Người xem có thể tự thao tác một chu trình: tạo giỏ ngẫu nhiên, xem Order Summary, thanh toán, theo dõi vận đơn, yêu cầu trả hàng hoặc hủy đơn, và xem kết quả hoàn tiền. Nhóm có thể chuyển sang vai seller để xử lý đơn và dùng công cụ demo để đẩy sự kiện giao hàng. Dữ liệu và luồng này hoạt động độc lập với sản phẩm của các nhóm khác.

Yêu cầu gốc trong `Ebay Clone Requiments.docx` đặt ra thanh toán mô phỏng PayPal/COD, tính tổng tiền và phí giao hàng, API vận chuyển giả lập, email thông báo, tự hủy đơn chờ thanh toán, retry và log giao dịch. Quyết định của nhóm là **PayPal Sandbox + Credit Card giả lập** thay cho COD. Ảnh checkout được dùng làm tham khảo bố cục, không phải đặc tả đầy đủ của eBay. `clone_ebay_sqlserver_schema.sql` là schema tham khảo, được phép điều chỉnh.

### Tiêu chí hoàn thành MVP

1. Có thể trình diễn đầy đủ một đơn thành công từ giỏ tới giao hàng, kèm các thay đổi dữ liệu tương ứng.
2. Có thể chủ động trình diễn thanh toán thất bại, đơn hết hạn, API vận chuyển lỗi rồi retry, giao thất bại, hủy đơn, trả hàng và hoàn tiền.
3. Các lần bấm lại, tải lại trang hoặc nhận lại cùng một kết quả thanh toán không tạo khoản thanh toán, vận đơn hay khoản hoàn trùng.
4. Người xem phân biệt được trạng thái đơn, trạng thái thanh toán, trạng thái giao hàng và trạng thái yêu cầu trả hàng.

## 2. Phạm vi MVP

| Có trong MVP | Để giai đoạn sau |
| --- | --- |
| Buyer và seller demo được tạo sẵn, chuyển vai trong giao diện demo | Đăng ký, đăng nhập thật, phân quyền production |
| Một seller cố định; buyer chọn trước số dòng sản phẩm muốn random (1-5) từ seller đó | Checkout nhiều seller, tách đơn và chia tiền seller |
| Mỗi dòng chọn số lượng; kiểm tra tồn kho | Đấu giá, catalog đầy đủ, seller tự đăng sản phẩm |
| Một địa chỉ nhận hàng của buyer, cho chọn hoặc sửa trước khi đặt | Sổ địa chỉ nhiều người dùng |
| Một loại giao hàng theo vùng; một kiện đi và tối đa một kiện trả | Nhiều kiện, nhiều hãng vận chuyển, lựa chọn tốc độ giao |
| PayPal Sandbox và Credit Card giả lập | COD, cổng thẻ live, tiền thật |
| Coupon phần trăm đơn giản; toàn đơn | Coupon chồng nhiều loại, giới hạn theo sản phẩm |
| Hủy trước khi gửi, tracking, giao thất bại, trả toàn bộ đơn, hoàn tiền | Trả một phần sản phẩm, đổi hàng, tranh chấp phức tạp |
| Email qua hộp thư bắt mail cục bộ khi demo | Gửi email Internet thực, SMS, push notification |
| Màn demo điều khiển vai và sự kiện vận chuyển | Dashboard quản trị hoàn chỉnh |

Một checkout tạo **một `OrderTable` và một `ShippingInfo` chiều đi**. Một yêu cầu trả hàng trong MVP áp dụng cho **toàn bộ đơn**. Số lượng hàng trong giỏ được chọn trước khi xác nhận đơn.

## 3. Giao diện và hành trình người dùng

### 3.1 Buyer

| Màn | Nội dung và hành động |
| --- | --- |
| Order Summary | Chọn số dòng hàng (1-5) rồi bấm **Tạo giỏ ngẫu nhiên**; danh sách hàng, số lượng, giá, tồn kho, seller, tạm tính; cho tạo lại trước khi đặt. Nếu không đủ sản phẩm hợp lệ, báo rõ và không tạo giỏ thiếu dòng. |
| Checkout | Địa chỉ nhận, khu vực và phí giao, coupon, chọn PayPal/Credit Card, danh sách hàng, tổng tiền, nút **Xác nhận và thanh toán**. Bố cục lấy cảm hứng từ ảnh tham khảo. |
| Kết quả thanh toán | Thành công, thất bại hoặc đang xác minh; mã đơn, phương thức, hướng dẫn thử lại nếu hợp lệ. |
| My Orders | Danh sách đơn, tổng tiền, trạng thái chính; lọc đơn đang chờ, đang giao, hoàn tất, đã hủy. |
| Order Detail & Tracking | Bốn trạng thái riêng: đơn, thanh toán, vận chuyển, trả hàng; bảng tiền; mã vận đơn; timeline sự kiện; hành động hủy hoặc trả hàng khi hợp lệ. |
| Return Detail | Lý do trả hàng, diễn tiến duyệt, mã vận đơn chiều trả, tình trạng hoàn tiền. |

### 3.2 Seller và công cụ demo

| Màn | Nội dung và hành động |
| --- | --- |
| Seller Orders | Xem đơn đã thanh toán; xác nhận chuẩn bị, tạo vận đơn, bàn giao hàng; duyệt hoặc từ chối yêu cầu trả với lý do. |
| Demo Control | Chuyển buyer/seller, phát sự kiện vận chuyển chiều đi và chiều trả, mô phỏng API shipping lỗi, xem hộp thư demo và log giao dịch. Mỗi nút chỉ hiện khi chuyển trạng thái hợp lệ. |

Nút random tạo **giỏ tạm**, chưa ghi nhận là đơn đã đặt. Nó chọn đúng số dòng buyer yêu cầu từ hàng có tồn kho, cùng seller, và đặt số lượng không vượt tồn. Sau khi buyer xác nhận, đơn lưu bản chụp sản phẩm, giá và địa chỉ; random lần nữa không đổi đơn cũ.

## 4. Quy tắc giá và thanh toán

MVP dùng **USD thống nhất** ở giao diện, database và PayPal Sandbox. Không hiển thị tỷ giá VND/EUR giả nếu chưa có nguồn tỷ giá và quy tắc chuyển đổi. Giá seed phù hợp USD.

`tạm tính = tổng(unitPrice × quantity)`  
`giảm giá = tạm tính × couponPercent`, làm tròn 2 chữ số  
`tổng thanh toán = max(0, tạm tính - giảm giá) + phí giao hàng`

Mặc định đề xuất: phí nội vùng **2 USD**, khác vùng **5 USD**, xác định bằng `Address.state`; một coupon tối đa **20%**, áp dụng trước phí giao hàng. Phí giao không được giảm. Nếu coupon không hợp lệ hoặc hết lượt, checkout hiển thị lý do và giữ nguyên tổng tiền. Backend luôn tính lại tổng trên dữ liệu sản phẩm, coupon và địa chỉ; không tin tổng do trình duyệt gửi lên.

Đặt hàng tạo đơn `AwaitingPayment`, giữ số lượng tồn trong **15 phút**. Nếu quá hạn và chưa có thanh toán thành công, job chuyển `Expired`, trả tồn và không cho capture. Nếu kết quả PayPal đến sát thời điểm hết hạn, backend đối soát trạng thái trước khi hết hạn hoặc hoàn tiền; không để đơn vừa `Expired` vừa giữ tiền. Thời gian 15 phút là giá trị demo có thể chỉnh bằng cấu hình.

### PayPal Sandbox

Backend tạo PayPal order bằng client secret lưu ngoài source; trình duyệt chỉ nhận client ID cần cho SDK. Buyer xác nhận bằng tài khoản PayPal thử. Backend xác nhận capture và lưu PayPal order ID, capture ID, số tiền và trạng thái. Không đánh dấu đã thanh toán chỉ vì trình duyệt quay về trang success. Nếu timeout hoặc phản hồi không rõ, hiển thị `Verifying`, truy vấn lại PayPal theo ID và cập nhật sau. Có khóa idempotency để thao tác lặp không capture hai lần.

### Credit Card giả lập

Giao diện cho nhập **dữ liệu thẻ thử**. Dịch vụ giả lập nhận các trường hợp định trước: hợp lệ, bị từ chối, hết hạn; trả transaction ID giả và kết quả. Không dùng hoặc lưu số thẻ thật, CVV, cũng không tự nhận đây là giao dịch PayPal. Có thể dùng một thẻ thử mẫu hiển thị ngay trong Demo Control để người chấm không cần chuẩn bị dữ liệu. Việc nhập lặp cùng yêu cầu không tạo thêm lần thu tiền.

Mỗi lần thử thanh toán là một bản ghi `Payment`. Đơn chỉ được chuyển sang `Paid` sau khi một lần thử được xác nhận thành công; không cho lần thử mới nếu đơn đã được trả tiền. Thanh toán thất bại có thể thử lại khi đơn chưa hết hạn.

## 5. Giao hàng và tracking

Khi seller bàn giao đơn đã thanh toán, hệ thống gọi **shipping API giả lập chạy cục bộ** với token riêng, tạo mã vận đơn duy nhất. Nếu API lỗi tạm thời, yêu cầu được retry tối đa 3 lần có giãn cách; tạo vận đơn dùng cùng idempotency key để retry không sinh nhiều mã. Sau 3 lần thất bại, đơn giữ trạng thái `ShipmentCreationFailed` để seller bấm thử lại; buyer không thấy một mã vận đơn chưa tồn tại.

Timeline chiều đi: `LabelCreated → PickedUp → InTransit → OutForDelivery → Delivered`. Từ `OutForDelivery` có thể đi `DeliveryFailed`; seller/demo chọn thử giao lại tối đa một lần, hoặc `ReturningToSender → ReturnedToSeller`. Mỗi sự kiện lưu thời gian, vị trí demo, ghi chú, nguồn và event ID. Sự kiện lặp được bỏ qua; không cho sự kiện cũ làm lùi trạng thái. Màn tracking hiển thị lịch sử chứ không chỉ trạng thái mới nhất.

Nếu giao thất bại và hàng đã về seller, hệ thống hoàn **toàn bộ số tiền buyer đã trả**, bao gồm phí giao hàng, vì buyer chưa nhận được hàng. Trong MVP đây là quy tắc demo; chính sách thật của marketplace có thể khác.

## 6. Hủy đơn, trả hàng và hoàn tiền

| Tình huống | Quy tắc MVP |
| --- | --- |
| Chưa thanh toán | Buyer hủy ngay; đơn `Cancelled`, giải phóng tồn; không có refund. |
| Đã thanh toán, chưa bàn giao vận chuyển | Buyer yêu cầu hủy; seller chấp nhận qua giao diện demo; hoàn toàn bộ tiền đã trả. Seller từ chối phải ghi lý do và tiếp tục xử lý đơn. |
| Đã bàn giao vận chuyển | Không hủy trực tiếp. Chờ giao hoặc xử lý giao thất bại. |
| Đã giao thành công | Buyer có thể gửi một yêu cầu trả **toàn bộ đơn** trong 7 ngày; lý do và ghi chú bắt buộc. |
| Seller duyệt trả hàng | Sinh mã vận đơn chiều trả, buyer/demo cập nhật tracking; seller xác nhận đã nhận hàng trả. |
| Seller từ chối | Phải ghi lý do; hiển thị cho buyer; chưa hoàn tiền. |
| Seller xác nhận nhận hàng trả | Tạo refund đúng một lần cho toàn bộ số tiền gốc, bao gồm phí giao chiều đi. Trạng thái `RefundPending → Refunded` hoặc `RefundFailed`. |

Với PayPal Sandbox, refund gọi API sandbox dựa trên capture ID và chỉ ghi `Refunded` khi PayPal xác nhận. Với Credit Card giả lập, refund được giả lập tương ứng. Refund thất bại có thể retry cùng một khóa idempotency. Phí vận chuyển chiều trả do hệ thống demo gánh, không thu thêm từ buyer. Với MVP, mỗi đơn có tối đa một yêu cầu trả; không trả từng món hoặc hoàn một phần. Hàng được trả về không tự động cộng lại tồn kho bán được; seller cần xác nhận tình trạng hàng, còn thao tác nhập lại tồn để giai đoạn sau.

**Giao không thành công và chuyển hoàn về seller** khác với **buyer nhận rồi chủ động trả hàng**. Hai trường hợp có nguồn phát sinh, timeline và quyền thao tác riêng, dù đều có thể kết thúc bằng refund.

## 7. Mô hình trạng thái

Giữ bốn nhóm trạng thái độc lập để tránh những tổ hợp không thể biểu diễn bằng một cột `OrderTable.status`:

| Nhóm | Giá trị MVP | Quy tắc chính |
| --- | --- | --- |
| Order | `AwaitingPayment`, `Paid`, `Preparing`, `Shipping`, `Delivered`, `CancelRequested`, `Cancelled`, `Expired`, `Closed` | Buyer chỉ yêu cầu hủy trước khi bàn giao; seller chỉ tạo shipment sau khi Paid. |
| Payment | `Created`, `Pending`, `Verifying`, `Succeeded`, `Failed`, `Cancelled` | Một đơn có nhiều lần thử nhưng tối đa một lần thành công. |
| Shipment | `NotCreated`, `LabelCreated`, `PickedUp`, `InTransit`, `OutForDelivery`, `DeliveryFailed`, `Delivered`, `ReturningToSender`, `ReturnedToSeller`, `ShipmentCreationFailed` | Mỗi chiều vận chuyển có lịch sử sự kiện riêng. |
| Return | `Requested`, `Approved`, `Rejected`, `ReturnShipping`, `ReceivedBySeller`, `RefundPending`, `Refunded`, `RefundFailed` | Tối đa một yêu cầu trả cho một đơn trong MVP. |

`Order.Closed` chỉ đặt khi giao thành công và hết cửa sổ trả hàng, hoặc khi yêu cầu trả/hủy đã xử lý hoàn tiền xong. Trên giao diện, đơn có thể vừa `Delivered` vừa có `Return.Requested`; không ép chúng thành một trạng thái chung.

## 8. Kiến trúc đề xuất

Tiếp tục cấu trúc sẵn có: `frontend` ASP.NET Core MVC, `backend` ASP.NET Core API, Entity Framework Core và SQL Server. Backend chịu trách nhiệm tính tiền, tạo đơn, thay đổi trạng thái và kiểm soát quyền. Frontend không cập nhật database trực tiếp.

Trong backend, chia theo trách nhiệm:

- **Checkout/Order:** giỏ tạm, tồn kho, snapshot, tính tổng, coupon, hạn thanh toán.
- **Payment:** một cổng giao tiếp chung với hai adapter `PayPalSandbox` và `FakeCard`; quản lý lần thử, đối soát và refund.
- **Shipping:** cổng giao tiếp chung với shipping API giả lập; tạo vận đơn, retry, lưu sự kiện tracking.
- **Returns:** yêu cầu hủy, yêu cầu trả hàng, duyệt và hoàn tiền.
- **Notification:** sự kiện đã xác nhận được đưa vào hàng đợi gửi email; hộp thư cục bộ để xem trong demo.

Tất cả endpoint thay đổi trạng thái kiểm tra vai demo, trạng thái hiện tại và idempotency key. Các thao tác sửa nhiều bảng cùng lúc chạy trong transaction SQL. Log có order ID, payment ID, PayPal capture ID hoặc tracking ID và correlation ID; không log secret hay dữ liệu thẻ. Demo Control chỉ có trong cấu hình development/demo.

Mục tiêu “xác nhận thanh toán không quá 2 giây” trong tài liệu gốc được áp dụng cho phản hồi từ hệ thống của nhóm: nếu PayPal chậm, trả trạng thái `Verifying` trong khoảng 2 giây và tiếp tục đối soát; không hứa rằng mạng/PayPal luôn hoàn tất capture trong 2 giây.

## 9. Thay đổi database dự kiến

Schema SQL mẫu và model EF hiện có là điểm xuất phát. **Lưu ý trạng thái repo:** `InitialCreate.Up()` hiện rỗng, nên chạy `dotnet ef database update` trên database trống sẽ không tạo các bảng nền. Với môi trường demo mới, bước dựng database phải chạy script SQL mẫu một lần để tạo `CloneEbayDB` và các bảng nền, sau đó áp dụng EF migration mới cho những thay đổi của MVP. Trước khi chạy script cần kiểm tra database đã tồn tại để tránh tạo trùng; không chạy script này trên database đã có dữ liệu cần giữ. Không chỉnh trực tiếp migration cũ.

| Bảng | Điều chỉnh MVP |
| --- | --- |
| `User`, `Store`, `Product`, `Inventory`, `Address` | Seed buyer/seller demo, sản phẩm, tồn kho, địa chỉ. Bổ sung ràng buộc cần thiết cho giá, số lượng và quan hệ. |
| `OrderTable` | Thêm `sellerId`, `subtotal`, `shippingFee`, `discountAmount`, `currency`, `paymentExpiresAt`, `addressSnapshot`, `couponCode`; giữ `totalPrice` là tổng cuối. |
| `OrderItem` | Giữ `unitPrice` snapshot; thêm `productTitleSnapshot`, `sellerIdSnapshot`; `quantity > 0`. |
| `Payment` | Mỗi lần thử một dòng; thêm `providerOrderId`, `providerTransactionId`, `idempotencyKey`, `errorCode`, `createdAt`, `updatedAt`. Unique trên các ID ngoài tương ứng. |
| `ShippingInfo` | Thêm `direction` (`Outbound`/`Return`), `createdAt`, `deliveredAt`, `failureReason`, `idempotencyKey`. Một chiều có tối đa một vận đơn active trong MVP. |
| `ShippingEvent` (mới) | `shippingInfoId`, `externalEventId`, `status`, `location`, `note`, `occurredAt`, `receivedAt`. Unique `externalEventId`. |
| `ReturnRequest` | Thêm `decidedAt`, `decisionReason`, `returnDeadline`, `receivedAt`, `refundId`; cả đơn, không có bảng return item trong MVP. |
| `Refund` (mới) | `orderId`, `paymentId`, `returnRequestId` tùy trường hợp, `amount`, `currency`, `reason`, `providerRefundId`, `idempotencyKey`, `status`, thời gian. |
| `Coupon` | Thêm lượt đã dùng hoặc bảng usage để không vượt `maxUsage`; lưu số tiền giảm snapshot ở đơn. |
| `NotificationOutbox` (mới) | `eventType`, `orderId`, `recipient`, `status`, `attempts`, `createdAt`, `sentAt` để retry email không gửi trùng. |

Ở MVP, có thể thêm `OrderStatusHistory` để truy nguyên thao tác buyer/seller và phục vụ demo; lịch sử tracking bắt buộc được lưu riêng. Trạng thái lưu ở database bằng chuỗi có kiểm soát trong code, không nhận chuỗi tùy ý từ client.

## 10. Email, lỗi và kiểm thử chấp nhận

Email bắt mail cục bộ cần có ít nhất ba mẫu: thanh toán thành công, giao thành công, giao thất bại. Thêm email xác nhận refund khi có kết quả. Gửi email sau khi transaction chính đã commit để lỗi email không làm giao dịch thanh toán thất bại; outbox retry khi gửi lỗi.

Các kịch bản chấp nhận tối thiểu:

1. Random giỏ một seller, thay số lượng, áp coupon hợp lệ và không hợp lệ, đổi vùng giao; tổng trên UI bằng tổng backend.
2. PayPal Sandbox thành công; tải lại trang success và gửi lại callback không làm tăng số lần thanh toán.
3. PayPal hủy, timeout và đối soát; Credit Card giả lập thành công, bị từ chối, hết hạn.
4. Đơn chưa thanh toán tự hết hạn, trả tồn; thanh toán lại sau hết hạn bị chặn.
5. Shipping API lỗi hai lần rồi thành công; vẫn chỉ có một tracking number.
6. Tracking thành công qua từng mốc; sự kiện trùng không tạo dòng trùng và sự kiện cũ không lùi trạng thái.
7. Giao thất bại rồi thử lại thành công; hoặc hàng quay về seller rồi refund.
8. Hủy trước thanh toán; hủy sau thanh toán và trước bàn giao; hủy sau bàn giao bị chặn.
9. Trả toàn bộ đơn sau giao, seller từ chối có lý do; luồng duyệt, vận chuyển trả, nhận hàng và refund.
10. Refund lỗi rồi retry; số tiền hoàn không vượt số tiền đã capture.
11. Email tương ứng xuất hiện một lần trong hộp thư demo; log có ID giao dịch nhưng không có secret/thẻ.

## 11. Thứ tự triển khai sau khi tài liệu được duyệt

1. Chốt quyết định và migration database, dữ liệu seed buyer/seller/sản phẩm/coupon.
2. Order Summary, checkout, tính giá và tạo đơn chờ thanh toán.
3. Credit Card giả lập và luồng trạng thái thanh toán; sau đó PayPal Sandbox.
4. Shipping API giả lập, retry, seller workflow và tracking.
5. Hủy, trả hàng, refund và email/outbox.
6. Demo Control, kiểm thử các kịch bản và hoàn thiện giao diện responsive.

## 12. Những quyết định cần nhóm duyệt trong bản này

Các giá trị dưới đây là **đề xuất để có thể triển khai MVP**, không phải yêu cầu bắt buộc từ file gốc:

1. Chỉ dùng **USD** trong demo để thống nhất với PayPal Sandbox.
2. Phí giao hàng hai mức **2 USD nội vùng / 5 USD khác vùng**.
3. Thời hạn thanh toán **15 phút**; thời hạn gửi yêu cầu trả **7 ngày**.
4. Mỗi yêu cầu trả áp dụng **toàn bộ đơn**, hoàn **toàn bộ số tiền đã trả** khi đủ điều kiện.
5. Buyer cần seller chấp nhận khi hủy đơn **đã thanh toán nhưng chưa bàn giao vận chuyển**.
6. Email xem trong hộp thư cục bộ; không gửi đến địa chỉ email thật.

Nếu nhóm đổi một trong các quyết định này, cần cập nhật quy tắc trạng thái, schema liên quan và kịch bản kiểm thử trước khi code.
