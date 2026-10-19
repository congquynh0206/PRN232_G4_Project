(() => {
  const { $, esc, money, date, label, api, safe, pager, openDetail } = window.G4;
  let page = 1;
  let quickFilter = '';
  let initialized = false;
  let rows = [];
  let requestId = 0;

  function statusPill(value, extra = '') {
    const tone = /lỗi|thất bại/i.test(value || '') ? ' is-error' : /thành công/i.test(value || '') ? ' is-success' : '';
    return `<span class="lookup-status${tone} ${extra}">${esc(value || 'Chưa có')}</span>`;
  }

  function rowHtml(order) {
    const payment = order.hasPaymentFailure && order.paymentStatus === 'Thành công'
      ? 'Thành công · có lần lỗi'
      : order.paymentStatus;
    return `<tr><td><strong>#${Number(order.orderId)}</strong></td><td>${esc(date(order.orderDate))}</td><td>${esc(order.buyer || '—')}</td><td>${esc(order.seller || '—')}</td><td>${money(order.total)}</td><td>${statusPill(payment)}</td><td>${statusPill(order.refundStatus)}</td><td>${money(order.refundedAmount)}</td><td>${statusPill(label(order.shippingStatus))}</td><td><button type="button" data-open-admin-order="${Number(order.orderId)}">Xem</button></td></tr>`;
  }

  function readFilters() {
    const values = Object.fromEntries(new FormData($('order-lookup-filters')).entries());
    for (const key of ['from', 'to']) if (values[key]) values[key] = new Date(values[key]).toISOString();
    if (quickFilter) values.quickFilter = quickFilter;
    return values;
  }

  async function load(next = page) {
    if (!$('order-lookup-rows')) return;
    const ticket = ++requestId;
    const host = document.querySelector('.order-lookup-table-wrap');
    host?.setAttribute('aria-busy', 'true');
    $('order-lookup-summary').textContent = 'Đang tìm đơn hàng…';
    try {
      const query = new URLSearchParams({ ...readFilters(), page: String(next), pageSize: '20' });
      const result = await api(`admin/order-lookup?${query}`);
      if (ticket !== requestId) return;
      page = result.page;
      rows = result.items || [];
      $('order-lookup-rows').innerHTML = rows.map(rowHtml).join('');
      $('order-lookup-empty').hidden = rows.length > 0;
      $('order-lookup-summary').textContent = `${result.totalCount} đơn · Trang ${result.page}`;
      pager('order-lookup-pagination', result, (p) => safe(() => load(p)));
    } catch (error) {
      if (ticket === requestId) {
        $('order-lookup-rows').innerHTML = '';
        $('order-lookup-empty').hidden = false;
        $('order-lookup-empty').textContent = 'Chưa tải được danh sách. Vui lòng thử lại.';
        $('order-lookup-summary').textContent = 'Tìm kiếm chưa thành công.';
      }
      throw error;
    } finally {
      if (ticket === requestId) host?.setAttribute('aria-busy', 'false');
    }
  }

  const table = (headers, rows) => `<div class="lookup-history-wrap"><table class="lookup-history"><thead><tr>${headers.map(h => `<th>${esc(h)}</th>`).join('')}</tr></thead><tbody>${rows || `<tr><td colspan="${headers.length}">Chưa có dữ liệu.</td></tr>`}</tbody></table></div>`;
  function detailHtml(data) {
    const o = data.order;
    const paymentRows = data.payments.map(p => `<tr><td>#${p.paymentId}</td><td>${esc(label(p.method))}</td><td>${statusPill(label(p.status))}</td><td>${money(p.amount)}</td><td>${esc(date(p.createdAt))}</td><td>${esc(date(p.paidAt))}</td><td>${esc(p.providerOrderReference || '—')}</td><td>${esc(p.captureReference || '—')}</td><td>${esc(p.errorCode || '—')}</td></tr>`).join('');
    const refundRows = data.refunds.map(r => `<tr><td>#${r.refundId}</td><td>Payment #${r.paymentId}</td><td>${statusPill(label(r.status))}</td><td>${money(r.amount)} ${esc(r.currency)}</td><td>${esc(label(r.reason))}</td><td>${esc(date(r.createdAt))}</td><td>${esc(date(r.completedAt))}</td><td>${esc(r.providerReference || '—')}</td></tr>`).join('');
    const shipments = data.shipments.length ? data.shipments.map(s => `<section class="finance-block"><h3>${s.direction === 'Return' ? 'Chiều trả về seller' : 'Chiều giao tới buyer'}</h3><p>${esc(s.carrier || 'Chưa có hãng vận chuyển')} · ${esc(s.trackingReference || 'Chưa có mã tracking')} · ${statusPill(label(s.status))}</p><ol class="lookup-events">${s.events.map(e => `<li>${esc(label(e.status))} · ${esc(date(e.occurredAt))}</li>`).join('') || '<li>Chưa có sự kiện tracking.</li>'}</ol></section>`).join('') : '<p class="empty-note">Đơn chưa có vận đơn.</p>';
    const disputes = data.disputes.map(d => `<li>Tranh chấp #${d.disputeId} · ${esc(label(d.status))} · ${esc(date(d.createdAt))} <button type="button" data-open-lookup-case="${d.disputeId}">Mở hồ sơ</button></li>`).join('');
    const products = data.items.map(i => `<tr><td>${esc(i.title || 'Sản phẩm')}</td><td>${Number(i.quantity)}</td><td>${money(i.unitPrice)}</td><td>${money(Number(i.quantity) * Number(i.unitPrice))}</td></tr>`).join('');
    return `<div class="detail lookup-detail" data-admin-order-detail="${o.orderId}">
      <h2>Đơn #${o.orderId}</h2><div class="lookup-actions"><button type="button" data-log-order="${o.orderId}">Mở nhật ký tích hợp</button>${data.disputes.length ? `<button type="button" data-lookup-dispute-tab>Đến tab tranh chấp</button>` : ''}</div>
      <dl><div><dt>Ngày tạo</dt><dd>${esc(date(o.orderDate))}</dd></div><div><dt>Buyer</dt><dd>${esc(o.buyer || '—')}</dd></div><div><dt>Seller</dt><dd>${esc(o.seller || '—')}</dd></div><div><dt>Tổng đơn</dt><dd>${money(o.total)}</dd></div><div><dt>Trạng thái đơn</dt><dd>${esc(label(o.orderStatus))}</dd></div><div><dt>Thanh toán</dt><dd>${esc(o.paymentStatus)}</dd></div><div><dt>Đã thu thành công</dt><dd>${money(data.collectedAmount)}</dd></div><div><dt>Đã hoàn thành công</dt><dd>${money(data.refundedAmount)}</dd></div><div><dt>Còn lại sau hoàn</dt><dd>${money(data.remainingAmount)}</dd></div></dl>
      <h2>Sản phẩm trong đơn</h2>${table(['Sản phẩm', 'SL', 'Đơn giá', 'Thành tiền'], products)}
      <h2>Lịch sử payment</h2>${table(['Mã', 'Phương thức', 'Trạng thái', 'Số tiền', 'Tạo lúc', 'Thanh toán lúc', 'Mã provider order', 'Mã capture', 'Mã lỗi'], paymentRows)}
      <h2>Lịch sử refund</h2>${table(['Mã refund', 'Payment', 'Trạng thái', 'Số tiền', 'Lý do', 'Tạo lúc', 'Hoàn tất lúc', 'Mã provider'], refundRows)}
      <h2>Tracking</h2>${shipments}
      <h2>Tranh chấp liên quan</h2>${disputes ? `<ul class="lookup-disputes">${disputes}</ul>` : '<p class="empty-note">Không có hồ sơ đã chuyển admin cho đơn này.</p>'}
    </div>`;
  }

  async function openOrder(id) {
    const data = await api(`admin/order-lookup/${id}`);
    openDetail(detailHtml(data));
  }

  function quick(button) {
    quickFilter = quickFilter === button.dataset.lookupQuick ? '' : button.dataset.lookupQuick;
    document.querySelectorAll('[data-lookup-quick]').forEach(x => x.setAttribute('aria-pressed', String(x.dataset.lookupQuick === quickFilter)));
    safe(() => load(1));
  }

  function init() {
    if (initialized || !$('order-lookup-filters')) return;
    initialized = true;
    $('order-lookup-filters').addEventListener('submit', event => { event.preventDefault(); safe(() => load(1)); });
    $('refresh-order-lookup').onclick = () => safe(() => load());
    $('clear-order-lookup-filters').onclick = () => {
      $('order-lookup-filters').reset(); quickFilter = '';
      document.querySelectorAll('[data-lookup-quick]').forEach(x => x.setAttribute('aria-pressed', 'false'));
      safe(() => load(1));
    };
    document.querySelectorAll('[data-lookup-quick]').forEach(button => {
      button.setAttribute('aria-pressed', 'false'); button.onclick = () => quick(button);
    });
    document.body.addEventListener('click', event => {
      const open = event.target.closest('[data-open-admin-order]');
      if (open) safe(() => openOrder(Number(open.dataset.openAdminOrder)));
      const dispute = event.target.closest('[data-open-lookup-case]');
      if (dispute) safe(() => G4.Disputes.open(Number(dispute.dataset.openLookupCase)));
      if (event.target.closest('[data-lookup-dispute-tab]')) {
        G4.closeDetail(); document.querySelector('[data-tab="disputes"]')?.click();
      }
    });
  }

  window.G4.AdminOrderLookup = { init, load };
})();
