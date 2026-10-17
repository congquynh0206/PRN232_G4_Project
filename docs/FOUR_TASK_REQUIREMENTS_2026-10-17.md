# Yêu cầu chi tiết bốn task ưu tiên

Ngày mô tả: 17/10/2026. Tài liệu chi tiết hóa B2, B1, A1 và S1 trong [phạm vi đã chốt](APPROVED_SCOPE_2026-10-17.md), theo thứ tự triển khai đề xuất. Đây là yêu cầu chức năng và tiêu chí nghiệm thu, chưa phải chức năng đã triển khai. Các lựa chọn giao diện và mặc định được cụ thể hóa để triển khai, không mở rộng phạm vi sang quản lý sản phẩm, chat hoặc COD. Ba task S2, A2 và N1 vẫn nằm trong backlog đã duyệt.

## 1. B2 — Buyer xem lịch sử thanh toán và refund

### Mục tiêu và điểm truy cập

Buyer kiểm tra được mỗi lần thử thanh toán, khoản hoàn tiền và số tiền thực tế còn lại của một đơn. Thêm tab Lịch sử giao dịch trên trang Buyer; từ chi tiết đơn có liên kết mở lịch sử đã lọc theo đơn đó.

### Giao diện và thao tác

- Mở tab hiển thị giao dịch mới nhất trước, có phân trang; mặc định 30 ngày gần nhất và có lựa chọn Tất cả thời gian để demo dữ liệu cũ.
- Có ô mã đơn, bộ lọc loại Thanh toán/Hoàn tiền/Tất cả, trạng thái và từ ngày đến ngày; nút Tìm kiếm, Xóa bộ lọc, Làm mới.
- Bảng gồm thời gian tạo, mã đơn, loại giao dịch, phương thức, số tiền USD, trạng thái, mã giao dịch nhà cung cấp và nút Xem chi tiết. Mã dài được rút gọn có nút sao chép toàn bộ; số tiền căn phải.
- Chi tiết hiển thị thêm thời điểm thành công, lý do hoàn hoặc lỗi thân thiện, giao dịch thanh toán gốc của refund và liên kết mở đơn. Credit Card ghi giả lập, PayPal ghi Sandbox; dữ liệu thiếu hiển thị Chưa có.
- Hiển thị đủ mọi lần thử, không chỉ lần cuối; callback/retry của cùng một giao dịch không được biến thành nhiều dòng giao dịch giả.
- Thẻ tổng hợp theo mã đơn/khoảng thời gian đang chọn: Đã thu trong kỳ và Đã hoàn trong kỳ, tính trên các giao dịch thành công; nhãn giải thích các bộ lọc chỉ nhằm tìm dòng, không biến giao dịch lỗi thành số tiền đã thu. Nếu thêm chênh lệch thu-hoàn trong kỳ thì dùng đúng tên đó, không gọi là số dư ví.
- Khi mở một đơn, phần tổng hợp riêng hiển thị toàn bộ vòng đời đơn: Đã thanh toán thành công, Đã hoàn thành công, Còn lại sau hoàn. Không lấy tập giao dịch đã bị cắt bởi bộ lọc thời gian để tính phần còn lại của đơn.

### Quy tắc dữ liệu

- Chỉ Payment Succeeded được tính đã thu, chỉ Refund Succeeded được tính đã hoàn. Pending, Failed hoặc Verifying chỉ thể hiện trạng thái.
- Đơn Cancelled/Closed không tự được coi là đã hoàn tiền. Không tự cộng lại giao dịch gateway khi người dùng mở màn hình hoặc làm mới.
- Ngày lọc danh sách là ngày tạo giao dịch, phải ghi rõ trên giao diện. Tổng tiền theo kỳ dùng PaidAt hoặc CompletedAt tương ứng và hiển thị chú thích để phân biệt với ngày tạo; không tự thay thời điểm thiếu bằng ngày tạo và giả là đã thành công trong kỳ.
- Khoản hoàn chênh lệch ship giả lập được gắn nhãn riêng, không trình bày như một khoản PayPal refund nếu thực tế không gọi PayPal.
- Buyer chỉ được xem đơn/giao dịch của mình; kiểm tra tại API, không nhận buyerId từ client làm căn cứ cấp quyền.
- Tổng hợp lấy toàn bộ dữ liệu đúng phạm vi, không chỉ trang bảng đang hiển thị. Sắp xếp ổn định theo thời gian và ID.

### Nghiệm thu và demo

1. Thanh toán thẻ thất bại rồi thử lại thành công: bảng có hai lần thử, tổng đã thu chỉ cộng lần thành công.
2. Refund pending/failed không tăng tổng đã hoàn; khi Succeeded thì cập nhật đúng số tiền, liên kết và mã refund nếu có.
3. Đơn trả 100 USD, hoàn thành công 20 USD: chi tiết đơn hiển thị đã thu 100, đã hoàn 20, còn lại 80 USD.
4. Tìm theo đơn, đổi trang, xóa bộ lọc hoặc làm mới không tạo Payment/Refund mới; số tổng không thay đổi chỉ vì đổi trang.
5. Buyer khác không đọc được chi tiết hoặc lịch sử của đơn này qua API.

## 2. B1 — Buyer CRUD địa chỉ và tính lại phí ship

### Mục tiêu và điểm truy cập

Buyer quản lý địa chỉ của mình và checkout theo địa chỉ đang chọn với giá ship do server tính. Dùng phần Địa chỉ giao hàng ở checkout, kèm nút Quản lý địa chỉ mở danh sách/form gọn, giữ giỏ hiện tại.

### Giao diện và thao tác

- Mỗi địa chỉ hiển thị tên người nhận, đường/số nhà, thành phố, tỉnh/vùng, quốc gia; có nhãn Mặc định, Chọn giao tới đây, Sửa, Xóa và Đặt mặc định.
- Form Thêm/Sửa có nhãn trường rõ ràng, thông báo lỗi ngay tại trường, nút Lưu/Hủy và khóa nút Lưu khi đang gửi.
- Tận dụng cấu trúc hiện tại: tên và đường tối đa 100 ký tự; thành phố, tỉnh/vùng, quốc gia tối đa 50 ký tự, bắt buộc và trim khoảng trắng. Không tự bổ sung dữ liệu hồ sơ không cần cho task.
- Hỏi xác nhận trước khi xóa, chỉ rõ địa chỉ đang xóa. Bấm Hủy không thay đổi dữ liệu.
- Mỗi buyer có tối đa một địa chỉ mặc định; địa chỉ đầu tiên tự làm mặc định. Xóa mặc định thì chọn địa chỉ còn lại có ID nhỏ nhất làm mặc định; không còn địa chỉ thì không có mặc định.
- Đổi địa chỉ mặc định chỉ ảnh hưởng lựa chọn khởi tạo ở lần checkout sau; trong lần checkout đang mở, địa chỉ giao thực tế theo lựa chọn rõ ràng của buyer.

### Tính lại giá và bảo vệ đơn cũ

- Khi chọn địa chỉ khác hoặc lưu sửa địa chỉ đang được chọn, lấy lại báo giá từ server theo giỏ, khối lượng, địa chỉ pickup từng seller và địa chỉ giao mới.
- Cập nhật phí ship gốc, giảm phí ship, coupon/điều kiện khuyến mãi liên quan, tổng từng seller và tổng cả giỏ; hiển thị lý do nếu promotion không còn áp dụng. Không tự đổi mã coupon để che kết quả không hợp lệ.
- Trong lúc lấy báo giá, hiển thị Đang tính lại phí vận chuyển và khóa tạo đơn. Nếu lỗi, giữ lựa chọn mới kèm lỗi/thử lại, không tiếp tục với giá cũ.
- Buyer đổi địa chỉ nhanh liên tiếp thì chỉ kết quả của lựa chọn mới nhất được hiển thị; phản hồi cũ đến muộn không được ghi đè.
- Xóa địa chỉ đang chọn thì bỏ báo giá cũ, chọn lại địa chỉ hợp lệ hoặc hiển thị yêu cầu thêm địa chỉ. Giỏ vẫn được giữ.
- Khi tạo đơn, server đọc lại địa chỉ/giỏ và tính giá cuối cùng; không tin số tiền client gửi. Địa chỉ bị xóa hoặc thay đổi cạnh tranh khiến báo giá không hợp lệ phải yêu cầu lấy lại giá.
- Đơn đã tạo giữ nguyên snapshot địa chỉ và tiền. Muốn đổi địa chỉ của đơn cũ vẫn dùng workflow đổi địa chỉ đơn hiện tại.
- API kiểm tra buyer sở hữu địa chỉ ở mọi thao tác. Việc đổi mặc định phải nguyên tử để không sinh hai mặc định khi gửi đồng thời.

### Nghiệm thu và demo

1. Buyer thêm địa chỉ A/B, sửa B, đặt B mặc định, xóa A và tải lại trang; dữ liệu/mặc định được giữ đúng.
2. Với cùng pickup Hà Nội và đơn 1,2 kg không có giảm ship: chọn địa chỉ nội vùng cho giá 2,10 USD; chọn địa chỉ khác vùng cho giá 5,20 USD theo công thức hiện có.
3. Giỏ có hai seller được tính lại riêng từng nhóm, không lấy phí của seller này áp cho seller kia.
4. Chọn A rồi B nhanh, mô phỏng phản hồi báo giá A về sau B: màn hình vẫn hiển thị giá theo B.
5. Sửa/xóa địa chỉ trong sổ sau khi tạo đơn không làm đổi snapshot hoặc phí của đơn cũ.
6. Không thể sửa/xóa/đặt mặc định địa chỉ của buyer khác qua API.

## 3. A1 — Admin tra cứu đơn và giao dịch phục vụ hỗ trợ

### Mục tiêu và điểm truy cập

Thêm tab Tra cứu đơn & giao dịch trên trang Admin. Mở tab có ngay danh sách đơn gần nhất, kể cả đơn chưa có tranh chấp, để dễ chọn đơn demo và hỗ trợ trường hợp đã trả tiền nhưng trạng thái chưa khớp hoặc refund bị lỗi.

### Danh sách và tìm kiếm

- Một ô tìm mã đơn, mã provider order/capture hoặc mã refund; chấp nhận dạng #125 cho mã đơn. Với mã giao dịch, tìm theo mã đầy đủ để cho kết quả xác định.
- Bộ lọc buyer/seller bằng tên/email/ID, từ ngày đến ngày, trạng thái đơn, trạng thái thanh toán hiện tại, trạng thái refund; ngày trên danh sách đơn là ngày tạo đơn và phải ghi rõ.
- Có các nút lọc nhanh Chờ thanh toán, Có thanh toán lỗi, Có refund, Có tranh chấp. Có nút Tất cả/Xóa bộ lọc để quay về danh sách gần nhất.
- Có thanh toán lỗi nghĩa là có ít nhất một lần thất bại, kể cả đơn đã thanh toán thành công sau đó. Có refund gồm cả đang chờ, thất bại và thành công; phân biệt bằng trạng thái/badge.
- Bảng gồm mã đơn, thời gian tạo, buyer, seller, tổng tiền, tình trạng thanh toán, đã hoàn thành công, tình trạng giao hàng và nút Xem. Đơn có lỗi cũ và đã trả thành công phải thể hiện cả hai ý, tránh khiến người xem hiểu chưa thanh toán.
- Mọi đơn có thể có nhiều payment/refund nhưng chỉ một dòng trong danh sách đơn. Các bộ lọc kết hợp nhất quán; đổi bộ lọc quay về trang 1, làm mới giữ bộ lọc.

### Chi tiết và phân quyền

- Mở Xem hiển thị tóm tắt đơn/sản phẩm và ba số tiền toàn vòng đời: đã thu thành công, đã hoàn thành công, còn lại sau hoàn.
- Lịch sử payment/refund thể hiện mọi lần thử với mã và lỗi như B2; tracking chia chiều đi và chiều trả, không trộn timeline.
- Có liên kết mở nhật ký tích hợp theo mã đơn và hồ sơ tranh chấp nếu có. Không có tranh chấp thì ghi rõ, không tạo hồ sơ tự động.
- Đóng chi tiết quay về đúng trang/bộ lọc, không mất vị trí đang tra cứu. Hỗ trợ liên kết từ mã đơn hiển thị ở màn khác.
- Cấp quyền đọc đơn và giao dịch cho admin đúng mục đích tra cứu, bao gồm đơn chưa escalated; không chỉ bỏ điều kiện quyền ở frontend hoặc nới quyền cho role khác.
- Không trả token, secret, số thẻ hoặc định danh seller đầy đủ. Không có nút sửa tay tiền, payment, tracking hay phán quyết ngay trên màn tra cứu; mở hồ sơ hiện có để xử lý nghiệp vụ.

### Nghiệm thu và demo

1. Admin mở tab và thấy đơn gần nhất mà không cần nhớ mã hoặc tạo tranh chấp.
2. Buyer thanh toán lỗi rồi thử thành công; admin chọn Có thanh toán lỗi, thấy đơn và hai lần thử, tổng thu chỉ tính lần thành công.
3. Tìm đúng mã đơn/capture/refund đều dẫn tới cùng đơn; một đơn có nhiều giao dịch không bị nhân dòng.
4. Đơn có refund pending/failed và succeeded thể hiện đúng số tiền thực đã hoàn. Đơn chưa có payment vẫn hiển thị bình thường.
5. Xem tracking, nhật ký hoặc tranh chấp rồi quay lại vẫn giữ bộ lọc/trang khi đi theo thao tác quay lại phù hợp.
6. Buyer/seller/shipper gọi API tra cứu admin bị từ chối; admin xem được đơn không có tranh chấp nhưng không có thêm quyền sửa trạng thái thủ công.

## 4. S1 — Seller xem báo cáo tài chính trực quan

### Mục tiêu và điểm truy cập

Thêm tab Báo cáo tài chính trên trang Seller. Phần trên là bộ lọc và thẻ tổng hợp, tiếp theo là biểu đồ xu hướng, cuối cùng là bảng số liệu để đối chiếu. Không thay thế tab ví/ledger/payout hiện có.

### Phạm vi thời gian và số liệu

- Mặc định tháng hiện tại; có Hôm nay, 7 ngày, 30 ngày, Tháng này và khoảng tùy chọn. Chọn nhóm theo ngày/tuần/tháng; tuần bắt đầu thứ Hai.
- Thẻ chính: Buyer đã trả, Đã hoàn cho buyer, Phí nền tảng thực thu sau fee credit và Net seller trong kỳ. Có thêm số đơn thanh toán thành công, đếm đơn riêng biệt.
- Khi có nền tảng tài trợ, hiển thị riêng phần trợ giá và phần đảo trợ giá. Khi có chi phí đổi địa chỉ/ship trả hoặc khoản thu hồi phí ship trả, hiển thị trong phần điều chỉnh có thể mở xem.
- Net seller là kết quả bán hàng sau phí, refund và điều chỉnh được ghi nhận; không gọi là tiền rút được. Số tiền đang Processing, OnHold, Available, khoản bù nợ và payout thuộc ví hiện có và không được cộng thành doanh thu mới.
- Thu tiền dùng thời điểm payment thành công; hoàn tiền dùng thời điểm refund thành công; phí và fee credit gắn với sự kiện tài chính tương ứng, chi phí khác theo thời điểm ghi nhận nghiệp vụ. Khoản đối soát ghi muộn phải được gắn đúng thời điểm nghiệp vụ có bằng chứng; thiếu dữ liệu phải thể hiện giới hạn đối soát.
- Đây là báo cáo phát sinh trong kỳ, không phải chọn đơn tạo trong kỳ rồi lấy toàn bộ lịch sử. Đơn tháng trước refund tháng này phải hiện phần hoàn/điều chỉnh trong tháng này, không tính lại tiền thu tháng trước.
- Phân nhóm ngày/tuần/tháng theo múi giờ hiển thị của người dùng, server truy vấn khoảng UTC tương ứng; kỳ hạn mức seller theo tháng UTC hiện có không thay đổi.

### Quy tắc tính tiền

- Chỉ payment/refund thành công được cộng vào các số thực thu/thực hoàn; đơn chưa trả tiền và giao dịch failed/pending không tạo doanh thu đã thu.
- Buyer đã trả khác SellerGross khi có trợ giá nền tảng; không trừ toàn bộ coupon nền tảng vào doanh thu seller hoặc cộng trợ giá hai lần.
- Phí thực thu bằng phí được ghi nhận trừ fee credit phát sinh đúng kỳ. Tiền net tính từ các bút toán doanh thu/chi phí tương ứng; không cộng toàn bộ ledger vì có các bút toán chuyển bucket và audit.
- Khi xác định net phải phân biệt cash refund cho buyer, phần subsidy đảo riêng, phần ship trả buyer chịu/hoàn lại seller và chi phí đã ghi; không trừ cùng một nghĩa vụ hai lần.
- Bù nợ, payout, chuyển Processing sang Available, đặt/giải phóng hold không thay đổi doanh thu hay net bán hàng một lần nữa. Đối chiếu mapping nghiệp vụ với settlement/ledger và giữ nguyên quy tắc tài chính hiện có.
- Kỳ chỉ có refund/chi phí có thể có net âm. Không ép về 0 để biểu đồ đẹp.
- Các thẻ, biểu đồ và bảng dùng cùng nguồn/phạm vi số liệu; tổng không chỉ cộng trang bảng hiện tại.

### Biểu đồ và bảng

- Dùng một biểu đồ đường để xem xu hướng theo thời gian với tối đa ba chuỗi mặc định: buyer đã trả, đã hoàn và net seller; có chú giải, tooltip ngày/số tiền và bật/tắt chuỗi. Không dùng biểu đồ nến hoặc tròn cho xu hướng này.
- Mỗi chuỗi khác nhau bằng nhãn/kiểu nét hoặc điểm ngoài màu sắc; luôn có bảng số liệu tương ứng để tra giá trị chính xác.
- Bảng gồm kỳ, số đơn trả tiền thành công, buyer đã trả, đã hoàn, phí thực thu, net; mở dòng để xem trợ giá/điều chỉnh và các đơn/giao dịch nguồn thuộc kỳ.
- Ngày không có phát sinh hiển thị 0; với kỳ quá dài, nhóm theo tuần/tháng phù hợp thay vì vẽ hàng nghìn điểm.
- Không có dữ liệu hiển thị trạng thái rỗng rõ ràng, không tạo doanh thu demo giả. Trên mobile thẻ xuống dòng, biểu đồ co theo màn hình và bảng dùng bố cục gọn hoặc cuộn trong khung riêng.

### Nghiệm thu và demo

1. Có đơn chờ trả và đơn thất bại: hai đơn này không tăng số tiền đã thu hoặc số đơn thanh toán thành công.
2. Đơn buyer trả 100 USD, không trợ giá/chi phí khác, phí 12% + 0,30 USD: phí 12,30 và net 87,70 USD.
3. Cùng đơn được refund một phần 20 USD và fee credit 2,46 USD: tổng toàn kỳ chứa đủ hai sự kiện là đã thu 100, đã hoàn 20, phí thực thu 9,84 và net 70,16 USD.
4. Nếu refund ở kỳ sau: kỳ sau chỉ ghi hoàn 20, fee credit 2,46, net thay đổi -17,54 USD; không ghi thêm thu 100 USD. Tooltip giải thích net âm khi cần.
5. Ví dụ subsidy, phí ship trả và bù nợ phải đối chiếu khớp ledger; payout/giải phóng hold không tạo doanh thu mới.
6. Tổng biểu đồ/bảng khớp các thẻ, đổi trang bảng không làm đổi tổng; seller khác không xem được báo cáo này qua API.

## 5. Tiêu chí chung và kiểm thử

- Giữ giao diện role hiện có, nhãn tiếng Việt, tiền USD hai chữ số thập phân, thời gian hiển thị rõ múi giờ. Lọc ngày có kiểm tra từ ngày không lớn hơn đến ngày.
- Có trạng thái đang tải, rỗng, lỗi và nút thử lại; không hiển thị dữ liệu cũ như kết quả của bộ lọc mới khi tải lỗi. Bấm nhanh/đổi bộ lọc liên tiếp không để phản hồi cũ ghi đè.
- Có nhãn trường, focus bàn phím nhìn thấy, lỗi dễ đọc, nút thao tác đủ lớn trên mobile; trạng thái không chỉ dựa vào màu.
- Lọc/phân trang thực hiện trên server; tổng hợp tính từ toàn bộ tập dữ liệu phù hợp. Không làm mới hoặc mở màn đọc mà gây payment/refund/phát sinh sổ tài chính mới.
- Migration nếu cần phải giữ dữ liệu SQL Server hiện có. Không chạy lại baseline hoặc reset dữ liệu thật để demo.
- Kiểm thử cần tập trung vào tính tiền, lọc ngày/phân trang, quyền sở hữu, nhiều địa chỉ mặc định đồng thời, báo giá trả về sai thứ tự và đơn có nhiều lần thanh toán; kiểm tra UI desktop/mobile bằng dữ liệu QA.
- Tái sử dụng phần đọc/tổng hợp giao dịch giữa B2 và A1, có kiểm tra quyền riêng. Tái sử dụng các quy tắc số liệu cho S1 thay vì viết công thức tiền khác nhau giữa các màn.
