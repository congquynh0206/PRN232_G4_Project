(() => {
  const { $, esc, money, date, label, api, safe, pager, openDetail, shippingStatus } = window.G4;
  let page = 1, quickFilter = '', initialized = false, filters = {}, requestId = 0, detailRequest = 0, failed = false;
  const types = { Auto: 'Tự nhận diện', Order: 'Mã đơn', Payment: 'Mã thanh toán', Refund: 'Mã hoàn tiền', Provider: 'Mã giao dịch' };
  const quickNames = { AwaitingPayment: 'Chờ thanh toán', PaymentFailed: 'Có thanh toán lỗi', HasRefund: 'Có hoàn tiền', HasDispute: 'Có tranh chấp' };
  const fields = { buyer: 'Người mua', seller: 'Người bán', status: 'Đơn hàng', paymentStatus: 'Thanh toán', refundStatus: 'Hoàn tiền', from: 'Tạo từ', to: 'Tạo đến' };
  const zone = () => Intl.DateTimeFormat().resolvedOptions().timeZone;
  // Also accept older API payloads without a suffix; never reinterpret an explicit offset.
  function stamp(value) {
    if (!value) return 'Chưa có';
    const normalized = typeof value === 'string' && !/(Z|[+-]\d{2}:?\d{2})$/i.test(value) ? value + 'Z' : value;
    return date(normalized);
  }
  function pill(value, tone = '') {
    tone ||= /thất bại|lỗi/i.test(value || '') ? 'is-error' : /thành công|đã thanh toán/i.test(value || '') ? 'is-success' : /chờ|xác minh/i.test(value || '') ? 'is-warn' : '';
    return `<span class="lookup-status ${tone}">${esc(value || 'Chưa có')}</span>`;
  }
  function paymentHtml(order) {
    const success = order.paymentStatus?.startsWith('Thành công');
    return pill(success ? 'Đã thanh toán' : order.paymentStatus) + (success && order.hasPaymentFailure ? '<small class="lookup-cell-note">Có lần thử thất bại trước đó</small>' : '');
  }
  function rowHtml(o) {
    return `<tr><td data-label="Đơn hàng"><div class="lookup-cell-stack"><strong class="lookup-order-number">#${Number(o.orderId)}</strong><span class="lookup-cell-note">${esc(stamp(o.orderDate))}</span>${pill(label(o.orderStatus))}${o.hasDispute ? '<span class="lookup-cell-note">Có hồ sơ tranh chấp</span>' : ''}</div></td>
      <td data-label="Người mua / Người bán"><div class="lookup-cell-stack"><span><span class="lookup-person-label">Mua</span> ${esc(o.buyer || '—')}</span><span><span class="lookup-person-label">Bán</span> ${esc(o.seller || '—')}</span></div></td>
      <td data-label="Tổng đơn"><span class="lookup-money">${money(o.total)}</span></td><td data-label="Thanh toán"><div class="lookup-cell-stack">${paymentHtml(o)}</div></td>
      <td data-label="Hoàn tiền"><div class="lookup-cell-stack">${o.hasRefund ? pill(o.refundStatus) + `<small class="lookup-cell-note">Đã hoàn: <strong>${money(o.refundedAmount)}</strong></small>` : '<span class="lookup-cell-note">Chưa có khoản hoàn</span>'}</div></td>
      <td data-label="Vận chuyển">${o.shippingStatus ? pill(shippingStatus(o.shippingStatus)) : '<span class="lookup-cell-note">Chưa có vận đơn</span>'}</td>
      <td><button type="button" data-open-admin-order="${Number(o.orderId)}" aria-label="Xem chi tiết đơn ${Number(o.orderId)}">Xem đơn</button></td></tr>`;
  }
  function readFilters() {
    const values = Object.fromEntries(new FormData($('order-lookup-filters')).entries());
    for (const key of Object.keys(values)) values[key] = values[key].trim();
    for (const key of ['from', 'to']) if (values[key]) values[key] = new Date(values[key]).toISOString();
    if (values.from && values.to && values.from >= values.to) throw new Error('Thời điểm kết thúc phải sau thời điểm bắt đầu.');
    if (!values.search) delete values.searchType;
    return Object.fromEntries(Object.entries(values).filter(([, value]) => value !== ''));
  }
  function renderFilters() {
    const chips = [];
    if (filters.search) chips.push(['search', `${types[filters.searchType || 'Auto']}: ${filters.search}`]);
    for (const [key, caption] of Object.entries(fields)) if (filters[key]) {
      const control = $('order-lookup-filters').elements.namedItem(key);
      const text = key === 'from' || key === 'to' ? stamp(filters[key]) : control?.tagName === 'SELECT' ? [...control.options].find(option => option.value === filters[key])?.textContent || filters[key] : filters[key];
      chips.push([key, `${caption}: ${text}`]);
    }
    $('lookup-applied-filters').innerHTML = chips.map(([key, text]) => `<button type="button" data-remove-lookup-filter="${key}" aria-label="${esc('Bỏ lọc ' + text)}">${esc(text)} <span aria-hidden="true">×</span></button>`).join('');
    const count = chips.filter(([key]) => key !== 'search').length;
    $('lookup-filter-count').textContent = count ? `(${count} đang áp dụng)` : '';
    $('clear-order-lookup-filters').hidden = !chips.length && !quickFilter;
    document.querySelectorAll('[data-lookup-quick]').forEach(x => x.setAttribute('aria-pressed', String(x.dataset.lookupQuick === quickFilter)));
  }
  async function load(next = page) {
    if (!$('order-lookup-rows')) return;
    const ticket = ++requestId;
    const host = document.querySelector('.order-lookup-table-wrap');
    host?.setAttribute('aria-busy', 'true');
    $('order-lookup-summary').textContent = 'Đang tìm đơn hàng…';
    try {
      const query = new URLSearchParams({ ...filters, ...(quickFilter ? { quickFilter } : {}), page: String(next), pageSize: '20' });
      const result = await api(`admin/order-lookup?${query}`);
      if (ticket !== requestId) return;
      failed = false;
      page = result.page;
      const rows = result.items || [];
      $('order-lookup-rows').innerHTML = rows.map(rowHtml).join('');
      $('order-lookup-empty').hidden = rows.length > 0;
      $('lookup-empty-title').textContent = 'Không tìm thấy đơn phù hợp';
      $('lookup-empty-message').textContent = 'Kiểm tra loại mã, nhập đầy đủ mã giao dịch hoặc bớt điều kiện lọc.';
      $('lookup-empty-action').textContent = 'Xem tất cả đơn';
      $('order-lookup-summary').textContent = result.totalCount ? `${result.totalCount} đơn${quickFilter ? ' · ' + quickNames[quickFilter] : ''} · Hiển thị ${(page - 1) * result.pageSize + 1}–${(page - 1) * result.pageSize + rows.length}` : '0 đơn phù hợp';
      pager('order-lookup-pagination', result, p => safe(() => load(p)));
      $('order-lookup-pagination').hidden = !result.totalCount;
    } catch (error) {
      if (ticket !== requestId) return;
      failed = true;
      $('order-lookup-rows').innerHTML = '';
      $('order-lookup-empty').hidden = false;
      $('lookup-empty-title').textContent = 'Chưa tải được kết quả';
      $('lookup-empty-message').textContent = error.message;
      $('lookup-empty-action').textContent = 'Thử lại';
      $('order-lookup-summary').textContent = 'Tìm kiếm chưa thành công';
      $('order-lookup-pagination').hidden = true;
      throw error;
    } finally {
      if (ticket === requestId) host?.setAttribute('aria-busy', 'false');
    }
  }
  const empty = text => `<p class="lookup-help">${esc(text)}</p>`;
  const refs = pairs => `<details><summary>Xem mã tham chiếu</summary><dl class="lookup-reference-list">${pairs.map(([key, value]) => `<div><dt>${esc(key)}</dt><dd>${esc(value || 'Chưa có')}</dd></div>`).join('')}</dl><p>Mã cổng thanh toán chỉ hiển thị 4 ký tự cuối.</p></details>`;
  function paymentCard(p) {
    return `<article class="lookup-transaction"><header><strong>Thanh toán #${p.paymentId} · ${esc(label(p.method))}</strong><span class="lookup-money">${money(p.amount)}</span></header><p>${pill(label(p.status))}</p><p>Tạo: ${esc(stamp(p.createdAt))}${p.paidAt ? `<br>Thành công: ${esc(stamp(p.paidAt))}` : ''}</p>${p.errorCode ? `<p class="lookup-error-code">Mã lỗi: ${esc(p.errorCode)}</p>` : ''}${refs([['Mã giao dịch', p.providerOrderReference], ['Mã thu tiền (capture)', p.captureReference]])}</article>`;
  }
  function refundCard(r) {
    const reason = { 'shipping-adjustment': 'Hoàn chênh lệch ship · Giả lập nội bộ', cancel: 'Hoàn do hủy đơn', return: 'Hoàn do trả hàng', dispute: 'Hoàn theo tranh chấp' }[r.reason] || r.reason;
    return `<article class="lookup-transaction"><header><strong>Hoàn tiền #${r.refundId}</strong><span class="lookup-money">${money(r.amount)} ${esc(r.currency)}</span></header><p>${pill(label(r.status))} · Thanh toán #${r.paymentId}</p><p>${esc(reason)}</p><p>Tạo: ${esc(stamp(r.createdAt))}${r.completedAt ? `<br>Hoàn tất: ${esc(stamp(r.completedAt))}` : ''}</p>${refs([['Mã hoàn tiền', r.providerReference]])}</article>`;
  }
  function shipmentCard(s) {
    return `<article class="lookup-shipment"><h3>${s.direction === 'Return' ? 'Chiều trả · Người mua → Người bán' : 'Chiều giao · Người bán → Người mua'}</h3>${pill(shippingStatus(s.status, s.direction))}<p>${esc(s.carrier || 'Chưa có hãng vận chuyển')} · Mã: ${esc(s.trackingReference || 'Chưa có')}<br>Tạo vận đơn: ${esc(stamp(s.createdAt))}${s.deliveredAt ? `<br>Giao thành công: ${esc(stamp(s.deliveredAt))}` : ''}</p><ol class="lookup-events">${s.events.map(e => `<li>${esc(shippingStatus(e.status, s.direction))}<time>${esc(stamp(e.occurredAt))}</time><span>${esc(window.G4.shippingActor?.(e) || 'Chưa ghi nhận người thực hiện (dữ liệu cũ)')}</span>${e.reattemptAt ? `<p>Dự kiến giao lại: ${esc(stamp(e.reattemptAt))}</p>` : ''}${e.note ? `<p>${esc(e.note)}</p>` : ''}</li>`).join('') || '<li>Chưa có sự kiện vận chuyển.</li>'}</ol></article>`;
  }
  function detailHtml(data) {
    const o = data.order;
    const tabs = [['overview', 'Tổng quan'], ['payments', `Thanh toán (${data.payments.length})`], ['refunds', `Hoàn tiền (${data.refunds.length})`], ['shipments', `Vận chuyển (${data.shipments.length})`]];
    const panel = (id, html) => `<section id="lookup-${o.orderId}-${id}" data-lookup-panel="${id}" role="tabpanel" aria-labelledby="lookup-${o.orderId}-tab-${id}" tabindex="0" ${id === 'overview' ? '' : 'hidden'}>${html}</section>`;
    const overview = `<dl class="lookup-overview"><div><dt>Người mua</dt><dd>${esc(o.buyer || '—')}</dd></div><div><dt>Người bán</dt><dd>${esc(o.seller || '—')}</dd></div><div><dt>Tổng giá trị đơn</dt><dd>${money(o.total)}</dd></div><div><dt>Thanh toán</dt><dd>${paymentHtml(o)}</dd></div></dl>
      <h3>Sản phẩm đã đặt</h3><ul class="lookup-products">${data.items.map(i => `<li><span>${esc(i.title || 'Sản phẩm')}<small>${Number(i.quantity)} × ${money(i.unitPrice)}</small></span><strong>${money(Number(i.quantity) * Number(i.unitPrice))}</strong></li>`).join('') || '<li>Chưa có dữ liệu sản phẩm.</li>'}</ul>
      <h3>Hồ sơ tranh chấp</h3>${data.disputes.length ? data.disputes.map(d => `<p>#${d.disputeId} · ${esc(label(d.status))} · ${esc(stamp(d.createdAt))} <button type="button" data-open-lookup-case="${d.disputeId}">Mở hồ sơ</button></p>`).join('') : empty(o.hasDispute ? 'Có hồ sơ đang được các bên xử lý, chưa chuyển admin.' : 'Đơn hàng chưa có hồ sơ tranh chấp.')}
      <div class="lookup-actions"><button type="button" data-log-order="${o.orderId}">Xem nhật ký tích hợp</button>${data.disputes.length ? '<button type="button" data-lookup-dispute-tab>Đến danh sách tranh chấp</button>' : ''}</div>`;
    return `<div class="lookup-detail" data-admin-order-detail="${o.orderId}"><header class="lookup-detail-head"><div><h2 tabindex="-1">Đơn hàng #${o.orderId}</h2><p class="lookup-detail-intro">Tạo ${esc(stamp(o.orderDate))}</p></div>${pill(label(o.orderStatus))}</header>
      <div class="lookup-totals"><div class="lookup-total"><span>Đã thu thành công</span><strong>${money(data.collectedAmount)}</strong></div><div class="lookup-total"><span>Đã hoàn thành công</span><strong>${money(data.refundedAmount)}</strong></div><div class="lookup-total"><span>Còn lại sau hoàn</span><strong>${money(data.remainingAmount)}</strong></div></div>
      <p class="lookup-help">Tổng toàn bộ lịch sử đơn. Các lần đang chờ, thất bại hoặc đang xác minh chưa cộng vào số tiền thành công.</p>
      <div class="lookup-detail-tabs" role="tablist" aria-label="Chi tiết đơn hàng">${tabs.map(([id, title]) => `<button type="button" id="lookup-${o.orderId}-tab-${id}" role="tab" data-lookup-tab="${id}" aria-controls="lookup-${o.orderId}-${id}" aria-selected="${id === 'overview'}" tabindex="${id === 'overview' ? 0 : -1}">${title}</button>`).join('')}</div>
      ${panel('overview', overview)}${panel('payments', data.payments.map(paymentCard).join('') || empty('Đơn này chưa có lần thử thanh toán.'))}${panel('refunds', data.refunds.map(refundCard).join('') || empty('Đơn này chưa có khoản hoàn tiền.'))}${panel('shipments', data.shipments.map(shipmentCard).join('') || empty('Đơn này chưa có vận đơn.'))}
      <p class="lookup-help">Giờ hiển thị theo ${esc(zone())}. Đóng chi tiết để trở lại đúng kết quả đang tra cứu.</p></div>`;
  }
  async function openOrder(id) {
    const ticket = ++detailRequest;
    openDetail(`<div class="lookup-detail-state" role="status">Đang tải đơn #${Number(id)}…</div>`);
    try {
      const data = await api(`admin/order-lookup/${id}`);
      if (ticket !== detailRequest) return;
      openDetail(detailHtml(data));
      document.querySelector('[data-admin-order-detail] h2')?.focus();
    } catch (error) {
      if (ticket !== detailRequest) return;
      openDetail(`<div class="lookup-detail-state"><p>${esc(error.message)}</p><button type="button" data-open-admin-order="${Number(id)}">Thử tải lại đơn #${Number(id)}</button></div>`);
    }
  }
  function selectTab(key, focus = false) {
    document.querySelectorAll('[data-lookup-tab]').forEach(button => {
      const active = button.dataset.lookupTab === key;
      button.setAttribute('aria-selected', String(active));
      button.tabIndex = active ? 0 : -1;
      if (active && focus) button.focus();
    });
    document.querySelectorAll('[data-lookup-panel]').forEach(panel => { panel.hidden = panel.dataset.lookupPanel !== key; });
  }
  function clear() {
    $('order-lookup-filters').reset();
    filters = {}; quickFilter = '';
    updateHelp(); renderFilters();
    return load(1);
  }
  function updateHelp() {
    const type = $('lookup-search-type').value;
    const copy = {
      Auto: 'Nhập số hoặc #125 để tìm đơn hàng. Với mã giao dịch, nhập đầy đủ để tìm chính xác.',
      Order: 'Nhập số đơn hàng, ví dụ 125 hoặc #125. Chỉ tìm theo mã đơn.',
      Payment: 'Nhập số thanh toán nội bộ, ví dụ 125. Xem số này trong lịch sử thanh toán của đơn.',
      Refund: 'Nhập số hoàn tiền nội bộ, ví dụ 125. Xem số này trong lịch sử hoàn tiền của đơn.',
      Provider: 'Nhập đầy đủ mã order/capture/refund từ cổng thanh toán. Không tìm bằng một phần mã.'
    };
    $('lookup-search-help').textContent = copy[type] || copy.Auto;
  }
  function init() {
    if (initialized || !$('order-lookup-filters')) return;
    initialized = true;
    $('lookup-timezone').textContent = `Ngày trên danh sách là ngày tạo đơn · Múi giờ ${zone()}`;
    $('lookup-search-type').onchange = updateHelp;
    $('order-lookup-filters').addEventListener('submit', event => {
      event.preventDefault();
      safe(() => { filters = readFilters(); renderFilters(); return load(1); });
    });
    $('refresh-order-lookup').onclick = () => safe(() => load());
    $('clear-order-lookup-filters').onclick = () => safe(clear);
    $('lookup-empty-action').onclick = () => safe(() => failed ? load() : clear());
    $('detail-dialog').addEventListener('close', () => { detailRequest++; });
    document.querySelectorAll('[data-lookup-quick]').forEach(button => {
      button.onclick = () => safe(() => {
        filters = readFilters();
        quickFilter = button.dataset.lookupQuick;
        renderFilters(); return load(1);
      });
    });
    $('lookup-applied-filters').addEventListener('click', event => {
      const button = event.target.closest('[data-remove-lookup-filter]');
      if (!button) return;
      const key = button.dataset.removeLookupFilter;
      delete filters[key];
      if (key === 'search') { delete filters.searchType; $('lookup-search-type').value = 'Auto'; updateHelp(); }
      const control = $('order-lookup-filters').elements.namedItem(key);
      if (control) control.value = '';
      renderFilters(); safe(() => load(1));
    });
    document.body.addEventListener('click', event => {
      const open = event.target.closest('[data-open-admin-order]');
      if (open) safe(() => openOrder(Number(open.dataset.openAdminOrder)));
      const tab = event.target.closest('[data-lookup-tab]');
      if (tab) selectTab(tab.dataset.lookupTab);
      const dispute = event.target.closest('[data-open-lookup-case]');
      if (dispute) safe(() => G4.Disputes.open(Number(dispute.dataset.openLookupCase)));
      if (event.target.closest('[data-lookup-dispute-tab]')) { G4.closeDetail(); document.querySelector('[data-tab="disputes"]')?.click(); }
    });
    document.body.addEventListener('keydown', event => {
      const button = event.target.closest('[data-lookup-tab]');
      if (!button || !['ArrowRight', 'ArrowLeft', 'Home', 'End'].includes(event.key)) return;
      const tabs = [...document.querySelectorAll('[data-lookup-tab]')];
      const i = tabs.indexOf(button);
      const next = event.key === 'Home' ? 0 : event.key === 'End' ? tabs.length - 1 : (i + (event.key === 'ArrowRight' ? 1 : -1) + tabs.length) % tabs.length;
      event.preventDefault(); selectTab(tabs[next].dataset.lookupTab, true);
    });
    renderFilters();
  }
  window.G4.AdminOrderLookup = { init, load };
})();
