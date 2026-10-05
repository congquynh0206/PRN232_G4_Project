# Quản lý khuyến mãi seller/admin và áp dụng tại checkout

Ngày: 04/10/2026 (Asia/Saigon).

Trạng thái: Người dùng đã duyệt bằng “ok code đi”. Đã triển khai đủ 5 loại, kiểm thử QA và chạy migration vào database chính sau khi người dùng duyệt riêng bước này như mục 8.

## 1. Mục tiêu và phạm vi

Seller quản lý khuyến mãi của shop; admin phát coupon toàn sàn do nền tảng tài trợ và giám sát chương trình của seller. Buyer nhìn thấy giá trước/sau giảm và từng khoản giảm; các chương trình thực sự được áp dụng vào báo giá, thanh toán và đối soát tài chính.

Triển khai đủ Sale event, Volume pricing, Order discount, Coupon và Shipping discount theo mục 4 của [BUSINESS_REQUIREMENTS.md](../../BUSINESS_REQUIREMENTS.md). Giữ quy tắc một order thuộc một seller, tiền USD, ship theo kg lẻ, UTC trong database và giờ địa phương trên giao diện.

Không mở rộng sang quảng cáo Promoted Listings, đấu giá, đổi địa chỉ, thu phí trả hàng theo lý do hoặc quy trình yêu cầu hoàn một phần. Snapshot khuyến mãi phải đủ thông tin để những luồng sau có thể sử dụng. Các luồng hoàn toàn bộ hiện có phải đối soát đúng nguồn tài trợ ngay trong lần triển khai này.

Làm việc trên checkout và các trang vai trò hiện có. Không tự commit hoặc đổi branch. Không reset database, không thay URL ảnh của sản phẩm hay thay dữ liệu đơn đã thanh toán.

## 2. Hiện trạng và hướng kiến trúc

`Coupon` hiện chỉ có mã, phần trăm giảm, ngày hiệu lực, giới hạn tổng lượt và một ProductId tùy chọn. Checkout hiện tìm coupon không gắn sản phẩm, giới hạn mức giảm 20%, tính lượt theo trạng thái order. Chưa có chủ sở hữu chương trình, nguồn tài trợ, ngân sách, giới hạn từng buyer hoặc màn quản lý.

Seller finance hiện dùng tiền buyer thanh toán làm GrossAmount. Cách này phải mở rộng: coupon nền tảng giảm tiền buyer trả nhưng vẫn phải bù phần đó cho seller. Hoàn tiền phải đồng thời đảo phần trợ giá trong sổ nội bộ.

Chọn một mô hình Promotion dùng chung và quy tắc riêng cho từng loại. Một màn quản lý có form thay đổi theo loại và một bộ tính giá phía server. Không tạo năm màn độc lập hoặc năm đường tính giá rời nhau.

Các phần chịu trách nhiệm riêng:

- Quản lý: kiểm tra dữ liệu chương trình, quyền sở hữu, trạng thái, phiên bản và audit.
- Tính giá: nhận cart, danh sách chương trình hợp lệ và phí ship gốc; trả giá và phân bổ giảm theo sản phẩm. Không ghi database.
- Giữ/ghi nhận sử dụng: giữ lượt và ngân sách cho order, tiêu thụ khi payment thành công, giải phóng khi order chưa trả tiền hủy/hết hạn.
- Checkout/payment: tính lại tại server, lưu snapshot và gọi quy trình giữ/tiêu thụ đúng giao dịch.
- Finance/refund: dùng snapshot nguồn tài trợ để ghi doanh thu, trợ giá và đảo các khoản đúng một lần.

## 3. Quyền và vòng đời chương trình

| Vai trò | Quyền |
| --- | --- |
| Seller | Tạo/sửa/tạm dừng/kích hoạt lại cả 5 loại thuộc shop mình; xem thống kê sử dụng của mình. |
| Admin | Tạo/sửa/tạm dừng/kích hoạt lại coupon toàn sàn; xem tất cả chương trình; tạm dừng chương trình seller kèm lý do. |
| Buyer | Nhận báo giá của cart mình và thông tin khuyến mãi đang áp dụng; không nhận API quản trị hoặc dữ liệu buyer khác. |

Chủ sở hữu và nguồn tài trợ do server xác định từ vai trò, không nhận tùy ý từ client. Seller chỉ chọn sản phẩm/danh mục thuộc shop mình. Coupon admin có thể áp dụng toàn sàn, một shop, danh mục hoặc sản phẩm được chọn.

Trạng thái hiển thị: Sắp bắt đầu, Đang chạy, Tạm dừng, Đã kết thúc. Khoảng hoạt động là `StartAt <= now < EndAt`. Tạm dừng có ưu tiên trong hiển thị; hết thời gian thì chương trình kết thúc. Thống kê riêng thể hiện đã hết lượt/ngân sách, không giả định chương trình hết thời gian.

Chương trình admin tạm dừng không được seller tự kích hoạt lại. Admin có thể gỡ tạm dừng và lưu lý do. Tạm dừng hoặc chỉnh sửa không phá quyền lợi đã giữ hợp lệ cho order trước đó.

Không xóa cứng chương trình đã có sử dụng. Đổi loại, chủ sở hữu hoặc mã coupon sau khi phát hành không được hỗ trợ; tạo chương trình mới khi cần. Mỗi sửa đổi tăng phiên bản và lưu actor, thời gian, thay đổi, lý do nếu là thao tác quản trị. Dùng rowversion tránh ghi đè khi hai người cùng sửa.

## 4. Dữ liệu và điều kiện của 5 loại

Trường chung: tên, loại, thời gian bắt đầu/kết thúc, phạm vi áp dụng, bật/tạm dừng và điều kiện đơn nếu loại hỗ trợ. Phạm vi seller là cả shop, danh mục trong shop hoặc danh sách sản phẩm trong shop. Danh mục áp dụng chính xác CategoryId được chọn; chưa bao gồm cây danh mục con. Phạm vi được chốt thành danh sách sản phẩm đủ điều kiện khi tính một order.

Điều kiện tiền/số lượng tính trên các sản phẩm đủ điều kiện, từ giá gốc snapshot trước giảm, không gồm phí ship. Ô không giới hạn để trống nghĩa là không giới hạn; giới hạn được nhập phải lớn hơn 0. Dùng decimal cho tiền, không dùng số thực nhị phân. Phần trăm phải lớn hơn 0 và không vượt 100; số tiền cố định và trần giảm phải lớn hơn 0.

| Loại | Cấu hình | Cách tính |
| --- | --- | --- |
| Sale event | Sản phẩm/danh mục/shop; giảm % hoặc số tiền trên mỗi đơn vị hàng. | Giảm tự động từng dòng đủ điều kiện; không sửa Product.Price gốc. |
| Volume pricing | Sản phẩm/danh mục/shop; các bậc số lượng tối thiểu và % giảm. | Xét số lượng của từng sản phẩm; chọn bậc cao nhất đạt điều kiện, áp dụng cho toàn bộ số lượng của sản phẩm đó. Không cộng số lượng các sản phẩm khác nhau để lên bậc. |
| Order discount | Tổng tiền/số lượng tối thiểu của phần hàng đủ điều kiện; giảm % hoặc số tiền; trần giảm tùy chọn. | Giảm tự động trên phần hàng đủ điều kiện còn lại sau các giảm giá sản phẩm được phép kết hợp. |
| Coupon | Mã; giảm % hoặc số tiền; phạm vi; giá trị đơn tối thiểu; trần giảm; tổng lượt, lượt/buyer, ngân sách tùy chọn. | Buyer nhập một mã; giảm trên giá hàng đủ điều kiện còn lại, không giảm phí ship. Seller tài trợ mã seller, nền tảng tài trợ mã admin. |
| Shipping discount | Giá trị/số lượng hàng tối thiểu; miễn ship, giảm % phí ship hoặc giảm số tiền cố định. | Seller chịu; không vượt phí ship gốc tính theo snapshot địa chỉ/khối lượng. |

Volume pricing yêu cầu bậc không trùng ngưỡng, ngưỡng nguyên dương tăng dần, mức giảm không giảm khi mua nhiều hơn. Form hiển thị ví dụ dễ hiểu như “Từ 3 sản phẩm: giảm 10%”.

Coupon mới chuẩn hóa trim + uppercase, dài 3–50 ký tự, chỉ dùng chữ ASCII, số, `_`, `-`. Mã duy nhất toàn hệ thống để buyer nhập không nhập nhằng giữa seller và admin. Mã không hợp lệ hoặc hết điều kiện trả thông báo tiếng Việt cụ thể; không âm thầm bỏ mã đã nhập.

Giới hạn từng buyer và tổng lượt được tính theo số order thanh toán thành công đã áp dụng mã, kể cả order đã refund. Ngân sách coupon là tổng số tiền giảm do mã đó tài trợ. Số lượt/ngân sách đã giữ cho đơn chưa trả tiền cũng phải tính vào phần còn có thể giữ, nhưng UI phân biệt “đang giữ” với “đã sử dụng”.

Các giới hạn lượt/buyer/ngân sách là cấu hình coupon trong lần này. Bốn loại tự động dùng thời gian, phạm vi và điều kiện của từng loại; form không hiện cấu hình không áp dụng cho loại đó.

## 5. Kết hợp và lựa chọn mức giảm

1. Mỗi order tối đa một coupon, do seller hoặc admin phát.
2. Có coupon: được kết hợp Sale event và Shipping discount; không áp dụng Volume pricing hoặc Order discount.
3. Không có coupon: xét các phương án Sale event, Volume pricing và Order discount trên hàng đủ điều kiện. Cùng một sản phẩm không cộng Sale event với Volume pricing. Order discount có thể kết hợp Sale event; không kết hợp Volume pricing với Order discount trong cùng order. Chọn phương án cho tổng tiền buyer trả thấp nhất, không chọn tham lam từng loại rồi cộng tất cả.
4. Shipping discount có thể đi cùng các phương án hợp lệ trên. Nếu nhiều chương trình shipping đủ điều kiện thì chọn một chương trình giảm nhiều nhất.
5. Các Sale event chồng nhau trên một dòng: chọn một chương trình giảm nhiều nhất. Các Volume pricing/Order discount chồng nhau được xét tương tự trong phương án hợp lệ.
6. Nếu hai phương án có cùng tổng tiền, ưu tiên phương án có ít chương trình hơn, sau đó thứ tự PromotionId tăng dần để kết quả ổn định.
7. Không giảm vượt tiền hàng đủ điều kiện; không giảm ship vượt phí ship gốc. Không tạo tổng thanh toán âm.

Điều 3 là quy ước bổ sung để làm rõ phần tài liệu chưa quy định đầy đủ. Phải duyệt cùng bản đặc tả trước khi code.

Mỗi khoản tiền cuối cùng làm tròn 0,01 USD, MidpointRounding.AwayFromZero. Khoản giảm cấp order/coupon phân bổ vào dòng hàng theo tỷ lệ số tiền còn đủ điều kiện. Chia phần lẻ theo phương pháp phần dư lớn nhất, hòa thì ProductId tăng dần; tổng dòng phải bằng tổng giảm của chương trình và không vượt tiền dòng. Khối lượng/giá ship tiếp tục dùng quy tắc hiện có.

Nếu các giảm hợp lệ làm tổng buyer phải trả bằng 0, không gửi yêu cầu thu 0 USD tới PayPal. Ghi nhận thanh toán 0 bằng cơ chế nội bộ giả lập, chốt sử dụng và settlement như một order thanh toán thành công; UI thông báo “Đơn đã được thanh toán bằng khuyến mãi”. Khóa duy nhất ngăn chốt trùng. Khi refund order này, không gọi cổng ngoài với 0 USD; vẫn đảo trợ giá/settlement nếu có.

## 6. Báo giá, snapshot và giữ lượt

Luồng: cart/địa chỉ/mã → server báo giá → buyer xác nhận → server tính lại và giữ khuyến mãi khi tạo order → payment thành công → tiêu thụ phần giữ và ghi tài chính → giao hàng/hoàn tiền hiện có.

Báo giá không giữ lượt/ngân sách và không cam kết giá cho đến khi order được tạo. Nếu điều kiện đã thay đổi khi buyer xác nhận, server trả lỗi rõ ràng và yêu cầu xem báo giá mới; không âm thầm tạo order với tổng tiền cao hơn màn xác nhận. Request tạo order có ExpectedTotal và PricingFingerprint của báo giá đã xem. Fingerprint gồm cart/địa chỉ, giá/khối lượng, các chương trình đã áp dụng và phiên bản, phân bổ giảm và tổng; server tính lại để so sánh, không dùng giá client gửi làm giá order. Thay đổi chương trình không liên quan đến cart không làm báo giá đó mất hiệu lực.

Snapshot chốt tại tạo order, phù hợp cơ chế hiện có; thanh toán dùng đúng snapshot đó. Lưu tên chương trình, mã, phiên bản, loại, nguồn tài trợ, điều kiện đã áp dụng, số tiền theo chương trình và phân bổ mỗi dòng. Lưu giá hàng gốc, giảm seller, giảm nền tảng, ship gốc, giảm ship seller, ship buyer trả, BuyerPayable và SellerGross. Không tính lại lịch sử bằng dữ liệu chương trình đang chạy.

Tạm dừng, hết thời gian hoặc chỉnh sửa chương trình sau khi order giữ thành công không hủy phần giữ đã cấp. Giữ có hiệu lực đến hạn thanh toán của order. Thanh toán thất bại được thử lại trong thời hạn; không tiêu thụ hoặc giải phóng giữ sớm khi order còn có thể trả tiền.

Payment thành công tiêu thụ giữ và cộng lượt/ngân sách đúng một lần. Card retry, PayPal capture/callback/reconcile và worker lặp không được cộng lại. Cùng checkout key và cart hợp lệ trả cùng order/giữ; key khác cart bị từ chối.

Hủy/hết hạn đơn chưa thanh toán giải phóng giữ đúng một lần trong cùng giao dịch cập nhật order/tồn kho. PayPal còn xác minh có thể đã thu tiền thì phải giữ quyền lợi cho đến khi đối soát biết kết quả. Refund của đơn đã trả tiền không giải phóng lượt hoặc ngân sách đã tiêu thụ.

Tạo order giữ các promotion theo thứ tự ID ổn định, khóa/kiểm tra số còn lại trong transaction SQL Server; hai buyer tranh suất cuối chỉ một người giữ được. Giảm limit/ngân sách qua màn quản lý không được thấp hơn tổng đã dùng + đang giữ. Bảng giữ có unique theo order + promotion; chuyển trạng thái dùng kiểm tra điều kiện và khóa duy nhất, không dựa vào kiểm tra trước rồi ghi sau ở hai giao dịch độc lập.

## 7. Tài chính và hoàn tiền

Đặt `BaseGoods` là tổng giá hàng gốc, `SellerGoodsDiscount` là tổng giảm trên hàng do seller chịu, `BaseShipping` là phí ship gốc, `SellerShippingDiscount` là giảm ship và `PlatformSubsidy` là coupon nền tảng trên hàng.

```text
BuyerShipping = BaseShipping - SellerShippingDiscount
BuyerPayable = BaseGoods - SellerGoodsDiscount + BuyerShipping - PlatformSubsidy
SellerGross = BuyerPayable + PlatformSubsidy
PlatformFee = quy tắc phí đang cấu hình, tính trên SellerGross
SellerNet = SellerGross - PlatformFee
```

Các giảm giá seller đã loại khỏi Gross, không trừ thêm lần nữa trong ledger. Nền tảng ghi khoản trợ giá riêng có khóa duy nhất theo order. MonthlySalesAmount và kiểm tra hạn mức seller dùng SellerGross, bao gồm phần nền tảng bù cho seller. Không thay mức % phí hay thời gian hold của project trong tính năng này.

Ví dụ: hàng 100 USD, Sale event seller giảm 10 USD, phí ship 5 USD, coupon nền tảng giảm 5 USD → buyer trả 90 USD, SellerGross 95 USD. Với phí hiện tại 12% + 0,30 USD: phí 11,70 USD, SellerNet 83,30 USD. Sale event được dùng trong ví dụ thay cho coupon seller để không vi phạm quy tắc một coupon/order.

Khi hoàn toàn bộ: cổng thanh toán chỉ hoàn số buyer thực trả (90 USD trong ví dụ). Ledger đồng thời đảo phần trợ giá 5 USD và đối soát toàn bộ SellerGross 95 USD cùng fee credit theo quy tắc hiện có. Không để settlement còn dư 5 USD hoặc hoàn thêm khoản trợ giá vào tài khoản buyer. Lưu khóa đảo trợ giá theo refund, retry/worker đồng thời không tạo hai khoản đảo.

Snapshot phân bổ theo dòng hàng phải sẵn sàng cho hoàn một phần sau này. Nếu một luồng hiện có cung cấp amount refund mà không chỉ định dòng hàng, dùng tỷ lệ hoàn trên BuyerPayable của order: trợ giá đảo cộng dồn bằng `min(PlatformSubsidy, round(PlatformSubsidy * BuyerRefundedCumulative / BuyerPayable, 2))`. Mỗi lần chỉ ghi phần chênh lệch so với trợ giá đã đảo; debit Gross tương ứng là tiền hoàn buyer cộng trợ giá đảo. Không vượt captured amount, tổng trợ giá hoặc Gross còn lại. BuyerPayable bằng 0 thì chỉ hỗ trợ hoàn toàn bộ trong phạm vi hiện tại và đảo toàn bộ trợ giá một lần, không chia cho 0. Không bổ sung UI yêu cầu hoàn một phần hoặc điều chỉnh bên chịu phí trả trong phạm vi này.

Đơn lịch sử dùng giá/settlement cũ. Không áp nguồn tài trợ mới cho payment/refund của order cũ. Đơn legacy không có snapshot nguồn được đánh dấu và đối soát theo chính sách cũ để giữ nguyên số tiền đã chốt.

## 8. Dữ liệu, migration và tương thích

Các nhóm dữ liệu dự kiến:

- Promotion: chủ sở hữu/nguồn, loại, tên, mã chuẩn hóa nếu là coupon, phạm vi, điều kiện, thời gian, giới hạn coupon, trạng thái quản trị, version/rowversion.
- PromotionTarget: các ProductId/CategoryId/shop áp dụng, ràng buộc và index phục vụ lọc.
- PromotionTier: ngưỡng số lượng và phần trăm của Volume pricing.
- PromotionUsage: buyer/order/promotion, số tiền, trạng thái Reserved/Consumed/Released và thời điểm; dùng để tính lượt/ngân sách và kiểm toán.
- OrderPromotionSnapshot và phân bổ vào OrderItem: chương trình/nguồn/phiên bản/tiền đã áp dụng, độc lập với các sửa đổi về sau.
- PromotionAudit: actor, thao tác, lý do quản trị, thời gian và thay đổi.
- Bổ sung snapshot trợ giá, giảm seller, ship gốc và SellerGross vào order khi cần; không đổi ý nghĩa tiền buyer đã thanh toán.

Giữ những trường báo giá frontend đang dùng (`subtotal`, `discount`, `shipping`, `total`, `totalWeightKg`). `discount` là tổng giảm tiền hàng, `shipping` là phí buyer trả sau giảm ship; thêm ship gốc, giảm ship, các dòng giảm và thông tin giá từng sản phẩm. Tổng phải khớp `subtotal - discount + shipping`. Frontend hiển thị chi tiết thay vì cộng trừ hai lần.

Migration thêm bảng/cột/index, không chạy lại baseline. Giữ bảng Coupon cũ để kiểm toán và chuyển các mã đủ điều kiện sang Promotion bằng khóa mapping duy nhất. Coupon cũ gắn ProductId lấy seller từ sản phẩm; coupon cũ toàn sàn chuyển thành coupon nền tảng cho order mới. Đơn cũ giữ nguồn legacy như mục 7.

Tiền/mức giảm/ngày và lượt dùng thành công lịch sử phải được giữ khi chuyển mã; không reset lượt mã đã dùng chỉ vì đổi bảng. Mã rỗng/trùng sau chuẩn hóa, owner không xác định hoặc dữ liệu điều kiện lỗi phải được báo ở bước kiểm tra trước migration; không âm thầm xóa hoặc đổi mã. Không áp ngân sách mới lên chi tiêu lịch sử khi ngân sách trước đó chưa được cấu hình.

Việc chạy migration vào database chính là bước triển khai riêng sau khi code/migration đã được kiểm thử. Không tự ghi dữ liệu giả lập hoặc chạy migration chưa duyệt lên database người dùng trong lúc phát triển.

## 9. API và giao diện

API quản lý đặt trong controller/service riêng, theo conventions `/api` và proxy frontend hiện có. Có endpoint phân trang/tìm/lọc, chi tiết, tạo, sửa có version, tạm dừng/kích hoạt và options sản phẩm/danh mục. Seller API chỉ trả của mình; admin API phân biệt xem tất cả với quyền sửa coupon nền tảng và thao tác giám sát seller. Thống kê chỉ lộ tổng số, không trả định danh buyer cho màn quản lý thông thường.

Validation server bắt buộc: thời gian hợp lệ, tiền/phần trăm/ngưỡng, mã trùng, sản phẩm/danh mục không thuộc shop, bậc sai, phiên bản cũ, giới hạn nhỏ hơn đã dùng/đang giữ. UI dùng thông báo tiếng Việt và lỗi gắn vào trường tương ứng. Query lọc/paging chạy ở server; không chỉ lọc trang hiện có.

Seller/admin thêm tab “Khuyến mãi”. Một bảng desktop/thẻ mobile với tên, loại, mức giảm, phạm vi, thời gian, trạng thái, đã dùng/đang giữ và thao tác. Tab Tất cả/Sắp bắt đầu/Đang chạy/Tạm dừng/Đã kết thúc; tìm tên/mã, lọc loại; admin thêm lọc nguồn/shop. Có empty/error/loading state và nút Tạo khuyến mãi.

Form tạo/sửa chia: Thông tin chung → Điều kiện/mức giảm → Thời gian/giới hạn. Chọn loại hiện đúng trường; trường bắt buộc có nhãn; bậc số lượng thêm/xóa bằng nút. Một vùng tóm tắt diễn giải như “Đơn hàng từ 50 USD, giảm 10%, tối đa 8 USD, shop chịu phí”. Không đưa tên enum, cấu trúc database hoặc khóa kỹ thuật vào UI. Chỉ thao tác tạm dừng quản trị cần nhập lý do.

Buyer tiếp tục dùng cart/checkout hiện có. Hiển thị giá gốc/sau giảm và tên chương trình trên sản phẩm, các dòng giảm trong tổng kết, phí ship gốc/sau giảm nếu có. Khi mã không áp dụng, giải thích điều kiện thiếu hoặc giới hạn đã hết; không chỉ báo “mã lỗi”. Khi chọn coupon, giải thích các giảm theo số lượng/đơn không kết hợp đã được thay thế; không tự bỏ mã để chọn phương án khác.

Chi tiết order buyer/seller dùng snapshot để trình bày giảm giá và nguồn tài trợ. Admin thấy lịch sử sửa/tạm dừng và số liệu; không có nút sửa trực tiếp ledger.

## 10. Kiểm thử và tiêu chí nghiệm thu

1. Seller tạo, sửa, tạm dừng và kích hoạt cả 5 loại; seller khác không xem/sửa. Admin phát coupon toàn sàn và tạm dừng chương trình seller có audit.
2. Sale event chỉ giảm sản phẩm đúng phạm vi; Product.Price gốc/ảnh sản phẩm không bị thay đổi.
3. Volume pricing xử lý đúng ngưỡng 2/3/5 sản phẩm, không gộp sản phẩm khác nhau; Order discount đúng điều kiện tiền và số lượng.
4. Coupon seller/admin đúng nguồn tài trợ, min order, cap, lịch, lượt/buyer, tổng lượt và ngân sách. Mã sai/hết điều kiện có lý do rõ ràng.
5. Shipping discount không vượt phí ship gốc; phí 1,2 kg vẫn là 2,10 USD nội vùng hoặc 5,20 USD khác vùng trước giảm.
6. Tổ hợp khuyến mãi đúng mục 5; chọn tổng có lợi nhất, kết quả ổn định và phân bổ làm tròn khớp từng xu.
7. Quote không giữ lượt. Order giữ một lần; checkout key lặp không tạo hai order/giữ; thay đổi báo giá yêu cầu buyer xem lại trước khi thu tiền.
8. Hai checkout tranh suất/ngân sách cuối trên SQL Server: một giữ thành công; không vượt giới hạn hoặc deadlock do thứ tự khóa ngược.
9. Card/PayPal thành công hoặc đối soát lặp chỉ tiêu thụ một lần. Thanh toán lỗi còn hạn giữ nguyên lượt. Cancel/expire giải phóng đúng một lần; PayPal chưa rõ kết quả không giải phóng sớm.
10. Đơn thanh toán 0 không gọi cổng thu tiền; UI, lượt, snapshot và settlement đúng. Chương trình sửa/tạm dừng không đổi tiền order đã giữ/thanh toán.
11. Ví dụ mục 7: buyer 90, Gross 95, phí 11,70, Net 83,30; hoàn toàn bộ trả buyer 90 và đảo trợ giá 5 đúng một lần, không còn Gross dư. Order legacy giữ nguyên cách đối soát cũ.
12. Migration giữ Coupon/Product/Order/Payment/Refund/ledger hiện có; mapping không trùng khi kiểm tra lại. Kiểm tra dữ liệu bất thường trước khi triển khai.
13. Build toàn solution, commerce suite hiện có và kiểm thử JS pass. Browser desktop/mobile kiểm tra tạo/sửa cả 5 form, tìm/lọc/paging, lỗi validation, checkout, quyền và chi tiết snapshot. Live concurrency dùng database QA riêng.

## 11. Điểm cần người dùng duyệt

Thiết kế tổng quát và đủ 5 loại đã được chốt trong hội thoại. Bản này bổ sung chi tiết triển khai; người dùng cần xem nhất:

- Quy tắc kết hợp khi không dùng coupon (mục 5, điều 3).
- Giữ quyền lợi của order đã tạo khi chương trình bị sửa/tạm dừng (mục 6).
- Đối soát coupon nền tảng và hoàn tiền (mục 7).
- Cách chuyển coupon cũ, giữ order lịch sử (mục 8).

Người dùng đã duyệt đặc tả và yêu cầu thực thi. Kết quả triển khai/kiểm thử được ghi trong kế hoạch cùng ngày và `docs/PROMOTIONS_TEST_GUIDE.md`; các thay đổi đang để chưa commit trên branch hiện có.
