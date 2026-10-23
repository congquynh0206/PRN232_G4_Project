# Đổi địa chỉ đơn — AD1

**Đã triển khai/nghiệm thu source ngày 20/10/2026** theo [đặc tả §6](BUSINESS_REQUIREMENTS.md#6-giao-hàng-và-đổi-địa-chỉ) và [backlog AD1](IMPLEMENTATION_BACKLOG.md#3-ad1--xác-nhận-địa-chỉ-trước-khi-áp-dụng). Migration mới chỉ được áp database QA riêng, **chưa áp database chính**. Không chạy binary mới trên schema cũ.

## Luồng xác nhận trước khi áp dụng

1. Buyer mở đơn Paid/Preparing/Shipping, trước OutForDelivery đầu tiên; nhập địa chỉ/lý do, bấm **Xem báo giá**. Server tính theo kg và vùng snapshot, không đọc địa chỉ hồ sơ đang chỉnh sửa.
2. Buyer xem địa chỉ cũ/mới, phí tuyến, phần seller chịu, cap, refund ship giả lập dự kiến và bên cần xác nhận; bấm **Xác nhận và gửi yêu cầu**. Chỉ lúc này request Pending được tạo, lưu BuyerUserId/BuyerConfirmedAt phía server.
3. Seller chấp thuận. Nếu đã có shipper nhận vận đơn, request thành **AwaitingShipper**; địa chỉ/tiền/bộ đếm chưa đổi. Nếu chưa có shipper, seller là bên duyệt cuối và áp dụng ngay, không chờ shipper tương lai.
4. Shipper đang sở hữu vận đơn chiều đi bấm **Có thể giao địa chỉ mới** hoặc **Không thể giao** kèm lý do. Chấp thuận cuối cùng kiểm tra lại toàn bộ điều kiện rồi mới áp dụng snapshot order/vận đơn, chi phí/refund và tăng bộ đếm atomic. Approved nghĩa là địa chỉ đã có hiệu lực.

Khi Pending/AwaitingShipper, địa chỉ cũ còn hiệu lực. Có thể tiếp tục PickedUp/InTransit theo địa chỉ cũ, nhưng **chặn OutForDelivery**; không tự từ chối yêu cầu chỉ vì shipper muốn bắt đầu giao. Return dùng workflow riêng, không bị chặn bởi yêu cầu chiều đi.

Tối đa một request đang chờ/đơn, tối đa **một lần thành công**. Từ chối hoặc Expired giữ nguyên địa chỉ, không thu/hoàn tiền hoặc tiêu lượt. Không đổi hồ sơ/default address của buyer. Không nhận yêu cầu khi có return/dispute đang xử lý hoặc đã bắt đầu giao. Đơn chưa thanh toán có thể hủy và checkout lại.

## Deadline, báo giá và cạnh tranh

**Giao diện 23/10/2026:** Buyer/Seller và thông báo địa chỉ Shipper hiển thị đồng hồ cùng ExpiresAt đã snapshot, không cộng lại 2 phút khi chuyển bên xác nhận. Hết hạn hiện chờ cập nhật, giữ địa chỉ cũ; quyết định vẫn do server kiểm tra. [Hướng dẫn đồng hồ](DEMO_COUNTDOWNS.md).

Mốc Development 2 phút từ 23/10/2026 áp dụng cho request mới. Request đã tạo giữ nguyên `ExpiresAt`, không tự kéo dài deadline 60 giây cũ. Không có migration hoặc cập nhật dữ liệu; khởi động lại API để chắc chắn nạp cấu hình mới.

- `AddressChanges:ConfirmationSeconds`: mặc định **86.400 giây (24 giờ)**, Development **120 giây (2 phút, cập nhật 23/10/2026)**, giới hạn cấu hình 1–604.800 giây. ExpiresAt UTC được snapshot lúc gửi yêu cầu; chuyển sang AwaitingShipper hoặc đổi cấu hình không gia hạn. Worker kiểm tra mỗi 5 giây; decision tại đúng hạn đã trả Expired. Khi bắt đầu giao sau hạn, server cũng hết hạn request trong transaction để dùng địa chỉ cũ mà không phải chờ worker.
- QuoteToken là fingerprint báo giá/địa chỉ/lý do/cap/revision/shipper/refund còn được hưởng; server luôn tính tiền lại, không nhận số tiền từ client. Thiếu token hoặc quote đã đổi trả 409 và cần xem lại báo giá. Quote không tạo request hoặc giữ lượt đổi.
- Trước áp dụng đọc lại trạng thái, snapshot, shipper đang gán, cap/revision, payment/refund và giá. Thay đổi quan trọng làm request **Rejected**, có lý do yêu cầu buyer xem quote và gửi lại; không dùng xác nhận cũ. Kể cả cap mới vẫn đủ hoặc cap đổi rồi đổi lại, revision làm mất hiệu lực xác nhận cũ.
- Cap update và claim tham gia cùng gate/transaction với quyết định đổi địa chỉ. Nếu chưa có shipper lúc buyer xác nhận nhưng claim trước seller approve, cần buyer xác nhận quote mới có shipper; nếu seller apply trước claim, người nhận vận đơn sau dùng địa chỉ có hiệu lực, không có bước ack mới.
- Request key unique; retry đúng request/quote trả cùng record kể cả sau khi áp dụng. Key với nội dung khác trả 409. Retry seller/shipper/worker không nhân refund, ledger, bộ đếm hoặc email. Shipper decision gửi RequestId, không ngầm chấp thuận một request mới hơn.

## Phí, refund và lịch sử

Seller đặt **Mức chịu thêm phí ship** trong Thiết lập giao hàng. Mặc định 0; tăng phí trong cap thì buyer không trả thêm, seller chịu tăng carrier cost qua ledger. Vượt cap thì từ chối, giữ tuyến cũ. Payment, tồn kho, promotion và platform fee snapshot gốc không bị tính lại.

Phí giảm chỉ hoàn phần ship buyer thực sự còn được hưởng: tối đa carrier savings, ship buyer đã trả trừ shipping-adjustment trước đó, và captured còn lại sau mọi refund. Free shipping qua promotion không tạo refund khoản ship chưa thu; payment 0 không phát refund tiền. Hoàn nội bộ dùng `SIM-SHIP-...`, có nhãn **giả lập**, không gọi PayPal refund thật. Hủy/return sau đó chỉ hoàn captured còn lại. Settlement giữ lịch mở khóa, không đảo subsidy/phí nền tảng chỉ vì đổi tuyến.

Request lưu WorkflowVersion, trạng thái, địa chỉ cũ/mới, quote, cap/revision, phí/refund, buyer/seller/shipper actor và thời điểm xác nhận, ExpiresAt/AppliedAt. Actor lấy từ tài khoản xác thực; timestamp UTC. Buyer/seller thấy địa chỉ chưa áp dụng và hạn chờ; shipper có quyết định khả năng giao. Seller “cần hành động” chỉ tính Pending; AwaitingShipper xem trong chi tiết, seller không duyệt lần hai.

Outbox mới dùng EventType ổn định AddressChangePending/AwaitingShipper/Approved/Rejected/Expired (mỗi tên có tiền tố AddressChange), EventKey `address-change:{requestId}:{status}:user:{userId}` và RecipientUserId. Email legacy giữ định danh/nội dung cũ. Buyer/seller nhận theo từng request/trạng thái/người nhận; AwaitingShipper gửi thêm cho shipper được gán. RecipientUserId giữ quyền xem đúng người nhận. Không gọi SMTP inline; transaction rollback cả địa chỉ/chi phí/refund/ledger/outbox/realtime. UI escape nội dung và giữ request key/quote khi lỗi mạng/5xx chưa rõ kết quả; lỗi 4xx cho lấy quote mới.

**Legacy:** request Approved trước nâng cấp (WorkflowVersion 0) giữ snapshot/kết quả/refund/lượt và actor chưa biết là null; không áp lại tiền hoặc suy diễn xác nhận. Ack lịch sử vẫn dùng endpoint cũ, không thể chấp thuận request mới. Legacy Pending được đặt IsPending và hạn `CreatedAt + 24 giờ`, giữ CreatedAt/Status/quote cũ; không được áp bằng xác nhận cũ, cần buyer xác nhận quote mới (sau từ chối/hết hạn). Không backfill AppliedAt/actor giả.

## API

| Endpoint | Nội dung |
| --- | --- |
| `POST /api/orders/{orderId}/address-changes/quote` | Buyer gửi `{address, reason, key}`, nhận token, địa chỉ cũ/mới, old/newShippingCost, sellerCap, buyerRefund, shipperId, withinCap |
| `POST /api/orders/{orderId}/address-changes` | Buyer gửi cùng body + `quoteToken` đã xác nhận |
| `POST /api/seller/address-changes/{requestId}/decision` | Seller gửi `{approve, reason}`; có thể trả AwaitingShipper hoặc Approved/Rejected/Expired |
| `POST /api/shipper/shipments/{shipmentId}/address-change/decision` | Shipper được gán gửi `{requestId, approve, reason}`; từ chối cần lý do |
| `POST /api/shipper/shipments/{shipmentId}/address-change/acknowledge` | Chỉ tương thích ack của Approved lịch sử; không duyệt request mới |
| `POST /api/seller/shipping/address-change-cap` | Seller cập nhật `{amount}`, server tăng revision nếu cap thay đổi |

Địa chỉ yêu cầu gồm fullName/street tối đa 100, city/state/country tối đa 50 ký tự; reason tối đa 1000, key tối đa 100. Validation trả 400; sai role/quyền sở hữu trả 403; state/quote lỗi trả 409. Decision terminal trả trạng thái hiện tại trên retry; không tự coi Approved nếu chưa đủ xác nhận.

## Migration và chạy

Migration **20261020153620_AddAddressChangeConfirmations**, sau SH1 **20261020150408_AddShipmentReattemptSchedulesAndActors**. [SQL để duyệt](../database/address-change-confirmations-migration.sql) thêm cột confirmation/deadline/workflow và revision cap, thay unique index OrderId thành filter `IsPending = 1` để bao phủ cả hai bước chờ. Không sửa snapshot/tiền của request Approved cũ. Nâng cấp database có dữ liệu bằng migration tăng dần, không chạy lại baseline/seed:

```powershell
dotnet ef database update 20261020153620_AddAddressChangeConfirmations --project backend/G4.Infrastructure --startup-project backend/G4.Api
```

Sau migration đúng môi trường, khởi động lại API/frontend và tải lại trang. Đợt này chỉ bootstrap/nâng schema trên **G4_AddressConfirmation_Test_20261020**, **chưa áp database chính**.

## Kiểm thử 20/10/2026

- Solution build output `artifacts/address-confirmation-validation`: 0 warning/error; model không còn thay đổi thiếu migration.
- Backend mặc định đạt, gồm hồi quy đổi địa chỉ/cancel/return/finance/SH1 và SMTP loopback. AD1 riêng bao phủ quote bắt buộc, chờ shipper không đổi tiền/địa chỉ, chưa gán shipper, cap/shipper/refund/snapshot stale, hai deadline boundary, refusal/timeout/retry, legacy, free-shipping promotion thật.
- SQL QA thật trên database riêng đạt: migration bảo toàn Approved cũ/actor null và deadline Pending; request/seller/shipper trùng đồng thời, chỉ ghi refund hoặc chi phí một lần; race cap update/claim/start/expiry; lỗi sau finance SaveChanges rollback địa chỉ/bộ đếm/request/tiền/ledger/outbox/realtime, retry thành công.
- Node UI **120/120** đạt. Chrome API fixture desktop 1440px/mobile 390px đạt: buyer xem quote trước gửi, seller chỉ chấp thuận chờ shipper, shipper accept/refuse, trạng thái/địa chỉ trước/sau và chặn nút bắt đầu giao, escape, không lỗi JavaScript/tràn ngang. Ảnh `artifacts/address-confirmation-ui/` đã xem lại. Đây là module UI với API fixture, chưa E2E HTTP nối SQL hoặc gọi carrier/provider thật.

```powershell
dotnet run --project tests/G4.Commerce.Tests -- --address-confirmations
$env:G4_ADDRESS_CONFIRMATION_SQL_CONNECTION='Server=localhost;Database=G4_AddressConfirmation_Test_20261020;Integrated Security=True;TrustServerCertificate=True'
dotnet run --project tests/G4.Commerce.Tests -- --address-confirmations-sql
node --test tests/*.test.cjs
node tests/address-confirmation-ui.e2e.cjs
```

Browser test cần Playwright/Chrome; có thể dùng NODE_PATH trỏ nơi cài package và CHROME_PATH trỏ Chrome. SQL test chỉ chấp nhận đúng tên QA trên, tự bootstrap nếu trống rồi migrate; không dùng database chính. Harness SQL cũ `--address-sql` đã cập nhật luồng mới nhưng chưa chạy lại database QA 13/10 trong đợt này.

## Ghi nhận lịch sử 13/10 (không thay thế nghiệm thu AD1)

Đã kiểm tra trên QA ngày 13/10/2026: build không lỗi/cảnh báo; toàn bộ commerce tests và 56 test JavaScript qua. Bao gồm cap, quyền buyer/seller/shipper, chống duyệt/gửi/ack đồng thời, phí kg lẻ, giữ snapshot giá/payment/tồn kho/hồ sơ, hoàn ship và hủy đơn hoàn phần còn lại. Kiểm thử hồi quy xác nhận tiền vẫn mở khóa sau giao thành công và buyer vẫn mở được tranh chấp sau hoàn ship; tranh chấp không tự đóng vì khoản hoàn ship và có thể hoàn phần captured còn lại. Trình duyệt xác minh form buyer, seller duyệt, cấu hình cap, vận đơn địa chỉ mới và shipper xác nhận để tiếp tục lấy hàng.

Sau đó đã áp dụng `AddOrderAddressChanges` trên database local `CloneEbayDB` để sửa lỗi checkout 500 do thiếu cột snapshot địa chỉ. Migration này cần cho checkout trên phiên bản code hiện tại, kể cả khi chưa dùng chức năng đổi địa chỉ. Các máy/database khác vẫn cần chạy lệnh migration trước khi khởi động API.
