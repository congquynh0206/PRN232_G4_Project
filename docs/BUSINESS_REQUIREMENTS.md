# Đặc tả nghiệp vụ G4_Project

**Trạng thái:** Bản nghiệp vụ để nhóm đọc và duyệt. Tài liệu mô tả hành vi đích, không khẳng định mọi chức năng đã có trong code.

## 1. Mục tiêu và phạm vi

Hệ thống mô phỏng một marketplace với một đơn hàng thuộc **một seller**: buyer chọn hàng → checkout → thanh toán → seller giao cho shipper → giao thành công hoặc chuyển hoàn → trả hàng/tranh chấp/refund → seller nhận tiền và yêu cầu payout. SQL Server là nguồn dữ liệu chính. PayPal chỉ dùng Sandbox, Credit Card và ngân hàng payout chỉ được giả lập; không dùng tiền, thẻ hoặc tài khoản ngân hàng thật.

Hệ thống có bốn vai trò `buyer`, `seller`, `shipper`, `admin`. API kiểm tra JWT và quyền sở hữu dữ liệu ở từng thao tác. Giá trị tiền tính bằng USD, làm tròn đến hai chữ số thập phân ở mỗi khoản tiền cuối cùng; thời gian lưu UTC và hiển thị theo múi giờ của người dùng. Các khoảng thời gian demo là cấu hình riêng của Development.

## 2. Quyền của từng vai trò

| Vai trò | Nghiệp vụ chính |
| --- | --- |
| Buyer | Random giỏ hàng, sửa số lượng/địa chỉ, xem báo giá, dùng promotion, thanh toán, theo dõi đơn và tracking, yêu cầu hủy/đổi địa chỉ/trả hàng, mở tranh chấp và theo dõi refund. |
| Seller | Chuẩn bị đơn, tạo vận đơn, cấu hình mức chịu thêm phí đổi địa chỉ, xử lý hủy/trả hàng, phản hồi tranh chấp, tạo promotion của shop, xem tài chính, trả nợ giả lập và yêu cầu payout. |
| Shipper | Nhận vận đơn từ danh sách chờ, xem địa chỉ lấy/giao, cập nhật tracking và xác nhận xử lý địa chỉ đã được duyệt. Chỉ shipper đã nhận vận đơn được cập nhật vận đơn đó. |
| Admin | Cấu hình phí nền tảng và promotion toàn sàn, xem hồ sơ tranh chấp/bằng chứng, phán quyết và giám sát nợ xấu; xử lý khóa vĩnh viễn/blacklist sau khi kiểm tra. Admin có thể xem nhật ký nhưng không trực tiếp sửa số dư hay mốc tracking. |

## 3. Giỏ hàng, checkout và đơn hàng

1. Nút **Random** chọn 1–5 sản phẩm còn tồn kho, cùng một seller, không phải hàng đấu giá. Buyer được sửa số lượng và chọn/sửa địa chỉ trước khi tạo đơn.
2. Checkout hiển thị từng sản phẩm, số lượng, giá, khối lượng, giảm giá theo từng promotion, phí ship và tổng phải trả. Server tính lại toàn bộ khi buyer đổi giỏ/địa chỉ/mã; client không quyết định giá.
3. Khi tạo đơn, hệ thống lưu snapshot sản phẩm, giá, khối lượng, địa chỉ, promotion, phí ship và tổng tiền; trừ tồn kho trong giao dịch. Một checkout key chỉ tạo tối đa một đơn. Đơn chờ thanh toán tối đa 15 phút.
4. Đơn chưa trả tiền có thể hủy ngay. Đơn đã trả tiền nhưng chưa bàn giao shipper cần seller duyệt yêu cầu hủy; khi duyệt phải refund. Sau khi shipper nhận hàng, không hủy trực tiếp: xử lý qua giao hàng thất bại/chuyển hoàn hoặc trả hàng sau khi đã giao.
5. Nếu hết 15 phút mà chưa thanh toán, đơn thành `Expired` và tồn kho được trả lại đúng một lần. Trường hợp PayPal còn đang xác minh phải đối soát trước khi hết hạn/hủy để tránh vừa thu tiền vừa nhả hàng.

### 3.1. Phí vận chuyển chiều đi

Khối lượng đơn bằng tổng `khối lượng sản phẩm × số lượng` và phải lớn hơn 0. **Tính theo kg lẻ thực tế, không làm tròn lên nguyên kg**:

| Vùng giao | Công thức, với `w` là kg | Ví dụ `w = 1,2 kg` |
| --- | --- | ---: |
| Nội vùng Hà Nội | `2,00 + max(0, w - 1) × 0,50` USD | 2,10 USD |
| Khác vùng | `5,00 + max(0, w - 1) × 1,00` USD | 5,20 USD |

Áp dụng phí cơ bản cho mọi đơn từ 0 đến 1 kg; làm tròn kết quả cuối cùng đến 0,01 USD. Phân vùng dựa trên địa chỉ lấy hàng của seller và địa chỉ giao hàng đã lưu trong order. Seller phải khai báo khối lượng sản phẩm; sản phẩm thiếu khối lượng không được checkout cho đến khi bổ sung.

## 4. Promotions

Promotions giảm giá cho buyer, tách khỏi **Promoted Listings** (quảng cáo trả phí để tăng hiển thị). Hệ thống hỗ trợ hai nguồn tài trợ: **seller** tạo chương trình cho hàng/shop của mình và **admin** tạo coupon toàn sàn do nền tảng tài trợ. Một order vẫn chỉ thuộc một seller.

| Loại | Quy tắc nghiệp vụ |
| --- | --- |
| Sale event | Seller giảm giá sản phẩm/danh mục trong khoảng thời gian xác định; buyer thấy giá trước/sau giảm trước checkout. |
| Volume pricing | Seller đặt các bậc giảm theo số lượng của cùng một sản phẩm. |
| Order discount | Seller giảm theo tổng giá trị/số lượng hàng đủ điều kiện trong đơn; có thể là phần trăm hoặc số tiền cố định. |
| Coupon | Seller hoặc admin phát mã; buyer nhập tại checkout. Có ngày hiệu lực, sản phẩm/shop áp dụng, đơn tối thiểu, mức giảm, trần giảm, tổng lượt dùng, lượt dùng mỗi buyer và ngân sách. |
| Shipping discount | Seller miễn/giảm phí ship cho đơn đủ điều kiện; khoản seller tài trợ không vượt phí ship tính theo công thức ở mục 3.1. |

Seller chỉ tạo/sửa/tạm dừng promotion của mình; admin quản lý coupon toàn sàn và xem tất cả chương trình để xử lý vi phạm. Chương trình đã áp dụng cho đơn đã thanh toán không bị thay đổi hồi tố. Coupon chỉ tính là đã sử dụng khi payment thành công; retry và callback trùng không được dùng thêm lượt. Hủy hoặc hết hạn đơn chưa trả tiền giải phóng lượt đã giữ. Refund sau khi trả tiền không tự trả lại lượt coupon.

**Quy tắc kết hợp cho G4_Project:** tối đa một coupon trên một đơn (buyer chọn một mã seller hoặc một mã admin); coupon có thể đi cùng sale event và shipping discount; coupon không đi cùng volume pricing hoặc order discount. Nếu nhiều chương trình cùng nhóm đủ điều kiện, chọn phương án có lợi nhất cho buyer. Không để tổng giảm vượt khoản tiền đủ điều kiện hoặc làm tổng thanh toán âm. Đây là quy tắc lấy cảm hứng từ [eBay Discounts Manager](https://www.ebay.com/help/seller-hub-promotions/selling-tools/promotions-manager?id=4094), được cố định cho mô phỏng này.

**Đối soát:** giảm giá do seller tài trợ làm giảm doanh thu seller; coupon toàn sàn do nền tảng tài trợ không được trừ vào doanh thu seller. Hệ thống lưu nguồn tài trợ và phần giảm trên từng order item để refund toàn bộ/một phần đúng nguồn. `BuyerPayable = giá hàng sau giảm của seller + phí ship buyer phải trả - coupon nền tảng`. `SellerGross = BuyerPayable + phần coupon nền tảng tài trợ`; platform fee tính trên `SellerGross`. Ví dụ hàng 100 USD, coupon seller 10 USD, ship 5 USD, coupon nền tảng 5 USD: buyer trả 90 USD, SellerGross là 95 USD, platform fee 10% + 0,30 USD là 9,80 USD, Net seller là 85,20 USD; nền tảng ghi riêng khoản tài trợ 5 USD.

## 5. Thanh toán

Buyer chọn PayPal Sandbox hoặc Credit Card giả lập. Thẻ giả lập hỗ trợ thành công, bị từ chối và hết hạn. Khi thanh toán thất bại, đơn vẫn chờ thanh toán trong thời hạn còn lại và buyer có thể thử lại bằng payment key mới. Cùng payment key không được tạo hai khoản thu; webhook/callback/reconcile lặp lại cũng không được ghi settlement, giảm tồn kho hay gửi thông báo hai lần.

Payment thành công chốt giá order, ghi mã giao dịch và chuyển đơn sang trạng thái đã thanh toán. Nếu payment từ cổng ngoài đã thành công nhưng ghi settlement nội bộ lỗi, worker phải đối soát và hoàn tất ledger theo khóa duy nhất.

## 6. Giao hàng và đổi địa chỉ

Seller chuẩn bị đơn, tạo vận đơn chiều đi `Outbound`, bàn giao cho shipper. Shipper chủ động nhận một vận đơn từ danh sách chờ; việc nhận phải nguyên tử để không có hai shipper cùng được giao. Timeline hợp lệ: `LabelCreated → PickedUp → InTransit → OutForDelivery → Delivered`.

Nếu giao thất bại, shipper ghi `DeliveryFailed` cùng lý do và số lần giao. Có thể thử giao lại bằng mốc `OutForDelivery`; nếu không giao được, đi `ReturningToSender → ReturnedToSeller`. Đây là vận đơn chiều đi bị chuyển hoàn, khác với return do buyer đã nhận rồi yêu cầu trả. Buyer và seller xem timeline riêng theo từng chiều; không trộn các sự kiện.

Buyer được **đổi địa chỉ thành công tối đa một lần**, trước `OutForDelivery`. Yêu cầu phải có lý do, địa chỉ cũ và mới. Seller cấu hình trước mức tăng phí ship tối đa mình sẵn sàng chịu cho một lần đổi địa chỉ. Server tính lại phí theo địa chỉ mới và khối lượng snapshot:

- Nếu phí mới tăng **không quá** hạn mức seller chịu, seller có thể duyệt; buyer không trả thêm, khoản tăng được ghi là chi phí của seller.
- Nếu phí mới tăng **vượt** hạn mức, hệ thống **từ chối đổi địa chỉ**, giữ nguyên địa chỉ và vận đơn cũ.
- Nếu phí mới giảm, buyer được hoàn phần chênh lệch bằng refund giả lập; thao tác idempotent và được ghi sổ.

Shipper chỉ xác nhận và dùng địa chỉ mới sau khi yêu cầu đã được seller duyệt. Yêu cầu bị từ chối không tính là một lần đổi thành công. Không sửa trực tiếp địa chỉ mặc định trong hồ sơ buyer để thay đổi một order cũ.

## 7. Trả hàng và refund

Buyer chỉ được yêu cầu **trả toàn bộ hàng của order** trong 7 ngày kể từ `Delivered` chiều đi; Development rút cửa sổ này xuống 1 phút. Một order chỉ có một luồng return đang hoạt động. Buyer chọn lý do và có thể gửi mô tả/bằng chứng.

| Lý do | Phí ship chiều về | Khoản refund mặc định khi return hợp lệ |
| --- | --- | --- |
| Sai hàng, sai mô tả, hàng hỏng/thiếu do seller | Seller chịu | Toàn bộ số tiền buyer đã trả, gồm phí ship chiều đi; nền tảng hoàn phần coupon do mình tài trợ. |
| Buyer đổi ý hoặc lý do không do seller | Buyer chịu | Giá hàng buyer đã trả sau giảm giá; phí ship chiều đi không hoàn, phí ship chiều về được trừ vào refund và phải hiển thị trước khi buyer xác nhận. |

Phí chiều về dùng công thức mục 3.1 theo địa chỉ và khối lượng snapshot. Nếu khoản buyer phải chịu lớn hơn khoản có thể refund, yêu cầu cần được xử lý riêng trước khi tạo vận đơn; hệ thống không tạo refund âm. Admin có thể điều chỉnh bên chịu phí trong phán quyết tranh chấp và phải lưu lý do.

Seller **duyệt** yêu cầu: lập tức khóa phần tiền còn lại của order vào `OnHold`, tạo vận đơn chiều về `Return` có tracking riêng. Nếu tạo nhãn lỗi, retry dùng lại return request và không tạo hai vận đơn. Khi tracking chiều về là `Delivered`, bắt đầu hạn **48 giờ** để seller xác nhận; Development dùng **1 phút**.

- Seller xác nhận hàng đúng và đã nhận: hệ thống tự thực hiện refund theo quy tắc trên; seller không phải bấm refund thêm lần nữa.
- Seller thấy hàng hỏng/tráo/thiếu: mở tranh chấp và nộp bằng chứng trong hạn xác nhận; khoản hold tiếp tục bị khóa.
- Seller không phản hồi trong hạn: worker tự refund cho buyer, dùng idempotency key; nếu gateway lỗi, giữ tiền và retry, không đánh dấu hoàn tất giả.

Seller **từ chối** yêu cầu: bắt buộc ghi lý do. Buyer có thể mở tranh chấp trong 3 ngày từ lúc bị từ chối; nếu buyer chấp nhận hoặc hết 3 ngày không phản hồi, luồng return đóng và tiền đi tiếp theo lịch mở khóa bình thường. Rejection tự nó không tạo vận đơn chiều về hoặc refund.

Refund toàn bộ hoặc một phần chỉ được thực hiện trên số tiền còn lại buyer đã trả, không vượt số đó và không trùng khi retry. Fee credit của platform fee phân bổ theo tỷ lệ phần giá trị seller bị hoàn. Phần coupon nền tảng được hoàn/điều chỉnh riêng, không biến thành nợ seller. Refund do giao thất bại/chuyển hoàn được xử lý khi seller đã nhận lại hàng hoặc theo phán quyết Admin.

## 8. Tranh chấp

Buyer có thể mở dispute vì chưa nhận hàng, sai mô tả, hàng hỏng/thiếu hoặc giao dịch không nhận diện. Seller có thể phản hồi tranh chấp về tình trạng hàng trả. Một order chỉ có một dispute đang mở; mở lặp không khóa tiền nhiều lần. Tiền chưa payout của order được chuyển vào `OnHold`; nếu đã payout, phần thiếu trở thành nghĩa vụ của seller theo mục 10.

Buyer và seller nhập mô tả bằng chữ và URL bằng chứng ảnh/video. Chỉ chấp nhận URL `https://`, lưu URL và người gửi/thời điểm; hệ thống không upload hoặc tự tải nội dung từ URL. Giao diện mở liên kết an toàn và không hiển thị nội dung không tin cậy như HTML.

Admin xem order, payment, tracking hai chiều, lịch sử trao đổi và bằng chứng, sau đó chọn một trong các kết quả:

1. **Seller thắng:** giải phóng hold về `Available` nếu order đủ điều kiện mở khóa; nếu chưa đủ, quay về `Processing` và giữ lịch ban đầu.
2. **Buyer thắng:** hệ thống khởi tạo refund tự động; chỉ sau khi gateway báo thành công mới trừ hold và đóng dispute. Gateway lỗi thì giữ `RefundPending` và retry.
3. **Hoàn một phần:** Admin nhập số tiền không vượt phần buyer còn có thể được hoàn, ghi lý do; phần tương ứng được refund, hold còn lại được giải phóng theo trạng thái của order.
4. **Cần trả hàng:** Admin chỉ định buyer gửi trả, tạo tracking chiều về, rồi phán quyết khi có kết quả. Quyết định phải ghi thời điểm và Admin thực hiện.

## 9. Phí nền tảng, seller balance và payout

Phí nền tảng cho bản này là **10% SellerGross + 0,30 USD/đơn**, có giới hạn không vượt SellerGross. Admin được thay đổi cấu hình cho **đơn phát sinh sau thời điểm hiệu lực**; đơn đã thanh toán giữ fee snapshot. Mọi thay đổi cấu hình có người sửa, thời điểm, giá trị cũ/mới. Số tiền sau phí (`Net`) đi vào `Processing`.

| Khoản | Ý nghĩa |
| --- | --- |
| `Processing` | Net đã ghi nhận nhưng chưa đủ điều kiện rút. |
| `OnHold` | Tiền của order bị khóa do return/dispute. |
| `Available` | Tiền có thể yêu cầu payout. |
| `Negative` | Nghĩa vụ seller phải bù sau refund/chargeback/chi phí khi số dư không đủ. |
| `PaidOut` | Tổng đã chi trong báo cáo/lịch sử, **không phải** số dư ví còn tiêu được. |

Chỉ chuyển `Processing → Available` khi đơn đã giao thành công (hoặc đã `Closed` phù hợp) **và** hết thời gian giữ của level. Return/dispute đang mở chặn việc mở khóa. Seller chỉ payout từ `Available` khi `Negative = 0` và tài khoản không bị đình chỉ/khóa. Payout giả lập đi `Created → InProgress → FundsSent → Completed`; ngân hàng ảo từ chối thì `Returned` và cộng lại `Available` đúng một lần. Lưu payout key, số tiền, mã tham chiếu ảo và tài khoản nhận đã che số.

Ledger chỉ thêm mới; mọi payment, discount subsidy, fee, hold, refund, fee credit, debt recovery và payout có khóa idempotency. Seller xem được số dư và lịch sử nhưng không tự sửa ledger.

## 10. Seller level và hạn mức

Level do **hệ thống tự đánh giá**, Admin xem kết quả và lý do; không nhập level tùy ý. Doanh số là `SellerGross` của payment thành công theo **tháng UTC**; tháng mới về 0. Kiểm tra hạn mức trước khi gọi cổng thanh toán và kiểm tra lại khi ghi nhận thành công. Payment đã thành công ở cổng nhưng vượt hạn mức do cạnh tranh phải được đối soát và xử lý hoàn tiền, không bỏ lửng.

| Level | Hạn mức/tháng | Giữ tiền sau khi giao | Development | Điều kiện lên cấp đề xuất |
| --- | ---: | ---: | ---: | --- |
| 1 – New | 5.000 USD | 21 ngày | 30 giây | Mặc định. |
| 2 – Verified | 10.000 USD | 7 ngày | 15 giây | ≥20 đơn hoàn tất; feedback tốt ≥90%; giao đúng hạn ≥90%; hoạt động ≥30 ngày; không có nợ âm. |
| 3 – Trusted | 1.000.000 USD | 0 ngày | 0 giây | ≥100 đơn hoàn tất; feedback tốt ≥97%; giao đúng hạn ≥95%; hoạt động ≥90 ngày; không có nợ âm. |

Tỷ lệ hủy/trả/tranh chấp được hiển thị trong đánh giá và là điều kiện giữ cấp: Level 2 lần lượt không quá 5%/10%/2%; Level 3 không quá 2%/5%/1% trên các order đã thanh toán trong 90 ngày gần nhất. Nếu mẫu dưới 20 order trong cửa sổ 90 ngày, giữ cấp hiện tại trừ khi có nợ hoặc tài khoản bị hạn chế. Số đơn hoàn tất tính cộng dồn, feedback và đúng hạn dùng các order có dữ liệu tương ứng; không coi thiếu feedback là đánh giá xấu. Worker đánh giá định kỳ và seller có nút yêu cầu đánh giá lại; thay đổi level/hold chỉ áp dụng cho settlement mới, không đổi lịch tiền của order cũ.

**Instant Payout ở Level 3** nghĩa là Net chuyển sang `Available` ngay sau khi **giao thành công**, không cần chờ thêm ngày; seller vẫn phải gửi yêu cầu payout hoặc dùng tiến trình payout giả lập. Tiền không được rút ngay lúc buyer vừa thanh toán.

## 11. Nợ âm, cảnh báo nợ xấu và blacklist

Refund/chargeback/chi phí vượt tiền còn giữ của seller tạo `Negative`. Khi `Negative > 0`, khóa payout nhưng seller vẫn được đăng bán/nhận order trong giai đoạn đầu. Khi order mới hoàn tất, Net của order được dùng bù `Negative` trước; phần dư mới vào `Available` theo điều kiện hold của order. Seller cũng có thể thanh toán nợ bằng **giao dịch giả lập riêng**, có receipt và idempotency key; không dùng thẻ hay ngân hàng thật.

Nếu `Negative` liên tục trên 0 quá **7 ngày** (Development: **1 phút**), worker bật `IsWarningBadDebt` và đình chỉ **đăng bán/nhận order mới**. Order và return đã tồn tại vẫn tiếp tục được xử lý để tránh bỏ mặc buyer. Nếu seller trả hết nợ trước khi Admin khóa vĩnh viễn, hệ thống gỡ cảnh báo/đình chỉ và lưu lịch sử các lần cảnh báo. Thời điểm bắt đầu nợ liên tục được lưu riêng; trả hết rồi âm lại bắt đầu một kỳ mới.

Admin xem dashboard seller bị cảnh báo, số nợ, tuổi nợ và ledger. Sau khi kiểm tra, Admin có thể đánh dấu **write-off**: ghi khoản nợ không còn kỳ vọng thu hồi trong báo cáo, khóa seller vĩnh viễn và đưa thông tin định danh đã xác minh vào blacklist. Write-off không xóa ledger, lịch sử nợ hoặc số tiền gốc phục vụ kiểm toán; hệ thống lưu người quyết định, lý do và thời điểm. Đây là thao tác riêng với đình chỉ tự động.

Để blacklist hoạt động, onboarding seller phải thu thập và xác minh **CCCD/Passport** hoặc **số tài khoản ngân hàng nhận payout**. Không thu thập số thẻ thanh toán của buyer làm định danh seller. Chuẩn hóa dữ liệu rồi lưu giá trị đối chiếu bằng HMAC với khóa nằm ngoài database; giao diện chỉ hiển thị phần đã che. Seller tạo mới hoặc cập nhật định danh trùng blacklist bị từ chối bằng thông báo chung và HTTP 403; không tiết lộ định danh nào trùng. Admin không được sửa/xóa trực tiếp bản ghi blacklist, chỉ dùng quy trình quyết định có audit.

## 12. Thông báo, nhật ký và tiêu chí hoàn thành

Thông báo/email được tạo cho payment thành công/thất bại cần hành động, giao thành công/thất bại, yêu cầu hủy/đổi địa chỉ/trả hàng, dispute, refund, cảnh báo nợ và payout. Lỗi gửi email không được làm thất bại giao dịch chính; worker retry có giới hạn. Nhật ký tài chính và quyết định Admin là bất biến, phân biệt sự kiện nghiệp vụ với lỗi tích hợp.

Các kịch bản tối thiểu để nghiệm thu:

1. Buyer đổi số lượng/địa chỉ và áp dụng coupon seller hoặc toàn sàn; checkout và payment lưu cùng một giá, không thu trùng.
2. Phí ship 1,2 kg đúng 2,10 USD nội Hà Nội hoặc 5,20 USD khác vùng; đổi địa chỉ tăng phí vượt cap seller bị từ chối.
3. Seller tạo vận đơn; một shipper nhận; tracking giao thành công và giao thất bại/chuyển hoàn đều đúng thứ tự.
4. Return được duyệt khóa tiền, có tracking chiều về; seller xác nhận thì tự refund; không phản hồi 48 giờ thì worker tự refund một lần.
5. Seller từ chối return; buyer mở dispute trong 3 ngày; Admin xử lý thắng/thua/hoàn một phần, tiền OnHold và fee credit khớp refund.
6. Level 3 chỉ rút sau `Delivered`; Level 1/2 chờ đúng thời gian, Development dùng 30/15 giây. Payout `Returned` trả tiền lại một lần.
7. Refund gây `Negative`, khóa payout, doanh thu mới bù nợ; quá 7 ngày bật cảnh báo, đình chỉ; trả hết nợ gỡ đình chỉ hoặc Admin write-off và blacklist.

## 13. Khoảng cách với code hiện tại

Code hiện có đăng nhập/phân quyền, checkout, coupon phần trăm cơ bản, thanh toán, tracking hai chiều, seller balance/level/payout và workflow tranh chấp có bằng chứng, thương lượng, deadline và chuyển admin. Đã bổ sung khối lượng/ship theo kg, địa chỉ lấy hàng, shipper tự nhận từng vận đơn và return tự refund sau hạn kể từ chiều trả giao tới seller; xem [RETURNS_AND_SHIPPING.md](RETURNS_AND_SHIPPING.md).

Các phần trong tài liệu này **chưa được coi là đã triển khai đầy đủ** gồm: promotion do seller/Admin quản lý và nguồn tài trợ; đổi địa chỉ đơn một lần và mức seller chịu; chia phí ship trả theo lý do và hạn chế hoàn phí chiều đi; hoàn một phần; mức fee mới; tiêu chí level mở rộng; nợ xấu, thu nợ giả lập và blacklist. Khi phát triển, cần migration, API, giao diện và kiểm thử tương ứng; giữ dữ liệu SQL Server hiện có bằng migration thay vì chạy lại baseline.

## 14. Các quy ước đề xuất để nhóm duyệt

Những điểm sau được điền thành quy tắc cụ thể để tài liệu có thể dùng làm đầu vào triển khai, nhưng **chưa phải lựa chọn trực tiếp của người yêu cầu**: ngưỡng tuổi tài khoản và tỷ lệ hủy/trả/tranh chấp của từng level; cách chia phí ship chiều về theo nhóm lý do; hoàn chênh lệch khi đổi sang địa chỉ có phí ship thấp hơn; một order chỉ dùng một coupon; và việc buyer đổi ý không được hoàn phí ship chiều đi. Nhóm có thể sửa các quy tắc này trước khi bắt đầu migration hoặc code tương ứng.
