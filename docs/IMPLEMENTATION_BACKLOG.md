# Backlog triển khai theo đặc tả ngày 18/10/2026

## ADDR2 — Gợi ý địa chỉ, SĐT theo địa chỉ, phí ship theo vùng (11/11/2026)

Nguồn: ghi chú review 10/11/2026 và quyết định của người dùng ngày 11/11/2026. Đặc tả ở
[§3.1](BUSINESS_REQUIREMENTS.md#31-phí-vận-chuyển-chiều-đi) và §6.

- **Phí ship mức 1:** `ShippingPricing.Zone` thay `IsHanoiLocal`: cùng tỉnh / cùng miền /
  khác miền (VN), cùng bang / khác bang (US), VN ↔ US; vẫn tính kg lẻ. Checkout, Best
  Offer, đổi địa chỉ và báo giá trả hàng dùng chung. Đơn đã tạo giữ phí snapshot.
- **Địa chỉ:** chỉ Việt Nam/Hoa Kỳ (`AddressRegions`: 34 tỉnh từ 01/07/2025 + tên cũ quy
  về tỉnh mới; 50 bang + DC lưu mã). `DeliveryAddressRules` dùng chung cho sổ địa chỉ và
  đổi địa chỉ; địa chỉ lấy hàng seller cũng phải thuộc danh sách.
- **SĐT:** bắt buộc, lưu `+84…`/`+1…` theo quốc gia, không thêm vào tài khoản. Đơn mới
  snapshot `DeliveryPhoneSnapshot`; seller/shipper thấy SĐT người nhận chiều đi.
- **Đổi địa chỉ:** body thêm `phone` (bắt buộc), `postalCode`; chỉ đổi SĐT/ZIP cũng là một
  yêu cầu và dùng lượt đổi duy nhất. Request lưu tên/đường/phường/ZIP/SĐT mới và SĐT/ZIP
  cũ; khi Approved cập nhật đủ snapshot cấu trúc của đơn (trước đây chỉ cập nhật chuỗi
  địa chỉ và tỉnh/quốc gia). Quote token và kiểm tra trùng key gồm cả SĐT/ZIP.
- **Gợi ý:** `GET api/address-suggestions/regions` (mọi role), `GET api/address-suggestions?q=`
  và `.../detail?ref=` (buyer). VietMap Autocomplete v4 `display_type=1`; dòng có phường +
  tỉnh dùng luôn, không gọi Place (1 lượt/lần chọn). Cache 10 phút, timeout 5 giây, lỗi
  401/423/mạng trả 503 để UI ẩn ô tìm. Khóa `VietMap:ApiKey` trong user-secrets G4.Api.
  VietMap chỉ nhận khóa trên URL nên client VietMap tắt logger mặc định của HttpClient
  (`RemoveAllLoggers`), log tích hợp riêng không chứa khóa/chữ buyer gõ.
- **Migration:** `20261111034557_AddDeliveryPhoneAndAddressChangeFields`, chỉ thêm 8 cột
  nullable ([SQL duyệt](../database/delivery-phone-address-fields-migration.sql)). Đã áp
  lên database demo `G4_Demo` (11/11, theo yêu cầu người dùng) và có trong `G4_Demo.sql`
  sinh lại; **chưa áp `CloneEbayDB`**. `has-pending-model-changes`: không đổi.
- **Kiểm thử 11/11:** build solution 0 warning/error; bộ backend mặc định đạt (gồm bảng
  vùng, `--buyer-address-book`, `--address-confirmations` + AddressContactChecks,
  `--vietmap` với HTTP giả); `--vietmap-live` gọi VietMap thật bằng key đạt; Node 160/160.
  Browser pane với code UI thật + API giả: sổ địa chỉ, đổi quốc gia/đầu số, tìm và chọn
  gợi ý, đổi địa chỉ có SĐT đến bước gửi; desktop và 375px không tràn ngang.
- **Demo:** `tools/G4.DemoData` và `G4_Demo.sql` dùng địa chỉ/SĐT chuẩn mới; xem
  [DEMO_DATABASE.md](DEMO_DATABASE.md). E2E 11/11 trên `G4_Demo` qua `Run-Demo.ps1`: sổ
  địa chỉ hiện +84/+1, tìm VietMap thật rồi lưu (server lưu `+84…`), báo giá đổi địa chỉ
  đúng vùng (chỉ SĐT 2,10; TP.HCM 5,20; Hoa Kỳ 16,00; số +1 cho địa chỉ VN bị 400).
- **Còn lại:** SQL QA tranh chấp/rollback riêng cho phần SĐT; áp migration lên
  `CloneEbayDB` khi người dùng yêu cầu. Gợi ý chưa ưu tiên theo vị trí (không có
  `focus`), nên cùng số nhà có thể ra tỉnh khác trước.

## BEST-OFFER — Trả giá buyer/seller ngày 08/11/2026

- Đã thêm cấu hình nhận trả giá theo sản phẩm, auto-accept/auto-decline, phiên thương lượng,
  counteroffer, giới hạn 5 lượt buyer, hết hạn 24 giờ, UI cho buyer/seller và worker dọn phiên.
  Khi accept, xử lý order/payment/settlement/ledger và tồn kho trong transaction; chỉ commit khi
  thu thẻ mô phỏng thành công.
- Migration được tạo: `20261108124914_AddBestOffers`; chưa áp vào database. Solution build
  `dotnet build G4_Project.sln --no-restore` đạt 0 warning/error. Chưa chạy test nghiệp vụ,
  SQL QA, hoặc browser E2E.
- Sửa ngày 09/11/2026 (không thêm migration): giới hạn 5 lượt theo sản phẩm qua mọi phiên,
  luồng thanh toán sau tạo đơn `AwaitingPayment` dùng hạn giữ hàng/điểm phạt Unpaid Item,
  counter phải nằm giữa hai mức giá, khóa chống gửi trùng cho chấp nhận/từ chối/counter,
  nhập lại mật khẩu và rate limit nhóm `payment` cho thao tác chấp nhận. Test `--best-offers`,
  `--password-confirmation` (24 route), `--rate-limits`, backend mặc định và Node 154/154 đạt.
  Chưa SQL QA đồng thời/browser E2E.
- PayPal/Klarna chưa hỗ trợ auto-pay Best Offer: source hiện không có authorization/vault được
  buyer cấp trước. Phạm vi thanh toán hiện tại chỉ thẻ mô phỏng. Hướng dẫn:
  [BEST_OFFERS.md](BEST_OFFERS.md).
- Làm lại UI/UX ngày 10/11/2026 (chỉ frontend, không migration): phiên thương lượng có so sánh
  giá niêm yết/hai bên/giá chốt, bộ lọc, đếm ngược và lịch sử tiếng Việt; danh sách sản phẩm
  gọn với form mở khi cần. Đã kiểm tra trên trình duyệt với dữ liệu demo, build frontend và Node
  UI test liên quan đạt. Chưa có E2E tự động cho các thao tác ghi.

## KLARNA1 — Pay Later sandbox (08/11/2026)

- HPP `PLACE_ORDER`, buyer reconcile, worker đối chiếu khi đóng tab, capture
  toàn bộ khi tạo outbound, cancel authorization hoặc refund sau capture;
  chi tiết và cách chạy ở [KLARNA_PAY_LATER.md](KLARNA_PAY_LATER.md).
- Migration bổ sung ZIP/address snapshot và Klarna HPP reference. Test service,
  HTTP gateway, frontend và toàn bộ hồi quy cần qua trước khi review. Live QA
  playground cần credential riêng; chưa khẳng định seller đã nhận tiền thật.
- SRS cần phân biệt authorized/captured/settled; lịch trả góp và risk/fee do
  Klarna quản lý; không tự ghi payout ngay hay công thức phí chưa được xác nhận.
## PERF2 — Nginx chia tải catalog qua hai API

- **Bổ sung realtime nhiều instance (11/11/2026):** API có cấu hình tùy chọn Redis
  SignalR backplane; Nginx demo chuyển tiếp `/api/` và WebSocket `/hubs/`; Compose
  chỉ bật `CommerceChangeWorker` trên `api-a` bằng `Realtime:DispatcherEnabled`,
  đồng thời giữ worker bảo trì tắt. Hướng dẫn và kịch bản ở [NGINX.md](NGINX.md).
  Đã build API trong Docker, chạy đủ 5 service, `nginx -t` thành công; 6 request
  catalog đều HTTP 200 và chia luân phiên qua hai API. Browser báo SignalR connected;
  log Nginx có negotiate 200 và WebSocket 101 tới api-b trong khi dispatcher ở
  api-a. CommerceChange `2653` (`notification-inbox`) được đánh dấu dispatched
  khoảng 0,1 giây sau khi tạo. Bằng chứng do người dùng chạy trên Docker Desktop
  ngày 11/11; chưa có client-side ack riêng để khẳng định browser nhận đúng callback.
  Chưa áp migration. Dispatcher hiện là single instance, chưa HA khi `api-a` dừng.

- Compose demo riêng chỉ publish Nginx trên loopback. Nginx chia GET catalog qua
  hai API, chuyển POST login tới `api-a` và trả 404 cho route ngoài demo. Hai API
  dùng JWT chung và tắt worker bảo trì/phát thông báo. [Cách chạy](NGINX.md).
- Ngày 04/11/2026, build Release và bộ backend mặc định đạt. Compose config hợp
  lệ và `nginx -t` trên image Linux kiểm tra được file gốc.
- Đã thử trên Nginx Windows 1.28.3 với hai API thật và database QA hiện có.
  8 lần đọc chia 4/4, route ngoài demo trả 404, sai method trả 403, thiếu JWT
  trả 401. JMeter 30 giây qua Nginx có 31 HTTP 200, chia 15/16, 0 lỗi, P95
  39,2 ms. Sau khi dừng API-B, cả 6 lượt đọc vẫn thành công qua API-A.
- Báo cáo và access log ở `artifacts/nginx/qa-20261104-092703/`. Chưa chạy cả
  stack Compose do ổ lưu Docker thiếu dung lượng. Kết quả chưa đánh giá sức tải
  tối đa. Đợt này không tạo hay áp migration và dùng lại database QA.

## PERF1 — Baseline catalog và kiểm tra quota bằng JMeter

- Hai kịch bản CLI dùng cùng API chỉ đọc có JWT buyer. Bài tải có timer điều tiết.
  Bài quota dừng theo số request hoặc thời lượng và kiểm tra 200, 429, header
  `Retry-After` và JSON `RateLimitExceeded`. [Cách chạy](JMETER.md).
- Ngày 04/11/2026 đã chạy JMeter 5.6.3 với API thật và SQL Server demo riêng
  `G4_Demo_JMeter_QA_20261104`. Bài tải 30 giây có 31 mẫu, 0 lỗi, 1,05 request/giây,
  P95 114,4 ms. Bài quota có đúng 150 mẫu gồm 120 HTTP 200 và 30 HTTP 429 hợp lệ.
  Lượt chỉ gửi hai mẫu đánh dấu lỗi vì chưa thấy 429. Hai bài đều xuất JTL và HTML.
- Kết quả là baseline trên máy local. Chưa đo giới hạn tải, chưa chạy qua Nginx
  hoặc quét lỗ hổng. Chỉ nhập schema/dữ liệu demo vào database QA riêng.

## CI1 — Build và test trên GitHub Actions

- Kế hoạch và phạm vi bản đầu gồm restore NuGet, build Release, chạy bộ backend
  mặc định và test JavaScript trên Ubuntu với .NET 8 và Node.js 22. Workflow chạy
  cho pull request và push vào `master`. [Workflow](../.github/workflows/ci.yml).
- Kiểm chứng local ngày 04/11/2026 trên Windows: YAML đọc được, build Release
  bằng SDK 8.0.200 đạt 0 cảnh báo/lỗi, bộ backend mặc định đạt gồm HTTP/SMTP
  loopback, Node 144/144 đạt. Restore local dùng cache và tắt NuGet Audit do
  lỗi DNS. Workflow giữ lệnh restore mặc định.
- Chờ lần chạy đầu trên GitHub Actions sau khi push để xác nhận runner Ubuntu.
  SQL, browser E2E và smoke chạy riêng. Đợt này không có migration hoặc thao tác
  database. [Cách chạy và xem log](PROJECT_RUN.md#ci-trên-github-actions).

## AUTH2 — Nhập lại mật khẩu cho quyết định ngày 31/10/2026

- Đã hoàn thành source cho 21 action API hiện có của admin/seller: phán quyết và
  alias, phí, promotion/moderation, payout/nợ/định danh, hủy/trả/hoàn tiền, phương
  án tranh chấp, đổi địa chỉ, cap/địa chỉ lấy hàng. Authorization filter kiểm
  UserId/role/password trước controller; không thay quyền sở hữu/nghiệp vụ tiền.
- Popup chung giữ nguyên payload/key, hiển thị quyết định và tiền liên quan, có
  hủy/Escape, lỗi tại ô nhập, xóa mật khẩu, chặn gửi trùng/hủy lúc đang gửi. Có
  quota xác nhận riêng 5 lần/60 giây/UserId và 429 thử lại thủ công. Request chỉ
  nhận challenge không tiêu quota nghiệp vụ; không cache mật khẩu hoặc tự retry
  sau kết quả mạng chưa rõ. Worker tiếp tục tự chạy.
- Solution build output `artifacts/password-confirmation-validation/build/` đạt
  0 warning/error; backend mặc định đầy đủ đạt, gồm HTTP rate limit và SMTP;
  `--password-confirmation` đạt với MVC/JWT/InMemory thật: 21 route, thiếu/sai mật
  khẩu không đổi tiền, role/ownership/current password, payout replay trừ một
  lần, admin fee, quota chung/cạnh tranh và độc lập user. Node 143/143 đạt.
- Chrome headless với Razor/cookie/proxy thật và API fixture đạt 1440px/375px:
  seller payout/admin fee, Unicode, sai mật khẩu, 429/manual retry, hủy rồi đổi
  amount, giữ payload/key, chặn khi pending, Escape không đóng dialog nền và
  escape nội dung. Đã xem ảnh desktop/mobile tại `artifacts/password-confirmation-ui/`.
- Không tạo/áp migration, không seed/migrate hoặc thay database chính. Chưa E2E
  SQL/provider mới; các kiểm tra tiền SQL/rollback lịch sử không được coi là kết
  quả mới của task này. Cần restart API/frontend và tải lại trang.
- A2/LIM1/FR1 còn trong backlog: nhánh yêu cầu trả trước kết luận, hạn mức riêng và
  policy free returns phải gắn xác nhận khi triển khai; không đánh dấu hoàn thành
  các task đó. [Kế hoạch/phạm vi/API/cấu hình](PASSWORD_CONFIRMATION.md).


## DEMO-DATA — SQL trình diễn ngày 31/10/2026

- Đã xuất bộ SQL độc lập, tự tạo database mới/schema migration hiện hành và dữ
  liệu: 16 tài khoản, 10 seller, 43 kịch bản; seller 2/3 có dữ liệu hiệu suất hỗ
  trợ, nợ/write-off/blacklist/payout/hold có ledger và lịch sử từ service hiện tại.
- Nhập thật vào QA SQL Server, so dòng/FK/check, 16 login/10 ví/doanh số/level và
  blacklist đạt. API trên bản nhập đọc 30 đơn và thực thi card, approve return/
  label, payout ba level, xét nâng cấp thật trên QA đạt. Script chống ghi đè và
  helper PowerShell được kiểm tra. [Cách dùng/phạm vi](DEMO_DATABASE.md).
- Có helper cấu hình process riêng: key định danh demo, Capture và timer đủ dài;
  không sửa config mặc định hoặc nghiệp vụ. Không có migration mới; chỉ áp các
  migration hiện có lên database build/QA riêng, không thay database chính.
- Không ghi hoàn thành INR1/FR1/LIM1/A2/RT2 hoặc bộ kiểm thử provider/đồng thời
  từ kết quả xuất dữ liệu. Log QA và chi tiết nằm trong hướng dẫn bộ tải xuống.

## RATE1 — Sliding Window và giao diện 429 ngày 28/10/2026

- Hoàn thành source: thư viện hosting chung G4.WebCommon, policy theo nhóm tại API,
  login/register theo IP tại MVC/API, UserId từ JWT, proxy allowlist local/Docker,
  JSON/Retry-After và thông báo/countdown. Carrier/worker không dùng quota user.
- Solution build output riêng 0 warning/error; backend mặc định và bộ HTTP
  `--rate-limits` đạt, Node 140/140 đạt. HTTP kiểm tra policy route thực tế, hai user,
  JWT giả, proxy giả, cạnh tranh, phục hồi; create/card/payout chạy InMemory rồi
  quota chặn create/card/PayPal/payout/refund không đổi tiền/tồn kho/outbox.
- Chrome fixture dùng MVC/Razor/proxy thật đạt desktop/mobile, 429 local/backend,
  không gửi backend khi MVC chặn, Retry-After, countdown, không tự replay và giữ key;
  đã xem ảnh `artifacts/rate-limit-ui/`. Log ở `artifacts/rate-limit-validation/`.
- Hồi quy Chrome login bốn role và điều hướng 320–1440px đạt; Docker Compose config,
  CSharpier check và git diff check đạt. Chưa chạy container Docker.
- Không tạo/áp migration, seed hoặc thay database chính/QA. Chưa chạy SQL/provider
  thật hoặc Docker. Bộ đếm RAM theo process, chưa triển khai Redis/Nginx/multi-node;
  NuGet audit chưa chạy do không truy cập nuget.org. [Cấu hình/test](RATE_LIMITING.md).

## BELL1 — Chuông bốn role ngày 27/10/2026

- Yêu cầu: chuông có badge chưa đọc 1–9/9+, danh sách lưu server, read/read-all theo
  tài khoản và cập nhật SignalR; mở chuông không đánh dấu đã đọc.
- Đã có source inbox riêng, snapshot sự kiện đã commit, key chống trùng, API theo
  claim và UI chung bốn role; không dùng trạng thái gửi email làm số chưa đọc.
- Hoàn thành/nghiệm thu source: solution/Release build 0 warning/error, backend
  mặc định đầy đủ (gồm SMTP loopback), InMemory chuông, SQL QA migration/unique/
  concurrent read/quyền/rollback và Node 135/135 đạt. Chrome nối Razor/API/SQL/
  SignalR thật bốn role ở 320/390/1440px đạt: scope, 9+, đọc/read-all, link theo role,
  pagination, keyboard/focus, draft, nhiều tab, reload/offline và escape/lỗi tải.
  Đã xem ảnh mobile/desktop; artifact tại `artifacts/notification-bell-qa/`.
- Migration `20261027153709_AddUserNotificationBell` chỉ áp database QA riêng
  `G4_NotificationBell_Test_20261027`, chưa áp database chính.
- [API, phạm vi sự kiện, cách chạy và bằng chứng](NOTIFICATION_BELL.md).

## FLOW-DOC — Luồng checkout và màn tiếp theo ngày 25/10/2026

- Đã tạo ba workflow Archify dựa trên source: tạo đơn/điều hướng, kết quả thẻ thử và
  PayPal cả giỏ. [Hướng dẫn, bảng nhánh và liên kết HTML](BUYER_CHECKOUT_FLOW.md).
  Phân biệt lỗi tạo đơn, mất phản hồi, đơn đã tạo, kết quả trộn, Verifying, hủy lượt
  PayPal và hủy đơn; thể hiện dialog/thẻ kết quả theo UI hiện có.
- Cả ba đạt validate/deliver/check/browser-check; dẫn chứng Git ghim commit
  `e7997b03bd86bb5160332dd94bd5dc853b441963`, Chrome kiểm tra bốn viewport desktop
  và sáng/tối theo quy trình Archify. Chưa đánh giá ảnh bằng mắt hoặc E2E ứng dụng
  nối database/provider thật. Không sửa code nghiệp vụ, không tạo/áp migration hoặc
  thay dữ liệu. Viewer tiếng Việt một phần; thông báo chưa dịch dùng tiếng Anh.

## ARCH-DOC — Sơ đồ kiến trúc source ngày 25/10/2026

- Đã tạo [sơ đồ HTML Archify](../.archify/architecture-g4-overview-20261025-155434/g4-overview.html)
  và [nguồn JSON](../.archify/architecture-g4-overview-20261025-155434/candidate.json),
  có dẫn chứng file/dòng ghim commit `e7997b03bd86bb5160332dd94bd5dc853b441963`.
  Phạm vi: frontend/proxy, API auth, nghiệp vụ/worker, SQL Server, SignalR và adapter;
  thể hiện rõ Sandbox, carrier/credit-card/payout mô phỏng và nhánh SMTP theo cấu hình.
- Archify `finalize` đạt validate/deliver/check/browser-check; Chrome kiểm tra bốn viewport
  1440–2048px, sáng/tối theo quy trình của công cụ và không tràn ngang. Chưa đánh giá
  ảnh bằng mắt; viewer tiếng Việt một phần, các thông báo còn lại dùng tiếng Anh.
  Receipt nằm cạnh artifact và trong thư mục `review-2` của cùng lần tạo sơ đồ.
- Đã cập nhật [hướng dẫn kiến trúc](BACKEND_ARCHITECTURE.md). Không sửa nghiệp vụ,
  không tạo/áp migration, không seed hoặc kiểm thử database/provider thật. Các trạng thái
  task nghiệp vụ bên dưới giữ nguyên phạm vi và bằng chứng lịch sử của từng đợt.

## UI-COUNTDOWN — Đồng hồ thao tác và tiền giữ từng đơn ngày 23/10/2026

- Đã triển khai đồng hồ tại payment, return window/receipt, phản hồi tranh chấp/receipt theo thỏa thuận, đổi địa chỉ Buyer/Seller/Shipper, xét cấp và nợ Seller/Admin. Dùng deadline server, đồng bộ giờ qua proxy, cập nhật mỗi giây; về 0 hiện chờ xử lý, không tự ghi nhận kết quả tiền/trạng thái. Không tạo deadline giả cho payout, return chưa duyệt hoặc nhánh chưa triển khai.
- Ví Seller thêm danh sách phân trang Processing/OnHold còn giữ theo đơn, snapshot level/thời gian và lý do chặn. Endpoint chỉ đọc `GET /api/seller/finance/funds/page` kiểm tra role và owner; chi tiết đơn chỉ bổ sung sellerFunds cho seller sở hữu đơn. OnHold dùng khoản giữ mới nhất sau tiêu/giải phóng, đọc nhất quán trong transaction; không tính lại bằng Net ban đầu hoặc level hiện tại. Receipt legacy thiếu deadline dùng fallback server như worker, policy dispute tách khỏi return thường.
- Bằng chứng: solution build đầu ra riêng đạt 0 warning/error; backend mặc định và Node 132/132 đạt. SellerFundsChecks kiểm tra owner, phân trang, khoản đã giải phóng/tiêu một phần, snapshot cũ, các hồ sơ cản trở, hai policy receipt khác nhau, không ghi ledger/số dư và dịch truy vấn thật sang SQL Server. Chrome Razor fixture đạt bốn role tại 320/390/1280px, lệch đồng hồ máy khách 9 giờ, reload không reset, không tràn và không request ghi; đã xem ảnh `artifacts/countdown-ui/`. Hồi quy Chrome điều hướng 320–1440px và RT1/PAY1 desktop/mobile đạt.
- Không có migration mới; không migrate/seed hoặc sửa database chính/QA. Browser dùng login/API fixture, SQL mới kiểm tra translation chưa chạy trên DB/provider thật. Log `artifacts/countdown-validation/`. Khởi động lại API/frontend và tải lại trang. [Hướng dẫn nguồn deadline/API/UI/test](DEMO_COUNTDOWNS.md).

## DEMO-TIME — Điều chỉnh mốc Development ngày 23/10/2026

- Theo yêu cầu mới: hold seller Level 1 = 300 giây (5 phút), Level 2 = 180 giây (3 phút); tự đánh giá seller level mỗi 60 giây (1 phút); xác nhận đổi địa chỉ 120 giây (2 phút). Đã cập nhật cấu hình Development, đặc tả, PROJECT_RUN và hướng dẫn tài chính/cấp seller/đổi địa chỉ/return window. Mặc định ngoài Development và các mốc demo khác giữ nguyên.
- Hold vẫn bắt đầu từ Outbound Delivered; thời lượng được snapshot khi tạo settlement. Settlement có sẵn giữ HoldSecondsSnapshot/ReleaseAt, request đổi địa chỉ có sẵn giữ ExpiresAt; không cập nhật hồi tố tiền/deadline/lịch sử. Chu kỳ xét cấp dùng cấu hình hiện hành và LastLevelEvaluatedAt, không đặt lại lịch sử đánh giá.
- Kiểm thử: solution build đầu ra riêng đạt 0 warning/error; bộ backend mặc định đạt (SMTP loopback ngoài sandbox), gồm hold 299/300 và 179/180 giây, đánh giá 59/60 giây, xác nhận địa chỉ 119/120 giây, snapshot, quyền, retry và bảo vệ tiền. Đối chiếu JSON xác nhận chỉ bốn giá trị Development thay đổi. Hai bộ SQL opt-in dùng chung fixture đã cập nhật biên 120/60 giây và kiểm tra compile; chưa chạy lại SQL/provider/browser trong đợt này. Log tại `artifacts/demo-timing-validation/`.
- Không tạo/áp migration, không seed hoặc thay database chính/QA. Khởi động lại API để chắc chắn nhận cấu hình mới. Các ghi nhận QA/lịch sử ngày 14–20/10 bên dưới vẫn giữ giá trị theo thời điểm kiểm thử.

## UI-CHECKOUT — Responsive checkout buyer ngày 23/10/2026

- Sửa phần sản phẩm/đơn hàng checkout trên điện thoại: tên shop/sản phẩm/khuyến mãi dài xuống dòng trong khung; ảnh, số lượng và giá bố trí bằng grid ở ≤600px; ô mã giảm giá và nút Áp dụng chiếm trọn hàng. Tổng kết nằm dưới sản phẩm ở ≤900px, giữ hai cột trên desktop. CSS chỉ áp dụng trong `#checkout`, nạp riêng cho Buyer.
- Đã tái hiện lỗi tràn checkout ở 320px bằng Chrome với hai seller và tên shop dài không có khoảng trắng. Sau sửa: frontend build đầu ra riêng 0 warning/error, Node 128/128 đạt; Chrome fixture Razor thật kiểm tra 320–1440px, biên 900/901px, tên dài, giảm giá, nhập/áp mã riêng seller, chuyển PayPal/thẻ, tổng tiền và nút thanh toán có thể chạm mà không tạo đơn/thanh toán. Ảnh ở `artifacts/checkout-responsive-ui/`.
- Không đổi API/logic thanh toán, không tạo/áp migration hoặc seed/thay database. Browser dùng login/cart/quote fixture, chưa kiểm thử SQL/provider thật. [Hướng dẫn module](MULTI_SELLER_CART.md#responsive-checkout-buyer-23102026).

## FORMAT — Trình bày mã nguồn ngày 23/10/2026

- Format C#, project XML, JavaScript, CSS, Razor và mã kiểm thử: căn lề, tách khối và xuống dòng tham số/điều kiện/thuộc tính dài. Mục tiêu 100 ký tự/dòng; giữ nguyên giá trị chuỗi và template literal dài. Không đổi nghiệp vụ, API hoặc cấu trúc dữ liệu; giữ thay đổi tên trang `Group 4` đã có trước phiên làm việc.
- Thêm `.editorconfig`, cấu hình CSharpier/Prettier và quy tắc trong `AGENTS.md` để các lần viết sau giữ cùng cách trình bày. Bỏ qua thư viện bên thứ ba, mã sinh tự động và migration lịch sử.
- Kiểm tra: CSharpier xác thực cây cú pháp khi format và kiểm tra lại 278 file; đối chiếu cây cú pháp JavaScript và CSS chuẩn hóa trên 83 file; Razor chỉ thay khoảng trắng. Build solution sang `artifacts/format-build/` đạt 0 warning/error; Node 128/128 và bộ backend mặc định đạt (SMTP loopback chạy ngoài sandbox). Build vào thư mục mặc định bị khóa file bởi ứng dụng đang chạy, nên dùng đầu ra riêng.
- Không tạo/áp migration, không seed hoặc thay database chính/QA; chưa chạy lại browser E2E hoặc SQL/provider. Hướng dẫn duy trì format ở [BACKEND_ARCHITECTURE.md](BACKEND_ARCHITECTURE.md#trình-bày-mã-nguồn).

## UI-NAV — Điều hướng Buyer/Seller/Admin ngày 21/10/2026

- Hoàn thành source theo yêu cầu người dùng: Buyer 4 mục, Thanh toán từ giỏ và có nút quay lại; Seller/Admin sidebar 230px có nhóm, ≤1000px dùng drawer; Buyer ≤760px dùng bottom navigation. Shipper giữ nguyên. Badge giữ ID duy nhất; native dialog quản lý focus/Escape, chọn mục đóng menu, resize chuyển cùng nav. URL fragment hỗ trợ Back/Forward/tải lại, link query cũ/PayPal return và fallback fragment không hợp lệ.
- Thay đổi ở `_RoleNavigation`, `_RoleLayout`, ba role views/scripts và CSS/JS điều hướng chung. Không thay API, quyền server, tiền hoặc workflow; đã đồng bộ đặc tả, [hướng dẫn role](AUTHORIZATION_AND_ROLE_PAGES.md#điều-hướng-ngày-21102026), PROJECT_RUN và nội dung checkout PayPal cả giỏ.
- Bằng chứng mới: `dotnet build frontend/frontend.csproj --no-restore -m:1 --disable-build-servers` đạt 0 warning/error; `node --test tests/*.test.cjs` đạt 128/128. Chrome `tests/role-navigation.e2e.cjs` dùng frontend Razor thật, login/API fixture: đủ mục, badge, Back/Forward/reload/query link, checkout/quay lại, keyboard/Escape/focus, resize drawer, không tràn ngang ở 320–1440px hoặc request ghi dữ liệu; ảnh đã xem tại `artifacts/role-navigation-ui/`. `tests/return-payout-ui.e2e.cjs` đã nối bộ điều hướng mới và hồi quy RT1/PAY1 desktop/mobile đạt.
- Không có migration mới; không seed/migrate hoặc sửa database chính/QA. Browser dùng dữ liệu giả lập, chưa E2E nối SQL/provider; không ghi nhận các task nghiệp vụ khác hoàn thành. Restart frontend/tải lại để nhận UI mới.
- Hai script E2E cấp seller/đăng ký seller đã đổi selector menu sang `data-tab` ổn định và đạt `node --check`; chưa chạy lại hai kịch bản nối API/SQL này trong đợt UI.

**Nguồn nghiệp vụ:** [BUSINESS_REQUIREMENTS.md](BUSINESS_REQUIREMENTS.md), phiên bản 2.0. Backlog này cụ thể hóa đầu vào code, không phải xác nhận đã làm. Mốc hiện trạng là đối chiếu source ngày 18/10; đợt cập nhật này chỉ sửa tài liệu, chưa chạy lại app/tests hoặc áp migration. Khi bắt đầu task phải đọc lại source mới nhất.

**Nhãn trạng thái:** `Cần triển khai` = thiếu luồng chính; `Cần điều chỉnh` = đã có nền tảng nhưng khác nghiệp vụ mới; `Giữ/hồi quy` = đã có source, cần giữ đúng; `Có source, cần xác minh triển khai` = có ghi nhận QA/migration riêng, chưa xác nhận database hiện dùng. Không đánh dấu done khi chỉ có tài liệu hoặc migration chưa nghiệm thu.

**Rà soát source/test 19/10/2026:** [báo cáo đối chiếu thực tế](IMPLEMENTATION_AUDIT_2026-10-19.md) xác nhận 7 task thiếu luồng và 5 task cần điều chỉnh bên dưới; B1/A1/S2/N1 có source và regression hiện tại đạt. Build solution đạt 0 warning/error, bộ backend mặc định đạt (SMTP loopback chạy ngoài sandbox), UI 115/115 đạt. Có 10 probe InMemory tái hiện các điểm lệch, gồm 3 lỗi server bổ sung cần sửa: lưu giỏ hơn hai seller (G1), API tạo đơn cũ nhận Quantity > 5 (G2), PayPal một đơn Verifying không giữ hạn mức (G3). Đây là audit, chưa sửa các lỗi hoặc hoàn thành task; chưa chạy SQL/browser E2E mới, không áp migration và không thay database chính. Các ghi nhận QA cũ bên dưới giữ nguyên theo từng đợt.

## 1. Danh mục và phụ thuộc

| Mã | Nội dung | Mốc hiện trạng | Đặc tả |
| --- | --- | --- | --- |
| B1 | CRUD địa chỉ/mặc định và lấy báo giá lại | Đã có source và hồi quy hợp nhất; chờ SQL QA/E2E địa chỉ | §3; bốn task ưu tiên §2; BUYER_ADDRESS_BOOK.md |
| B2 | Lịch sử payment/refund buyer | Đã gỡ 11/11/2026 theo quyết định người dùng: xóa tab, API, JS/CSS và test riêng; không migration | §5; bốn task ưu tiên §1; [PAYMENT_HISTORY.md](PAYMENT_HISTORY.md) |
| S1 | Báo cáo tài chính theo thời gian | Đã có source API/UI, test InMemory/UI, LocalDB QA riêng và HTTP role/filter; chờ QA browser, review và nghiệm thu | §2, §9; bốn task ưu tiên §4 |
| S2 | Feedback sản phẩm và trả lời | Có source, cần xác minh triển khai; giữ tách seller feedback | §2; PRODUCT_FEEDBACK.md |
| A1 | Admin tra cứu order/payment/refund | Đã có source tra cứu chỉ đọc; chờ build và QA nghiệm thu | §2; bốn task ưu tiên §3; [ADMIN_ORDER_LOOKUP.md](ADMIN_ORDER_LOOKUP.md) |
| A2 | Admin yêu cầu trả trước phán quyết | Cần triển khai; không nhầm với ReturnRefund qua thương lượng | §8 |
| N1 | Email nghiệp vụ đã bổ sung | Có source, cần xác minh triển khai; nối sự kiện mới theo từng task | §12 |
| AD1 | Đổi địa chỉ áp dụng sau đủ xác nhận | Đã triển khai/nghiệm thu source 20/10: backend, SQL QA và browser fixture đạt; migration chưa áp database chính | §6; ORDER_ADDRESS_CHANGES.md |
| ADDR2 | Gợi ý địa chỉ VietMap, SĐT theo địa chỉ, phí ship theo vùng | Source 11/11: backend/Node/VietMap thật/browser fixture đạt; migration đã áp `G4_Demo`, chưa áp database chính | §3.1, §6; mục ADDR2 đầu file |
| SH1 | ReattemptScheduled và actor từng event | Đã triển khai/nghiệm thu source 20/10: backend, SQL QA và browser fixture đạt; migration chưa áp database chính | §6; RETURNS_AND_SHIPPING.md |
| RT1 | Return window và snapshot deadline | Đã triển khai/nghiệm thu source 20/10: backend, SQL QA và browser fixture đạt; migration chưa áp database chính | §7, §12.1; RETURN_WINDOW_POLICY.md |
| RT2 | Đóng/kháng nghị return từ chối | Cần triển khai deadline/worker; chưa có nhánh đủ theo RejectedAt | §7 |
| INR1 | Hồ sơ chưa nhận hàng | Cần triển khai; OpenAsync hiện yêu cầu Delivered | §8.1 |
| FR1 | Free returns theo mặt hàng | Cần triển khai policy/snapshot/phân phí; đã có giá return theo lý do | §3, §7 |
| LIM1 | Admin override hạn mức seller | Cần triển khai; mức hiện từ cấu hình ứng dụng theo level | §10 |
| PAY1 | Payout chung mọi level | Đã triển khai/nghiệm thu source 20/10: backend, SQL QA và browser fixture đạt; migration chưa áp database chính | §9; PAYOUT_WORKFLOW.md |
| LED1 | Ledger chỉ thêm và đối soát nguồn/đích | Cần củng cố; có FinancialTransaction/key, chưa có guard bất biến tổng quát | §9 |
| FS1 | Free shipping dùng promotion hiện có | Giữ/hồi quy; Shipping/FreeShipping đã có source | §4 |
| BASE | Kg lẻ, policy phí, hold, level, nợ/write-off | Giữ/hồi quy theo quy tắc 18/10 | §3.1, §9–11 |

Thứ tự thực hiện theo task được giao. B2/B1/A1/S1 giữ thứ tự ưu tiên đã ghi ngày 17/10 nếu không có chỉ đạo mới. Với các luồng mới: RT1 trước RT2; FR1 cung cấp snapshot cho giá return/A2; SH1 và ngày giao dự kiến phục vụ INR1; LIM1 phải xử lý capture reservations; PAY1 phối hợp LED1 và N1. Đổi deadline/state phải đồng bộ tất cả consumer trong cùng thay đổi, tránh patch chỉ UI.

## 2. B1/B2/S1/A1 và phần đã có

Chi tiết UI, filter, ngày tính tổng, phân trang và quyền tiếp tục theo [FOUR_TASK_REQUIREMENTS_2026-10-17.md](FOUR_TASK_REQUIREMENTS_2026-10-17.md). Không viết lại giỏ/catalog hoặc sửa tay giao dịch tài chính để làm báo cáo.

- **B1:** xem `CatalogController`, `buyer-cart.js`, `buyer.js`. Tối đa một mặc định/buyer, thêm đầu tiên mặc định, xóa mặc định chọn ID còn lại nhỏ nhất; chỉ chọn checkout hiện hành quyết định địa chỉ giao. Không làm mất tham chiếu/snapshot order khi xóa địa chỉ. Báo giá cũ phải vô hiệu khi địa chỉ thay/xóa, gồm điều kiện promotion; chặn checkout khi chưa có quote mới.
- **B2:** dùng Payment/Refund, quan hệ order và provider reference; hiện Order detail chưa phải màn lịch sử xuyên đơn. Tách ngày tạo dùng filter danh sách với PaidAt/CompletedAt dùng tổng trong kỳ; pending/failed/Verifying không thành tiền đã thu/hoàn. Tổng vòng đời order không bị cắt bởi thời gian/trang.
- **S1:** đọc settlement/ledger và funding snapshot; không cộng đơn chưa trả tiền. Trình bày gross/refund/fee/subsidy/điều chỉnh/net, biểu đồ/bảng ngày/tuần/tháng; net khác Available/PaidOut. Không trừ refund/fee hai lần.
- **A1:** API admin toàn cục chỉ đọc, tìm order/provider capture/refund/buyer/seller, filter/phân trang; che thông tin nhạy cảm. Không nới quyền order detail cho vai trò khác ngoài phạm vi cần thiết.
- **S2/N1:** giữ review theo product khác SellerOrderFeedback, trả lời đúng quyền; giữ outbox transaction/key/người nhận/retry. Xem PRODUCT_FEEDBACK và NOTIFICATION_COMPLETION để xác minh migration/QA thực tế, không tự coi database chính đã nâng.

**Hợp nhất S1 vào master ngày 21/10/2026:** xử lý conflict mục lục tài liệu và script Seller, giữ cả báo cáo tài chính lẫn PAY1. Build solution đạt 0 warning/error; backend `--seller-finance-report` và `--payout-workflow` đạt; UI Node 128/128 đạt. Không tạo/áp migration, không thay database; chưa chạy lại SQL/HTTP/browser. Hai lỗi đã review (đọc báo cáo thiếu snapshot nhất quán khi có giao dịch đồng thời, bật/tắt chuỗi làm hiện biểu đồ cũ sau lỗi tải) chưa sửa trong đợt hợp nhất này; S1 chưa được nghiệm thu hoàn tất.

**Hợp nhất hoanglt/hungntm 19/10/2026:** đã tích hợp B1 và A1, giữ cấu hình frontend HTTP 5251 và thay đổi tên hiển thị. Sửa xóa địa chỉ có tham chiếu bằng lưu trữ ngoài sổ, giữ nguyên AddressId/snapshot/tiền và checkout idempotency; khóa form địa chỉ khi đang gửi; cập nhật harness giỏ nạp module địa chỉ thật. Build solution pass 0 warning/error, bộ backend mặc định pass, 115/115 test UI pass. QA Chrome desktop/mobile admin bằng fixture pass. Chưa SQL QA/E2E CRUD địa chỉ trên DB thật; không áp migration hoặc thay database chính. Xem [BUYER_ADDRESS_BOOK.md](BUYER_ADDRESS_BOOK.md).

Nghiệm thu: success/failure/verifying/refund pending; tổng đúng toàn bộ phạm vi, page/filter ổn định; truy cập chéo bị chặn; CRUD địa chỉ không sửa đơn cũ; báo cáo khớp settlement và ledger.

**A1 cập nhật 19/10/2026:** đã thêm API admin chỉ đọc, tìm kiếm đa mã giao dịch, lọc nhanh/trạng thái, phân trang theo đơn và chi tiết payment/refund/tracking. Solution build thành công; chưa chạy QA/test nghiệp vụ; xem [ADMIN_ORDER_LOOKUP.md](ADMIN_ORDER_LOOKUP.md). Không phát sinh migration.

**A1 sửa tìm mã/UTC và UX 19/10/2026:** hoàn thành hai lỗi được giao: mã đơn tách khỏi ID payment/refund, provider exact-match; timestamp DTO có UTC/Z và UI giữ offset. Đã thiết kế lại tìm kiếm có loại mã, lọc nâng cao thu gọn, nhãn điều kiện, danh sách responsive và chi tiết chia tab. Build output riêng và backend mặc định pass, test A1 InMemory/SQL translation pass, 113/113 test UI pass; QA trình duyệt bằng fixture desktop/mobile pass. Chưa E2E trên SQL Server thật cho đợt này, không áp migration; không tuyên bố hoàn tất toàn bộ nghiệm thu A1. Xem [ADMIN_ORDER_LOOKUP.md](ADMIN_ORDER_LOOKUP.md).

## 3. AD1 — Xác nhận địa chỉ trước khi áp dụng

**Hoàn thành source ngày 20/10/2026:** buyer xem/xác nhận quote, seller duyệt, shipper đã nhận chấp thuận khả năng giao trước khi đổi snapshot/tiền; chưa có shipper thì seller duyệt cuối. Chặn OutForDelivery khi chờ, deadline snapshot 24 giờ/Development 60 giây, stale cap/shipper/quote yêu cầu xác nhận lại. Request Approved cũ giữ nguyên. Build/backend, UI 120/120, SQL cạnh tranh/rollback và browser fixture desktop/mobile đạt. Migration chỉ áp `G4_AddressConfirmation_Test_20261020`, chưa áp database chính. Xem [ORDER_ADDRESS_CHANGES.md](ORDER_ADDRESS_CHANGES.md).

**Điểm bắt đầu:** `AddressChangeService`, `ShipmentService`, `AddressChangesController`, `OrdersController`, UI address-changes/seller/shipper, snapshot và migration liên quan.

- Buyer gửi lý do/địa chỉ và xác nhận quote; seller chấp thuận; shipper đã nhận chấp thuận khả năng giao. Chưa gán shipper thì không chờ một shipper chưa tồn tại.
- Trong lúc chờ, địa chỉ cũ hiệu lực. Chặn bắt đầu OutForDelivery khi đang chờ; deadline chờ cấu hình được, hết hạn/từ chối giữ nguyên địa chỉ/lượt đổi. Không giữ vô thời hạn.
- Chỉ sau đủ xác nhận mới ghi snapshot order/shipment, chi phí/refund/bộ đếm; khóa/đọc lại order, shipper, cap và giá. Đổi shipper/cap/quote quan trọng làm mất hiệu lực xác nhận cũ.
- Tăng carrier cost trong cap: seller chịu; vượt cap từ chối, không thêm payment buyer. Giảm carrier cost không tự thành refund buyer; tối đa phần ship buyer thực trả còn được hưởng sau promotion/refund.
- Migration lưu các xác nhận/actor/deadline/trạng thái; tên state mới chọn khi code, cập nhật UI/query/worker theo cùng mô hình. Request cũ đã Approved giữ snapshot/kết quả cũ, không áp lại chi phí hoặc refund.

**Nghiệm thu:** seller approve nhưng shipper chưa approve chưa đổi địa chỉ; chưa shipper thì approve kết thúc hợp lệ; reject/timeout không mất lượt; lần thành công thứ hai bị chặn; đồng thời OutForDelivery/cap/shipper thay đổi không áp quote cũ; free ship không tạo refund khoản chưa thu; retry không nhân chi phí/thông báo.

## 4. SH1 — Tracking giao lại và actor

**Hoàn thành source ngày 20/10/2026:** bắt buộc bước lên lịch, thời gian UTC/lý do và actor server cho cả hai chiều; lịch không tăng lượt, chỉ bắt đầu giao mới tăng (tối đa hai). Lịch là dự kiến, không tự kích hoạt khi đến giờ. Migration thêm cột nullable giữ lịch sử, không suy diễn actor cũ. Build/backend, 117/117 UI, SQL cạnh tranh/rollback và browser fixture desktop/mobile đạt; migration chỉ áp database QA riêng, chưa áp database chính. Bằng chứng và cách chạy ở [RETURNS_AND_SHIPPING.md](RETURNS_AND_SHIPPING.md#sh1--lên-lịch-giao-lại-và-actor-20102026).

**Điểm bắt đầu:** `ShippingState`, `ShipmentService.RecordEventAsync`, ShippingEvent/ShippingInfo, contract/UI/timeline, các shipping tests.

- Bắt buộc `DeliveryFailed → ReattemptScheduled → OutForDelivery`; bỏ đường giao lại trực tiếp. Lưu lịch giao lại dự kiến và lý do.
- Tăng DeliveryAttempts khi bắt đầu OutForDelivery, tối đa hai lượt mặc định; lên lịch không tăng. Chuyển hoàn qua ReturningToSender/ReturnedToSeller theo event thực tế.
- Cùng state machine cho Direction Outbound/Return. Event có actor phía server và thời gian UTC, location/note, event ID unique; không nhận actor giả từ client. Legacy event giữ được đọc, không gán bừa actor lịch sử.
- Cập nhật query “cần hành động”, claimability, UI, log/thông báo/realtime khi thêm state. Không biến ReturnedToSeller thành Delivered buyer.

**Nghiệm thu:** không nhảy bước, lịch không tiêu lượt, lượt ba bị chặn; chỉ shipper được gán thao tác; event lặp không tăng lượt; tracking return không tăng số đơn giao thành công cho buyer.

## 5. RT1 — Deadline dùng chung và return demo 5 phút

**Hoàn thành source 20/10/2026:** snapshot cửa sổ tại Outbound Delivered, Development 300 giây/mặc định 7 ngày; quote/request/dispute/worker/UI dùng chung deadline; legacy giữ 7 ngày và request cũ không bị viết lại. Receipt vẫn 45 giây; request/dispute hợp lệ tiếp tục sau cửa sổ. Rejected return được bảo vệ chuyển tiếp, RT2 deadline/auto-close vẫn riêng. Build/backend, SQL race/rollback và Chrome fixture đạt; UI cả đợt 125/125. Migration chỉ áp QA, chưa áp database chính. Xem [RT1](RETURN_WINDOW_POLICY.md).

**Điểm bắt đầu:** `ReturnQuoteBuilder`, `ReturnService`, `CheckoutService.CloseDeliveredOutsideReturnWindowAsync`, `DisputeService`, Orders/return UI, configuration và test TimeProvider.

- Return window mặc định 7 ngày, Development 300 giây từ outbound DeliveredAt. Dùng chính sách chung cho quote/request/close và điều kiện dispute liên quan; tìm hết hardcode `AddDays(-7)` trước sửa.
- Không đổi Returns:ReceiptSeconds sang 300: receipt là 48 giờ/Development 45 giây từ return Delivered. Disputes:ReturnReceiptSeconds phục vụ nhánh thỏa thuận phải khớp receipt policy liên quan.
- Lưu deadline/policy version theo order/hồ sơ; cấu hình đổi không reset deadline hoặc sửa quyền lợi đã snapshot. Chốt cách backfill legacy trước migration; không suy diễn bản cũ là demo 5 phút khi chưa lưu bằng chứng đó.
- Hồ sơ mở đúng hạn vẫn tiếp tục sau cửa sổ. RT2 từ RejectedAt và INR từ estimated delivery không bị cắt bởi return window.

**Nghiệm thu:** các mốc sát hạn server/UI/quote/worker nhất quán, không chỉ thay text; mock thời gian thay vì ngủ 5 phút; không đóng đơn khi return/dispute/quyền phản đối còn cản trở; receipt vẫn 45 giây; deadline cũ không thay khi config đổi.

## 6. RT2 — Return bị từ chối và quyền phản đối

**Điểm bắt đầu:** `ReturnService.RejectAsync/MaintainAsync`, `DisputeService.OpenAsync`, ReturnRequest workflow, list/UI, worker và notifications.

- RejectedAt và deadline phản đối 3 ngày, Development 45 giây bằng cấu hình riêng; hiển thị trong quyết định/email.
- Buyer chấp nhận hoặc im lặng hết hạn thì Closed đúng một lần và xử lý phần giữ tiền theo điều kiện hiện tại, không tự tạo nhãn/refund.
- Phản đối đúng hạn tạo/liên kết dispute và chặn tự đóng/release, kể cả ngoài Delivered + return window. Không cho mở return trùng để reset deadline.
- Khóa/đọc lại khi race buyer phản đối với worker đóng; cần một kết quả nhất quán. Legacy rejection cần chính sách backfill/chuyển tiếp, không tự đóng hàng loạt vì deadline suy diễn.

**Nghiệm thu:** rejection không refund; im lặng đóng một lần; phản đối đúng hạn không bị gate Delivered + 7 ngày chặn; phản đối/worker đồng thời không vừa đóng vừa tạo quyết định tài chính khác nhau.

## 7. INR1 — Chưa nhận hàng

**Điểm bắt đầu:** Dispute/DisputeEntry/contracts/controller/UI, order/shipment estimated date, finance hold, deadlines và notifications.

- Snapshot/hiển thị ngày giao dự kiến; order đã trả tiền và quá hạn được báo chưa nhận. Loại INR khác return dispute, không dùng gate chỉ Delivered hiện có.
- Cửa sổ từ ngày giao dự kiến: mặc định 30 ngày, Development 300 giây; cấu hình riêng. Đây là mặc định mô phỏng, không mặc nhận thành policy eBay mọi thị trường.
- Buyer chỉ cần mô tả; không bắt URL ảnh chứng minh không nhận. Seller nộp tracking/bằng chứng hoặc đề xuất; quá hạn/chưa thống nhất chuyển admin theo deadline hồ sơ.
- Không tạo return shipment khi buyer không có hàng. Không auto-refund lúc mở; tracking Delivered nhưng buyer phủ nhận vẫn được xét bằng chứng khi đủ điều kiện. Một dispute mở/order, không refund xung đột.
- Hàng đến muộn ghi bổ sung, buyer xác nhận/admin xem xét; không tự đóng vì carrier event hoặc tự thu lại sau refund. Trạng thái carrier/order không đồng nghĩa kết quả hồ sơ/tiền.

**Nghiệm thu:** đơn chưa Delivered vẫn mở được khi quá ngày dự kiến; chưa đến ngày bị chặn; đã Delivered bị phủ nhận có luồng xem xét; không bắt URL buyer; không return giả; hold/refund theo quyết định và retry không trùng.

## 8. FR1 — Free returns theo mặt hàng

**Điểm bắt đầu:** policy Product/contract có giới hạn, OrderItem snapshot, checkout/cart UI, ReturnPricing/ReturnQuoteBuilder, DisputeDecisionPricing/finance, migration và tests.

- Hiển thị policy trước mua trong giỏ random/checkout; seller chỉ sửa policy của mặt hàng mình bằng UI/API gọn phục vụ task, không bổ sung catalog/CRUD toàn diện.
- Snapshot policy/version và khối lượng theo item; đơn cũ cần phiên bản tương thích, không hồi tố thành free returns khi seller đổi policy.
- Đổi ý: seller chịu phần free returns, buyer chịu phần non-free. Một nhãn toàn order phân theo trọng lượng snapshot, xử lý cent dư xác định để tổng đúng nhãn. Lỗi seller xử lý theo trách nhiệm bất kể cờ free.
- Free returns chỉ miễn phí chiều trả, không tự hoàn ship đi khi đổi ý. Quote ghi rõ buyer thực nhận, phí mỗi bên, ship đi giữ lại, funding; xác nhận trước request. Không tạo refund âm.
- Không thu nhãn hai lần; buyer chịu phần nào thì ReturnShippingRecovery chỉ phục hồi phần đó sau refund thành công. Tôn trọng policy/subsidy/fee credit, tránh trừ fee/refund hai lần.

**Nghiệm thu:** ví dụ hàng 100, ship đi 5, nhãn 5: đổi ý non-free nhận 95; all-free nhận 100, seller chịu nhãn; lỗi seller full nhận 105. Đơn hỗn hợp phân phí đúng tổng/cents; đổi policy sau mua không đổi quote cũ; buyer không nhận tiền ship chưa trả. Giữ FS1 shipping promotion hiện có.

## 9. LIM1 — Admin hạn mức riêng seller

**Điểm bắt đầu:** SellerAccount/level policy, SellerFinanceService monthly validation, PayPal single/cart reservations, admin API/UI và audit history.

- Theo level là mặc định 5.000/10.000/1.000.000 USD/tháng UTC theo SellerGross thành công. Override per seller ưu tiên khi hiệu lực; số tiền dương, reason/actor/trước-sau/ngày lưu/ngày hiệu lực và key chống trùng.
- Hiệu lực hiện tại/tương lai; không sửa/xóa lịch sử, có kết thúc override quay lại mặc định. Đổi level không ngầm bỏ override còn hiệu lực; cấm seller/client tự đặt.
- Hạ dưới doanh số đã đạt không sửa/hủy giao dịch cũ. Chặn thu mới vượt mức, tính capture reservations, kiểm tra dưới khóa; payment đã capture theo reservation hợp lệ phải ghi nhận/đối soát đúng.
- Không đổi logic tự xét cấp hoặc lịch hold/fee snapshot của settlement cũ. Làm rõ override có hiệu lực ở bước kiểm tra nào và lưu bằng chứng mức đã áp dụng.

**Nghiệm thu:** immediate/future policy đúng thời điểm; kết thúc override về level; replay key không nhân audit; seller bị từ chối; cap giảm giữa capture không bỏ tiền đã thu; hai payment đồng thời không vượt reservation; tháng UTC mới reset doanh số đúng.

## 10. PAY1 và LED1 — Payout và ledger

**PAY1 hoàn thành source 20/10/2026; LED1 vẫn còn riêng.** Mọi level payout qua Created/InProgress/FundsSent/Completed hoặc Returned, lưu lịch sử actor/UTC chỉ thêm, tổng đang chuyển/đã chi và terminal-only email/realtime. Level 3 vẫn instant vào ví; legacy giữ kết quả, không backfill lịch sử giả hoặc trừ tiền lại. Build/backend, SQL concurrent/rollback và Chrome fixture đạt; UI 125/125. Migration chỉ áp QA, chưa áp database chính. [PAY1](PAYOUT_WORKFLOW.md). Guard mới bảo vệ SellerPayoutEvent, chưa phải guard/đối soát toàn bộ FinancialTransaction của LED1.

**Điểm bắt đầu:** SellerFinanceService.RequestPayoutAsync/AdvancePayoutsAsync/AddEntry, FinancialTransaction, SellerPayout, DbContext guards, finance UI, notifications và kiểm thử.

- Mọi level tạo Created và đi InProgress/FundsSent/Completed; bỏ shortcut Level 3 Completed tức thời ở request. Net Level 3 vào Available ngay vẫn giữ nguyên.
- Pending payout trừ Available atomic, không thể rút lại khoản đó; tính tổng đang chuyển từ các payout chưa kết thúc thay vì bắt buộc thêm bucket trùng. Dùng ngân hàng ảo/đích che, không thêm ngân hàng production hoặc payout theo lịch.
- Returned chỉ trả hệ thống một lần, sau đó bù nợ; nếu có bù nợ không hứa toàn bộ sẽ còn Available. Worker race/retry không tạo tiền; payout đã Completed/Returned cũ không chạy lại.
- Bổ sung lịch sử chuyển bước/actor; email Completed/Returned chỉ theo kết quả đã commit, giữ EventKey/RecipientUserId và quyền N1.
- Ledger chỉ thêm, guard sửa/xóa FinancialTransaction, adjustment có lý do/tham chiếu. Chuyển nguồn/đích cùng transaction, khóa duy nhất, đối soát và rollback. Không bổ sung dòng đối ứng rồi vô tình tính đôi các report/hold đang đọc ledger cũ; cần version/compatibility và hồi quy các consumer.
- Không viết lại toàn bộ thành kế toán kép. Test meaningful cho tiền/đồng thời; không kiểm tra chỉ vì tên field tồn tại.

**Nghiệm thu:** Level 3 chỉ instant vào ví; mọi payout qua đủ bước; 87,70 rút 50 còn Available 37,70 + đang chuyển 50; Returned hoàn 50 một lần trước bù nợ. Không sửa/xóa lịch sử, đối soát nguồn/đích và không trừ phí/hoàn hai lần; trạng thái terminal cũ giữ nguyên.

## 11. A2 — Admin yêu cầu trả trước kết luận

- Dùng DisputeService/ReturnService/ShipmentService/finance hiện có; chỉ admin đúng phạm vi có quyền yêu cầu, ghi actor/time/reason/căn cứ.
- Tạo hoặc dùng lại return/nhãn phù hợp, không nhân phí; có trạng thái nhãn lỗi/đang trả/đã về/giao trả thất bại và hướng dẫn buyer/seller.
- Giữ hold, không coi yêu cầu trả là phán quyết cuối. Chờ kết quả hàng về rồi admin tiếp tục SellerWins/FullRefund/PartialRefund theo preview và policy FR1.
- Worker receipt thường không tự refund trong nhánh đang chờ admin. Refund lỗi giữ pending và retry, không đóng giả. Nối outbox/realtime với N1, đúng người nhận và chống trùng.

**Nghiệm thu:** nhánh mới và tái sử dụng hồ sơ cũ; nhãn lỗi/retry không tạo hai shipment/fee; return Delivered chưa tự refund/đóng dispute; admin quyết định tiếp đúng tiền; giao trả thất bại còn hồ sơ để xử lý.

## 12. Hoàn thành và cập nhật trạng thái

Mỗi task khi hoàn thành ghi ngay bên dưới phần của task hoặc thêm bản ghi ngày: thay đổi/API/config/migration, kiểm thử đã chạy, QA database, migration chính đã/chưa áp, giới hạn còn lại. Đồng bộ hướng dẫn module và PROJECT_RUN khi hành vi chạy thực tế thay đổi. Đánh dấu hoàn thành chỉ khi đạt nghiệm thu phù hợp, không dựa vào các kết quả test cũ trong docs.

Đợt **18/10/2026 cập nhật tài liệu**: chưa task mới nào ở trên được ghi hoàn thành; không sửa code, không đổi cấu hình runtime và không áp database migration. Các thay đổi source chưa commit của S2/N1 và phần khác được giữ nguyên.

## 13. G1/G2 — Giới hạn seller và số lượng tại server

**19/10/2026 — đã sửa và đạt regression source.** G1: SaveAsync đếm seller trên toàn bộ sản phẩm được giữ lại trước khi ghi giỏ, chặn hơn hai seller và giữ nguyên version khi từ chối; giỏ cũ sai có thể sửa xuống một/hai seller hoặc xóa hết, giữ khả năng xóa hàng không còn bán được. G2: ValidateAndQuoteAsync kiểm tra số lượng 1–5 cho cả báo giá và tạo đơn mới qua API một seller; retry đúng đơn đã tồn tại giữ snapshot và không trừ tồn lần nữa. ArgumentException được handler hiện có ánh xạ HTTP 400; không thay quyền API hoặc công thức tiền.

- Thêm `CheckoutLimitChecks` vào backend mặc định và tùy chọn `--checkout-limits`. Kiểm tra seller thứ ba gồm dòng không đổi/giỏ cũ, giỏ một/hai seller/rỗng; số lượng -1/0/6/int.MaxValue ở save/quote/create; biên 1/5 hợp lệ; không ghi giỏ/version/stock/order/batch/payment/promotion usage/ledger/outbox khi từ chối; idempotency và snapshot lịch sử.
- Test mới tái hiện G1 trước sửa; sau sửa test riêng đạt. Solution build output `artifacts/checkout-limits-validation` đạt 0 warning/error; backend mặc định đạt, gồm SMTP loopback ngoài sandbox; UI 115/115 đạt.
- Không phát sinh migration; không áp migration/seed và không thay database chính. Chưa SQL QA hoặc HTTP/browser E2E mới. Cần khởi động lại API để dùng source mới. Chi tiết tại [MULTI_SELLER_CART.md](MULTI_SELLER_CART.md).
- G3, AD1/SH1/RT1/PAY1/LED1 và các task còn lại chưa được sửa trong đợt này. Báo cáo audit phía trên giữ ghi nhận tại thời điểm trước bản sửa.

## 14. G3 — Reservation hạn mức PayPal một đơn

**19/10/2026 — đã sửa và đạt regression InMemory/SQL QA.** Single-order capture kiểm tra và ghi reservation SellerGross/Verifying trong cùng transaction giữ seller gate, commit trước gọi provider. Không đủ hạn mức thì Payment vẫn Pending và không capture; payment khác (PayPal/card) tính cả reservation đang chờ. Settlement một đơn tôn trọng reservation hợp lệ như cả giỏ; không kiểm tra lại hạn mức mới để bỏ lửng tiền đã thu. Failed chắc chắn giải phóng phần giữ bằng trạng thái, giữ field làm lịch sử; retry/new key, reconcile, timeout hoặc client cancellation không nhân thu/settlement/ledger/outbox. Legacy Verifying không có reservation vẫn tính vào phần giữ, Capture chỉ query provider; reservation còn chờ không mất chỉ vì sang tháng UTC.

- Build solution output `artifacts/paypal-reservation-validation` đạt 0 warning/error; backend mặc định đạt, gồm `PayPalReservationChecks` mới và SMTP loopback ngoài sandbox; UI Node 115/115 đạt.
- `--paypal-reservations`: giữ trước provider, chặn PayPal/card cạnh tranh, không để Pending thành Verifying khi preflight lỗi; các kết quả từ chối giải phóng đúng; subsidy tính trong SellerGross; retry/cap giảm/hết hạn; phản hồi sai tiền; client cancellation; tháng UTC và dữ liệu legacy.
- `--paypal-reservations-sql`: đã chạy trên `G4_PayPalReservation_Test_20261019`; hai request thật qua context riêng tranh 150 USD cho hai đơn 100 USD chỉ một request được tới gateway. Xác minh reservation commit trước provider, thả khóa trước chờ network, rollback settlement/payment/order/outbox, reconcile hoàn tất không capture lại, và race terminal success/failure với timeout đến muộn.
- Không có migration mới; đã bootstrap baseline khi QA riêng trống và áp các migration hiện có lên QA đó. Không seed/migrate/thay database chính. Provider dùng gateway giả, chưa PayPal Sandbox ngoài hệ thống hoặc browser E2E mới. Cần khởi động lại API; xem [COMMERCE_REFRESH.md](COMMERCE_REFRESH.md).
- G1/G2/G3 đã sửa trong source. AD1/SH1/RT1/PAY1/LED1 và các task thiếu luồng vẫn giữ trạng thái riêng, không được đánh dấu hoàn thành theo kết quả G3.

## 15. SH1 — Giao lại có lịch và actor ngày 20/10/2026

- Đã bỏ chuyển trực tiếp DeliveryFailed → OutForDelivery. Shipper được gán nhập lịch tương lai/lý do, sau đó bắt đầu giao thực tế; hai chiều dùng chung state machine. Hết hai lượt vẫn phải ghi ReturningToSender → ReturnedToSeller thực tế.
- Event lấy actor từ server, lưu UTC và snapshot lịch; event ID trùng cùng payload là no-op, khác payload bị từ chối. Thêm bộ lọc/claimability, timeline buyer/seller/shipper/admin, email theo từng shipment/event/người nhận và realtime từ transaction đã commit. UI giữ payload/event ID khi kết quả mạng chưa rõ để thử lại an toàn.
- Migration `20261020150408_AddShipmentReattemptSchedulesAndActors` và [SQL để duyệt](../database/shipment-reattempt-actors-migration.sql) chỉ thêm cột nullable/index/FK. Lịch sử cũ giữ actor null, timestamp/note/lượt giao không đổi. Đã áp và kiểm tra trên `G4_ShippingReattempt_Test_20261020`; **chưa áp database chính**, không chạy baseline/seed lên database chính.
- Build solution output `artifacts/shipping-reattempt-validation` đạt 0 warning/error; backend mặc định đạt (gồm SH1 và SMTP loopback), UI Node 117/117 đạt. SQL QA thật đạt: bảo toàn legacy, hai chiều, hai request lên lịch/bắt đầu đồng thời chỉ áp một lần, quyền shipper, claim/filter, giới hạn lượt và rollback shipment/event/email/realtime.
- Browser fixture dùng Razor/CSS/JS thật đạt trên Chrome 1440px/390px, cả Outbound/Return: validation bắt buộc/tương lai, UTC, thử lại cùng key sau lỗi mạng, timeline/actor, escape note, không tràn ngang. Ảnh `artifacts/shipping-reattempt-ui/`. Đây là API fixture, không phải E2E HTTP + SQL hoặc gọi carrier thật.
- AD1/RT1/PAY1/LED1 và các task thiếu luồng khác chưa làm trong đợt SH1. Báo cáo audit ngày 19/10 vẫn giữ bằng chứng trước sửa.

## 16. AD1 — Xác nhận địa chỉ trước áp dụng ngày 20/10/2026

- Đã có quote server và buyer xác nhận trước tạo request; state Pending → AwaitingShipper → Approved khi đã gán shipper, hoặc Pending → Approved khi chưa gán. Rejected/Expired không đổi địa chỉ/tiền/lượt. Chặn bắt đầu giao khi còn chờ; lưu actor/deadline/applied time phía server.
- Apply cuối kiểm tra lại trạng thái/snapshot, cap/revision, shipper, quote/payment/refund còn được hưởng. Cap update/claim cùng gate; stale request bị từ chối để buyer xác nhận lại. Free shipping dùng promotion hiện có, không refund khoản buyer chưa trả; mọi chi phí/refund mô phỏng và outbox/realtime cùng transaction, retry an toàn.
- Migration `20261020153620_AddAddressChangeConfirmations` và [SQL duyệt](../database/address-change-confirmations-migration.sql): thêm confirmation/deadline/workflow/revision, unique active request theo IsPending; giữ Approved cũ/actor null, Pending cũ có hạn CreatedAt + 24 giờ và cần quote mới. Chỉ bootstrap/migrate QA `G4_AddressConfirmation_Test_20261020`, **chưa áp database chính**.
- Build output `artifacts/address-confirmation-validation` đạt 0 warning/error; backend mặc định đạt (gồm AD1/SH1/SMTP loopback); UI Node 120/120 đạt. AD1 SQL thật đạt request/seller/shipper concurrent, cuối cùng ghi refund/cost một lần, race cap/claim/start/expiry và rollback sau finance SaveChanges rồi retry. Model/snapshot không thiếu migration.
- Browser fixture buyer/seller/shipper Chrome 1440px/390px đạt, gồm quote-before-submit, chờ shipper chưa đổi địa chỉ, accept/refuse và trạng thái/thao tác sau quyết định, escape, không lỗi JS/tràn ngang; ảnh đã xem ở `artifacts/address-confirmation-ui/`. Không phải E2E HTTP nối SQL hoặc provider/carrier thật. Không chạy lại QA SQL 13/10 hoặc thay database chính.
- Chỉ làm AD1 trong đợt này; RT1/PAY1/LED1 và bảy task thiếu luồng từ audit còn phạm vi riêng. Xem [hướng dẫn AD1](ORDER_ADDRESS_CHANGES.md).


## 17. RT1/PAY1 — Deadline trả hàng và payout ngày 20/10/2026

- RT1 đã dùng ReturnWindowPolicy chung cho quote/API cũ/mới/dispute/close, snapshot từ Outbound Delivered: 7 ngày/Development 300 giây, đúng hạn không mở mới; thay cấu hình không đổi đơn đã giao. Legacy giữ quyền 7 ngày và ReturnRequest.ReturnDeadline cũ. Request đúng hạn tiếp tục, receipt 45 giây riêng. Không đóng return/dispute còn cản trở; quyền phản đối rejection được bảo vệ chuyển tiếp, chưa triển khai RT2 deadline/auto-close.
- PAY1 đã bỏ Level 3 Completed tức thời: mọi level đi qua ngân hàng mô phỏng, Created/InProgress/FundsSent/Completed hoặc Returned. Ví trừ một lần, tổng pending/paid-out toàn bộ payout; history actor/UTC chỉ thêm và chống trùng, email terminal/realtime atomic, Returned bù nợ một lần. Legacy terminal không chạy lại; pending cũ tiếp tục từ bước thực, không bịa lịch sử hoặc trừ ví lại. Level 3 vẫn vào ví ngay sau payment/bù nợ; LED1 FinancialTransaction guard/đối soát vẫn chưa làm.
- Migration `20261020162229_SnapshotReturnWindowDeadlines` và `20261020162647_UnifyPayoutWorkflowAndHistory`; SQL duyệt [RT1](../database/return-window-deadlines-migration.sql), [PAY1](../database/payout-workflow-history-migration.sql). Chỉ bootstrap/migrate **G4_ReturnWindowPayout_Test_20261020**, **chưa áp database chính**, không reset dữ liệu chính.
- Build output `artifacts/return-window-payout-validation` 0 warning/error, backend mặc định đạt gồm các module trước và SMTP loopback. Test RT1 299/300 giây, policy cũ/mới, replay, tiếp tục return/appeal và receipt riêng; PAY1 ba level, số dư/pending/paid-out, lịch sử/actor, debt recovery, legacy/retry/guard. Hai fixture cũ đã dùng đồng hồ giả nhất quán cho DeliveredAt thay vì thời gian thực chạy sau thời điểm fake clock.
- SQL QA thật đạt bảo toàn migration/legacy, request/close cạnh tranh, snapshot/event rollback, payout cùng key và hai key tranh tiền, worker concurrent đủ bước, DTO/history/tổng, rollback request/Completed/Returned sau SaveChanges và retry. UI Node 125/125 đạt.
- Chrome fixture desktop 1440px/mobile 390px dùng dialog return và Seller Razor/CSS/JS thật: deadline, hết hạn vô hiệu quote, request payout, pending balances, các bước/history, Completed/Returned; không lỗi JS hoặc tràn ngang, ảnh đã xem tại `artifacts/return-window-payout-ui/`. Đây là API fixture, chưa E2E HTTP nối SQL hoặc carrier/bank/provider thật.
- Đã cập nhật [RT1](RETURN_WINDOW_POLICY.md), [PAY1](PAYOUT_WORKFLOW.md), module và PROJECT_RUN. Không làm các task khác hoặc áp migration chính trong đợt này.
## Khám phá ưu đãi phía Buyer

- Đã cập nhật source cho tab Ưu đãi, công khai coupon theo lựa chọn của bên quản lý
  và xem trước báo giá theo seller. Build API/frontend, 29 test JavaScript và
  kiểm tra API với EF InMemory (`--buyer-offers`) đã qua. Nghiệm thu SQL Server và trình duyệt theo
  [hướng dẫn Promotions](PROMOTIONS_TEST_GUIDE.md#kiểm-tra-tab-ưu-đãi-và-bộ-chọn-mã)
  còn chờ. Migration `20261103090000_AddPublicCoupons` chưa áp dụng.

## BIN-IP — Immediate Payment (08/11/2026, chưa nghiệm thu đầy đủ)

- Có source Seller cấu hình/create, Buyer Buy It Now/checkout/Pay Now, snapshot
  không giữ kho, thẻ local atomic, unique key/session replay, conditional inventory
  update, giá/ownership/quyền, settlement/history và UI recovery.
- Migration `20261108144118_AddImmediateCheckout` và snapshot đã tạo; SQL idempotent
  đã xuất. Chưa áp database chính/QA vì socket SQL bị chặn (10013).
- Build đã đạt 0 warning/error. Node 151/151 đạt. Functional/API Immediate đạt;
  gồm Seller create/settings/quyền, giá/tồn/hết hạn và refund retry.
- Backend tổng dừng ở test HTTP RateLimitChecks do socket 10013 sau các kiểm tra
  nghiệp vụ trước đó; không ghi toàn bộ PASS. SQL runner đã thử nhưng bị chặn kết
  nối trước case; chưa xác minh 2/1, 20/5 hoặc rollback trên SQL thật.
- Chưa có xUnit độc lập, browser E2E hoặc Immediate provider ngoài/VOID saga.
  Không tìm thấy source Klarna để chạy regression. Không đánh dấu task hoàn thành.
- [Chi tiết API/file/demo/khoảng cách và hướng xử lý](IMMEDIATE_PAYMENT.md).

## BIN-IP-SAGA — Sửa đúng authorize/Capture/Void và INSERT PAID (08/11/2026)

Phần này thay thế ghi nhận nhánh thẻ atomic trước đó trong BIN-IP.

- Checkout không giữ kho. Pay Now lưu attempt, authorize ngoài SQL transaction,
  claim tồn bằng conditional UPDATE trong transaction ngắn, rồi mới Capture.
  Người thua Void, không Capture. Capture đã xác nhận mới INSERT Order Paid;
  không có INSERT AwaitingPayment trung gian của nhánh Immediate.
- Provider giả lập có state bền vững riêng, query/idempotency, Void/Refund; worker
  đối soát Unknown, phục hồi lease sau restart và retry compensation. DB ghi đơn
  lỗi được khôi phục hoặc Refund. Refund sau mua hỗ trợ từng phần và key cũ.
- API thêm POST buyer/immediate-checkouts/{id}/reconcile. UI không cho retry
  khi Pending/Verifying và không báo Void/Refund hoàn tất trước xác nhận server.
- Migration mới: 20261108150534_AddImmediateAuthorizationSaga và
  20261108151440_AddSimulatedCardRefundHistory. Chưa áp database chính/QA.
- Functional/API và bộ saga đã chạy đạt: insert PAID, authorize/capture timeout,
  không Capture người thua, Void/Refund retry, stock/quote thay đổi, DB failure
  recovery/refund, partial refund và phục hồi lease. Đây là InMemory, không phải
  bằng chứng concurrency SQL. Build hiện đã đạt 0 warning/error; lượt cuối được
  cập nhật dưới đây sau khi chạy xong.
- SQL runner đã thử nhưng socket 10013 chặn kết nối trước case. Runner hiện có
  barrier 2/1 và 20/5 kiểm tra captured/voided, no open transaction ở gateway,
  duplicate submit và rollback/reconcile. Chưa được báo SQL PASS.
- Provider vẫn là giả lập thẻ. PayPal cũ không đổi; không có source Klarna để
  nghiệm thu. Chưa có xUnit độc lập hoặc browser E2E mới.
- Chi tiết state/API/schema/demo: docs/IMMEDIATE_PAYMENT.md.

Kết quả cuối BIN-IP-SAGA: solution build 0 warning/error; --immediate-checkout
functional/API + saga đạt (bao gồm partial refund, lease hết hạn và checkout-key
collision); Node 154/154 đạt, trong đó 10 test UI Immediate. Backend tổng dừng
RateLimitChecks/socket 10013; SQL runner cũng bị chặn socket trước case. Đã xuất
SQL idempotent cho hai migration mới, chưa áp DB chính/QA, chưa chạy browser E2E.
Không ghi SQL concurrency/rollback là PASS; còn bước nghiệm thu trên SQL thực tế.

## BIN-IP-UI + BUYER-REG — Trang checkout riêng, sắp xếp, đăng ký buyer (09/11/2026)

- Buy It Now ở /Buyer/Products chuyển sang trang riêng /Buyer/Checkout?product={id};
  luồng Pay Now/đối soát giữ nguyên. Thêm GET catalog/products/{id}. Sửa trang
  Products bị trống do section thiếu class active.
- Danh sách sản phẩm seller và catalog buyer sắp mới nhất trước (Id giảm dần);
  catalog vẫn lấy 100 sản phẩm, nay là 100 sản phẩm mới nhất.
- Đăng ký buyer chỉ Development: /Account/RegisterBuyer và POST api/auth/register-buyer.
  Form kiểu eBay (Tên, Họ, Email, Mật khẩu), không có mã xác minh email; Họ + Tên lưu
  vào Username. Mật khẩu PBKDF2 chung với đăng ký seller; email trùng trả 409; dùng
  chung khóa đăng ký và unique index email. Đăng ký xong tự đăng nhập vào /Buyer.
  Rate limit nhóm Registration ở API và frontend. Không có migration.
- Kiểm thử: build solution 0 warning/error (xuất ra thư mục riêng vì API đang chạy khóa
  bin); --buyer-registration, --registration, --immediate-checkout và --rate-limits đạt;
  Node 155/155 (11 test UI Immediate). Chưa chạy browser E2E hoặc SQL Server cho phần này.

Merge GiapDD vào master (09/11/2026): gộp tay 6 conflict, giữ cả hai bên (chặn tài khoản
bị khóa + đăng ký buyer; rate limit Cart.BuyItNow + ImmediateCheckout; hạn thanh toán cấu
hình cho đơn thường, null cho Immediate; RefundGateway nhận cả Klarna và thẻ Immediate;
Seller có cả tab Best Offer và link Sản phẩm). Snapshot EF sinh lại từ model;
`has-pending-model-changes` báo không thay đổi. Thứ tự migration: ...AddKlarnaHostedPayment
→ AddImmediateCheckout → AddImmediateAuthorizationSaga → AddSimulatedCardRefundHistory, chưa
áp database chính. Kiểm thử sau merge: build 0 warning/error, bộ backend mặc định đạt,
--klarna/--best-offers/--unpaid-items/--immediate-checkout/--buyer-registration/--rate-limits
đạt, Node 156/156. Klarna đã có trên master nên ghi chú "không tìm thấy Klarna" ở BIN-IP
không còn đúng. Chưa chạy browser E2E/SQL Server sau merge.

**Mua hàng kiểu eBay — 11/11/2026** ([quyết định](BUSINESS_REQUIREMENTS.md)):

- Trang `/Buyer/Products` (mục "Sản phẩm" trong menu buyer): Buy It Now / Thêm vào giỏ /
  Trả giá. `catalog/products` trả thêm seller, ảnh, `allowOffers`; sửa `catalog/products/{id}`
  lọc trước khi projection (SQL không dịch được filter sau projection có subquery).
- Buy It Now sản phẩm thường: checkout một món, tạo đơn rồi thu tiền ngay (credit card /
  PayPal / Klarna); thẻ lỗi thì thử lại trên cùng đơn hoặc trả sau ở Đơn hàng. Sản phẩm
  Immediate: chỉ credit card, không vào giỏ, luồng authorize/capture giữ nguyên.
- Giỏ không giới hạn seller; "Chỉ thanh toán người bán này" dùng `cart/quote`/`cart/checkout`
  với tập con seller, chỉ nhóm đã thanh toán rời giỏ. Gỡ `cart/buy-now` + nút "Buy It Now
  (Không ép trả ngay)". Nút thanh toán: "Xác nhận và thanh toán". Không có migration.
- Kiểm thử: build API/frontend/test 0 warning/error; bộ backend mặc định đạt;
  `--checkout-limits` (3 seller quote/checkout), `--unpaid-items` (`PayOnlyOneSellerAsync`),
  `--immediate-checkout`, `--rate-limits` đạt; Node 169/171 (2 lỗi `klarna-address-ui` có từ
  trước, thuộc thay đổi sổ địa chỉ đang làm dở). Browser trên API/frontend riêng nối
  `G4_Demo` (worker tắt): trang Sản phẩm desktop/mobile, thêm giỏ 3 seller, thanh toán riêng
  một seller (báo giá đúng một nhóm), báo giá cả giỏ 3 seller, checkout Buy It Now sản phẩm
  thường hiện đủ 3 phương thức, link Trả giá mở đúng form; giỏ buyer demo đã trả về trống.
  Chưa bấm thanh toán thật trên trình duyệt; chưa chạy `role-navigation.e2e.cjs`.
- Còn lại: PayPal cho sản phẩm Immediate (đợt riêng); `tools/G4.DemoData` vẫn ghi "Pay Now"
  trong hướng dẫn demo sinh tự động.
- Bổ sung cùng ngày: bỏ giới hạn 5 sản phẩm của giỏ (`cart/save`) và của một đơn
  (`CheckoutService`); số lượng mỗi sản phẩm vẫn 1–5, Random vẫn 2–5 sản phẩm. Checkout cả
  giỏ quá 10 seller thì ẩn PayPal (giới hạn purchase unit). Test: `ProductCountAsync` trong
  `--checkout-limits` (giỏ 8 sản phẩm, một đơn 6 sản phẩm) và test 9 trong
  `tests/buyer-cart-ui.test.cjs`.
- Bổ sung cùng ngày (2): Buy It Now dùng tab Thanh toán `/Buyer?tab=checkout&product={id}`
  thay cho trang riêng `/Buyer/Checkout` (đã xóa view và `immediate-checkout.js`; action cũ
  chuyển hướng, giữ `product`/`checkout`). Module mới `buyer-buy-now.js`, `buyer-cart.js`
  nhường tab khi có `product`; "Tiếp tục thanh toán" từ giỏ thoát chế độ này. Immediate tạo
  session khi bấm "Xác nhận và thanh toán". Không có migration, không đổi API. Test: Node
  164/164 (`tests/buyer-buy-now-ui.test.cjs` 15 test, `tests/buyer-products-ui.test.cjs`),
  build frontend 0 warning/error. Browser trên frontend riêng cổng 5191 nối API đang chạy:
  redirect link cũ, báo giá sản phẩm Immediate (chỉ credit card) và sản phẩm thường (đủ 3
  phương thức), đổi số lượng/địa chỉ tự báo giá lại, quay về giỏ, mobile 375px không tràn.
  Chưa bấm thanh toán thật trên trình duyệt; chưa chạy `role-navigation.e2e.cjs`.
- Bổ sung cùng ngày (3): khối "Thanh toán bằng" ở tab Thanh toán đổi sang thẻ có viền theo
  kiểu eBay "Pay with": logo PayPal, logo Visa/Mastercard/Discover/Diners cho credit card,
  logo Klarna (`frontend/wwwroot/images/payments/`); form thẻ thử nghiệm mở ngay dưới ô
  credit card. Giữ nguyên id/name các radio nên JS không đổi. Node 164/164; kiểm tra trình
  duyệt desktop và mobile 375px (không tràn ngang).
