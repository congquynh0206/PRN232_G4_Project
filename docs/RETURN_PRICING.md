# Trả hàng và refund theo lý do

**Căn cứ mới 18/10/2026:** [đặc tả §7](BUSINESS_REQUIREMENTS.md#7-trả-hàng-và-refund) và [task FR1](IMPLEMENTATION_BACKLOG.md#8-fr1--free-returns-theo-mặt-hàng). Công thức bên dưới là phiên bản đã triển khai trước free returns. Bản mới phải snapshot policy item, phân phí một nhãn theo khối lượng cho đơn trộn free/non-free, không trừ phần seller chịu vào refund buyer và không sửa hồi tố snapshot cũ. Free shipping chiều đi không tự bật free returns. Giữ dữ liệu/kiểm thử lịch sử như ghi nhận theo phiên bản cũ.

Áp dụng từ migration `20261014061325_AddReasonBasedReturnPricing`. Từ RT1 ngày 20/10, buyer trả toàn bộ hàng trong deadline snapshot từ Delivered chiều đi (mặc định 7 ngày/Development 300 giây cho delivery mới, legacy giữ 7 ngày; xem [RT1](RETURN_WINDOW_POLICY.md)); cơ chế seller duyệt, tracking chiều trả, receipt và tự refund vẫn như [RETURNS_AND_SHIPPING.md](RETURNS_AND_SHIPPING.md). Thời hạn demo receipt vẫn 45 giây, môi trường khác 48 giờ.

## Báo giá và xác nhận

Buyer chọn một trong năm lý do: Sai hàng, Sai mô tả, Hàng hỏng/thiếu do seller, Đổi ý, hoặc Lý do khác không do seller. Form tính lại khoản hoàn khi đổi lý do, hiển thị bên chịu phí và cho thêm mô tả tối đa 2.000 ký tự. Phải xem và xác nhận báo giá trước khi gửi; server không nhận số tiền buyer tự nhập.

| Nhóm lý do | Phí chiều về | Tiền buyer được hoàn |
| --- | --- | --- |
| Sai hàng, sai mô tả, hỏng/thiếu do seller | Seller chịu | Số đã trả còn lại, gồm phí ship chiều đi |
| Đổi ý hoặc lý do không do seller | Buyer chịu, trừ vào refund | Tiền hàng sau giảm giá trừ phí chiều về; không hoàn ship chiều đi |

Phí chiều về dùng công thức kg lẻ và vùng của snapshot đơn, đảo chiều địa chỉ buyer/seller; không đọc lại địa chỉ hồ sơ hay khối lượng Product. Địa chỉ đổi đã được duyệt trước giao hàng là địa chỉ thực tế của tuyến trả. Đơn cũ thiếu khối lượng hoặc vùng địa chỉ snapshot bị chặn báo giá thay vì đoán phí.

Gọi số tiền đã thanh toán là `P`, phần ship buyer đã trả là `S`, hoàn chênh lệch ship trước đó là `A`, phí chiều về là `R`, coupon nền tảng là `U`:

- Tiền hàng buyer đã trả: `P - S`.
- Lỗi seller: refund buyer `P - A`; seller chịu `R`.
- Buyer đổi ý: refund buyer `P - S - R`; giữ lại ship chiều đi `S - A`.
- Nếu `R > P - S`, buyer đổi ý bị chặn trước khi tạo yêu cầu/nhãn; cần xử lý riêng. Nếu bằng nhau, refund bằng 0 là hợp lệ, chỉ ghi nội bộ và không gọi cổng thu/hoàn tiền.
- Coupon nền tảng `U` được đảo riêng khi toàn bộ hàng được trả, không cộng vào tiền mặt hoàn buyer, không khôi phục lượt coupon đã dùng.

Ví dụ hàng 100 USD, coupon nền tảng 10 USD, ship chiều đi 5 USD, chiều về 5 USD: buyer đã trả 95 USD, SellerGross 105 USD. Buyer đổi ý nhận 85 USD; nền tảng thu hồi riêng 10 USD tài trợ. Phí gốc 12,90 USD được credit 12,29 USD theo phần SellerGross hàng trả 100/105. Seller giữ 4,39 USD sau phí trên ship chiều đi. Nếu lỗi seller, buyer nhận 95 USD, credit phí gốc 12,90 USD và seller chịu chi phí ship về 5 USD; thiếu số dư thì phát sinh Negative do chi phí này.

## Snapshot, phí vận chuyển và ledger

`ReturnRequest` lưu reason code, mô tả, request key, fingerprint báo giá, nguồn trả phí, tiền hàng, ship chiều đi giữ lại, phí chiều về, refund buyer, phần tài trợ nền tảng, khối lượng và địa chỉ chiều trả. DbContext chặn sửa các snapshot sau xác nhận. Gửi lại đúng key/payload dùng lại yêu cầu; đổi khoản tiền/tuyến hoặc dùng key với payload khác bị từ chối. Yêu cầu mới cho order đã có return cũng bị chặn.

Seller duyệt thì khóa tiền như trước. Khi nhãn chiều trả được tạo thành công, hệ thống ghi `ReturnShippingCost` đúng một lần và dùng tiền đang giữ/đang xử lý, Available rồi Negative để chi phí vận chuyển được ghi nhận. Nhãn lỗi chưa ghi chi phí; retry cùng nhãn/key không thu thêm. Với buyer chịu phí, đây là khoản ứng trước từ tiền order; khi refund thành công, khoản trừ vào refund được ghi vào `ReturnShippingRecovery` để seller không chịu phí hai lần. Không credit khoản này khi gateway còn lỗi/pending.

Refund lưu tiền buyer thực nhận, SellerGross bị đảo, phần tài trợ đảo và phí buyer bù. Fee credit dùng snapshot phí ban đầu; không tính tỷ lệ chỉ từ tiền mặt refund đã bị trừ postage. Refund lỗi giữ tiền và retry cùng amount/key. Refund 0 không gọi PayPal/Card. Fee credit làm phát sinh khoản credit tiền thì bù Negative trước; phần dư vẫn theo lịch hold.

Khi return hoàn tất, phần hold còn lại chuyển về Processing theo lịch gốc hoặc Available nếu đủ điều kiện. Level 3 Instant nhận phần còn lại vào ví ngay; không tạo payout. Hold của order khác không bị sử dụng hoặc giải phóng.

Yêu cầu đã tồn tại trước migration giữ `PricingPolicyVersion = 0`, tiếp tục chính sách cũ, không tính lại refund/thu postage hồi tố. Thỏa thuận ReturnRefund mới có đủ snapshot tuyến/khối lượng được ghi nhận là hoàn toàn bộ, seller chịu postage. Thỏa thuận trên đơn cũ thiếu snapshot giữ cách xử lý cũ. Với yêu cầu mới đã có báo giá, thỏa thuận trả hàng dùng báo giá đó. Phán quyết admin mới có thể kết luận lại trách nhiệm và hoàn toàn bộ/một phần, lưu quyết định điều chỉnh riêng và giữ báo giá gốc; bên chịu phí tự suy ra từ trách nhiệm. Xem [ADMIN_DISPUTE_DECISIONS.md](ADMIN_DISPUTE_DECISIONS.md).

## API

- `GET /api/orders/{id}/returns/quote?reasonCode=ChangedMind`: buyer sở hữu order, báo giá chỉ đọc.
- `POST /api/orders/{id}/returns`: `reasonCode`, `description`, `key`, `pricingFingerprint`. Không còn nhận body chỉ có reason chữ tự do.
- Code hợp lệ: `WrongItem`, `NotAsDescribed`, `DamagedOrMissing`, `ChangedMind`, `OtherBuyerReason`.
- Chi tiết order trả thêm snapshot return và ReturnRequestId của refund; shipper vẫn không nhận dữ liệu tiền.
- Duyệt/từ chối/tạo nhãn/receipt khóa order trước khi đọc trạng thái để tránh ghi đè và deadlock từ thao tác đồng thời.

## Migration và kiểm thử

```powershell
dotnet ef database update --project backend/G4.Infrastructure --startup-project backend/G4.Api
dotnet build G4_Project.sln -c Release --no-restore -m:1 -p:UseSharedCompilation=false -p:UseAppHost=false
dotnet tests/G4.Commerce.Tests/bin/Release/net8.0/G4.Commerce.Tests.dll
node --test tests/returns-ui.test.cjs tests/ui-navigation.test.cjs
```

SQL tương đương: [return-pricing-migration.sql](../database/return-pricing-migration.sql). Migration chỉ thêm cột/index; không reset baseline, tồn kho, payment/refund hay yêu cầu cũ.

Đặt `G4_RETURN_SQL_TEST_CONNECTION` trỏ tới **G4_ReturnPolicy_Test_20261014**, chạy commerce DLL với `--return-sql`. Test guard đúng tên database QA, khởi tạo baseline/migration/seed chỉ tại database này. Bao gồm request/approve đồng thời, một nhãn/một phí, hai receipt cùng worker chỉ một refund, tiền đúng báo giá và ví Instant/nợ khớp.

Ngày 14/10/2026 đã xác minh trên SQL QA và giao diện desktop/mobile 390px: đổi lý do cập nhật khoản hoàn; request lưu đúng 85 USD; seller thấy báo giá trước khi duyệt; shipper nhận và tracking chiều trả; receipt qua giao diện thực hiện Card giả lập đúng 85 USD. SMTP/PayPal Sandbox bên ngoài không được gọi trong QA này.

Đã áp dụng migration trên database local `CloneEbayDB` ngày 14/10/2026 sau backup COPY_ONLY có CHECKSUM và VERIFYONLY tại `artifacts/return-policy/CloneEbayDB-before-return-pricing-20261014.bak`. Đối chiếu trước/sau: số yêu cầu/refund, tổng tiền refund, sáu settlement (Gross 1.322,50 USD, phí 160,50 USD, Net 1.162,00 USD) và toàn bộ số dư ví đều giữ nguyên. Database khác vẫn cần chạy migration. Khởi động lại API/frontend để sử dụng code mới.
