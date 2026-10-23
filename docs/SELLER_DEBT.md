# Quản lý nợ người bán

**Đồng hồ 23/10/2026:** Seller/Admin xem thời gian tới WarningAt, DueAt và EarliestWriteOffAt. WarningAt lấy từ kỳ nợ, không trừ cứng 15 giây ở client. Mốc write-off là dự kiến nếu không có thu hồi mới; chỉ quyết định của hệ thống mới xác nhận khóa bán. [Hướng dẫn chung](DEMO_COUNTDOWNS.md).

Write-off/khóa bán vĩnh viễn do worker tự đánh giá và kiểm tra blacklist khi đăng ký seller được mô tả tại [SELLER_REGISTRATION_WRITE_OFF.md](SELLER_REGISTRATION_WRITE_OFF.md). Trả hết nợ chỉ gỡ đình chỉ tạm do nợ; giữ khóa bán vĩnh viễn nếu đã có quyết định.

`NegativeBalance` là số tiền nợ dương, bằng USD, tối đa 2 chữ số thập phân. Ví khả dụng, tiền đang xử lý và tiền đang giữ được theo dõi riêng. Trả nợ ngân hàng không cộng tiền vào ví, không ghi doanh thu và không thu phí nền tảng.

## Kỳ nợ và thời hạn

- Kỳ bắt đầu khi số nợ từ 0 tăng lên. `DebtCycleNumber`, ngày bắt đầu, hạn trả và thời điểm cảnh báo được lưu; `SellerDebtCycle` giữ các kỳ đã kết thúc.
- Trả một phần hoặc phát sinh thêm nợ giữ nguyên hạn của kỳ đang mở. Trả hết kết thúc kỳ; phát sinh nợ lần nữa tạo kỳ mới.
- Mặc định `Finance:Debt:DueSeconds=604800` (7 ngày), `WarningBeforeSeconds=86400` (cảnh báo trước hạn một ngày). Development dùng 60 và 15 giây. Cần `0 < WarningBeforeSeconds < DueSeconds`.
- Thay cấu hình chỉ áp dụng cho kỳ mới. Lưu UTC; giao diện chuyển sang giờ địa phương. Quá hạn khi giờ hiện tại **bằng hoặc sau** hạn trả.
- Worker đánh giá mỗi 5 giây. API kiểm tra trực tiếp thời hạn khi nhận đơn/đăng sản phẩm mới; không phụ thuộc worker đã chạy hay chưa.
- Nợ tồn tại trước nâng cấp bắt đầu thời gian ân hạn khi worker/API ghi nhận lần đầu. Không dùng `UpdatedAt` để suy đoán tuổi nợ, không sửa số dư cũ.

## Quyền của seller

Còn nợ nhưng chưa quá hạn: vẫn bán hàng, không tạo payout mới. Tiền bán hàng sau phí bù nợ trước khi cộng phần còn lại vào số dư.

Quá hạn: `DebtSuspended` chặn đơn mới và đăng bán. `SellerAccount.Status` giữ nguyên, nên trả hết nợ không xóa khóa/hạn chế từ quyết định khác. Seller vẫn đăng nhập, trả nợ, giao hàng, xử lý hủy/return/dispute của đơn đã tồn tại. Đơn tạo trước hạn đình chỉ và còn hạn thanh toán vẫn được thanh toán; không gia hạn thời gian thanh toán.

Trả hết bằng ngân hàng, doanh thu, tiền đang xử lý vừa đến hạn, tiền tranh chấp được giải phóng hoặc payout bị trả về đều kết thúc kỳ nợ, gỡ đúng hạn chế do nợ và ghi lịch sử khôi phục nếu từng bị đình chỉ.

Payout đã được chấp nhận vẫn tiếp tục. Khi ngân hàng trả tiền payout về, tiền về ví được dùng bù nợ trước; chỉ phần dư ở lại ví. Tạo payout và cập nhật số dư được khóa cùng với các nghiệp vụ tài chính khác của seller.

## Trả nợ giả lập và chống gửi trùng

`POST /api/seller/debt/repayments` nhận:

```json
{
  "amount": 10.00,
  "expectedDebt": 40.00,
  "expectedCycleNumber": 1,
  "key": "a-unique-request-key",
  "simulateFailure": false
}
```

Số tiền phải dương và không vượt nợ đã xác nhận. Nếu số nợ hoặc kỳ nợ đã thay đổi, API trả 409 để seller tải lại và xác nhận. Không tự giảm số tiền người dùng đã chọn.

Ngân hàng giả lập trả kết quả ngay: `Succeeded` hoặc `Rejected`, có biên nhận và mã `DEBT-BANK-*`. Bị từ chối không thay đổi số dư và không ghi bút toán tiền. Thành công lưu biên nhận, giảm nợ, ghi `DebtRepayment` và đưa thông báo vào outbox trong cùng transaction SQL Server. Bút toán khoản giảm nợ dùng số âm theo quy ước `DebtRecovery` hiện có.

Mã yêu cầu được khóa theo seller. Gửi lại cùng mã và nội dung nhận lại biên nhận gốc, kể cả kỳ đó đã kết thúc; thay nội dung với cùng mã bị từ chối. Giao diện giữ mã và nội dung trong `sessionStorage` khi chưa rõ kết quả, khóa các trường tiền/giả lập và cho kiểm tra lại cùng giao dịch. Không tạo mã mới khi chỉ thử lại lỗi mạng.

## API và giao diện

- Seller: `GET /api/seller/debt`, `POST /api/seller/debt/repayments`, `GET /api/seller/debt/history?kind=repayments|events|cycles&page=1&pageSize=10`.
- Admin: `GET /api/admin/seller-debts?state=debt|overdue|all&sellerId=2`, `GET /api/admin/seller-debts/{sellerId}`, `GET /api/admin/seller-debts/{sellerId}/history?kind=repayments|events|cycles|transactions`.
- Đăng sản phẩm: `POST /api/seller/products` với title, description, price, weightKg, quantity và categoryId tùy chọn. API kiểm tra hạn chế trong transaction trước khi thêm sản phẩm/tồn kho. Dự án chưa có màn hình đăng sản phẩm riêng.
- Seller xem/trả nợ trong tab **Tài chính người bán**. Admin xem tab **Khoản nợ seller**, gồm biên nhận, các kỳ, cảnh báo/hạn chế và sổ cái có phân trang.

Thông báo gồm phát sinh nợ, sắp đến hạn, quá hạn/đình chỉ, trả nợ thành công/thất bại, hết nợ và gỡ đình chỉ. Thông báo nợ dùng `SellerId` và `EventKey`, không bịa mã đơn hàng. Chỉ seller sở hữu và admin xem được. Khóa sự kiện ngăn gửi lặp; hệ thống email hiện có xử lý Capture/SMTP và retry.

## Nâng cấp và kiểm thử

Dừng API, áp dụng migration `20261014102533_AddSellerDebtManagement`, rồi khởi động API/frontend mới. SQL để duyệt: [seller-debt-migration.sql](../database/seller-debt-migration.sql). Migration thêm dữ liệu quản lý nợ và cho phép thông báo không gắn đơn; không đặt lại số dư hoặc trạng thái seller.

```powershell
dotnet ef database update --project backend/G4.Infrastructure --startup-project backend/G4.Api
dotnet build G4_Project.sln -m:1 -p:UseSharedCompilation=false
dotnet run --project tests/G4.Commerce.Tests --no-build
node --test tests/*.test.cjs
```

Kiểm thử SQL chỉ cho phép database QA riêng, tự tạo bảng nền/migration nếu chưa có. Không chạy trên `CloneEbayDB`:

```powershell
$env:G4_DEBT_SQL_TEST_CONNECTION = 'Server=localhost;Database=G4_SellerDebt_Test_20261014;Integrated Security=True;TrustServerCertificate=True;'
dotnet run --project tests/G4.Commerce.Tests --no-build -- --debt-sql
```

Đã kiểm thử nghiệp vụ trả một phần/hết nợ, ngân hàng từ chối, mã gửi trùng/sai nội dung, số nợ/kỳ nợ thay đổi, mốc cảnh báo/quá hạn, khôi phục giữ khóa khác, bảo mật thông báo, payout bị trả về và tiền đang xử lý bù nợ. Kiểm thử SQL kiểm tra migration bảo toàn số dư, các yêu cầu đồng thời và doanh thu cạnh tranh với trả nợ.

Kịch bản giao diện: phát sinh nợ sau payout/refund, mở tab tài chính, trả một phần và quan sát hạn không đổi; thử ngân hàng từ chối; chờ 60 giây Development; admin lọc quá hạn; xác nhận đơn cũ vẫn xử lý được; trả hết và quan sát biên nhận, lịch sử gỡ hạn chế và thông báo.
