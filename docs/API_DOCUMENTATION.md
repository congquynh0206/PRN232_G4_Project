# Danh sách API

Dự án G4_Project (PRN232, nhóm 4). Tổng cộng 151 endpoint của `G4.Api`.

- Base URL: `http://localhost:5251` (Docker: `http://localhost:8080`)
- Swagger UI: `http://localhost:5251/swagger`, file OpenAPI: [api/openapi.json](api/openapi.json)
- Đăng nhập bằng `POST /api/auth/login`, rồi gửi header `Authorization: Bearer <token>` cho các API còn lại.
- Realtime: SignalR hub `/hubs/commerce`, sự kiện `CommerceChanged`.

## 1. Xác thực và đăng ký

| # | Method | Endpoint | Vai trò | Mô tả |
| --- | --- | --- | --- | --- |
| 1 | POST | `/api/auth/login` | Ẩn danh | Đăng nhập bằng email/mật khẩu, trả JWT 8 giờ |
| 2 | POST | `/api/auth/register-buyer` | Ẩn danh | Đăng ký buyer, trả JWT để đăng nhập ngay |
| 3 | POST | `/api/auth/register-seller` | Ẩn danh | Đăng ký seller thử nghiệm kèm định danh giả lập |
| 4 | GET | `/api/auth/seller-registration-samples` | Ẩn danh | Bộ dữ liệu định danh mẫu để điền form đăng ký seller |
| 5 | GET | `/api/seller/identity` | seller | Xem thông tin định danh (đã che) của seller |
| 6 | POST | `/api/seller/identity` | seller | Đổi thông tin định danh seller |
| 7 | GET | `/api/admin/seller-identities/{sellerId}` | admin | Xem định danh của một seller |
| 8 | GET | `/api/admin/seller-blacklist` | admin | Danh sách định danh bị blacklist (sau write-off). Query: `page`, `pageSize`, `sellerId` |

## 2. Catalog và sản phẩm

| # | Method | Endpoint | Vai trò | Mô tả |
| --- | --- | --- | --- | --- |
| 9 | POST | `/api/seller/products` | seller | Đăng sản phẩm mới |
| 10 | GET | `/api/seller/products` | seller | Sản phẩm của seller |
| 11 | POST | `/api/seller/products/{id}/payment-settings` | seller | Cài đặt Buy It Now / thanh toán ngay cho sản phẩm |
| 12 | GET | `/api/catalog/products` | buyer | Danh sách sản phẩm đang bán |
| 13 | GET | `/api/catalog/products/{id}` | buyer | Chi tiết một sản phẩm |
| 14 | GET | `/api/catalog/random` | buyer | Lấy ngẫu nhiên sản phẩm còn hàng (`count`) |

## 3. Giỏ hàng và tạo đơn

| # | Method | Endpoint | Vai trò | Mô tả |
| --- | --- | --- | --- | --- |
| 15 | GET | `/api/cart` | buyer | Xem giỏ hàng đã lưu |
| 16 | POST | `/api/cart/save` | buyer | Lưu số lượng, dòng hàng, địa chỉ của giỏ |
| 17 | POST | `/api/cart/random` | buyer | Random giỏ 2–5 sản phẩm từ hai seller |
| 18 | POST | `/api/cart/quote` | buyer | Báo giá giỏ theo từng seller (giảm giá, phí ship, tổng) |
| 19 | POST | `/api/cart/checkout` | buyer | Tạo nhóm đơn từ giỏ, mỗi seller một đơn (idempotent theo key) |
| 20 | POST | `/api/quote` | buyer | Báo giá một đơn lẻ |
| 21 | POST | `/api/orders` | buyer | Tạo một đơn lẻ (idempotent theo key) |

## 4. Thanh toán đơn

| # | Method | Endpoint | Vai trò | Mô tả |
| --- | --- | --- | --- | --- |
| 22 | POST | `/api/orders/{id}/pay/card` | buyer | Thanh toán đơn bằng thẻ giả lập |
| 23 | POST | `/api/orders/{id}/pay/paypal` | buyer | Tạo PayPal order cho một đơn, trả link duyệt |
| 24 | POST | `/api/orders/{id}/pay/paypal/{providerId}/capture` | buyer | Capture PayPal order sau khi buyer duyệt |
| 25 | POST | `/api/orders/{id}/pay/paypal/reconcile` | buyer | Đối soát thanh toán PayPal đang Verifying |
| 26 | POST | `/api/orders/{id}/pay/klarna` | buyer | Tạo phiên Klarna Hosted Payment Page, trả URL chuyển hướng |
| 27 | POST | `/api/orders/{id}/pay/klarna/reconcile` | buyer | Đối soát kết quả Klarna khi buyer quay về |
| 28 | POST | `/api/orders/{id}/pay/klarna/cancel` | buyer | Buyer hủy từ Klarna: tắt phiên HPP còn treo rồi đối soát, payment thành Failed để chọn cách khác |
| 29 | POST | `/api/seller/orders/{id}/refund` | seller | Seller hoàn tiền đơn |

## 5. Thanh toán ngay (Buy It Now)

| # | Method | Endpoint | Vai trò | Mô tả |
| --- | --- | --- | --- | --- |
| 30 | POST | `/api/buyer/immediate-checkouts` | buyer | Bắt đầu phiên Buy It Now / thanh toán ngay |
| 31 | GET | `/api/buyer/immediate-checkouts/{id}` | buyer | Xem trạng thái phiên thanh toán ngay |
| 32 | POST | `/api/buyer/immediate-checkouts/{id}/pay/card` | buyer | Thanh toán phiên bằng thẻ giả lập; chỉ tạo đơn sau khi capture |
| 33 | POST | `/api/buyer/immediate-checkouts/{id}/reconcile` | buyer | Đối soát phiên đang Verifying |

## 6. PayPal cho cả giỏ

| # | Method | Endpoint | Vai trò | Mô tả |
| --- | --- | --- | --- | --- |
| 34 | POST | `/api/buyer/paypal-checkouts` | buyer | Tạo phiên PayPal cho cả giỏ (nhiều đơn) |
| 35 | GET | `/api/buyer/paypal-checkouts/{id}` | buyer | Xem phiên PayPal giỏ hàng |
| 36 | POST | `/api/buyer/paypal-checkouts/{id}/capture` | buyer | Capture sau khi buyer duyệt trên PayPal |
| 37 | POST | `/api/buyer/paypal-checkouts/{id}/reconcile` | buyer | Đối soát phiên PayPal chưa rõ kết quả |
| 38 | POST | `/api/buyer/paypal-checkouts/{id}/cancel` | buyer | Hủy phiên PayPal chưa capture |

## 7. Đơn hàng

| # | Method | Endpoint | Vai trò | Mô tả |
| --- | --- | --- | --- | --- |
| 39 | GET | `/api/orders` | buyer, seller | Danh sách đơn của mình (không phân trang) |
| 40 | GET | `/api/orders/page` | buyer, seller | Danh sách đơn phân trang, lọc `filter`, `needsAction` |
| 41 | GET | `/api/orders/{id}` | buyer, seller, shipper | Chi tiết đơn; chỉ bên liên quan đến đơn được xem |
| 42 | POST | `/api/orders/{id}/cancel` | buyer | Hủy đơn chưa trả tiền hoặc gửi yêu cầu hủy đơn đã trả |
| 43 | POST | `/api/seller/orders/{id}/cancel-unpaid` | seller | Hủy đơn quá hạn thanh toán, cộng điểm phạt buyer |
| 44 | GET | `/api/buyer/unpaid-strikes` | buyer | Điểm phạt Unpaid Item của buyer |
| 45 | POST | `/api/seller/orders/{id}/prepare` | seller | Đánh dấu đang chuẩn bị hàng |
| 46 | POST | `/api/seller/orders/{id}/cancel/decision` | seller | Duyệt/từ chối yêu cầu hủy; duyệt thì refund |

## 8. Sổ địa chỉ và gợi ý địa chỉ

| # | Method | Endpoint | Vai trò | Mô tả |
| --- | --- | --- | --- | --- |
| 47 | GET | `/api/address-suggestions/regions` | buyer | Danh sách quốc gia, tỉnh/bang |
| 48 | GET | `/api/address-suggestions` | buyer | Gợi ý địa chỉ Việt Nam qua VietMap (`q`) |
| 49 | GET | `/api/address-suggestions/detail` | buyer | Chi tiết một gợi ý VietMap (`ref`) |
| 50 | GET | `/api/addresses` | buyer | Sổ địa chỉ của buyer |
| 51 | POST | `/api/addresses/create` | buyer | Thêm địa chỉ |
| 52 | POST | `/api/addresses/{id}/edit` | buyer | Sửa địa chỉ (đơn cũ giữ snapshot) |
| 53 | POST | `/api/addresses/{id}/default` | buyer | Đặt địa chỉ mặc định |
| 54 | POST | `/api/addresses/{id}/delete` | buyer | Xóa/lưu trữ địa chỉ |

## 9. Đổi địa chỉ đơn

| # | Method | Endpoint | Vai trò | Mô tả |
| --- | --- | --- | --- | --- |
| 55 | POST | `/api/orders/{orderId}/address-changes/quote` | buyer | Báo giá đổi địa chỉ giao hàng |
| 56 | POST | `/api/shipper/shipments/{id}/address-change/decision` | shipper | Shipper xác nhận có giao được địa chỉ mới |
| 57 | POST | `/api/orders/{orderId}/address-changes` | buyer | Gửi yêu cầu đổi địa chỉ theo báo giá đã xác nhận |
| 58 | POST | `/api/seller/address-changes/{id}/decision` | seller | Seller chấp thuận/từ chối đổi địa chỉ |
| 59 | POST | `/api/shipper/shipments/{id}/address-change/acknowledge` | shipper | Shipper xác nhận đã biết địa chỉ mới |

## 10. Vận chuyển

| # | Method | Endpoint | Vai trò | Mô tả |
| --- | --- | --- | --- | --- |
| 60 | POST | `/api/carrier/labels` | Nội bộ (header `X-Carrier-Key`) | Bộ giả lập hãng vận chuyển tạo nhãn; API tự gọi |
| 61 | GET | `/api/seller/shipping/settings` | seller | Cài đặt vận chuyển: cap đổi địa chỉ, khối lượng, địa chỉ lấy hàng. Query: `page`, `pageSize` |
| 62 | POST | `/api/seller/shipping/address-change-cap` | seller | Đổi mức seller chịu phí đổi địa chỉ |
| 63 | POST | `/api/seller/shipping/products/{id}/weight` | seller | Khai báo khối lượng sản phẩm (kg) |
| 64 | POST | `/api/seller/shipping/pickup` | seller | Đổi địa chỉ lấy hàng |
| 65 | POST | `/api/seller/orders/{id}/ship` | seller | Tạo vận đơn chiều đi (Outbound) |
| 66 | POST | `/api/carrier/fail-next` | seller | Cho lần tạo nhãn kế tiếp của đơn bị lỗi (giả lập) |
| 67 | POST | `/api/seller/orders/{id}/ship/simulate-failure` | seller | Tạo vận đơn và giả lập hãng vận chuyển lỗi |
| 68 | POST | `/api/shipments/{id}/events` | shipper | Ghi mốc tracking (PickedUp, InTransit, Delivered, giao lại...) |
| 69 | GET | `/api/shipper/shipments` | shipper | Vận đơn chờ nhận và vận đơn của mình |
| 70 | GET | `/api/shipper/shipments/page` | shipper | Vận đơn phân trang, lọc trạng thái/chiều/tìm kiếm. Query: `page`, `pageSize`, `filter`, `search`, `status`, `direction` |
| 71 | POST | `/api/shipper/shipments/{id}/claim` | shipper | Nhận vận đơn (nguyên tử, chỉ một shipper thắng) |

## 11. Trả hàng

| # | Method | Endpoint | Vai trò | Mô tả |
| --- | --- | --- | --- | --- |
| 72 | GET | `/api/seller/returns/page` | seller | Danh sách yêu cầu trả hàng. Query: `filter`, `page`, `pageSize` |
| 73 | POST | `/api/orders/{id}/returns` | buyer | Gửi yêu cầu trả hàng |
| 74 | GET | `/api/orders/{id}/returns/quote` | buyer | Báo giá tiền hoàn theo lý do (`reasonCode`) |
| 75 | POST | `/api/seller/returns/{id}/approve` | seller | Duyệt trả hàng, tạo vận đơn chiều trả |
| 76 | POST | `/api/seller/returns/{id}/ship` | seller | Thử tạo lại vận đơn trả |
| 77 | POST | `/api/seller/returns/{id}/approve/simulate-failure` | seller | Duyệt và giả lập lỗi tạo vận đơn trả |
| 78 | POST | `/api/seller/returns/{id}/reject` | seller | Từ chối trả hàng kèm lý do |
| 79 | POST | `/api/seller/returns/{id}/receive` | seller | Xác nhận hàng trả hợp lệ và hoàn tiền |
| 80 | POST | `/api/seller/returns/{id}/issue` | seller | Báo vấn đề với hàng trả, kèm bằng chứng |

## 12. Tranh chấp

| # | Method | Endpoint | Vai trò | Mô tả |
| --- | --- | --- | --- | --- |
| 81 | GET | `/api/disputes/page` | buyer, seller, admin | Danh sách tranh chấp (`filter`: open...) |
| 82 | GET | `/api/disputes/{id}` | buyer, seller, admin | Chi tiết tranh chấp và lịch sử bằng chứng |
| 83 | POST | `/api/orders/{id}/disputes` | buyer | Mở tranh chấp cho đơn |
| 84 | POST | `/api/disputes/{id}/evidence` | buyer, seller | Thêm mô tả/link bằng chứng |
| 85 | POST | `/api/disputes/{id}/proposal` | seller | Seller gửi phương án giải quyết |
| 86 | POST | `/api/disputes/{id}/response` | buyer | Buyer chấp nhận phương án hoặc chuyển admin |
| 87 | POST | `/api/disputes/{id}/resolution` | admin | Chốt phán quyết, hoàn toàn bộ/một phần |
| 88 | POST | `/api/disputes/{id}/decision-preview` | admin | Xem trước tác động tài chính của phán quyết |

## 13. Trả giá (Best Offer)

| # | Method | Endpoint | Vai trò | Mô tả |
| --- | --- | --- | --- | --- |
| 89 | GET | `/api/best-offers/catalog` | buyer | Sản phẩm nhận trả giá |
| 90 | GET | `/api/best-offers/buyer` | buyer | Các phiên trả giá của buyer |
| 91 | GET | `/api/best-offers/seller` | seller | Các phiên trả giá gửi tới seller |
| 92 | GET | `/api/best-offers/seller/products` | seller | Sản phẩm và ngưỡng auto-accept/decline |
| 93 | POST | `/api/best-offers` | buyer | Gửi đề nghị giá (chọn địa chỉ, cách thanh toán) |
| 94 | POST | `/api/best-offers/{id}/seller/accept` | seller | Seller chấp nhận giá |
| 95 | POST | `/api/best-offers/{id}/seller/decline` | seller | Seller từ chối |
| 96 | POST | `/api/best-offers/{id}/seller/counter` | seller | Seller trả giá lại |
| 97 | POST | `/api/best-offers/{id}/buyer/accept` | buyer | Buyer chấp nhận giá seller đưa |
| 98 | POST | `/api/best-offers/{id}/buyer/payment-profile` | buyer | Đổi hồ sơ thanh toán của phiên |
| 99 | POST | `/api/best-offers/{id}/buyer/decline` | buyer | Buyer từ chối |
| 100 | POST | `/api/best-offers/{id}/buyer/counter` | buyer | Buyer trả giá lại |
| 101 | POST | `/api/best-offers/seller/settings` | seller | Bật/tắt trả giá, đặt ngưỡng tự động |

## 14. Khuyến mãi

| # | Method | Endpoint | Vai trò | Mô tả |
| --- | --- | --- | --- | --- |
| 102 | GET | `/api/{role}/promotions/offers` | buyer (`role`=buyer) | Ưu đãi đang chạy hiển thị cho buyer. Query: `type`, `sellerId`, `page` |
| 103 | GET | `/api/{role}/promotions/page` | seller, admin (`role` khớp) | Danh sách khuyến mãi phân trang. Query: `page`, `pageSize`, `filter`, `search`, `type`, `sellerId`, `fundingSource` |
| 104 | GET | `/api/{role}/promotions/options` | seller, admin | Dữ liệu chọn (sản phẩm, danh mục, seller) |
| 105 | GET | `/api/{role}/promotions/{id}` | seller, admin | Chi tiết khuyến mãi |
| 106 | GET | `/api/{role}/promotions/{id}/audits` | seller, admin | Lịch sử thay đổi khuyến mãi |
| 107 | POST | `/api/{role}/promotions` | seller, admin | Tạo khuyến mãi (sale, volume, order discount, coupon) |
| 108 | POST | `/api/{role}/promotions/{id}/edit` | seller, admin | Sửa khuyến mãi |
| 109 | POST | `/api/{role}/promotions/{id}/state` | seller, admin | Bật/tắt khuyến mãi |
| 110 | POST | `/api/{role}/promotions/{id}/suspension` | admin | Đình chỉ/gỡ đình chỉ khuyến mãi của seller |

## 15. Đánh giá sản phẩm

| # | Method | Endpoint | Vai trò | Mô tả |
| --- | --- | --- | --- | --- |
| 111 | GET | `/api/seller/product-feedback/products` | seller | Tổng hợp đánh giá theo sản phẩm. Query: `Search`, `Rating`, `Reply`, `From`, `To`, `Page`, `PageSize` |
| 112 | GET | `/api/seller/product-feedback/products/{id}/reviews` | seller | Đánh giá của một sản phẩm. Query: `Search`, `Rating`, `Reply`, `From`, `To`, `Page`, `PageSize` |
| 113 | POST | `/api/seller/product-feedback/reviews/{id}/reply` | seller | Trả lời đánh giá |
| 114 | GET | `/api/orders/{id}/product-reviews` | buyer | Đánh giá sản phẩm của buyer trong đơn |
| 115 | POST | `/api/orders/{id}/products/{productId}/review` | buyer | Gửi đánh giá sản phẩm sau khi nhận hàng |

## 16. Tài chính seller

| # | Method | Endpoint | Vai trò | Mô tả |
| --- | --- | --- | --- | --- |
| 116 | GET | `/api/seller/finance` | seller | Tóm tắt ví: khả dụng, đang xử lý, đang giữ, âm, level |
| 117 | GET | `/api/seller/finance/funds/page` | seller | Tiền đang giữ theo từng đơn. Query: `page`, `pageSize` |
| 118 | GET | `/api/seller/finance/report` | seller | Báo cáo tài chính theo kỳ (`from`, `to`, `group`, `timeZone`) |
| 119 | GET | `/api/seller/finance/transactions/page` | seller | Sổ giao dịch ví. Query: `page`, `pageSize` |
| 120 | GET | `/api/seller/finance/payouts/page` | seller | Lịch sử rút tiền. Query: `page`, `pageSize` |
| 121 | POST | `/api/seller/finance/evaluate-level` | seller | Yêu cầu xét lại cấp seller |
| 122 | POST | `/api/seller/payouts` | seller | Yêu cầu rút tiền về ngân hàng giả lập |
| 123 | POST | `/api/orders/{orderId}/fund-hold` | buyer | Yêu cầu giữ tiền đơn đang tranh chấp |
| 124 | POST | `/api/orders/{orderId}/fund-hold/resolve` | admin | Giải quyết khoản giữ tiền |
| 125 | GET | `/api/admin/fund-holds` | admin | Danh sách khoản giữ tiền |
| 126 | GET | `/api/admin/fund-holds/page` | admin | Khoản giữ tiền phân trang. Query: `page`, `pageSize` |

## 17. Cấp seller

| # | Method | Endpoint | Vai trò | Mô tả |
| --- | --- | --- | --- | --- |
| 127 | POST | `/api/orders/{id}/seller-feedback` | buyer | Đánh giá seller (dùng để xét cấp) |
| 128 | GET | `/api/seller/level` | seller | Cấp hiện tại, số liệu và lịch sử xét cấp |
| 129 | GET | `/api/admin/seller-levels` | admin | Danh sách cấp seller. Query: `page`, `pageSize`, `sellerId`, `level` |
| 130 | GET | `/api/admin/seller-levels/{sellerId}` | admin | Chi tiết cấp của một seller |
| 131 | POST | `/api/dev/seller-levels/simulate` | seller, admin | Mô phỏng xét cấp, không ghi dữ liệu |

## 18. Nợ seller

| # | Method | Endpoint | Vai trò | Mô tả |
| --- | --- | --- | --- | --- |
| 132 | GET | `/api/seller/debt` | seller | Khoản nợ hiện tại và hạn trả |
| 133 | POST | `/api/seller/debt/repayments` | seller | Trả nợ |
| 134 | GET | `/api/admin/seller-debts` | admin | Danh sách seller có nợ (`state`) |
| 135 | GET | `/api/admin/seller-debts/{sellerId}` | admin | Chi tiết nợ của một seller |
| 136 | GET | `/api/seller/debt/history` | seller | Lịch sử nợ (`kind`: repayments...) |
| 137 | GET | `/api/admin/seller-debts/{sellerId}/history` | admin | Lịch sử nợ của một seller. Query: `kind`, `page`, `pageSize` |

## 19. Quản trị (admin)

| # | Method | Endpoint | Vai trò | Mô tả |
| --- | --- | --- | --- | --- |
| 138 | GET | `/api/admin/order-lookup` | admin | Tra cứu đơn/payment/refund theo nhiều bộ lọc. Query: `Search`, `Buyer`, `Seller`, `From`, `To`, `Status`, `PaymentStatus`, `RefundStatus`, `QuickFilter`, `Page`, `PageSize`, `OrderId`, `SearchType` |
| 139 | GET | `/api/admin/order-lookup/{orderId}` | admin | Chi tiết đơn kèm giao dịch |
| 140 | GET | `/api/admin/platform-fees` | admin | Chính sách phí nền tảng và lịch sử. Query: `page`, `pageSize` |
| 141 | POST | `/api/admin/platform-fees` | admin | Đặt mức phí mới có ngày hiệu lực |
| 142 | GET | `/api/admin/seller-write-offs` | admin | Các quyết định write-off nợ khó thu. Query: `page`, `pageSize`, `sellerId` |

## 20. Thông báo và nhật ký

| # | Method | Endpoint | Vai trò | Mô tả |
| --- | --- | --- | --- | --- |
| 143 | GET | `/api/me/notifications` | buyer, seller, shipper, admin | Thông báo chuông của mình. Query: `page`, `pageSize` |
| 144 | POST | `/api/me/notifications/{id}/read` | buyer, seller, shipper, admin | Đánh dấu một thông báo đã đọc |
| 145 | POST | `/api/me/notifications/read-all` | buyer, seller, shipper, admin | Đánh dấu tất cả đã đọc |
| 146 | GET | `/api/integration-logs` | admin, seller | Nhật ký gọi dịch vụ ngoài (PayPal, carrier, mail...). Query: `OrderId`, `Service`, `Outcome`, `From`, `To`, `Page`, `PageSize` |
| 147 | GET | `/api/notifications` | buyer, seller, shipper, admin | Email thông báo (người nhận chỉ thấy thư của mình). Query: `OrderId`, `EventType`, `Status`, `Page`, `PageSize` |
| 148 | GET | `/api/notifications/{id}` | buyer, seller, shipper, admin | Chi tiết một email thông báo |
| 149 | POST | `/api/admin/notifications/{id}/retry` | admin | Gửi lại email thất bại |
| 150 | GET | `/api/inbox` | admin | Hộp thư email đã ghi nhận |
| 151 | GET | `/api/inbox/page` | admin | Hộp thư email phân trang. Query: `page`, `pageSize` |
