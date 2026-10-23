(() => {
  const { $, api, esc, money, date, safe, pager } = G4;
  let page = 1,
    request = 0,
    loading = false;
  const timer = (deadline, options) => G4.Countdown?.html(deadline, options) || '';
  const stamp = (value) =>
    date(typeof value === 'string' && !/(Z|[+-]\d{2}:\d{2})$/i.test(value) ? value + 'Z' : value);
  const blocks = {
    Dispute: 'Tiền đang bị giữ do tranh chấp; chờ kết quả hồ sơ.',
    Return: 'Tiền đang bị giữ do trả hàng; chờ xử lý yêu cầu.',
    Settlement: 'Đang xử lý khoản giữ/hoàn tiền; chờ kết quả từ hệ thống.',
    Order: 'Trạng thái đơn chưa đủ điều kiện mở khóa tiền.',
  };

  function row(item) {
    const blocked = blocks[item.blockReason];
    const waiting = blocked
      ? 'Đã đủ thời gian giữ — đang chờ xử lý trả hàng/tranh chấp hoặc trạng thái đơn'
      : 'Đã đủ thời gian giữ — chờ hệ thống chuyển tiền vào ví nếu đủ điều kiện';
    const release =
      Number(item.processing) > 0
        ? item.releaseAt
          ? timer(item.releaseAt, {
              label: 'Tới hạn mở khóa tiền',
              expired: waiting,
              kind: 'funds',
              scope: item.orderId,
            })
          : '<p>Chưa bắt đầu đếm — đang chờ giao thành công tới buyer.</p>'
        : '';
    const response = timer(item.responseDueAt, {
      label:
        item.responseKind === 'seller-response'
          ? 'Seller gửi phương án trong'
          : item.responseKind === 'buyer-response'
            ? 'Buyer trả lời trong'
            : 'Seller kiểm tra hàng trả trong',
      expired: 'Đã đến hạn phản hồi — chờ cập nhật hồ sơ từ hệ thống',
      kind: 'funds',
      scope: item.orderId,
    });
    const policy =
      item.holdPolicyVersion === 0
        ? 'Chính sách cũ, dùng hạn đã lưu'
        : `Level ${Number(item.sellerLevel)} · Thời gian giữ đã chốt: ` +
          (G4.Countdown?.format(item.holdSeconds) || `${Number(item.holdSeconds)} giây`);
    return `<article class="seller-fund" data-fund-order="${Number(item.orderId)}">
      <div class="seller-fund-head"><strong>Đơn #${Number(item.orderId)}</strong>
        <span>${esc(policy)}</span></div>
      <div class="seller-fund-amounts">
        <span>Chờ hết thời gian giữ: <strong>${money(item.processing)}</strong></span>
        <span>Giữ do hồ sơ: <strong>${money(item.onHold)}</strong></span>
      </div>
      ${item.holdStartedAt ? `<p class="small">Bắt đầu: ${esc(stamp(item.holdStartedAt))}</p>` : ''}
      ${item.releaseAt ? `<p class="small">Hạn đã lưu: ${esc(stamp(item.releaseAt))}</p>` : ''}
      ${blocked ? `<p>${esc(blocked)}</p>` : ''}${release}${response}
      <button type="button" data-open-order="${Number(item.orderId)}">Xem đơn và xử lý</button>
      ${
        item.disputeId
          ? `<button type="button" data-open-case="${Number(item.disputeId)}">
        Xem tranh chấp #${Number(item.disputeId)}</button>`
          : ''
      }
    </article>`;
  }

  function detail(order) {
    return order.sellerFunds
      ? row(order.sellerFunds)
      : order.settlement
        ? '<p class="small">Không còn khoản tiền đang giữ của đơn này.</p>'
        : '';
  }

  async function load(next = page) {
    if (!$('seller-funds-list')) return;
    const ticket = ++request;
    loading = true;
    $('seller-funds-list').setAttribute('aria-busy', 'true');
    try {
      const result = await api(`seller/finance/funds/page?page=${next}&pageSize=10`);
      if (ticket !== request) return;
      page = result.page;
      $('seller-funds-status').textContent = `${result.totalCount} đơn còn tiền đang giữ.`;
      $('seller-funds-list').innerHTML = result.items.length
        ? result.items.map(row).join('')
        : '<p class="empty-note">Không có khoản tiền đang giữ.</p>';
      pager('seller-funds-pagination', result, (value) => safe(() => load(value)));
    } catch (error) {
      if (ticket === request) {
        $('seller-funds-status').textContent = 'Chưa tải được tiền giữ. Hãy bấm Làm mới.';
        $('seller-funds-list').innerHTML = '';
        $('seller-funds-pagination').innerHTML = '';
      }
      throw error;
    } finally {
      if (ticket === request) {
        loading = false;
        $('seller-funds-list').setAttribute('aria-busy', 'false');
      }
    }
  }

  function init() {
    $('refresh-seller-funds').onclick = () => safe(() => load());
    const refresh = () => {
      if (
        !loading &&
        !document.hidden &&
        $('finance').classList.contains('active') &&
        !document.activeElement?.closest('#seller-funds-list, #seller-funds-pagination')
      )
        return load();
    };
    G4.Countdown?.subscribe('funds', refresh);
    setInterval(() => safe(refresh), 10000);
  }
  G4.SellerFunds = { init, load, row, detail };
})();
