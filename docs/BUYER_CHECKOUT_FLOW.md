# Luồng màn hình checkout buyer

Đối chiếu source tại commit `e7997b03bd86bb5160332dd94bd5dc853b441963` ngày
25/10/2026. Đây là luồng đang có trong source, không phải đề xuất màn hình mới.

## Mở và đọc sơ đồ

1. [Checkout: bấm xong đi màn nào?](../.archify/workflow-buyer-checkout-20261025-160506/01-checkout.html)
2. [Thẻ thử: các kết quả và thao tác tiếp](../.archify/workflow-buyer-checkout-20261025-160506/02-card.html)
3. [PayPal cả giỏ: quay về và các nhánh](../.archify/workflow-buyer-checkout-20261025-160506/03-paypal.html)

Mở HTML bằng trình duyệt. Đọc theo mũi tên và điều kiện trên từng nhánh; các hàng tách
luồng chính, chờ xử lý và lỗi. Bấm node/SRC để xem chi tiết và dẫn chứng source.
Nguồn JSON và script tạo candidate nằm cạnh HTML. Nội dung tiếng Việt; một số điều
khiển Archify dùng tiếng Anh.

## Điều hướng thực tế

`Giỏ hàng` → `Thanh toán` → bấm `Đặt hàng & thanh toán` → tạo đơn từng seller →
`Đơn của tôi` → thực hiện phương thức đã chọn. Ba phần đầu là tab trong `/Buyer`.
Giỏ bị xóa khi giao dịch tạo đơn commit; thanh toán lỗi không tự khôi phục giỏ.

| Tình huống | UI tiếp theo / thao tác |
| --- | --- |
| Thiếu địa chỉ hoặc báo giá hợp lệ | Ở Thanh toán; bổ sung địa chỉ hoặc lấy báo giá. |
| Tạo đơn trả 400/409 | Ở Thanh toán; reset báo giá, tải giỏ và xác nhận giá mới. |
| Không nhận được kết quả tạo đơn | UI hướng dẫn kiểm tra Đơn của tôi hoặc thử lại giỏ hiện tại; chưa kết luận server đã tạo hay chưa. |
| Đã nhận danh sách đơn, bước tiếp theo gặp lỗi | UI cố mở/tải Đơn của tôi, hướng dẫn tiếp tục thanh toán từ các đơn đã tạo. |
| Thẻ thử: tất cả thành công | Thông báo thành công, tải danh sách, mở dialog chi tiết đơn đầu tiên. |
| Thẻ thử: tất cả chưa xác nhận thành công hoặc kết quả trộn | Thông báo mã đơn cần xử lý, vẫn tải danh sách và mở đơn đầu tiên; phải xem đúng đơn chưa trả. |
| PayPal Pending và có approval URL | Rời ứng dụng sang trang PayPal để duyệt hoặc hủy. |
| PayPal quay về qua return/cancel | Về Buyer, chọn Đơn của tôi và hiển thị khối kết quả PayPal; bấm Xem đơn để mở dialog. |
| Lượt PayPal tạo xong nhưng không redirect | Hiển thị kết quả trong khối PayPal và đọc trạng thái server. |
| PayPal khởi tạo lỗi hoặc kết quả chưa rõ | Hiển thị lỗi và nút Kiểm tra lại lượt PayPal; các đơn đã tạo còn trong Đơn của tôi. |

## Kết quả PayPal và quyền làm tiếp

| Trạng thái UI | Thao tác được hiển thị |
| --- | --- |
| Creating | Kiểm tra kết quả; hủy lượt theo điều kiện server. |
| Pending | Kiểm tra; tiếp tục xác nhận khi có URL; hủy lượt nếu còn được phép. |
| Verifying | Kiểm tra/chờ reconcile; chưa hủy hoặc thanh toán lại. |
| Succeeded | Xem từng đơn, tiếp tục theo dõi giao hàng. |
| Failed | Thanh toán các đơn còn lại nếu còn đơn chờ trả. |
| PartiallyPaid | Giữ đơn đã trả; thanh toán phần còn lại nếu còn đơn chờ. |
| Cancelled | Hủy lượt PayPal không tự hủy đơn; có thể trả các đơn còn lại nếu còn hợp lệ. |

HTTP capture trả thành công không chứng minh mọi payment đã Succeeded. Khối PayPal đọc
trạng thái lượt và từng đơn. Tương tự, lỗi request thẻ chưa chứng minh payment thất bại;
cần tải lại trạng thái server trước khi quyết định xử lý.

Đơn Paid/Preparing có thể yêu cầu hủy và chờ seller xử lý. Đơn AwaitingPayment có thao
tác thử thanh toán/hủy theo điều kiện server. Worker hết hạn đơn chờ khi quá deadline,
riêng PayPal Verifying được đối soát trước. Sơ đồ dừng ở sau checkout/thanh toán, chưa
triển khai sơ đồ chi tiết giao hàng, trả hàng và tranh chấp.

## Đối chiếu source

- `frontend/wwwroot/js/buyer-cart.js`: `recalculate`, `pay`, chuyển tab và catch lỗi.
- `frontend/wwwroot/js/buyer.js`: `actions`, `showOrder`, retry và mở dialog.
- `frontend/wwwroot/js/paypal-cart.js`: khối kết quả, điều kiện nút, redirect và reconcile.
- `frontend/Controllers/BuyerController.cs`: PayPal return/cancel.
- `frontend/wwwroot/js/role-navigation.js`: chọn tab orders từ query string.
- `backend/G4.Infrastructure/Checkout/BuyerCartService.cs`: checkout batch và retry.
- `backend/G4.Infrastructure/Checkout/CheckoutService.cs`: card và expire pending.
- `backend/G4.Infrastructure/Payments/PayPalCartPaymentService.cs`: kết quả và hủy lượt.

## Kiểm chứng ngày 25/10/2026

Cả ba sơ đồ đạt Archify `finalize`: validate, deliver, check và browser-check. Chrome
kiểm tra viewport 1440×900, 1600×1000, 1920×1080, 2048×1320 và sáng/tối theo quy trình
của công cụ. Receipt nằm cạnh HTML; receipt cuối của PayPal nằm trong `review-2`.
Chưa đánh giá ảnh bằng mắt hoặc chạy E2E ứng dụng với SQL/PayPal thật. Không sửa code
nghiệp vụ, không tạo/áp migration, không seed hoặc thay database.
