(() => {
  const { $, api, esc, money, date, safe, pager, returnStatus } = G4;
  let page = 1,
    request = 0;
  const reasons = {
    WrongItem: 'Sai hàng',
    NotAsDescribed: 'Sai mô tả',
    DamagedOrMissing: 'Hàng hỏng/thiếu do seller',
    ChangedMind: 'Đổi ý',
    OtherBuyerReason: 'Lý do không do seller',
  };
  function row(r) {
    const buttons = [
      `<button type="button" data-open-order="${Number(r.orderId)}">Xem đơn và xử lý</button>`,
    ];
    if (r.status === 'Requested')
      buttons.push(
        `<button type="button" data-action="approve-return" data-return="${Number(r.id)}" data-id="${Number(r.orderId)}">Duyệt trả hàng</button><button type="button" data-action="reject-return" data-return="${Number(r.id)}" data-id="${Number(r.orderId)}">Từ chối trả hàng</button>`,
      );
    return `<article class="return-card"><strong>Yêu cầu #${Number(r.id)} · Đơn #${Number(r.orderId)} · ${esc(returnStatus(r.status))}</strong><p>${esc(r.productTitle || 'Đơn hàng')} · ${esc(r.buyerName || 'Người mua')}</p><p>${esc(reasons[r.reasonCode] || r.reason || '')}</p>${r.description ? `<p>${esc(r.description)}</p>` : ''}${r.pricingPolicyVersion === 1 ? `<p>Dự kiến hoàn ${money(r.buyerRefundAmount)} · Phí trả ${money(r.returnShippingFee)} · ${r.shippingPayer === 'Buyer' ? 'Buyer chịu phí' : 'Seller chịu phí'}</p>` : ''}${r.shipmentStatus === 'ShipmentCreationFailed' ? '<p role="status">Tạo vận đơn trả thất bại. Mở đơn để thử lại.</p>' : ''}${G4.Countdown?.receipt(r, r.disputed) || ''}${r.disputed ? '<p>Đang có tranh chấp; mở hồ sơ để theo dõi xử lý.</p>' : ''}<div class="actions">${buttons.join('')}</div></article>`;
  }
  async function load(p = page) {
    const token = ++request;
    $('seller-return-list').setAttribute('aria-busy', 'true');
    try {
      const data = await api(
        `seller/returns/page?filter=${$('seller-return-filter').value}&page=${p}&pageSize=10`,
      );
      if (token !== request) return;
      page = data.page;
      $('seller-return-summary').textContent =
        `${data.totalCount} yêu cầu trong nhóm này · ${data.counts.approval} chờ duyệt · ${data.counts.receipt} chờ kiểm tra hàng.`;
      $('seller-return-count').textContent = data.counts.approval || '';
      $('seller-return-list').innerHTML = data.items.length
        ? data.items.map(row).join('')
        : '<p class="empty-note">Không có yêu cầu trong nhóm này.</p>';
      pager('seller-return-pagination', data, (next) => safe(() => load(next)));
    } finally {
      if (token === request) $('seller-return-list').setAttribute('aria-busy', 'false');
    }
  }
  function init() {
    $('refresh-seller-returns').onclick = () => safe(load);
    $('seller-return-filter').onchange = () => safe(() => load(1));
  }
  G4.SellerReturns = { init, load, row };
})();
