(() => {
  const $ = id => document.getElementById(id), esc = G4.esc;
  let page = 1, request = 0, orderFilter = null;
  const form = $('transaction-filters');
  const transactionDialog = $('transaction-dialog');
  const dateText = value => value ? G4.date(value) : 'Chưa có';
  const status = value => ({ Succeeded: 'Thành công', Failed: 'Thất bại', Pending: 'Đang xử lý', Verifying: 'Đang xác minh' }[value] || value);
  const method = value => value === 'PayPal' ? 'PayPal Sandbox' : value === 'Card' ? 'Thẻ giả lập' : value || 'Chưa có';
  async function load(next = page) {
    const token = ++request; page = next;
    const params = new URLSearchParams({ page, pageSize: 20 });
    const values = new FormData(form);
    const period = values.get('period') || '30';
    const localDate = d => `${d.getFullYear()}-${String(d.getMonth() + 1).padStart(2, '0')}-${String(d.getDate()).padStart(2, '0')}`;
    const today = new Date();
    today.setHours(0, 0, 0, 0);
    if (period === '30') {
      const start = new Date(today); start.setDate(start.getDate() - 29);
      params.set('from', values.get('from') || localDate(start));
      params.set('to', values.get('to') || localDate(today));
      params.set('paidFrom', `${params.get('from')}T00:00:00`);
      const paidEnd = new Date(`${params.get('to')}T00:00:00`); paidEnd.setDate(paidEnd.getDate() + 1);
      params.set('paidTo', `${localDate(paidEnd)}T00:00:00`); params.set('completedFrom', `${params.get('from')}T00:00:00`); params.set('completedTo', `${localDate(paidEnd)}T00:00:00`);
    } else if (period === 'all') {
      if (values.get('from')) params.set('from', values.get('from'));
      if (values.get('to')) params.set('to', values.get('to'));
      params.set('paidFrom', values.get('from') ? `${values.get('from')}T00:00:00` : '0001-01-01T00:00:00');
      const end = values.get('to') ? new Date(`${values.get('to')}T00:00:00`) : null;
      if (end) { end.setDate(end.getDate() + 1); params.set('paidTo', `${localDate(end)}T00:00:00`); params.set('completedTo', `${localDate(end)}T00:00:00`); }
      params.set('completedFrom', params.get('paidFrom'));
    } else {
      for (const key of ['from', 'to']) if (values.get(key)) params.set(key, values.get(key));
      if (values.get('from')) { params.set('paidFrom', `${values.get('from')}T00:00:00`); params.set('completedFrom', `${values.get('from')}T00:00:00`); }
      if (values.get('to')) { const end = new Date(`${values.get('to')}T00:00:00`); end.setDate(end.getDate() + 1); params.set('paidTo', `${localDate(end)}T00:00:00`); params.set('completedTo', `${localDate(end)}T00:00:00`); }
    }
    for (const key of ['orderId', 'type', 'status']) if (values.get(key)) params.set(key, values.get(key));
    if (orderFilter) params.set('orderId', orderFilter);
    $('transaction-rows').innerHTML = '<tr><td colspan="8">Đang tải giao dịch…</td></tr>';
    const data = await G4.api(`buyer/transactions?${params}`);
    if (token !== request) return;
    $('transaction-summary').textContent = `${data.totalCount} giao dịch`;
    $('transaction-period-note').textContent = `Dòng được lọc theo thời điểm tạo giao dịch. Tổng tiền tính theo PaidAt và CompletedAt trong khoảng đã chọn; giao dịch thiếu thời điểm thành công không được tính.`;
    $('transaction-totals').innerHTML = `<article><span>Đã thu trong kỳ</span><strong>${G4.money(data.periodTotals.paid)}</strong></article><article><span>Đã hoàn trong kỳ</span><strong>${G4.money(data.periodTotals.refunded)}</strong></article><small>Chỉ giao dịch thành công được cộng. Loại và trạng thái chỉ lọc dòng, không làm đổi quy tắc tổng.</small>`;
    const totals = new Map(data.orderTotals.map(x => [x.orderId, x]));
    window.G4BuyerTransactions.rows = data.items;
    $('transaction-rows').innerHTML = data.items.length ? data.items.map(x => {
      const t = totals.get(x.orderId), reference = x.reference || 'Chưa có';
      const detail = x.type === 'Refund'
        ? `${x.detail === 'shipping-adjustment' ? 'Hoàn chênh lệch ship giả lập' : `Lý do: ${esc(x.detail || 'Chưa có')}`} · Payment #${x.originalPaymentId} (${esc(status(x.originalPaymentStatus))})`
        : x.status === 'Failed' ? `Lỗi: ${esc(x.detail || 'Chưa có thông tin lỗi')}` : x.status === 'Succeeded' ? `Thành công lúc ${dateText(x.completedAt)}` : 'Chưa hoàn tất';
      return `<tr><td>${dateText(x.createdAt)}</td><td><button type="button" class="transaction-order-link" data-transaction-order="${x.orderId}">#${x.orderId}</button></td><td>${x.type === 'Payment' ? 'Thanh toán' : 'Hoàn tiền'}</td><td>${method(x.method)}</td><td class="amount">${G4.money(x.amount)}</td><td>${esc(status(x.status))}</td><td><span title="${esc(reference)}">${esc(reference.length > 22 ? `${reference.slice(0, 10)}…${reference.slice(-8)}` : reference)}</span>${x.reference ? ` <button type="button" data-copy="${esc(x.reference)}" aria-label="Sao chép mã giao dịch">Sao chép</button>` : ''}</td><td><button type="button" data-transaction-detail="${x.type}:${x.id}">Xem chi tiết</button></td></tr>`;
    }).join('') : '<tr><td colspan="8">Không có giao dịch phù hợp.</td></tr>';
    G4.pager('transaction-pagination', data, p => G4.safe(() => load(p)));
  }
  form.addEventListener('submit', e => { e.preventDefault(); orderFilter = null; G4.safe(() => load(1)); });
  form.elements.period.addEventListener('change', () => {
    const custom = form.elements.period.value === 'custom';
    form.elements.from.disabled = !custom && form.elements.period.value !== '30';
    form.elements.to.disabled = !custom && form.elements.period.value !== '30';
  });
  $('transaction-clear').addEventListener('click', () => { orderFilter = null; setTimeout(() => G4.safe(() => load(1)), 0); });
  $('transactions-refresh').onclick = () => G4.safe(() => load(page));
  $('transaction-close').onclick = () => transactionDialog.close();
  document.body.addEventListener('click', e => {
    const copy = e.target.closest('[data-copy]');
    if (copy) navigator.clipboard?.writeText(copy.dataset.copy).then(() => G4.message('Đã sao chép mã giao dịch.', true));
    const order = e.target.closest('[data-transaction-order]');
    if (order) { e.preventDefault(); G4.Navigation.select('orders'); G4.safe(() => window.G4BuyerShowOrder?.(Number(order.dataset.transactionOrder))); }
    const detail = e.target.closest('[data-transaction-detail]');
    if (detail) {
      const [type, id] = detail.dataset.transactionDetail.split(':');
      G4.safe(async () => {
      const data = await G4.api(`buyer/transactions/${type}/${id}`);
      $('transaction-dialog-title').textContent = `${data.type === 'Payment' ? 'Thanh toán' : 'Hoàn tiền'} · ${data.orderId ? `Đơn #${data.orderId}` : ''}`;
      $('transaction-dialog-content').innerHTML = `<dl><dt>Thời điểm tạo</dt><dd>${dateText(data.createdAt)}</dd><dt>Phương thức</dt><dd>${method(data.method)}</dd><dt>Số tiền</dt><dd>${G4.money(data.amount)}</dd><dt>Trạng thái</dt><dd>${esc(status(data.status))}</dd><dt>Mã giao dịch</dt><dd>${esc(data.reference || 'Chưa có')}</dd><dt>Thành công lúc</dt><dd>${dateText(data.completedAt)}</dd><dt>Chi tiết</dt><dd>${data.type === 'Refund' ? `${esc(data.detail || 'Chưa có lý do')} · Payment #${data.originalPaymentId || 'Chưa có'} · ${esc(data.originalPaymentReference || 'Chưa có mã payment')}` : data.status === 'Failed' ? `Lỗi: ${esc(data.detail || 'Chưa có thông tin lỗi')}` : 'Chưa có lỗi'}</dd></dl><button type="button" data-transaction-order="${data.orderId}">Mở đơn #${data.orderId}</button>`;
      if (!transactionDialog.open) transactionDialog.showModal();
      });
    }
  });
  window.G4BuyerTransactions = { load, rows: [], openForOrder(id) { orderFilter = id; form.reset(); form.elements.orderId.value = id; form.elements.period.value = 'all'; form.elements.from.disabled = false; form.elements.to.disabled = false; G4.Navigation.select('transactions'); G4.safe(() => load(1)); } };
  form.elements.from.disabled = true; form.elements.to.disabled = true;
})();
