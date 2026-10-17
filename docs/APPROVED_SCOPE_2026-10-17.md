# Phạm vi bổ sung đã chốt ngày 17/10/2026

Tài liệu ghi lại quyết định trực tiếp của người dùng cho đợt bổ sung chức năng của nhóm 4, phụ trách thanh toán và giao hàng. Đây là danh sách cần làm, không phải xác nhận đã triển khai. Các quyết định dưới đây thay thế đề xuất trước đó nếu khác nhau về phạm vi đợt này.

## 1. Buyer

### B1. CRUD địa chỉ và tính lại phí vận chuyển

- Xem danh sách, thêm, sửa, xóa địa chỉ của chính buyer.
- Cho phép chọn địa chỉ mặc định và chọn địa chỉ giao hàng khi checkout.
- Khi chọn địa chỉ khác hoặc sửa địa chỉ đang chọn, server tính lại phí ship và tổng thanh toán cho từng seller trong giỏ hiện tại; cập nhật cả giảm giá có điều kiện liên quan nếu có.
- Khi địa chỉ đang chọn bị xóa hoặc báo giá chưa tải thành công, yêu cầu chọn địa chỉ hợp lệ và lấy báo giá mới trước khi tiếp tục đặt hàng.
- Địa chỉ đã snapshot trong đơn cũ không thay đổi khi CRUD sổ địa chỉ. Đổi địa chỉ của đơn đã tạo tiếp tục theo workflow hiện tại.
- Không bổ sung giỏ hàng mới hoặc trang chọn sản phẩm; giữ nguyên cách tạo và sử dụng giỏ hiện tại.

### B2. Lịch sử thanh toán và hoàn tiền rõ ràng

- Có bảng lịch sử thanh toán và refund, tra được theo đơn hàng.
- Hiển thị các lần thử, kể cả thất bại hoặc đang xác minh: phương thức, số tiền, tiền tệ, trạng thái, thời gian, mã giao dịch/mã refund và lý do hoặc lỗi phù hợp.
- Hiển thị tổng đã thanh toán thành công, tổng đã hoàn thành công và phần còn lại sau hoàn tiền.
- Pending/failed không được cộng vào tiền đã thu hoặc đã hoàn. Trạng thái tiền độc lập với trạng thái đơn.
- Có bộ lọc thời gian, mã đơn, loại giao dịch, trạng thái và phân trang; buyer chỉ xem giao dịch thuộc đơn của mình.

## 2. Seller

### S1. Báo cáo doanh số và tài chính trực quan

- Thêm báo cáo gọn, dễ nhìn, hỗ trợ chọn khoảng thời gian và xem theo ngày/tuần/tháng.
- Các thẻ số liệu phân biệt tiền hàng đã thu, tiền đã hoàn, phí nền tảng và net seller; trình bày riêng phần nền tảng tài trợ hoặc điều chỉnh khi cần để đối soát chính xác.
- Đơn chưa thanh toán không được tính là doanh thu đã thu; giao dịch failed/pending không được cộng như đã thành công.
- Net seller sau phí/refund/điều chỉnh không đồng nghĩa với Available hoặc tiền đã payout. Hiển thị nhãn rõ và đối chiếu với settlement/ledger hiện có, tránh trừ phí hoặc refund hai lần.
- Có thể dùng biểu đồ theo thời gian kết hợp bảng chi tiết để vừa dễ nhìn vừa tra được số liệu; màu sắc và chú giải nhất quán, xem được trên mobile.
- Seller chỉ xem dữ liệu của mình.

### S2. Tổng hợp và trả lời feedback theo sản phẩm

**Trạng thái:** Đã triển khai và kiểm thử trên database QA riêng; database chính chưa áp dụng migration. Xem [PRODUCT_FEEDBACK.md](PRODUCT_FEEDBACK.md) để chạy và demo.

- Một khu vực riêng để seller xem feedback của khách hàng, nhóm theo từng sản phẩm của seller.
- Hiển thị ảnh/tên sản phẩm, điểm trung bình, số đánh giá, số phản hồi chưa trả lời và danh sách rating/bình luận của khách.
- Có tìm kiếm sản phẩm, lọc số sao, trạng thái đã/chưa trả lời, thời gian và phân trang.
- Cho phép seller mở đánh giá và trả lời ngay tại đó; phản hồi gắn với đúng đánh giá, lưu người trả lời và thời gian. Không biến chức năng này thành chat.
- Không cho seller sửa/xóa nội dung đánh giá của buyer hoặc trả lời đánh giá thuộc sản phẩm của seller khác.
- Khi triển khai, đối chiếu nguồn Review gắn ProductId. Feedback đánh giá seller theo đơn đang có phải được giữ riêng; không tự nhân một đánh giá seller thành đánh giá cho tất cả sản phẩm trong đơn, không làm thay đổi cách xét cấp seller hiện tại.
- Cách buyer xem phản hồi và nguồn tạo feedback theo sản phẩm cần được nối với dữ liệu đánh giá sản phẩm thực tế; không dùng dữ liệu tổng hợp seller để giả lập phản hồi theo sản phẩm.

### Phần seller giữ nguyên

- Giữ nguyên xác nhận/chuẩn bị đơn, tạo vận đơn, coupon/promotion và xử lý hủy/trả hàng/refund hiện tại.
- Không bổ sung giao diện tạo, sửa, xem hoặc quản lý sản phẩm/tồn kho; đây là phạm vi nhóm khác. Việc hiển thị tên/ảnh sản phẩm trong báo cáo feedback chỉ phục vụ tra cứu đánh giá.

## 3. Admin

### A1. Tra cứu đơn hàng và giao dịch phục vụ hỗ trợ

- Thêm nơi tra cứu đơn và giao dịch thanh toán/refund để hỗ trợ xử lý sự cố.
- Tìm theo mã đơn, mã giao dịch/mã refund, buyer, seller; lọc thời gian và trạng thái, có phân trang.
- Chi tiết liên kết được thông tin đơn, các lần thanh toán/refund, tracking, nhật ký tích hợp và tranh chấp hiện có nếu được phép xem.
- Trình bày rõ tiền đã thu, tiền đã hoàn và giao dịch đang chờ/lỗi. Áp dụng quyền admin phía server, che thông tin nhạy cảm.
- Tra cứu là thao tác đọc; các quyết định tài chính vẫn đi qua workflow hiện có, không thêm thao tác sửa tay số dư, trạng thái payment hoặc tracking.

### A2. Admin yêu cầu trả hàng trước khi tiếp tục phán quyết

- Bổ sung nhánh xử lý tranh chấp: admin yêu cầu buyer trả hàng mới → tạo yêu cầu/vận đơn chiều trả → chờ hàng giao về seller → admin tiếp tục phán quyết.
- Tận dụng return request và tracking chiều trả hiện có; nếu đơn đã có luồng trả hàng phù hợp thì liên kết và dùng lại, không tạo trùng yêu cầu hoặc vận đơn.
- Quyết định yêu cầu trả hàng ghi admin thực hiện, thời gian, lý do và căn cứ; buyer/seller xem được trạng thái và hướng dẫn bước tiếp theo.
- Giữ tiền đang hold trong khi chờ xử lý. Yêu cầu trả hàng không tự đóng tranh chấp hoặc tự refund; sau khi hàng về, admin tiếp tục chọn seller thắng, hoàn toàn bộ hoặc hoàn một phần theo nghiệp vụ hiện có.
- Xử lý retry tạo nhãn bằng khóa chống trùng, không thu phí trả hàng hai lần. Phí và trách nhiệm trả hàng tuân theo chính sách hiện có.
- Có trạng thái thể hiện tạo nhãn lỗi, đang trả, đã về seller hoặc giao trả thất bại để hồ sơ không bị hiển thị như đã hoàn tất khi chưa có kết quả.

### Phần admin và tương tác giữ nguyên

- Giữ nguyên các kết quả phán quyết và workflow hoàn trả hiện tại; chỉ mở rộng nhánh A2 nêu trên.
- Giữ nguyên khiếu nại, bằng chứng và đánh giá seller sau giao hàng.
- Không thêm chat.

## 4. Thông báo và email

### N1. Bổ sung thông báo cho các sự kiện còn thiếu

- Giữ các email/thông báo hiện có cho thanh toán thành công, giao hàng, refund, tranh chấp, đổi địa chỉ và nợ.
- Bổ sung thông báo/email thanh toán thất bại cần buyer hành động; phân biệt kết quả thất bại đã xác định với giao dịch đang xác minh, không báo thất bại khi chưa biết kết quả.
- Bổ sung thông báo/email khi buyer yêu cầu hủy đơn; gửi tới seller cần xử lý và xác nhận kết quả tiếp nhận cho buyer phù hợp với trường hợp hủy ngay hoặc chờ duyệt hiện có.
- Bổ sung thông báo/email khi tạo yêu cầu trả hàng, duyệt hoặc từ chối trả hàng; bên nhận đúng với người cần hành động, có mã đơn, lý do và bước tiếp theo. Bao gồm nhánh admin yêu cầu trả hàng A2 khi áp dụng.
- Bổ sung thông báo/email kết quả payout cho seller: hoàn tất hoặc ngân hàng giả lập trả lại tiền; trạng thái và số tiền dựa trên kết quả đã lưu.
- Dùng outbox và cơ chế retry hiện có; lỗi gửi thư không làm thất bại giao dịch chính.
- Chống trùng theo sự kiện/lần thanh toán/yêu cầu/payout tương ứng. Retry hoặc callback trùng không tạo thông báo trùng; các lần thất bại hoặc yêu cầu khác nhau vẫn có thông báo riêng khi cần.
- Nội dung dễ hiểu, dẫn tới đúng đơn/yêu cầu/giao dịch nếu có liên kết hợp lệ; không lộ thông tin nhạy cảm hoặc dữ liệu của tài khoản khác.

## 5. Danh sách công việc đã duyệt

Yêu cầu chi tiết bốn task ưu tiên B2, B1, A1 và S1: xem [FOUR_TASK_REQUIREMENTS_2026-10-17.md](FOUR_TASK_REQUIREMENTS_2026-10-17.md). Ba task S2, A2 và N1 vẫn thuộc phạm vi đã duyệt, không bị loại khỏi backlog.

| Mã | Chức năng cần bổ sung |
| --- | --- |
| B1 | Buyer CRUD địa chỉ, chọn mặc định và tính lại phí ship/tổng tiền khi đổi địa chỉ checkout. |
| B2 | Buyer có bảng lịch sử thanh toán/refund và tổng hợp tiền rõ ràng. |
| S1 | Seller có báo cáo tài chính trực quan theo thời gian. |
| S2 | Seller tổng hợp và trả lời feedback theo từng sản phẩm. |
| A1 | Admin tra cứu đơn và giao dịch phục vụ hỗ trợ. |
| A2 | Admin yêu cầu trả hàng mới, tạo vận đơn trả, chờ hàng về và tiếp tục phán quyết. |
| N1 | Bổ sung thông báo/email thanh toán thất bại, yêu cầu hủy, yêu cầu/duyệt/từ chối trả hàng và kết quả payout. |

Các đề xuất khác như COD, catalog đầy đủ, quản trị người dùng, mở rộng email ngoài N1 hoặc thay đổi thời hạn demo không tự động được duyệt qua tài liệu này. Những chức năng đã tồn tại ngoài bảy mục bổ sung tiếp tục giữ theo nghiệp vụ hiện tại; các khoảng cách cũ trong tài liệu khác không mặc nhiên trở thành công việc của đợt này.

Khi triển khai bảy mục trên, bổ sung migration nếu cần và giữ dữ liệu hiện có; kiểm thử các phép tính tiền, quyền sở hữu, bộ lọc báo cáo, trả lời đúng đánh giá, việc thay đổi địa chỉ không sửa đơn cũ, luồng trả hàng do admin yêu cầu và phát/gửi lại thông báo không trùng.
