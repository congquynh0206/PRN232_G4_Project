# Chạy dự án checkout & giao hàng (nhóm 4)

**Đọc trước khi phát triển (18/10/2026):** [mục lục tài liệu](README.md), [đặc tả mục tiêu phiên bản 2.0](BUSINESS_REQUIREMENTS.md) và [backlog triển khai](IMPLEMENTATION_BACKLOG.md). Trang này mô tả cách chạy và cấu hình/source hiện có; ghi nhận migration/test bên dưới là theo từng đợt, không chứng minh các thay đổi mới đã triển khai.

**Khoảng cách cấu hình/workflow cần nhớ:** mục tiêu return Development là 300 giây từ outbound Delivered, nhưng source hiện còn kiểm tra hardcode 7 ngày; `Returns:ReceiptSeconds = 45` là thời gian kiểm tra hàng trả về, không đổi khóa đó thành 300. Chưa có nhánh đầy đủ cho deadline từ chối return/INR, ReattemptScheduled, free returns theo item, hạn mức riêng admin hoặc địa chỉ chờ đủ xác nhận. Payout Level 3 còn shortcut Completed cần bỏ theo PAY1. Chỉ hướng dẫn dùng giá trị/API mới như chức năng chạy được sau khi task tương ứng hoàn thành và cập nhật tài liệu này.

Dự án chạy hai ứng dụng .NET 8: API ở `http://localhost:5251` và giao diện ở `http://localhost:5131`. Ứng dụng đọc user, sản phẩm, tồn kho, địa chỉ và coupon từ SQL Server; frontend không tự tạo dữ liệu khi mở trang. Người dùng đăng nhập bằng tài khoản Development và được chuyển tới trang buyer, seller, shipper hoặc admin tương ứng.

Xem cách chia các project tại [BACKEND_ARCHITECTURE.md](BACKEND_ARCHITECTURE.md), đăng nhập/phân quyền tại [AUTHORIZATION_AND_ROLE_PAGES.md](AUTHORIZATION_AND_ROLE_PAGES.md), tài chính tại [SELLER_FINANCE.md](SELLER_FINANCE.md), và thương lượng tranh chấp tại [DISPUTES.md](DISPUTES.md).

SQL Server là nguồn dữ liệu khi chạy ứng dụng. EF Core InMemory chỉ còn được dùng bên trong kiểm thử tự động, không dùng để khởi tạo dữ liệu cho giao diện.

Giỏ buyer hiện random từ đúng hai seller, lưu theo tài khoản và checkout thành các đơn riêng. Cần migration `AddBuyerCartCheckoutBatch` và seed thêm seller thứ hai trước khi dùng; xem [MULTI_SELLER_CART.md](MULTI_SELLER_CART.md).

Đổi địa chỉ đơn đã thanh toán cần migration `AddOrderAddressChanges`. Buyer gửi yêu cầu, seller duyệt trong mức phí chấp nhận và shipper xác nhận địa chỉ mới trước tracking; xem [ORDER_ADDRESS_CHANGES.md](ORDER_ADDRESS_CHANGES.md).

## Chạy với SQL Server

Đăng ký seller thử nghiệm và write-off/blacklist tự động cần migration `20261015041522_AddSellerRegistrationAndAutomaticWriteOff`. Sau khi nâng cấp, trang đăng nhập có liên kết đăng ký seller; admin có tab Nợ khó thu / Blacklist. Khóa HMAC Development tự giữ trong `backend/G4.Api/.local-security/`, cần giữ nguyên cùng database. Xem [SELLER_REGISTRATION_WRITE_OFF.md](SELLER_REGISTRATION_WRITE_OFF.md).

Quản lý nợ seller cần migration `20261014102533_AddSellerDebtManagement`. Development dùng hạn trả 60 giây và cảnh báo trước 15 giây; môi trường khác mặc định 7 ngày. Seller trả nợ tại tab Tài chính; admin có tab Khoản nợ seller. Xem [SELLER_DEBT.md](SELLER_DEBT.md).

1. Tạo một database **trống** bằng [database/baseline.sql](../database/baseline.sql) trong SSMS hoặc `sqlcmd`. Script này chứa `CREATE DATABASE CloneEbayDB`; **không chạy lại** trên database đã có bảng/dữ liệu.
2. Cấu hình `ConnectionStrings__DefaultConnection` trong môi trường theo SQL Server của bạn. Mẫu cấu hình trong `backend/G4.Api/appsettings.json` dùng SQL Server Docker qua cổng `14333`; SQL Server cài trực tiếp thường có server/cổng khác.
3. Từ thư mục gốc chạy `dotnet ef database update --project backend/G4.Infrastructure --startup-project backend/G4.Api`. Migration cũ `InitialCreate` không tạo bảng; bảng nền phải đến từ bước 1. Các migration tiếp theo thêm checkout, giao hàng và tài chính seller. Có thể xem [database/checkout-shipping-migration.sql](../database/checkout-shipping-migration.sql) và [database/seller-finance-migration.sql](../database/seller-finance-migration.sql) nếu muốn duyệt SQL trước.

   Giao diện quản lý dùng `OrderTable.UpdatedAt` và quy trình tranh chấp mới cần migration `AddDisputeWorkflow`. Với cơ sở dữ liệu đã có, dừng API và chạy lại lệnh migration trên trước khi khởi động API mới. `AddOrderUpdatedAt` điền thời điểm ban đầu từ `OrderDate`; `AddDisputeWorkflow` thêm lịch sử bằng chứng và trạng thái thương lượng. SQL để duyệt: [dispute-workflow-migration.sql](../database/dispute-workflow-migration.sql).
4. Chạy [database/seed-data.sql](../database/seed-data.sql) để thêm/cập nhật buyer, seller, shipper, admin, store, 5 sản phẩm, tồn kho, địa chỉ và coupon `G4SAVE10`. Script có thể chạy lại an toàn: không tạo bản ghi trùng và không đặt lại số lượng tồn kho đã bị đơn hàng trừ.
5. Chạy `powershell -ExecutionPolicy Bypass -File scripts/start-mailhog.ps1` để khởi động MailHog Windows (lần đầu tải bản v1.0.1 từ GitHub chính thức vào thư mục `artifacts/mailhog`, cần Internet). Nếu dùng Docker, mở Docker Desktop và chạy `docker compose up -d --no-deps mailhog` thay thế. Hộp thư thử nghiệm ở `http://localhost:8025`.
6. Chạy API: `dotnet run --project backend/G4.Api --urls http://localhost:5251`.
7. Chạy frontend ở cửa sổ khác: `dotnet run --project frontend --urls http://localhost:5131`.

Với SQL Server cài trực tiếp và Windows Authentication, có thể chạy dữ liệu tham chiếu bằng:

```powershell
sqlcmd -S localhost -E -C -i database/seed-data.sql
```

Nếu đã có `CloneEbayDB` từ trước, kiểm tra các bảng nền có khớp [database/baseline.sql](../database/baseline.sql) trước khi chạy migration; sao lưu dữ liệu trước khi thay schema.

## Chạy bằng Visual Studio

1. Mở `G4_Project.sln` và đợi Visual Studio restore các project.
2. Chọn **Configure Startup Projects** → **Multiple startup projects**.
3. Đặt `G4.Api` và `frontend` thành **Start**, chọn profile `https` cho cả hai. Frontend Development gọi API tại `https://localhost:7087` để tránh HTTP tự chuyển hướng làm mất header Bearer; các project Domain/Application/Contracts/Infrastructure để **None**. Profile HTTP vẫn dùng được nếu chọn `http` cho cả hai, khi đó mở frontend `http://localhost:5131` và Swagger `http://localhost:5251/swagger`.
4. Mở **Manage User Secrets** của project `G4.Api` và đặt `ConnectionStrings:DefaultConnection`.
5. Chạy [database/seed-data.sql](../database/seed-data.sql) trong SSMS nếu database chưa có dữ liệu tham chiếu.
6. Bấm đúp `start-mailhog.cmd` trong thư mục gốc dự án để bật MailHog và mở hộp thư, không cần gõ lệnh. Có thể tạo shortcut của file này trên Desktop. Nếu dùng MailHog Docker có sẵn, mở Docker Desktop → Containers → Start container `mailhog` thay thế. Chỉ cần bật một trong hai cách.
7. Nhấn Start. Với profile HTTPS, giao diện mở tại `https://localhost:7018`, Swagger ở `https://localhost:7087/swagger`; xem thư tại `http://localhost:8025`. Nếu chọn profile HTTP cho cả hai, dùng `http://localhost:5131` và `http://localhost:5251/swagger`.

Đăng nhập bằng một trong bốn email `buyer@example.test`, `seller@example.test`, `shipper@example.test`, `admin@example.test`; mật khẩu Development dùng chung là `G4@123456`.

Sau khi đổi cấu trúc, project startup backend là `backend/G4.Api/G4.Api.csproj`; cấu hình trỏ tới `backend/backend.csproj` cũ không còn hợp lệ.

## PayPal Sandbox và dữ liệu thử

Đặt `PayPal__ClientId` và `PayPal__ClientSecret` từ ứng dụng **Sandbox** của PayPal bằng biến môi trường hoặc .NET User Secrets của project `G4.Api`. `UserSecretsId` cũ được giữ nguyên nên secret đã lưu trên máy vẫn dùng được. Không ghi secret vào source. Tài khoản buyer sandbox dùng ở trang PayPal khi được chuyển hướng. Nếu chưa có credential, nút PayPal sẽ báo chưa cấu hình; các luồng thẻ và giao hàng vẫn chạy được.

Credit Card ở đây chỉ là mô phỏng: `4111111111111111` + `12/30` thành công; `4000000000000002` + `12/30` bị từ chối; tháng/năm đã qua trả về hết hạn. Không nhập thẻ thật. Seller tạo vận đơn, xử lý hủy/trả hàng và hoàn tiền trên trang **Seller**; shipper phát các mốc tracking trên trang **Shipper**. Buyer/seller/admin có tab **Email thông báo**; seller/admin có **Nhật ký tích hợp**. Development mặc định gửi SMTP tới MailHog tại `127.0.0.1:1025`; khi chạy API bằng Docker Compose, host được đổi thành `mailhog`. Thư được giữ trong MailHog để test, không tự gửi tới Gmail. Có thể đặt `Mail__Mode=Capture` để chỉ lưu trong app khi không chạy MailHog; xem [hướng dẫn test email/nhật ký](EMAIL_INTEGRATION_TEST_GUIDE.md).

Code email/nhật ký mới cần migration `20261005062401_AddEmailIntegrationDiagnostics` sau `AddPromotions`, duyệt SQL tại [email-integration-migration.sql](../database/email-integration-migration.sql). Đợt 05/10 đã kiểm thử trên database QA riêng; triển khai database chính phải được duyệt trước khi chạy migration. Đừng khởi động API phiên bản mới trên schema cũ.

Tab **Tài chính người bán** hiển thị ví khả dụng, tiền đang xử lý/đang giữ/số dư âm, level và lịch sử rút tiền. Level 1/2 bắt đầu giữ từ giao thành công: Development 30/15 giây, môi trường khác 21/7 ngày. Level 3 Instant tự cộng Net sau phí/bù nợ vào ví khi payment thành công, không cần payout. Payout chỉ dùng để rút ra ngân hàng giả lập. Admin có tab **Phí nền tảng** để đặt mức phí, lịch hiệu lực và xem lịch sử; mặc định 12% SellerGross + 0,30 USD. Cần migration `20261014051607_AddPlatformFeePoliciesAndDeliveryHolds`; xem [SELLER_FINANCE.md](SELLER_FINANCE.md). Settlement cũ giữ lịch và phí trước nâng cấp. Worker cũng tự tạo đối soát cho payment/refund cũ sau khi nâng database.

Buyer và seller có tab **Tranh chấp** để thương lượng với mô tả và link bằng chứng bắt buộc, không sửa/xóa sau khi gửi. Admin chỉ xử lý hồ sơ đã chuyển lên. Development dùng **45 giây** cho phản hồi seller, phản hồi buyer và xác nhận hàng trả; worker chạy mỗi 5 giây. Xem quy tắc đầy đủ tại [DISPUTES.md](DISPUTES.md).

Phán quyết admin có preview tài chính, chọn căn cứ, kết luận trách nhiệm và hoàn toàn bộ/một phần cần migration `20261014091619_AddAdminDisputeDecisions`. Xem [ADMIN_DISPUTE_DECISIONS.md](ADMIN_DISPUTE_DECISIONS.md); báo giá buyer đã xác nhận và các quyết định cũ giữ nguyên. Sau migration khởi động lại API/frontend.

## Tracking hai chiều

Trong trang **Buyer** hoặc **Seller**, mở chi tiết đơn để xem các khối vận đơn:

- **Giao tới buyer (Seller → Buyer):** mã vận đơn chiều đi, trạng thái và timeline chỉ chứa sự kiện của vận đơn này. Chưa có vận đơn thì hiển thị trạng thái chờ tạo. `Delivered` được ghi là **Buyer đã nhận hàng**.
- **Trả về seller (Buyer → Seller):** xuất hiện khi đã có vận đơn trả sau khi seller duyệt yêu cầu. Có mã, trạng thái và timeline riêng; `Delivered` được ghi là **Seller đã nhận hàng trả**. Chưa yêu cầu trả hoặc chưa có vận đơn trả thì không xuất hiện khối này.
- **Giao thất bại và chuyển hoàn:** nằm trong khối giao tới buyer, với các mốc **Đang chuyển hoàn về seller → Seller đã nhận hàng chuyển hoàn**. Đây là hàng chưa giao thành công, khác với buyer đã nhận rồi yêu cầu trả.

Seller thao tác tạo vận đơn và xác nhận nhận hàng trả trong chi tiết đơn. Shipper cập nhật các mốc vận chuyển trên trang riêng. Quyết định duyệt/từ chối yêu cầu, hủy đơn và hoàn tiền nằm ở **Thao tác đơn hàng**. Kết quả hoàn tiền hiển thị riêng, không được tính là một mốc vận chuyển.

Nếu tạo vận đơn trả thất bại, seller bấm **Thử tạo vận đơn trả lại** trong khối trả về seller. API `POST /api/seller/returns/{id}/ship` chỉ xử lý yêu cầu đã duyệt, dùng lại vận đơn và khóa tạo nhãn cũ; không tạo lại vận đơn chiều đi.

Tracking hai chiều dùng `ShippingInfo.Direction` và `ShippingEvent.ShippingInfoId`. Phần kg, receipt deadline và shipper nhận vận đơn cần migration `AddReturnWeightAndShipmentClaims`; chạy `dotnet ef database update` và seed mới trước khi dùng. Xem hướng dẫn chuẩn bị và test tại [RETURNS_AND_SHIPPING.md](RETURNS_AND_SHIPPING.md).

Trả hàng theo lý do cần migration `20261014061325_AddReasonBasedReturnPricing`. Buyer chọn lý do và xem tiền hoàn trước khi xác nhận: lỗi seller hoàn toàn bộ khoản buyer còn được nhận và seller chịu phí trả; đổi ý/lý do buyer không hoàn phí chiều đi, trừ phí trả khỏi tiền hàng. Báo giá được lưu cố định, vận đơn/refund retry không ghi phí hai lần; yêu cầu cũ giữ chính sách cũ. Xem [RETURN_PRICING.md](RETURN_PRICING.md) và SQL [return-pricing-migration.sql](../database/return-pricing-migration.sql). Dừng API, chạy migration rồi khởi động lại API/frontend mới.

Kiểm tra giao diện bằng các kịch bản:

1. Buyer đặt và thanh toán bằng thẻ giả lập, seller tạo vận đơn: chỉ có khối giao tới buyer. Shipper phát tracking đến `Delivered` và kiểm tra nhãn **Buyer đã nhận hàng**.
2. Buyer yêu cầu trả, seller duyệt: xuất hiện khối trả về seller. Shipper nhận riêng vận đơn trả rồi phát tracking; timeline chiều đi không thay đổi. Sau `Delivered`, seller xác nhận hàng hợp lệ để hoàn ngay hoặc chờ 45 giây Development để tự hoàn; hai timeline và kết quả hoàn tiền vẫn hiển thị riêng.
3. Với một đơn mới, mô phỏng `DeliveryFailed → ReturningToSender → ReturnedToSeller`: chỉ có timeline chiều đi kèm giải thích chuyển hoàn, không sinh khối yêu cầu trả hàng.
4. Đăng nhập lần lượt bằng buyer, seller, shipper và admin; kiểm tra mỗi tài khoản chỉ thấy trang và thao tác của role đó. Kiểm tra cả màn hình điện thoại và desktop.

## Kiểm thử

```powershell
dotnet build G4_Project.sln
dotnet run --project tests/G4.Commerce.Tests
node --test tests/*.test.cjs
./tests/smoke-commerce.ps1
```

`smoke-commerce.ps1` yêu cầu cả API và giao diện đang chạy trên database đã chạy `seed-data.sql`, sau đó tạo đơn thử mới. Luồng PayPal thật cần credential sandbox và buyer test account nên không có trong smoke tự động; unit test dùng gateway giả để kiểm tra timeout/đối soát mà không gọi PayPal.

Kiểm thử console còn xác nhận tính phí, idempotency settlement/payout, fund hold, payout, refund tạo số dư âm và giao dịch mới tự bù nợ.



Tab **Cấp seller** dùng đánh giá đơn mua thực tế, dữ liệu giao đúng hạn và tỷ lệ hủy/trả/tranh chấp; worker tự xét mỗi giờ, Development 15 giây. Buyer có nút **Đánh giá seller** trong đơn Delivered/Closed đã thanh toán; seller yêu cầu xét lại và admin xem lịch sử. Màn mô phỏng Development có sáu tình huống và không ghi dữ liệu tài chính. Cần migration `20261015102627_AddSellerLevelsAndOrderFeedback` trước khi chạy API mới; xem [SELLER_LEVELS.md](SELLER_LEVELS.md). Migration chỉ được kiểm thử trên DB QA riêng, không tự áp dụng lên CloneEbayDB.


PayPal checkout hiện thanh toán cả giỏ bằng một lần xác nhận, lưu capture riêng từng đơn. Seller có tab Trả hàng / Hoàn tiền; các role nhận cập nhật qua SignalR và modal đã thu gọn. Cần migration `20261015161703_AddPayPalCartAndRealtime`, khởi động lại API/frontend và tải lại trang. Xem [COMMERCE_REFRESH.md](COMMERCE_REFRESH.md) cho cấu hình URL/CORS local, HTTPS, Docker và kịch bản kiểm thử.

## Feedback sản phẩm

Seller có tab **Đánh giá sản phẩm** để tổng hợp, lọc và trả lời đánh giá theo từng sản phẩm. Buyer mở **Đánh giá sản phẩm & phản hồi** trong chi tiết đơn đã trả tiền; được gửi sau Delivered/Closed và xem phản hồi từ shop. Chức năng dùng Review riêng, không thay đổi seller level. Cần migration **20261017042643_AddProductFeedbackReplies**; xem [PRODUCT_FEEDBACK.md](PRODUCT_FEEDBACK.md). Đợt này chỉ nâng database QA riêng, chưa nâng database chính.

## Bổ sung thông báo N1

Email thông báo có thêm thanh toán thất bại, hủy ngay/yêu cầu hủy, yêu cầu/duyệt/từ chối trả hàng và kết quả payout. Email mới chỉ hiển thị cho đúng tài khoản nhận hoặc admin; outbox và retry tiếp tục dùng cơ chế hiện có. Cần migration **20261017050657_CompleteBusinessNotifications**. Xem [NOTIFICATION_COMPLETION.md](NOTIFICATION_COMPLETION.md) để demo, chạy test và xem phạm vi A2 còn riêng. Migration mới chỉ được áp dụng lên database QA riêng trong đợt này.
