# Tài liệu dự án G4_Project

Điểm đọc đầu tiên khi triển khai nghiệp vụ. Cập nhật ngày 18/10/2026 theo báo cáo đặc tả phiên bản 2.0 và quyết định của người dùng.

## Tài liệu có hiệu lực

| Cần biết | Đọc |
| --- | --- |
| Quy tắc mục tiêu và phạm vi đã chốt | [BUSINESS_REQUIREMENTS.md](BUSINESS_REQUIREMENTS.md) |
| Phần đã có/chưa có, task và nghiệm thu | [IMPLEMENTATION_BACKLOG.md](IMPLEMENTATION_BACKLOG.md) |
| Đối chiếu source/test thực tế ngày 19/10, lỗi đã tái hiện | [IMPLEMENTATION_AUDIT_2026-10-19.md](IMPLEMENTATION_AUDIT_2026-10-19.md) |
| Chạy API/frontend, database và cấu hình hiện tại | [PROJECT_RUN.md](PROJECT_RUN.md) |
| Phân lớp và nơi đặt code | [BACKEND_ARCHITECTURE.md](BACKEND_ARCHITECTURE.md) |
| Yêu cầu chi tiết B2/B1/A1/S1 vẫn còn hiệu lực | [FOUR_TASK_REQUIREMENTS_2026-10-17.md](FOUR_TASK_REQUIREMENTS_2026-10-17.md) |
| Quyết định bảy nhóm chức năng ngày 17/10 | [APPROVED_SCOPE_2026-10-17.md](APPROVED_SCOPE_2026-10-17.md) |

**Thứ tự ưu tiên:** yêu cầu người dùng mới nhất → BUSINESS_REQUIREMENTS → backlog/tiêu chí task → hướng dẫn module. Tài liệu ngày 17/10 giữ chi tiết chưa bị quyết định 18/10 thay thế. Đặc tả là hành vi cần làm; hướng dẫn chạy/module có thể mô tả source trước nâng cấp. Không coi một cấu hình/API được đề cập trong yêu cầu là đã có trong code.

## Hướng dẫn theo module

| Module | Tài liệu |
| --- | --- |
| Auth và quyền | [AUTHORIZATION_AND_ROLE_PAGES.md](AUTHORIZATION_AND_ROLE_PAGES.md) |
| Giỏ nhiều seller, PayPal/realtime | [MULTI_SELLER_CART.md](MULTI_SELLER_CART.md), [COMMERCE_REFRESH.md](COMMERCE_REFRESH.md) |
| Sổ địa chỉ buyer, bảo vệ đơn cũ và retry | [BUYER_ADDRESS_BOOK.md](BUYER_ADDRESS_BOOK.md) |
| Đổi địa chỉ đơn | [ORDER_ADDRESS_CHANGES.md](ORDER_ADDRESS_CHANGES.md) |
| Tracking/return và giá trả hàng | [RETURNS_AND_SHIPPING.md](RETURNS_AND_SHIPPING.md), [RETURN_PRICING.md](RETURN_PRICING.md), [RT1 deadline](RETURN_WINDOW_POLICY.md) |
| Dispute/phán quyết | [DISPUTES.md](DISPUTES.md), [ADMIN_DISPUTE_DECISIONS.md](ADMIN_DISPUTE_DECISIONS.md) |
| Tài chính, cấp, nợ, định danh | [SELLER_FINANCE.md](SELLER_FINANCE.md), [PAY1 payout](PAYOUT_WORKFLOW.md), [SELLER_LEVELS.md](SELLER_LEVELS.md), [SELLER_DEBT.md](SELLER_DEBT.md), [SELLER_REGISTRATION_WRITE_OFF.md](SELLER_REGISTRATION_WRITE_OFF.md) |
| Promotion | [PROMOTIONS_TEST_GUIDE.md](PROMOTIONS_TEST_GUIDE.md) |
| Review sản phẩm, thông báo | [PRODUCT_FEEDBACK.md](PRODUCT_FEEDBACK.md), [NOTIFICATION_COMPLETION.md](NOTIFICATION_COMPLETION.md) |
| Email/nhật ký | [EMAIL_INTEGRATION_TEST_GUIDE.md](EMAIL_INTEGRATION_TEST_GUIDE.md) |
| Admin tra cứu đơn và giao dịch | [ADMIN_ORDER_LOOKUP.md](ADMIN_ORDER_LOOKUP.md) |

Tài liệu trong `superpowers/plans` và `superpowers/specs` là kế hoạch theo thời điểm; không ưu tiên hơn đặc tả hiện hành. Báo cáo Word và ảnh QA trong `artifacts/` là bản xuất, không phải nguồn duy nhất để các lần code sau đọc.

## Quy tắc duy trì

- Khi có thay đổi nghiệp vụ được chốt: sửa đặc tả và backlog, rồi đồng bộ hướng dẫn module bị ảnh hưởng.
- Khi code xong: ghi task, test và môi trường/migration; không đánh dấu hoàn thành chỉ vì đã viết docs.
- Không đổi ghi nhận test/migration lịch sử thành kết quả của phiên làm việc mới. QA riêng không đồng nghĩa database chính đã nâng.
