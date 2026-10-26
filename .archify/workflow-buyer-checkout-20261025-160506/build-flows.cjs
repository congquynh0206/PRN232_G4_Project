const fs = require('node:fs');
const path = require('node:path');

const outputDirectory = '.archify/workflow-buyer-checkout-20261025-160506';
const revision = 'e7997b03bd86bb5160332dd94bd5dc853b441963';
const cart = 'frontend/wwwroot/js/buyer-cart.js';
const buyer = 'frontend/wwwroot/js/buyer.js';
const paypal = 'frontend/wwwroot/js/paypal-cart.js';
const service = 'backend/G4.Infrastructure/Checkout/CheckoutService.cs';
const paypalService = 'backend/G4.Infrastructure/Payments/PayPalCartPaymentService.cs';

function source(file, line, end, label) {
  return { path: file, line, end_line: end, label };
}

function node(id, lane, col, label, sublabel, tag, sources, type = 'frontend') {
  return { id, lane, col, type, label, sublabel, tag, sources };
}

function edge(from, to, label, role = 'branch') {
  return {
    id: `${from}-${to}`,
    from,
    to,
    label,
    role,
    variant: role === 'main' ? 'emphasis' : role === 'error' ? 'security' : 'default',
  };
}

function card(dot, title, items) {
  return { dot, title, items };
}

function diagram(slug, title, lanes, nodes, edges, mainPath, cards) {
  return {
    schema_version: 2,
    diagram_type: 'workflow',
    meta: {
      title,
      locale: 'vi',
      translations: {
        'page.title': '{title}',
        'diagram.description.workflow': 'Luồng thao tác và màn hình từ source G4_Project.',
        'legend.title': 'Chú giải',
        'legend.workflow.frontend': 'Màn hình / thao tác',
        'legend.workflow.backend': 'Xử lý hệ thống',
        'legend.workflow.security': 'Nhánh lỗi / chưa xác nhận',
        'viewer.theme.toggle.title': 'Đổi sáng / tối (T)',
        'viewer.theme.toggle': 'Đổi giao diện sáng / tối',
        'viewer.theme.dark': 'Tối',
        'viewer.theme.light': 'Sáng',
        'viewer.motion.live': 'Chuyển động',
        'viewer.motion.still': 'Tĩnh',
        'node.focus': 'Xem {label}',
        'node.focus.detail': 'Xem {label}, {detail}',
      },
      output: `${outputDirectory}/${slug}.html`,
      quality_profile: 'showcase',
      repository: {
        url: 'https://github.com/congquynh0206/PRN232_G4_Project.git',
        revision,
      },
    },
    lanes,
    mainPath,
    nodes,
    edges,
    cards: [
      ...cards,
      card('slate', 'Cách đọc', [
        'Đọc mũi tên theo thứ tự; tên node là màn hình, thao tác hoặc kết quả. SRC mở file/dòng của source đã ghim commit.',
        'Đây là hành vi hiện có trong source, chưa phải kết quả chạy E2E với database và PayPal thật. Một số nút Archify dùng tiếng Anh.',
      ]),
    ],
  };
}

const checkout = diagram(
  '01-checkout',
  '01 · Checkout: bấm xong đi màn nào?',
  [
    { id: 'screens', label: 'Luồng màn hình chính' },
    { id: 'card', label: 'Đã chọn thẻ thử' },
    { id: 'paypal', label: 'Đã chọn PayPal' },
    { id: 'errors', label: 'Chưa đi tiếp được', variant: 'exception' },
  ],
  [
    node('cart', 'screens', 0, 'Giỏ hàng', 'Xem sản phẩm / số lượng', 'Buyer · #summary', [
      source('frontend/Views/Buyer/Index.cshtml', 9, 32, 'Giỏ và nút tiếp tục'),
    ]),
    node('checkout', 'screens', 1, 'Thanh toán', 'Địa chỉ / mã / phương thức', 'Buyer · #checkout', [
      source(cart, 287, 327, 'Báo giá theo địa chỉ hiện tại'),
      source(cart, 397, 400, 'Mở phần Thanh toán'),
    ]),
    node('place', 'screens', 2, 'Bấm đặt hàng', 'Tạo một đơn mỗi seller', 'Đặt hàng & thanh toán', [
      source(cart, 330, 343, 'Gọi checkout trước thanh toán'),
      source('backend/G4.Infrastructure/Checkout/BuyerCartService.cs', 364, 397,
        'Tạo các đơn và xóa giỏ khi commit'),
    ], 'backend'),
    node('orders', 'screens', 3, 'Đơn của tôi', 'Tạo xong → chuyển tab', 'Buyer · #orders', [
      source(cart, 338, 343, 'Chọn tab orders và tải danh sách'),
      source('frontend/Views/Buyer/Index.cshtml', 141, 147, 'Tên màn Đơn của tôi'),
    ]),
    node('method', 'screens', 4, 'Chạy cách đã chọn', 'Chọn từ màn Thanh toán', 'Chỉ xử lý đơn chưa trả', [
      source(cart, 335, 361, 'Phân nhánh phương thức đã chọn'),
    ], 'backend'),
    node('card-branch', 'card', 5, 'Thẻ: xem chi tiết', 'Thử từng đơn → mở đơn đầu', 'Đọc sơ đồ 02', [
      source(cart, 353, 374, 'Thanh toán tuần tự rồi mở chi tiết'),
    ]),
    node('paypal-branch', 'paypal', 5, 'Chuyển sang PayPal', 'Nếu Pending + có URL duyệt', 'Đọc sơ đồ 03', [
      source(cart, 343, 351, 'Khởi tạo PayPal cả giỏ'),
      source(paypal, 53, 70, 'Chỉ redirect khi có approval URL'),
    ]),
    node('no-quote', 'errors', 1, 'Ở lại Thanh toán', 'Chọn địa chỉ / lấy báo giá', 'Nút đặt chưa dùng được', [
      source(cart, 287, 323, 'Chặn khi chưa có quote mới'),
    ], 'security'),
    node('rejected', 'errors', 2, 'Ở lại Thanh toán', 'Tải giỏ / cập nhật báo giá', 'Checkout trả 400 / 409', [
      source(cart, 383, 386, 'Reset quote khi checkout bị từ chối'),
    ], 'security'),
    node('unknown', 'errors', 3, 'Kiểm tra Đơn của tôi', 'Hoặc thử lại giỏ hiện tại', 'Chưa rõ kết quả tạo đơn', [
      source(cart, 334, 338, 'Giữ attempt checkout khi gọi API'),
      source(cart, 375, 388, 'Thông báo khi kết quả chưa rõ'),
      source('backend/G4.Infrastructure/Checkout/BuyerCartService.cs', 345, 361,
        'Retry cùng khóa trả về đơn đã có'),
    ], 'security'),
    node('created-error', 'errors', 4, 'Đơn đã tạo vẫn còn', 'Tiếp tục trong Đơn của tôi', 'Bước sau tạo đơn gặp lỗi', [
      source(cart, 375, 381, 'Nhánh orders đã có rồi gặp lỗi'),
    ], 'security'),
  ],
  [
    edge('cart', 'checkout', 'Tiếp tục', 'main'),
    edge('checkout', 'place', 'Đủ báo giá', 'main'),
    edge('place', 'orders', 'Tạo được đơn', 'main'),
    edge('orders', 'method', 'Tiếp tục trả tiền', 'main'),
    edge('method', 'card-branch', 'Thẻ thử'),
    edge('method', 'paypal-branch', 'PayPal + có đơn chờ'),
    edge('checkout', 'no-quote', 'Thiếu / lỗi báo giá', 'error'),
    edge('place', 'rejected', '400 / 409', 'error'),
    edge('place', 'unknown', 'Mất phản hồi', 'error'),
    edge('orders', 'created-error', 'Bước sau lỗi', 'error'),
  ],
  ['cart', 'checkout', 'place', 'orders', 'method', 'card-branch'],
  [
    card('cyan', 'Màn tiếp theo thực tế', [
      'Giỏ hàng, Thanh toán và Đơn của tôi là các tab trong /Buyer, không phải ba ứng dụng riêng.',
      'Sau khi tạo đơn và tải lại giỏ thành công, UI chuyển sang Đơn của tôi trước khi chạy nhánh thẻ hoặc PayPal.',
      'Không có màn thành công chung riêng. Với thẻ, UI mở dialog chi tiết đơn đầu tiên; với PayPal, kết quả nằm trong Đơn của tôi.',
    ]),
    card('rose', 'Ba tình huống dừng trước thanh toán', [
      'Chưa có địa chỉ/báo giá hợp lệ: ở lại Thanh toán, bổ sung địa chỉ hoặc cập nhật báo giá.',
      'Checkout trả 400/409: reset báo giá, yêu cầu tải lại giỏ và xác nhận giá mới.',
      'Timeout/mất phản hồi: không biết chắc server đã tạo hay chưa. UI hướng dẫn kiểm tra Đơn của tôi hoặc thử lại cùng giỏ; backend hỗ trợ khóa checkout.',
    ]),
    card('emerald', 'Sau khi tạo đơn', [
      'Một seller nhận một đơn riêng. Giỏ được xóa khi giao dịch tạo đơn commit; lỗi thanh toán không tự đưa sản phẩm trở lại giỏ.',
      'Nếu lỗi xảy ra sau khi đã nhận danh sách đơn, UI cố tải Đơn của tôi và hướng dẫn tiếp tục thanh toán ở đó.',
    ]),
  ],
);

const cardFlow = diagram(
  '02-card',
  '02 · Thẻ thử: kết quả và thao tác tiếp',
  [
    { id: 'main', label: 'Thao tác và kết quả chính' },
    { id: 'unpaid', label: 'Còn đơn chưa được xác nhận thanh toán', variant: 'exception' },
    { id: 'mixed', label: 'Kết quả khác nhau giữa các đơn' },
  ],
  [
    node('start', 'main', 0, 'Đơn của tôi', 'Đơn đã tạo thành công', 'Đã chọn thẻ thử', [
      source(cart, 338, 354, 'Tạo đơn rồi vào tab orders'),
    ]),
    node('pay', 'main', 1, 'Trả tiền từng đơn', 'Lần lượt các đơn chờ trả', 'Chung thông tin thẻ thử', [
      source(cart, 353, 365, 'Vòng lặp gọi thanh toán card'),
    ], 'backend'),
    node('summary', 'main', 2, 'Tổng hợp kết quả', 'Xét trạng thái từng lần trả', 'Chỉ Succeeded là đã trả', [
      source(cart, 361, 371, 'Xếp các kết quả khác vào chưa thành công'),
    ], 'backend'),
    node('success', 'main', 3, 'Tất cả thành công', 'Thông báo đã thanh toán', 'Đơn thành công → Paid', [
      source(cart, 366, 371, 'Thông báo khi không còn kết quả lỗi'),
      source(service, 433, 459, 'Chuyển trạng thái và ghi nhận tiền'),
      source('backend/G4.Domain/Rules/OrderState.cs', 5, 9, 'PaymentSucceeded chuyển sang Paid'),
    ]),
    node('failed', 'unpaid', 3, 'Chưa đơn nào xong', 'Báo các mã đơn cần kiểm tra', 'Từ chối / hết hạn thẻ / lỗi', [
      source(cart, 355, 371, 'UI ghi lỗi và liệt kê đơn chưa thành công'),
      source(service, 401, 415, 'Trạng thái từ thẻ giả lập'),
    ], 'security'),
    node('partial', 'mixed', 3, 'Chỉ một số đơn xong', 'Giữ nguyên các đơn đã trả', 'Xử lý các đơn còn lại', [
      source(cart, 353, 371, 'Từng đơn có kết quả độc lập'),
    ]),
    node('detail', 'main', 4, 'Chi tiết đơn đầu', 'Dialog trong Đơn của tôi', 'Không phải trang thành công', [
      source(cart, 373, 374, 'Mở đơn đầu sau khi tổng hợp'),
      source(buyer, 125, 141, 'Dialog hiển thị trạng thái thật'),
    ]),
    node('paid-next', 'main', 5, 'Đã trả: chờ seller', 'Theo dõi trong Đơn của tôi', 'Paid / Preparing', [
      source(buyer, 105, 106, 'Paid/Preparing có yêu cầu hủy'),
      source('frontend/Views/Buyer/Index.cshtml', 159, 163, 'Bộ lọc chờ giao / đang giao'),
    ]),
    node('retry', 'unpaid', 5, 'Chưa trả: mở đúng đơn', 'Thử lại / PayPal / hủy', 'Nếu server còn cho phép', [
      source(buyer, 97, 104, 'Nút thao tác theo trạng thái thật'),
      source(buyer, 180, 198, 'Retry và tải lại chi tiết'),
    ]),
    node('expired', 'mixed', 5, 'Đơn hết hạn', 'Worker hết hạn đơn chờ', 'Quá hạn + không Verifying', [
      source(service, 465, 494, 'Worker kiểm tra hạn và trạng thái'),
      source('backend/G4.Domain/Rules/OrderState.cs', 5, 9, 'Sự kiện Expire thành Expired'),
    ], 'backend'),
  ],
  [
    edge('start', 'pay', 'Các đơn chờ trả', 'main'),
    edge('pay', 'summary', 'Xong vòng lặp', 'main'),
    edge('summary', 'success', 'Tất cả Succeeded', 'main'),
    edge('summary', 'failed', 'Chưa có Succeeded', 'error'),
    edge('summary', 'partial', 'Kết quả trộn'),
    edge('success', 'detail', 'Mở đơn đầu', 'main'),
    edge('failed', 'detail', 'Vẫn mở đơn đầu'),
    edge('partial', 'detail', 'Vẫn mở đơn đầu'),
    edge('detail', 'paid-next', 'Đơn đã trả', 'main'),
    edge('detail', 'retry', 'Đơn chưa trả'),
    edge('retry', 'expired', 'Quá hạn cho phép'),
  ],
  ['start', 'pay', 'summary', 'success', 'detail', 'paid-next'],
  [
    card('cyan', 'Sau checkout có ba nhóm kết quả', [
      'Toàn bộ đơn trả thành công; toàn bộ chưa được xác nhận thành công; hoặc chỉ một số đơn trả thành công.',
      'Cả ba nhóm đều tải lại danh sách và mở dialog của đơn đầu tiên. Với giỏ hai seller, phải xem đúng mã đơn còn lỗi, vì dialog đầu tiên có thể là đơn đã trả.',
      'Đơn Paid xuất hiện trong nhóm Chờ giao; seller tiếp tục chuẩn bị và tạo vận đơn. Màn buyer vẫn là Đơn của tôi.',
    ]),
    card('rose', 'Thông báo UI và kết quả thực tế', [
      'Frontend cũng ghi Failed khi request thẻ ném lỗi. Nếu mất phản hồi, cần đọc lại trạng thái đơn; thông báo lỗi request chưa chứng minh tiền chưa được ghi nhận.',
      'Nếu đơn chưa trả và không gắn lượt PayPal cả giỏ đang xử lý, chi tiết cho thử thẻ, PayPal riêng đơn hoặc hủy. Nếu có lượt PayPal cả giỏ, nút mở lại lượt đó.',
      'Retry thẻ trong chi tiết hiện dùng thẻ thử thành công 4111...1111, hạn 12/30. Đây là mô phỏng, không phải cổng thẻ thật.',
    ]),
    card('amber', 'Hết hạn là nhánh riêng', [
      'Đơn chờ thanh toán có deadline theo server. Worker hết hạn và trả tồn khi quá hạn; payment PayPal đang Verifying được bảo vệ khỏi nhánh hết hạn này.',
      'Nếu lỗi ném ra ngoài trước khi vòng lặp/refresh hoàn tất, UI có thể chỉ giữ màn Đơn của tôi và thông báo tiếp tục thanh toán, chưa mở dialog tự động.',
    ]),
  ],
);

const paypalFlow = diagram(
  '03-paypal',
  '03 · PayPal cả giỏ: quay về và các nhánh',
  [
    { id: 'main', label: 'Đi PayPal và quay về ứng dụng' },
    { id: 'waiting', label: 'Chờ xác nhận / chờ đối soát' },
    { id: 'retry', label: 'Có thể xử lý lại các đơn còn thiếu' },
    { id: 'errors', label: 'Chưa khởi tạo được lượt', variant: 'exception' },
  ],
  [
    node('start', 'main', 0, 'Bắt đầu PayPal', 'Đơn đã có trong Đơn của tôi', 'Một lượt cho cả giỏ', [
      source(cart, 338, 351, 'Khởi tạo sau khi đã tạo đơn'),
      source(paypal, 53, 64, 'Gọi tạo lượt theo checkoutKey'),
    ]),
    node('approve', 'main', 1, 'Trang PayPal', 'Đăng nhập / duyệt hoặc hủy', 'Chỉ Pending + có URL', [
      source(paypal, 41, 46, 'Đi tiếp trang xác nhận nếu Pending'),
      source(paypal, 61, 64, 'Redirect approval URL'),
    ], 'external'),
    node('return', 'main', 2, 'Quay về Buyer', 'Duyệt: capture · hủy: quay về', 'Return / Cancel callback', [
      source('frontend/Controllers/BuyerController.cs', 13, 47, 'Return capture và Cancel redirect'),
    ], 'backend'),
    node('panel', 'main', 3, 'Đơn của tôi', 'Khối PayPal cả giỏ', 'Đọc trạng thái server', [
      source(paypal, 17, 40, 'Khối kết quả PayPal'),
      source(paypal, 98, 115, 'Đọc lại lượt từ URL/session'),
      source('frontend/wwwroot/js/role-navigation.js', 107, 116, 'URL checkoutId chọn tab orders'),
    ]),
    node('paid', 'main', 4, 'Đã thanh toán', 'Succeeded', 'Mỗi đơn xem riêng', [
      source(paypal, 17, 30, 'Hiển thị Succeeded và từng đơn'),
      source(paypalService, 499, 510, 'Tổng hợp trạng thái từ payment'),
    ]),
    node('detail', 'main', 5, 'Bấm Xem đơn', 'Mở dialog chi tiết đơn', 'Tiếp tục theo dõi giao hàng', [
      source(paypal, 27, 30, 'Nút Xem đơn theo mã'),
      source(buyer, 125, 141, 'Mở dialog chi tiết'),
      source(buyer, 207, 212, 'Bắt sự kiện mở đơn'),
    ]),
    node('pending', 'waiting', 3, 'Còn chờ thao tác', 'Creating / Pending', 'Đọc điều kiện ở thẻ dưới', [
      source(paypal, 17, 30, 'Các nút phụ thuộc trạng thái'),
    ]),
    node('verifying', 'waiting', 4, 'Đang xác minh', 'Verifying', 'Chưa hủy / thanh toán lại', [
      source(paypal, 17, 30, 'UI không hiện nút trả lại khi Verifying'),
      source(paypalService, 528, 535, 'Server chặn hủy khi Verifying'),
    ]),
    node('check', 'waiting', 5, 'Kiểm tra kết quả', 'Đọc lại / reconcile / realtime', 'Chờ kết luận từ server', [
      source(paypal, 40, 40, 'Nút kiểm tra gọi reconcile'),
      source(paypal, 76, 85, 'Đọc trạng thái và reconcile'),
      source(paypal, 113, 115, 'Cập nhật qua realtime'),
    ]),
    node('retryable', 'retry', 4, 'Chưa trả đủ / đã hủy', 'Thất bại / một phần / hủy', 'Giữ các đơn đã trả', [
      source(paypal, 17, 30, 'Nút thanh toán phần còn lại'),
      source(paypalService, 499, 510, 'Failed và PartiallyPaid'),
      source(paypalService, 532, 545, 'Hủy lượt không đổi đơn đã trả'),
    ]),
    node('retry', 'retry', 5, 'Trả các đơn còn lại', 'Bắt đầu lượt PayPal mới', 'Chỉ khi còn đơn chờ trả', [
      source(paypal, 27, 30, 'Điều kiện nút retry'),
      source(paypal, 50, 63, 'Retry dùng fresh attempt'),
    ]),
    node('init-error', 'errors', 0, 'Hiện lỗi khởi tạo', 'Ở lại khối kết quả', 'Đơn đã tạo vẫn còn', [
      source(paypal, 65, 70, 'Lỗi cấu hình / khởi tạo chưa rõ và retry'),
    ], 'security'),
  ],
  [
    edge('start', 'approve', 'Pending + URL', 'main'),
    edge('approve', 'return', 'Duyệt / hủy', 'main'),
    edge('return', 'panel', 'Mở lại Buyer', 'main'),
    edge('panel', 'paid', 'Succeeded', 'main'),
    edge('paid', 'detail', 'Xem từng đơn', 'main'),
    edge('panel', 'pending', 'Creating / Pending'),
    edge('panel', 'verifying', 'Verifying'),
    edge('panel', 'retryable', 'Failed / Partial / Cancelled'),
    edge('verifying', 'check', 'Đọc lại kết quả'),
    edge('retryable', 'retry', 'Còn đơn chờ trả'),
    edge('start', 'init-error', 'Khởi tạo lỗi / chưa rõ', 'error'),
    edge('start', 'panel', 'Không chuyển trang'),
  ],
  ['start', 'approve', 'return', 'panel', 'paid', 'detail'],
  [
    card('cyan', 'Màn sau PayPal là gì?', [
      'Sau khi PayPal trả về, controller chuyển về /Buyer với paypalCheckoutId. Điều hướng chọn Đơn của tôi; khối Thanh toán PayPal cả giỏ đọc trạng thái từ server.',
      'Return gọi capture, nhưng HTTP thành công không có nghĩa tất cả payment đã Succeeded. UI hiển thị trạng thái thật của lượt và từng đơn.',
      'PayPal không tự mở chi tiết đơn như nhánh thẻ. Buyer bấm Xem đơn để mở dialog.',
    ]),
    card('amber', 'Bảy trạng thái được UI hiển thị', [
      'Creating: đang khởi tạo. Pending: chờ duyệt, có thể Tiếp tục xác nhận PayPal khi có URL. Cả hai có nút Hủy lượt này.',
      'Verifying: đang xác minh. Chỉ kiểm tra kết quả/chờ đối soát; chưa cho hủy hoặc tạo lượt trả lại.',
      'Succeeded: tất cả đơn trả xong. Failed: chưa trả thành công. PartiallyPaid: mới trả một phần. Cancelled: hủy lượt PayPal.',
      'Failed / PartiallyPaid / Cancelled chỉ hiện Trả các đơn còn lại nếu còn đơn AwaitingPayment chưa Succeeded.',
    ]),
    card('rose', 'Hủy PayPal khác hủy đơn', [
      'Bấm hủy ở PayPal quay về Buyer. JS chỉ yêu cầu hủy lượt khi trạng thái đọc lại vẫn là Pending; server cũng chặn hủy khi Verifying.',
      'Hủy lượt PayPal không tự hủy đơn. Các đơn chưa trả tiếp tục chờ trong giới hạn deadline; các đơn đã trả giữ nguyên.',
      'Nếu không có credential hoặc khởi tạo lỗi/mất phản hồi, UI ở lại và hiện nút Kiểm tra lại lượt PayPal. Không tự coi kết quả chưa rõ là thành công.',
    ]),
    card('emerald', 'Thao tác từ các nhánh chờ', [
      'Pending + có URL: bấm Tiếp tục xác nhận PayPal để đi lại trang ngoài. Creating/Pending: có thể bấm Hủy lượt này theo điều kiện server.',
      'Kiểm tra kết quả gọi reconcile. Sự kiện realtime cũng tải lại lượt. Kết quả mới sẽ đưa nội dung khối về một trong các nhóm trạng thái trên.',
      'Trả các đơn còn lại bắt đầu một attempt mới; backend giữ nguyên các đơn đã Succeeded.',
    ]),
  ],
);

for (const [slug, data] of [
  ['01-checkout', checkout],
  ['02-card', cardFlow],
  ['03-paypal', paypalFlow],
]) {
  fs.writeFileSync(path.join(__dirname, `${slug}.json`), JSON.stringify(data, null, 2) + '\n');
}
