# Đặc tả nghiệp vụ G4_Project

**Phiên bản 2.0 — chốt nghiệp vụ ngày 18/10/2026, cập nhật mốc demo ngày 23/10/2026.** Đây là đặc tả nghiệp vụ mục tiêu dùng để code và nghiệm thu, đã cập nhật theo các xác nhận của người dùng và các đề xuất được đồng ý trong báo cáo phiên bản 2.0. Không phải xác nhận mọi chức năng đã triển khai.

**Thứ tự áp dụng:** yêu cầu mới nhất của người dùng → tài liệu này → tiêu chí/hiện trạng trong [IMPLEMENTATION_BACKLOG.md](IMPLEMENTATION_BACKLOG.md) → các hướng dẫn module. [Phạm vi 17/10](APPROVED_SCOPE_2026-10-17.md) và [bốn task ưu tiên](FOUR_TASK_REQUIREMENTS_2026-10-17.md) tiếp tục áp dụng cho B1/B2/S1/S2/A1/A2/N1, trừ phần đã được đặc tả 18/10 này thay thế. Mã nguồn cũ là bằng chứng hiện trạng, không tự thay đổi quy tắc đã chốt.

Đọc [README.md](README.md) để tìm tài liệu. Trạng thái triển khai phải được cập nhật ở backlog sau khi code và kiểm thử; viết đặc tả hoặc tạo báo cáo Word không tự hoàn thành một task. Các mặc định mới phục vụ mô phỏng dưới đây phải được cấu hình/snapshot khi triển khai, không được nhận là khóa cấu hình/API đã tồn tại.

## 1. Mục tiêu và phạm vi

Hệ thống mô phỏng một marketplace với một đơn hàng thuộc **một seller**: buyer chọn hàng → checkout → thanh toán → seller giao cho shipper → giao thành công hoặc chuyển hoàn → trả hàng/tranh chấp/refund → seller nhận tiền và yêu cầu payout. SQL Server là nguồn dữ liệu chính. PayPal chỉ dùng Sandbox, Credit Card và ngân hàng payout chỉ được giả lập; không dùng tiền, thẻ hoặc tài khoản ngân hàng thật.

Hệ thống có bốn vai trò `buyer`, `seller`, `shipper`, `admin`. API kiểm tra JWT và quyền sở hữu dữ liệu ở từng thao tác. Giá trị tiền tính bằng USD, làm tròn đến hai chữ số thập phân ở mỗi khoản tiền cuối cùng; thời gian lưu UTC và hiển thị theo múi giờ của người dùng. Các khoảng thời gian demo là cấu hình riêng của Development.

## 2. Quyền của từng vai trò

**Quyết định giao diện 21/10/2026:** Buyer dùng bốn mục Giỏ hàng, Đơn hàng, Tranh chấp, Thông báo; Thanh toán là bước tiếp theo của giỏ và có nút quay lại. Seller/Admin dùng sidebar chia nhóm trên desktop, menu ngăn kéo trên màn hình nhỏ; Buyer dùng thanh điều hướng dưới trên mobile. Giữ badge cần xử lý, hỗ trợ bàn phím, Back/Forward và khôi phục mục khi tải lại. Giữ nguyên điều hướng Shipper. Thay đổi này không đổi quyền/API hoặc quy tắc tài chính.

| Vai trò | Nghiệp vụ chính |
| --- | --- |
| Buyer | Random giỏ, quản lý địa chỉ/mặc định, xem báo giá và policy free returns trước mua, dùng promotion, thanh toán, lịch sử payment/refund, theo dõi tracking, yêu cầu hủy/đổi địa chỉ/return, báo chưa nhận hàng và mở dispute, đánh giá sau giao. |
| Seller | Chuẩn bị đơn, tạo nhãn, cấu hình cap đổi địa chỉ và policy free returns mặt hàng, xử lý hủy/return/dispute, promotion shop, báo cáo tài chính, trả lời review sản phẩm, theo dõi cấp/nợ và yêu cầu payout giả lập. |
| Shipper | Nhận vận đơn nguyên tử, tracking/giao lại, xem địa chỉ lấy/giao và chấp thuận khả năng giao địa chỉ mới nếu đã nhận vận đơn. Chỉ shipper được giao vận đơn được thao tác. |
| Admin | Policy phí, hạn mức riêng seller, promotion theo quyền, tra cứu order/payment/refund, phán quyết và yêu cầu trả hàng trước phán quyết, theo dõi cấp/nợ/write-off/blacklist. Không sửa tay số dư/payment/tracking hoặc tùy ý nhập seller level. |

## 3. Giỏ hàng, checkout và đơn hàng

**Giao diện demo được duyệt 23/10/2026:** hiển thị đồng hồ tại các thao tác có deadline thực tế (payment, return, receipt, dispute, đổi địa chỉ, giữ tiền, xét cấp và nợ). Ví Seller tách Processing/OnHold còn lại theo từng đơn, dùng snapshot đã lưu và nêu lý do còn bị giữ. Hết đếm ngược chỉ là đến hạn; chỉ kết quả server mới xác nhận refund/mở khóa/đổi trạng thái. Không thêm timer giả cho bước chưa có deadline, không thay đổi chính sách hoặc quyền xử lý tiền. [Phạm vi và nguồn deadline](DEMO_COUNTDOWNS.md).

1. Nút **Random** chọn 2–5 sản phẩm còn tồn kho từ đúng hai seller, không phải hàng đấu giá. Giỏ lưu theo buyer trong database; buyer được sửa số lượng, xóa sản phẩm và chọn/sửa địa chỉ trước khi tạo đơn. Nếu xóa hết hàng của một seller, giỏ còn lại được checkout bình thường. Không cần trang danh sách/chi tiết sản phẩm.
2. Checkout nhóm sản phẩm theo seller và hiển thị số lượng, giá, khối lượng, giảm giá theo từng promotion, phí ship và tổng riêng mỗi nhóm. Mã giảm giá nhập riêng theo seller. Server tính lại toàn bộ khi buyer đổi giỏ/địa chỉ/mã; client không quyết định giá.
3. Khi tạo đơn, hệ thống lưu snapshot sản phẩm, giá, khối lượng, địa chỉ, promotion, phí ship và tổng tiền; trừ tồn kho trong giao dịch. Một batch checkout key chỉ tạo một nhóm đơn, mỗi seller có một đơn. Lỗi tạo bất kỳ đơn nào rollback toàn bộ nhóm; retry đúng request trả về nhóm đơn đã tạo. Giỏ được xóa sau khi tạo nhóm thành công. Thanh toán theo từng đơn; đơn chưa trả tiền nằm trong Đơn của tôi và chờ tối đa 15 phút.
4. Đơn chưa trả tiền có thể hủy ngay. Đơn đã trả tiền nhưng chưa bàn giao shipper cần seller duyệt yêu cầu hủy; khi duyệt phải refund. Sau khi shipper nhận hàng, không hủy trực tiếp: xử lý qua giao hàng thất bại/chuyển hoàn hoặc trả hàng sau khi đã giao.
5. Nếu hết 15 phút mà chưa thanh toán, đơn thành `Expired` và tồn kho được trả lại đúng một lần. Trường hợp PayPal còn đang xác minh phải đối soát trước khi hết hạn/hủy để tránh vừa thu tiền vừa nhả hàng.

6. B1 phải đủ thêm/sửa/xóa/đặt mặc định và kiểm tra quyền địa chỉ. Địa chỉ đang chọn bị sửa/thay/xóa làm mất hiệu lực báo giá; phải lấy báo giá mới trước đặt hàng. Sổ địa chỉ không sửa snapshot đơn cũ. Giỏ tối đa 5 sản phẩm, số lượng mỗi dòng 1–5.
7. Công bố free returns từng mặt hàng ngay trong giỏ random và checkout; snapshot policy/phiên bản và khối lượng từng dòng khi mua. Sau mua, seller đổi policy không thay quyền lợi đơn cũ. Một kiện mỗi chiều và return toàn bộ order trong phạm vi chính; không bổ sung catalog hoặc CRUD sản phẩm/tồn kho toàn diện.

### 3.1. Phí vận chuyển chiều đi

Khối lượng đơn bằng tổng `khối lượng sản phẩm × số lượng` và phải lớn hơn 0. **Tính theo kg lẻ thực tế, không làm tròn lên nguyên kg**:

| Vùng giao | Công thức, với `w` là kg | Ví dụ `w = 1,2 kg` |
| --- | --- | ---: |
| Nội vùng Hà Nội | `2,00 + max(0, w - 1) × 0,50` USD | 2,10 USD |
| Khác vùng | `5,00 + max(0, w - 1) × 1,00` USD | 5,20 USD |

Áp dụng phí cơ bản cho mọi đơn lớn hơn 0 đến 1 kg; làm tròn kết quả cuối cùng đến 0,01 USD. Chỉ dùng mức nội Hà Nội khi cả nơi lấy và nơi giao ở Hà Nội, Việt Nam. Seller phải khai báo khối lượng sản phẩm; thiếu khối lượng không được checkout. Không dùng `ceil(kg)`.

## 4. Promotions

Promotions giảm giá cho buyer, tách khỏi **Promoted Listings** (quảng cáo trả phí để tăng hiển thị). Hệ thống hỗ trợ hai nguồn tài trợ: **seller** tạo chương trình cho hàng/shop của mình và **admin** tạo coupon toàn sàn do nền tảng tài trợ. Một order vẫn chỉ thuộc một seller.

| Loại | Quy tắc nghiệp vụ |
| --- | --- |
| Sale event | Seller giảm giá sản phẩm/danh mục trong khoảng thời gian xác định; buyer thấy giá trước/sau giảm trước checkout. |
| Volume pricing | Seller đặt các bậc giảm theo số lượng của cùng một sản phẩm. |
| Order discount | Seller giảm theo tổng giá trị/số lượng hàng đủ điều kiện trong đơn; có thể là phần trăm hoặc số tiền cố định. |
| Coupon | Seller hoặc admin phát mã; buyer nhập tại checkout. Có ngày hiệu lực, sản phẩm/shop áp dụng, đơn tối thiểu, mức giảm, trần giảm, tổng lượt dùng, lượt dùng mỗi buyer và ngân sách. |
| Shipping discount / free shipping | Miễn/giảm phí giao chiều đi cho mặt hàng/giỏ đủ điều kiện; dùng loại `Shipping` và cờ `FreeShipping` hiện có, không tạo cơ chế miễn ship song song. Giữ rõ nguồn tài trợ, điều kiện, thời gian, trần giảm, ngân sách và lượt dùng. |

Seller chỉ tạo/sửa/bật/tắt promotion do mình tài trợ. Admin toàn quyền quản lý coupon do nền tảng tài trợ; với chương trình seller, admin chỉ xem, đình chỉ hoặc gỡ đình chỉ kèm lý do. Gỡ đình chỉ không tự bật chương trình: seller phải chủ động kích hoạt lại. Seller không được tự gỡ đình chỉ, kể cả qua API hoặc form chỉnh sửa. Chương trình đã áp dụng cho đơn đã thanh toán không bị thay đổi hồi tố. Coupon chỉ tính là đã sử dụng khi payment thành công; retry và callback trùng không được dùng thêm lượt. Hủy hoặc hết hạn đơn chưa trả tiền giải phóng lượt đã giữ. Refund sau khi trả tiền không tự trả lại lượt coupon.

**Quy tắc kết hợp cho G4_Project:** tối đa một coupon trên một đơn (buyer chọn một mã seller hoặc một mã admin); coupon có thể đi cùng sale event và shipping discount; coupon không đi cùng volume pricing hoặc order discount. Nếu nhiều chương trình cùng nhóm đủ điều kiện, chọn phương án có lợi nhất cho buyer. Không để tổng giảm vượt khoản tiền đủ điều kiện hoặc làm tổng thanh toán âm. Đây là quy tắc lấy cảm hứng từ [eBay Discounts Manager](https://www.ebay.com/help/seller-hub-promotions/selling-tools/promotions-manager?id=4094), được cố định cho mô phỏng này.

**Đối soát:** giảm giá do seller tài trợ làm giảm doanh thu seller; coupon toàn sàn do nền tảng tài trợ không được trừ vào doanh thu seller. Hệ thống lưu nguồn tài trợ và phần giảm trên từng order item để refund toàn bộ/một phần đúng nguồn. `BuyerPayable = giá hàng sau giảm của seller + phí ship buyer phải trả - coupon nền tảng`. `SellerGross = BuyerPayable + phần coupon nền tảng tài trợ`; platform fee tính trên `SellerGross`. Ví dụ hàng 100 USD, coupon seller 10 USD, ship 5 USD, coupon nền tảng 5 USD: buyer trả 90 USD, SellerGross là 95 USD, platform fee 12% + 0,30 USD là 11,70 USD, Net seller là 83,30 USD; nền tảng ghi riêng khoản tài trợ 5 USD.

**Free shipping khác free returns:** free shipping giảm phí chiều đi buyer trả; free returns là cam kết chịu phí chiều trả theo mặt hàng. Mã miễn ship không tự bật free returns. Chi phí carrier, phần buyer trả và phần seller/platform tài trợ phải tách riêng; carrier không giao miễn phí chỉ vì checkout buyer thấy 0 USD.

## 5. Thanh toán

Buyer chọn PayPal Sandbox hoặc Credit Card giả lập; không lưu PAN/CVV thật. Thẻ giả lập hỗ trợ thành công, bị từ chối và hết hạn. Khi thất bại đã xác định, đơn vẫn chờ trong thời hạn còn lại và buyer thử lại bằng payment key mới. Cùng key không tạo hai khoản thu; capture/reconcile lặp không ghi settlement, trừ tồn hay gửi thông báo hai lần. Timeout/chưa rõ kết quả là `Verifying`, không coi là thất bại chắc chắn hoặc thu lại ngay.

Payment thành công chốt giá order, ghi mã giao dịch và chuyển đơn sang trạng thái đã thanh toán. Nếu payment từ cổng ngoài đã thành công nhưng ghi settlement nội bộ lỗi, worker phải đối soát và hoàn tất ledger theo khóa duy nhất.

**Tích hợp đã chọn:** capture API + reconcile là luồng chính demo local. PayPal cả giỏ một lần approve, capture/refund theo từng order; một số order có thể thành công trong khi order khác failed/verifying. Xác minh provider order/capture/reference ID, amount/currency phía server. Mỗi lần thử lưu `Payment`, không bắt buộc tạo bộ bảng PaymentIntent/Attempt/Event mới.

**Webhook tùy chọn:** PayPal chủ động gửi sự kiện đến endpoint HTTPS; chưa bắt buộc cho đợt code này. Nếu bổ sung, xác minh chữ ký, chống trùng event ID, xử lý chung với capture/reconcile và giữ đối soát dự phòng. Không mô tả endpoint webhook/chữ ký như đã có hoặc dùng thông báo trình duyệt làm bằng chứng thu tiền.

**Lịch sử buyer B2:** đủ các lần thử và refund, số tiền/tiền tệ, trạng thái, thời gian, tham chiếu, lý do/lỗi, lọc/phân trang. Chỉ Succeeded cộng vào tổng thu/hoàn; tổng vòng đời order không bị cắt theo trang hoặc bộ lọc thời gian. Hoàn ship giả lập có nhãn riêng. Chi tiết xem [bốn task ưu tiên](FOUR_TASK_REQUIREMENTS_2026-10-17.md).

## 6. Giao hàng và đổi địa chỉ

Seller chuẩn bị đơn, tạo vận đơn chiều đi `Outbound`, bàn giao cho shipper. Shipper chủ động nhận một vận đơn từ danh sách chờ; việc nhận phải nguyên tử để không có hai shipper cùng được giao. Timeline hợp lệ: `LabelCreated → PickedUp → InTransit → OutForDelivery → Delivered`.

Nếu giao thất bại: `OutForDelivery → DeliveryFailed → ReattemptScheduled → OutForDelivery`. Lịch giao lại lưu thời gian dự kiến và lý do; chỉ tăng `DeliveryAttempts` khi thật sự bắt đầu OutForDelivery, không tăng khi lên lịch. Mặc định tối đa hai lượt/vận đơn. Không thể/hết lượt giao lại thì `DeliveryFailed → ReturningToSender → ReturnedToSeller`; các mốc cần sự kiện thực tế, không tự coi là seller đã nhận hàng chỉ vì hết lượt.

Làm rõ SH1 ngày 20/10: khi tạo lịch phải chọn thời điểm tương lai. Đây là thời gian dự kiến, không tự phát event khi đến giờ và không khóa thao tác bắt đầu sớm nếu shipper thực tế đã xuất phát; phải có event OutForDelivery của shipper được gán mới tăng lượt.

Hai chiều dùng cùng bộ trạng thái và `ShippingInfo.Direction = Outbound/Return`, tracking và timeline riêng; không thêm `ReturnPickedUp/ReturnDelivered` thành bộ trạng thái khác. `Return` Delivered là hàng về seller, không ghi thêm giao thành công cho buyer. Mỗi `ShippingEvent` phải có actor, thời điểm UTC, event ID chống trùng, location/note và vận đơn liên quan.

Buyer được **đổi địa chỉ thành công tối đa một lần**, trước `OutForDelivery`. Yêu cầu phải có lý do, địa chỉ cũ và mới. Seller cấu hình trước mức tăng phí ship tối đa mình sẵn sàng chịu cho một lần đổi địa chỉ. Server tính lại phí theo địa chỉ mới và khối lượng snapshot:

- Nếu phí mới tăng **không quá** hạn mức seller chịu, seller có thể duyệt; buyer không trả thêm, khoản tăng được ghi là chi phí của seller.
- Nếu phí mới tăng **vượt** hạn mức, hệ thống **từ chối đổi địa chỉ**, giữ nguyên địa chỉ và vận đơn cũ.
- Nếu phí giảm, chỉ hoàn phần ship buyer thực sự còn được hưởng sau promotion/refund trước đó, không vượt phần ship đã trả và captured còn lại. Buyer trả ship 0 USD không tự được hoàn chi phí carrier tiết kiệm; điều chỉnh nội bộ giả lập có nhãn riêng, idempotent và ledger.

**Thứ tự xác nhận mới:** buyer gửi địa chỉ/lý do và xác nhận báo giá → seller chấp thuận → nếu đã có shipper nhận vận đơn, shipper chấp thuận khả năng giao → mới áp dụng địa chỉ. Nếu chưa có shipper, seller là bên duyệt cuối; shipper nhận sau dùng địa chỉ đã có hiệu lực. Không bắt đơn chờ một shipper chưa được gán.

Trong lúc chờ, địa chỉ cũ còn hiệu lực; chặn bắt đầu giao tới buyer khi yêu cầu còn chờ xác nhận. Có deadline chờ cấu hình được; hết hạn hoặc bị từ chối giữ địa chỉ cũ và không tiêu thụ lượt đổi thành công. Chỉ một yêu cầu đang chờ/đơn. Ngay trước áp dụng đọc lại trạng thái, shipper, cap/báo giá; thay đổi quan trọng phải xác nhận lại. Cập nhật địa chỉ/chi phí/refund/bộ đếm atomic, có lịch sử và chống trùng. Không tự sửa đơn cũ theo sổ địa chỉ. Đây là ngoại lệ mô phỏng, không phải quy trình eBay tiêu chuẩn.

Làm rõ AD1 ngày 20/10, cập nhật thời gian demo ngày 23/10/2026: hạn chờ tổng từ khi buyer gửi yêu cầu là 24 giờ mặc định/120 giây (2 phút) Development (`AddressChanges:ConfirmationSeconds`), snapshot theo request, không gia hạn khi chuyển bước. Seller chấp thuận nhưng đã có shipper thì AwaitingShipper; chỉ Approved mới có hiệu lực. Cap/revision, shipper hoặc quote thay đổi quan trọng làm yêu cầu cũ bị từ chối để buyer xem và xác nhận báo giá mới; chưa mất lượt thành công. Request Approved lịch sử giữ nguyên kết quả, không áp lại phí/refund hoặc gán actor cũ không có bằng chứng.

## 7. Trả hàng và refund

Buyer chỉ được yêu cầu **trả toàn bộ hàng của order** trong 7 ngày kể từ `Delivered` chiều đi; **Development: 5 phút**. Đây là thời gian mở yêu cầu, khác deadline kiểm tra hàng trả và phản đối từ chối. Một luồng return đang hoạt động/đơn; hồ sơ bị từ chối đi theo quyền phản đối, không mở hồ sơ trùng để né hạn. Buyer chọn lý do và xác nhận báo giá trước gửi.

Làm rõ RT1 ngày 20/10: `Returns:WindowSeconds` được snapshot cùng Outbound Delivered (mặc định 604800/Development 300). Quote/request/dispute thường/worker dùng cùng deadline, tại đúng hạn không mở yêu cầu mới. Đơn đã giao trước nâng cấp giữ 7 ngày, không chuyển hồi tố thành 5 phút; ReturnDeadline của hồ sơ cũ giữ lịch sử. Request/hồ sơ mở đúng hạn tiếp tục sau cửa sổ. Khi RT2 chưa có deadline riêng, giữ quyền của return đã mở đúng hạn rồi bị từ chối, không để worker RT1 tự đóng mất quyền phản đối.

| Lý do | Phí ship chiều về | Khoản refund mặc định khi return hợp lệ |
| --- | --- | --- |
| Sai hàng, sai mô tả, hàng hỏng/thiếu do seller | Seller chịu | Toàn bộ số tiền buyer đã trả, gồm phí ship chiều đi; nền tảng hoàn phần coupon do mình tài trợ. |
| Buyer đổi ý, mặt hàng không free returns | Buyer chịu phần tương ứng | Giá hàng buyer đã trả sau giảm giá; không hoàn ship đi, trừ phần ship trả buyer chịu đã báo trước. |
| Buyer đổi ý, mặt hàng có free returns | Seller chịu phần tương ứng | Không trừ phần phí trả đã miễn khỏi refund; free returns không tự hoàn phí giao chiều đi. |

**Policy free returns:** công bố trước mua và snapshot theo từng order item. Sửa policy sau đó không đổi quyền đơn cũ. Với một nhãn cho đơn trộn free/non-free, phân phí nhãn theo khối lượng snapshot từng dòng; seller chịu phần free, buyer chịu phần còn lại khi đổi ý. Làm tròn/phân phần lẻ xác định để tổng đúng phí nhãn. Lỗi seller vẫn xử lý theo trách nhiệm, không bắt buyer chịu vì sản phẩm không free returns.

Ví dụ không giảm giá: hàng 100 USD, buyer trả ship đi 5 USD, nhãn trả 5 USD. Đổi ý không free returns: buyer nhận 95 USD; đổi ý toàn bộ free returns: 100 USD và seller chịu nhãn trả; lỗi seller hoàn toàn bộ: 105 USD và seller chịu nhãn trả. Đây là tiền buyer nhận, không phải net seller cuối cùng.

Phí chiều về dùng công thức mục 3.1 theo địa chỉ và khối lượng snapshot. Nếu khoản buyer phải chịu lớn hơn khoản có thể refund, yêu cầu cần được xử lý riêng trước khi tạo vận đơn; hệ thống không tạo refund âm. Admin phân xử trách nhiệm dựa trên căn cứ trong tranh chấp; hệ thống suy ra bên chịu phí và lưu quyết định điều chỉnh riêng cùng lý do, không chọn bên chịu phí tùy ý hoặc sửa báo giá gốc.

Seller **duyệt** yêu cầu: khóa phần có thể giữ của order vào `OnHold`, tạo vận đơn chiều về `Return` có tracking riêng. Nếu nhãn lỗi, retry dùng lại request/key, không tạo hai vận đơn hoặc thu phí hai lần. Khi chiều trả `Delivered`, bắt đầu hạn **48 giờ** để seller xác nhận; **Development: 45 giây**, không phải cửa sổ buyer mở return.

- Seller xác nhận hàng đúng và đã nhận: hệ thống tự thực hiện refund theo quy tắc trên; seller không phải bấm refund thêm lần nữa.
- Seller thấy hàng hỏng/tráo/thiếu: mở tranh chấp và nộp bằng chứng trong hạn xác nhận; khoản hold tiếp tục bị khóa.
- Seller không phản hồi trong hạn: worker tự refund cho buyer, dùng idempotency key; nếu gateway lỗi, giữ tiền và retry, không đánh dấu hoàn tất giả.

Seller **từ chối**: bắt buộc lý do, lưu `RejectedAt` và deadline phản đối **3 ngày từ RejectedAt [DEMO: 45 giây, cấu hình riêng]**. Buyer chấp nhận hoặc im lặng hết hạn thì worker đóng một lần và giải phóng theo điều kiện tài chính; rejection không tự tạo nhãn/refund. Phản đối đúng hạn mở dispute và chặn tự đóng/release liên quan, kể cả đã qua Delivered + 7 ngày/5 phút. Không dùng `Disputes:BuyerResponseSeconds` của một phương án thương lượng thay cho deadline từ chối return.

Refund toàn bộ hoặc một phần chỉ được thực hiện trên số tiền còn lại buyer đã trả, không vượt số đó và không trùng khi retry. Fee credit của platform fee phân bổ theo tỷ lệ phần giá trị seller bị hoàn. Phần coupon nền tảng được hoàn/điều chỉnh riêng, không biến thành nợ seller. Refund do giao thất bại/chuyển hoàn được xử lý khi seller đã nhận lại hàng hoặc theo phán quyết Admin.

Refund Pending/Failed chưa phải đã hoàn. Thẻ dùng gateway giả lập; refund PayPal chính gọi Sandbox theo capture; hoàn ship đổi địa chỉ là điều chỉnh giả lập nội bộ kể cả payment gốc PayPal. Return không tự nhập lại hàng hỏng vào tồn bán được; phải căn cứ tình trạng thực tế.

## 8. Tranh chấp

Phân biệt return dispute, Item Not Received và dispute tình trạng hàng trả; một order chỉ một dispute đang mở, không có các quyết định refund xung đột. Mở lại/retry không giữ tiền hai lần. Hold chỉ giữ phần có thể giữ của order từ Processing/Available, không tự khóa cả ví hoặc tạo Negative ngay chỉ vì thiếu hold; refund/chi phí thực tế thiếu nguồn bù mới tạo nợ. Payment dispute/chargeback từ ngân hàng là mở rộng, không nằm trong luồng chính đã chốt.

Mô tả và URL bằng chứng HTTP/HTTPS hợp lệ, giới hạn số lượng/độ dài, không chứa username/password. Lưu actor/thời điểm; lịch sử chỉ thêm; không upload/tự tải URL hoặc khẳng định nội dung đã được xác thực. UI encode và mở link an toàn. Các bước thương lượng/bổ sung bằng chứng giữ kiểm tra hiện có; riêng buyer mở INR chỉ cần mô tả, không bắt URL/ảnh chứng minh “không nhận”.

Admin xem order, payment, tracking hai chiều, lịch sử trao đổi và bằng chứng, sau đó chọn một trong các kết quả:

1. **Seller thắng:** giải phóng hold về `Available` nếu order đủ điều kiện mở khóa; nếu chưa đủ, quay về `Processing` và giữ lịch ban đầu.
2. **Buyer thắng:** hệ thống khởi tạo refund tự động; chỉ sau khi gateway báo thành công mới trừ hold và đóng dispute. Gateway lỗi thì giữ `RefundPending` và retry.
3. **Hoàn một phần:** Admin nhập số tiền không vượt phần buyer còn có thể được hoàn, ghi lý do; phần tương ứng được refund, hold còn lại được giải phóng theo trạng thái của order.
4. **Cần trả hàng:** Admin chỉ định buyer gửi trả, tạo tracking chiều về, rồi phán quyết khi có kết quả. Quyết định phải ghi thời điểm và Admin thực hiện.

Nhánh admin yêu cầu trả hàng không phải kết quả cuối, không tự đóng dispute hoặc refund. Liên kết/dùng lại return phù hợp, chống trùng nhãn/phí, giữ hold; hiển thị nhãn lỗi/đang trả/đã về/giao trả thất bại để admin tiếp tục phán quyết. Dừng auto-refund return thường khi hồ sơ đang chờ admin, không để worker hoàn tiền trước kết luận.

### 8.1. Item Not Received

- Đơn đã thanh toán, quá ngày giao dự kiến, buyer chưa nhận: được mở INR, **không yêu cầu order Delivered**. Ngày giao dự kiến phải được snapshot/hiển thị, không đoán từ ngày tạo hồ sơ.
- Thời hạn mở tính từ ngày giao dự kiến, độc lập return: mặc định mô phỏng **30 ngày [DEMO: 5 phút]**. Đây là mặc định đặc tả được cụ thể hóa trong báo cáo 2.0, chưa phải cấu hình/code hiện có.
- Buyer nhập mô tả; seller cung cấp tracking/bằng chứng hoặc đề xuất giải quyết trong hạn. Quá hạn/chưa thống nhất chuyển admin; không tự refund ngay khi mở.
- Tracking Delivered nhưng buyer phủ nhận nhận hàng vẫn cho gửi hồ sơ đủ điều kiện để xét bằng chứng. Không yêu cầu vận đơn trả khi buyer không có hàng để trả.
- Hàng đến muộn phải cập nhật hồ sơ và buyer xác nhận/admin xem xét; không tự đóng hồ sơ chỉ từ event carrier hoặc tự thu tiền lại sau refund.

## 9. Phí nền tảng, seller balance và payout

Phí nền tảng mặc định là **12% SellerGross + 0,30 USD/đơn**, có giới hạn không vượt SellerGross. Admin đặt mức phí có hiệu lực ngay hoặc trong tương lai; settlement chọn mức phí có hiệu lực tại thời điểm payment được ghi nhận thành công (`PaidAt`), kể cả khi đối soát được chạy muộn hơn. Đơn đã có settlement giữ fee snapshot. Mọi thay đổi cấu hình lưu người sửa, thời điểm lưu, giá trị cũ/mới, thời điểm hiệu lực và lý do; lịch sử chỉ thêm mới. Net của Level 1/2 vào `Processing`; Level 3 tự vào ví `Available` ngay khi thanh toán thành công, sau khi bù nợ âm nếu có.

| Khoản | Ý nghĩa |
| --- | --- |
| `Processing` | Net đã ghi nhận nhưng chưa đủ điều kiện rút. |
| `OnHold` | Tiền của order bị khóa do return/dispute. |
| `Available` | Số dư ví khả dụng trong hệ thống; có thể yêu cầu payout ra ngân hàng giả lập. |
| `Negative` | Nghĩa vụ seller phải bù sau refund/chargeback/chi phí khi số dư không đủ. |
| `PaidOut` | Tổng đã chi trong báo cáo/lịch sử, **không phải** số dư ví còn tiêu được. |

Level 1/2 chuyển `Processing → Available` khi đã Delivered chiều đi và hết hold snapshot; trước Delivered chưa có hạn mở khóa. Return/dispute cản trở release. Không dùng Delivered HOẶC hết timer, điều kiện nào đến trước. Level 3 tự ghi net sau phí/bù nợ vào Available ngay khi payment thành công; không chờ giao và không tạo payout tự động.

Cập nhật demo 23/10/2026: cửa sổ return 5 phút bằng hold Level 1 (5 phút) và dài hơn hold Level 2 (3 phút). Buyer vẫn được return trong hạn sau release/payout nếu đủ điều kiện; yêu cầu return/dispute đã mở hợp lệ tiếp tục được xử lý sau cửa sổ. Giữ/thu hồi phần còn có thể dùng, thiếu nguồn bù thực tế thì ghi nợ. Available không đồng nghĩa hết quyền return hoặc chắc chắn được rút; payout kiểm tra riêng `Negative = 0` và trạng thái hạn chế.

**Payout chung mọi level:** seller yêu cầu rút thủ công, trừ Available và tạo payout/ledger atomic. Worker/ngân hàng ảo chuyển `Created → InProgress → FundsSent → Completed`; `FundsSent` chưa phải ngân hàng xác nhận hoàn tất. Lỗi ngân hàng đi `FundsSent → Returned`, hoàn hệ thống đúng một lần rồi bù nợ nếu có. Bỏ nhánh Level 3 tạo Completed tức thời trong code cũ. Đích ngân hàng giả lập được che; không tích hợp tiền production.

Ví dụ Available 87,70 USD, rút 50 USD: còn 37,70 USD, đang chuyển 50 USD. Completed tăng lịch sử đã chi 50; Returned trả hệ thống 50 trước các khoản bù nợ/điều chỉnh phát sinh. Tổng đang payout tính từ SellerPayout chưa kết thúc; chưa bắt buộc thêm trường balance `PayoutPending`. Lưu payout ID/key, amount/currency, đích che, bank reference, actor và lịch sử chuyển bước. Payout theo lịch là tùy chọn nâng cao.

Làm rõ PAY1 ngày 20/10: mọi yêu cầu mới là Created; worker mô phỏng ngân hàng chuyển một bước mỗi lượt, chỉ FundsSent mới có bước tiếp xác nhận Completed/Returned. Legacy Completed/Returned giữ nguyên kết quả; pending cũ tiếp tục từ bước thực tế, không ghi lại các bước/actor chưa biết hoặc trừ ví lần nữa. PayoutPending/PaidOut là tổng truy vấn toàn bộ payout seller, không cắt theo trang.

**Ledger nghiệp vụ:** mọi payment, subsidy, fee, hold, release, refund, fee credit, debt recovery và payout có khóa duy nhất/tham chiếu. Số dư và ledger lưu cùng transaction; không sửa/xóa dòng cũ, sửa sai bằng bút toán điều chỉnh có lý do. Chuyển bucket truy được nguồn/đích: release 87,70 ghi Processing −87,70 và Available +87,70, cùng nghiệp vụ. Kiểm tra đối soát số dư/lịch sử và xử lý đồng thời; không gọi mọi dòng hiện có là kế toán kép. Kế toán kép đầy đủ là hướng nâng cao, không viết lại toàn bộ tài chính chỉ để đổi tên mô hình.

## 10. Seller level và hạn mức

Level do **hệ thống tự đánh giá**, Admin xem kết quả/lý do; không nhập level tùy ý. Doanh số là `SellerGross` của payment thành công theo **tháng UTC**; tháng mới về 0. Kiểm tra hạn mức trước gọi cổng và khi ghi nhận thành công; tôn trọng reservation capture hợp lệ để không bỏ lửng payment đã thu.

Reservation capture phải được ghi cùng trạng thái đang xử lý trước gọi provider, tính theo SellerGross và cạnh tranh chung với các payment khác của cùng seller. Timeout, request bị hủy hoặc sang tháng UTC không tự giải phóng khoản capture chưa rõ kết quả; doanh số tháng mới vẫn về 0, còn nghĩa vụ đang chờ được giữ đến khi đối soát. Thất bại đã xác định giải phóng reservation; thành công chuyển phần đó thành doanh số đúng một lần. Đây là quy tắc áp dụng cho cả PayPal một đơn và cả giỏ, không phải khóa tiền trong ví buyer/seller.

**Admin cấu hình hạn mức riêng seller:** mức theo level là mặc định; override có số tiền dương, actor, lý do, thời điểm lưu/hiệu lực từ hiện tại hoặc tương lai và lịch sử chỉ thêm. Có thể kết thúc override để về mức level; đổi level không tự xóa override còn hiệu lực. Hạ mức dưới doanh số đã đạt không sửa/hủy payment hoặc settlement cũ; chặn giao dịch mới, tính cả capture đang giữ hạn mức. Kiểm tra lại dưới khóa; chỉ admin được thay, không cập nhật bằng client/seller. Snapshot/lịch sử phải giải thích được mức có hiệu lực tại lần kiểm tra. Chi tiết triển khai chọn API/schema phù hợp, không coi là endpoint đã tồn tại.

| Level | Hạn mức mặc định/tháng | Khi Net vào ví | Development | Điều kiện lên cấp |
| --- | ---: | ---: | ---: | --- |
| 1 – New | 5.000 USD | 21 ngày sau giao thành công | 5 phút (300 giây) sau Delivered | Mặc định. |
| 2 – Verified | 10.000 USD | 7 ngày sau giao thành công | 3 phút (180 giây) sau Delivered | ≥20 đơn hoàn tất; feedback tốt ≥90%; giao đúng hạn ≥90%; hoạt động ≥30 ngày; không có nợ âm. |
| 3 – Trusted / Instant | 1.000.000 USD | Ngay khi thanh toán thành công | 0 giây, không chờ Delivered | ≥100 đơn hoàn tất; feedback tốt ≥97%; giao đúng hạn ≥95%; hoạt động ≥90 ngày; không có nợ âm. |

Tỷ lệ hủy/trả/tranh chấp được hiển thị trong đánh giá và là điều kiện giữ cấp: Level 2 lần lượt không quá 5%/10%/2%; Level 3 không quá 2%/5%/1% trên các order đã thanh toán trong 90 ngày gần nhất. Nếu mẫu dưới 20 order trong cửa sổ 90 ngày, giữ cấp hiện tại trừ khi có nợ hoặc tài khoản bị hạn chế. Số đơn hoàn tất tính cộng dồn theo đơn đã thanh toán có trạng thái Delivered/Closed hoặc bằng chứng giao thành công chiều đi; feedback và đúng hạn dùng các order trong cửa sổ có dữ liệu tương ứng; không coi thiếu feedback là đánh giá xấu. Worker đánh giá định kỳ (mặc định mỗi giờ, Development mỗi 60 giây/1 phút từ 23/10/2026) và seller có nút yêu cầu đánh giá lại; thay đổi level/hold chỉ áp dụng cho settlement mới, không đổi lịch tiền của order cũ.

**Instant ở Level 3** nghĩa là Net sau phí và bù nợ tự vào số dư ví `Available` ngay khi payment thành công; không tạo `SellerPayout` và không ghi khoản nhận vào ví là `PaidOut`. Return/dispute/refund phát sinh sau đó vẫn khóa hoặc thu hồi tiền theo nghiệp vụ. Đây là quy ước mô phỏng đã được xác nhận ngày 14/10/2026, không phải cam kết eBay thật xử lý thanh toán tức thời.

## 11. Nợ âm, cảnh báo nợ xấu và blacklist

Refund/chi phí thực tế vượt nguồn tiền có thể dùng tạo `Negative`. Khi `Negative > 0`, khóa payout nhưng seller vẫn được bán/nhận đơn trước quá hạn. Net của **payment mới thành công** bù nợ trước khi phần dư vào Processing/Available; không chờ order giao xong hoặc cấn từ đơn chưa thu tiền. Tiền release/payout Returned cũng có thể bù nợ. Seller trả nợ bằng giao dịch giả lập riêng, có receipt/idempotency; không dùng thẻ/ngân hàng thật. Chargeback là mở rộng nếu sau này tích hợp.

Nếu `Negative` liên tục trên 0 quá **7 ngày** (Development: **60 giây**, cảnh báo trước hạn 15 giây), worker ghi cảnh báo nợ/đình chỉ đăng bán và nhận đơn mới theo chu kỳ nợ hiện có. Order/return/dispute cũ vẫn xử lý. Trả hết trước khi **worker khóa bán vĩnh viễn** thì gỡ đình chỉ do nợ; hạn chế khác giữ riêng. Lưu thời điểm bắt đầu nợ liên tục; trả hết rồi âm lại bắt đầu kỳ mới. Không bắt buộc tạo trường `IsWarningBadDebt` trùng với các cờ/lịch sử hiện có.

Admin xem dashboard seller bị cảnh báo, số nợ, tuổi nợ và ledger. Worker tự ghi **write-off** khi nợ liên tục ít nhất 30 ngày, đã quá hạn và không có khoản thu hồi trong 7 ngày gần nhất; Development dùng 300 giây và 60 giây. Chính sách chốt theo kỳ nợ, có cấu hình. Ngay trước quyết định, worker khóa và đọc lại số nợ; đã trả hết thì không xử lý. Quyết định giữ nguyên nợ/ledger, khóa bán vĩnh viễn và đưa định danh đã xác minh vào blacklist, lưu tiêu chí, actor hệ thống và thời điểm. Admin theo dõi kết quả, không cần bấm write-off thủ công. Trả hết sau quyết định chỉ cập nhật khoản thu hồi, không tự gỡ khóa bán vĩnh viễn. Đây là bước riêng sau đình chỉ do nợ quá hạn.

Write-off là quy tắc rủi ro do nợ trong mô phỏng, không tự kết luận gian lận, không xóa nghĩa vụ hoặc lịch sử. Appeal/gỡ blacklist là mở rộng riêng, không ngầm thêm vào luồng bắt buộc.

Đăng ký seller Development nhập tài khoản, shop và **CCCD/Passport** hoặc **tài khoản ngân hàng nhận payout** giả lập. Chuẩn hóa và xác minh định dạng, kiểm tra blacklist trước khi tạo tài khoản; hợp lệ thì tạo seller/shop/ví cấp 1, Active, số dư 0, không chờ admin duyệt. Trùng blacklist bị từ chối trước khi tạo tài khoản, không phải tạo tài khoản mới rồi ban. Màn hình có mẫu hợp lệ/không hợp lệ/trùng blacklist; không dùng dữ liệu thật. Không thu thập số thẻ thanh toán của buyer làm định danh seller. Chuẩn hóa dữ liệu rồi lưu giá trị đối chiếu bằng HMAC với khóa nằm ngoài database; giao diện chỉ hiển thị phần đã che. Seller tạo mới hoặc cập nhật định danh trùng blacklist bị từ chối bằng thông báo chung và HTTP 403; không tiết lộ định danh nào trùng. Admin không được sửa/xóa trực tiếp bản ghi blacklist, chỉ dùng quy trình quyết định có audit.

## 12. Thông báo, nhật ký và tiêu chí hoàn thành

**Quyết định 27/10/2026 — Chuông thông báo:** cả bốn role có chuông nhận cập nhật liên
quan đến tài khoản qua SignalR. Badge đếm thông báo chưa đọc 1–9, trên 9 là `9+`,
không có thì ẩn. Danh sách lưu server, có nội dung/thời gian và liên kết tới đơn/mục
liên quan; reload/đăng nhập lại không mất. Mở chuông không đánh dấu đã đọc; bấm một
thông báo mới đánh dấu đã đọc, có thao tác đánh dấu tất cả. Trạng thái đọc tách theo
từng tài khoản, gồm cả admin; gửi lại event/email không tạo trùng chuông. Quyền/tiền
vẫn do API nghiệp vụ kiểm tra. [Chi tiết và nghiệm thu](NOTIFICATION_BELL.md).

Thông báo/email được tạo cho payment thành công/thất bại cần hành động, giao thành công/thất bại, yêu cầu hủy/đổi địa chỉ/trả hàng, dispute, refund, cảnh báo nợ và payout. Lỗi gửi email không được làm thất bại giao dịch chính; worker retry có giới hạn. Nhật ký tài chính và quyết định Admin là bất biến, phân biệt sự kiện nghiệp vụ với lỗi tích hợp.

Các kịch bản tối thiểu để nghiệm thu:

1. Buyer đổi số lượng/địa chỉ và áp dụng coupon seller hoặc toàn sàn; checkout và payment lưu cùng một giá, không thu trùng.
2. Phí ship 1,2 kg đúng 2,10 USD nội Hà Nội hoặc 5,20 USD khác vùng; đổi địa chỉ tăng phí vượt cap seller bị từ chối.
3. Seller tạo vận đơn; một shipper nhận; tracking giao thành công và giao thất bại/chuyển hoàn đều đúng thứ tự.
4. Return được duyệt khóa tiền, có tracking chiều về; seller xác nhận thì tự refund; không phản hồi 48 giờ thì worker tự refund một lần.
5. Seller từ chối return; buyer mở dispute trong 3 ngày; Admin xử lý thắng/thua/hoàn một phần, tiền OnHold và fee credit khớp refund.
6. Level 3 tự nhận Net vào ví khi payment thành công, không cần payout. Level 1/2 chờ đúng thời gian tính từ Delivered; Development dùng 300/180 giây (5/3 phút). Payout `Returned` trả tiền lại một lần. Mức phí admin theo thời điểm hiệu lực và fee snapshot của đơn cũ được giữ nguyên.
7. Refund gây `Negative`, khóa payout, doanh thu mới bù nợ; quá 7 ngày bật cảnh báo, đình chỉ; trả hết nợ gỡ đình chỉ hoặc worker tự write-off và blacklist khi đủ tiêu chí.

### 12.1. Bảng thời hạn làm căn cứ triển khai

| Nghiệp vụ và mốc bắt đầu | Mặc định mô phỏng | Development mục tiêu |
| --- | --- | --- |
| Chờ thanh toán từ tạo đơn | 15 phút | 15 phút; đối soát Verifying trước kết luận |
| Buyer mở return từ Delivered chiều đi | 7 ngày | **300 giây** |
| Seller kiểm tra từ Delivered chiều trả | 48 giờ | **45 giây** |
| Buyer phản đối từ RejectedAt | 3 ngày | **45 giây**, cấu hình riêng |
| Seller/buyer trả lời phương án dispute | Theo chính sách hồ sơ hiện có | 45 giây mỗi lượt |
| Buyer mở INR từ ngày giao dự kiến | 30 ngày | **300 giây**, cửa sổ riêng |
| Hold Level 1/2 từ Delivered chiều đi | 21/7 ngày | 300/180 giây (5/3 phút) |
| Tự đánh giá seller level | 1 giờ/lần | 60 giây (1 phút)/lần |
| Xác nhận đổi địa chỉ từ lúc gửi yêu cầu | 24 giờ | 120 giây (2 phút), chung deadline seller/shipper |
| Nợ quá hạn từ bắt đầu nợ liên tục | 7 ngày | 60 giây, cảnh báo trước 15 giây |
| Write-off / không có thu hồi | 30 ngày / 7 ngày | 300 / 60 giây |

Deadline chốt theo hồ sơ; timer chỉ là lúc đủ điều kiện, không bảo đảm worker hoàn tất đúng giây đó. Cấu hình return window mới phải dùng chung trong báo giá, tạo request, điều kiện dispute liên quan, đóng order và UI. Không thay `Returns:ReceiptSeconds` thành 300 để giả lập cửa sổ mở return. Hồ sơ từ chối dùng deadline riêng; INR không bị ràng buộc Delivered + return window. Tên khóa cho các cấu hình chưa có được chọn khi triển khai và ghi lại ở hướng dẫn chạy.

## 13. Hiện trạng và tiêu chí triển khai

[IMPLEMENTATION_BACKLOG.md](IMPLEMENTATION_BACKLOG.md) là nơi theo dõi mã task, hiện trạng đã đối chiếu ngày 18/10, điểm code liên quan, phụ thuộc và tiêu chí nghiệm thu. Không dùng câu “đã bổ sung” của tài liệu module cũ để đánh dấu các thay đổi 18/10 là hoàn thành.

Nền tảng có source: auth/phân quyền, cart/checkout hai seller và PayPal cả giỏ, promotion/funding, ship theo kg lẻ/tracking hai chiều, return/refund/dispute, policy phí/hold/finance, nợ/định danh/write-off tự động, xét cấp, feedback, email/log/SignalR. Điều này không xác nhận migration của database đang chạy; S2/N1 có ghi nhận QA riêng và database chính chưa nâng tại thời điểm tài liệu module được viết.

Khoảng cách phải xử lý: B1/B2/S1/A1/A2; xác nhận địa chỉ trước áp dụng; ReattemptScheduled/actor; cửa sổ return demo; đóng return sau từ chối; INR; free returns theo item; admin override hạn mức; payout chung mọi cấp; bảo vệ/đối soát ledger. Free shipping đã có loại/cờ, cần tái sử dụng và kiểm tra tích hợp, không coi là thiếu hoàn toàn.

Khi code: migration giữ dữ liệu và snapshot cũ, kiểm tra quyền phía server, tiền/deadline/đồng thời/retry/rollback và UI theo phạm vi. Ghi rõ test đã chạy, môi trường QA và migration đã/chưa áp dụng. Không tự đổi dữ liệu chính, seed lại baseline hoặc coi tài liệu là bằng chứng test đã pass.

## 14. Phạm vi và căn cứ quyết định

- Phạm vi chính: một seller/order, cart tối đa hai seller, một kiện mỗi chiều, return toàn bộ; USD, Sandbox/thẻ/ngân hàng giả lập, bốn vai trò. Giữ các quy tắc kết hợp promotion và xét cấp đang có trừ khi có quyết định mới.
- Được bổ sung policy free returns theo mặt hàng để công bố/snapshot tại checkout; không mở rộng thành catalog/CRUD sản phẩm hoặc tồn kho đầy đủ của nhóm khác. Không thêm chat, COD hay quản trị người dùng toàn diện.
- Tùy chọn, không tự đưa vào kế hoạch bắt buộc: webhook, kế toán kép đầy đủ, payout theo lịch/ngân hàng thật, chargeback, appeal blacklist, order nhiều seller hoặc tách nhiều kiện.
- Các mặc định mô phỏng cụ thể hóa trong báo cáo 2.0 (deadline phản đối demo 45 giây, INR 30 ngày/5 phút, phân phí return hỗn hợp theo khối lượng) dùng làm đầu vào triển khai; không nhận là policy eBay hoặc hiện trạng code. Giá trị snapshot/lịch sử cũ được giữ và nâng cấp có chính sách tương thích.
- Tài liệu này là bản nguồn Markdown được duy trì trong repo, không phụ thuộc file trong Downloads hay báo cáo nằm trong thư mục artifacts bị ignore. Quyết định mới được cập nhật vào đây và backlog trước khi đồng bộ hướng dẫn module.

Nguồn tham khảo đã đối chiếu trong phiên làm việc 18/10/2026: [đổi địa chỉ trên eBay](https://www.ebay.com/help/selling/shipping-items/changing-shipping-details-purchase?id=4028), [phí trả hàng](https://www.ebay.com/help/buying/returns-refunds/start-return/return-postage?id=4066), [Item Not Received](https://www.ebay.com/help/buying/resolving-buying-problems/item-not-received?id=4042), [PayPal webhooks](https://developer.paypal.com/api/webhooks/overview/), [payout eBay](https://www.ebay.com/help/selling/getting-paid/getting-paid-items-sold?id=4814). Đây là nguồn so sánh khái niệm, không thay thế chính sách mô phỏng đã chốt.
