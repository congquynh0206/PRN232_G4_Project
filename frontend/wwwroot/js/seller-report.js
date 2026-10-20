(() => {
  const { $, api, esc, money, pager } = G4;
  const fields = [
    ['buyerPaid', 'Buyer đã trả', 'paid'],
    ['buyerRefunded', 'Đã hoàn', 'refunded'],
    ['platformFee', 'Phí thực thu', 'fee'],
    ['net', 'Net seller', 'net'],
  ];
  const types = {
    Sale: 'Tiền buyer đã trả', PlatformSubsidy: 'Trợ giá nền tảng', PlatformFee: 'Phí nền tảng',
    Refund: 'Hoàn cho buyer', PlatformSubsidyReversal: 'Đảo trợ giá', FeeCredit: 'Hoàn phí nền tảng',
    ShippingAdjustmentCost: 'Phí đổi địa chỉ', ShippingAdjustmentDebt: 'Phí đổi địa chỉ ghi nợ',
    ReturnShippingCost: 'Phí ship trả hàng',
  };
  let request = 0, last = null;
  const zone = Intl.DateTimeFormat().resolvedOptions().timeZone || 'UTC';
  const day = (d) => `${d.getFullYear()}-${String(d.getMonth() + 1).padStart(2, '0')}-${String(d.getDate()).padStart(2, '0')}`;
  function preset() {
    const today = new Date();
    const start = new Date(today.getFullYear(), today.getMonth(), today.getDate());
    const value = $('report-preset').value;
    if (value === 'custom') return;
    if (value === 'month') start.setDate(1);
    if (value === '7' || value === '30') start.setDate(start.getDate() - Number(value) + 1);
    $('report-from').value = day(start);
    $('report-to').value = day(today);
  }
  function clear() {
    $('report-cards').innerHTML = '';
    $('report-chart').innerHTML = '';
    $('report-rows').innerHTML = '';
    $('report-pagination').innerHTML = '';
  }
  function period(value, group) {
    if (group === 'month') return value.slice(0, 7);
    if (group === 'week') return `Tuần từ ${value}`;
    return value;
  }
  function cards(t) {
    $('report-cards').innerHTML = fields.map(([key, title]) =>
      `<article class="report-card"><span>${title}</span><strong>${money(t[key])}</strong></article>`
    ).join('') + `<article class="report-card"><span>Đơn thanh toán thành công</span><strong>${t.paidOrders}</strong></article>`;
  }
  function chart(data) {
    const selected = [...document.querySelectorAll('[data-report-series]:checked')].map(x => x.dataset.reportSeries);
    const keys = ['buyerPaid', 'buyerRefunded', 'net'].filter(x => selected.includes(x));
    const host = $('report-chart');
    if (!data.length || !keys.length) { host.innerHTML = '<p class="empty-note">Chọn ít nhất một chuỗi để xem biểu đồ.</p>'; return; }
    const values = data.flatMap(p => keys.map(k => Number(p[k])));
    const max = Math.max(0, ...values), min = Math.min(0, ...values);
    const span = max - min || 1, left = 55, right = 720, top = 14, bottom = 195;
    const x = i => left + i * (right - left) / Math.max(1, data.length - 1);
    const y = value => bottom - (Number(value) - min) / span * (bottom - top);
    const colors = { buyerPaid: '#28699b', buyerRefunded: '#bb5b38', net: '#405a3b' };
    const names = { buyerPaid: 'Buyer đã trả', buyerRefunded: 'Đã hoàn', net: 'Net seller' };
    const series = keys.map(k => {
      const points = data.map((p, i) => `${x(i)},${y(p[k])}`).join(' ');
      const markers = data.map((p, i) => {
        const cx = x(i), cy = y(p[k]);
        const shape = k === 'buyerRefunded'
          ? `<rect x="${cx - 3}" y="${cy - 3}" width="6" height="6" />`
          : `<circle cx="${cx}" cy="${cy}" r="3" />`;
        return `<g fill="${colors[k]}">${shape}<title>${esc(period(p.period, last.group))} · ${names[k]}: ${money(p[k])}</title></g>`;
      }).join('');
      return `<polyline fill="none" stroke="${colors[k]}" stroke-width="2" ${k === 'buyerRefunded' ? 'stroke-dasharray="6 4"' : ''} points="${points}"/>${markers}`;
    }).join('');
    const zero = y(0);
    host.innerHTML = `<svg viewBox="0 0 750 230" role="img" aria-label="Biểu đồ đường: ${keys.map(k => names[k]).join(', ')}. Bảng bên dưới có giá trị chính xác.">
      <line x1="${left}" y1="${zero}" x2="${right}" y2="${zero}" stroke="#929da4"/>
      <text x="3" y="${top + 6}">${esc(money(max))}</text><text x="3" y="${bottom}">${esc(money(min))}</text>
      <text x="${left}" y="222">${esc(period(data[0].period, last.group))}</text>
      <text x="${right}" y="222" text-anchor="end">${esc(period(data.at(-1).period, last.group))}</text>${series}</svg>`;
  }
  function detail(p) {
    const adjustments = [
      ['Trợ giá nền tảng', p.platformSubsidy], ['Đảo trợ giá', p.subsidyReversal],
      ['Phí đổi địa chỉ', p.shippingAdjustments], ['Chi phí ship trả hàng', p.returnShippingCost],
      ['Thu hồi phí ship trả từ buyer (đã phản ánh trong khoản refund)', p.returnShippingRecovery],
    ].map(([name, amount]) => `<li>${name}: ${money(amount)}</li>`).join('');
    const entries = (p.sources || []).map(s => `<li>${esc(types[s.type] || s.type)} · ${money(s.amount)} · bút toán #${s.transactionId}` +
      (s.orderId ? ` · <a href="/Seller?orderId=${s.orderId}">đơn #${s.orderId}</a>` : '') +
      (s.refundId ? ` · refund #${s.refundId}` : '') + '</li>').join('');
    return `<details><summary>${esc(period(p.period, last.group))} · xem đối chiếu</summary>
      <div class="report-detail"><strong>Trợ giá và điều chỉnh</strong><ul>${adjustments}</ul>
      <strong>Giao dịch nguồn</strong>${entries ? `<ul>${entries}</ul>` : '<p>Không có bút toán trong kỳ.</p>'}</div></details>`;
  }
  function table(result) {
    $('report-rows').innerHTML = result.items.map(p => `<tr><th scope="row">${detail(p)}</th><td>${p.paidOrders}</td><td>${money(p.buyerPaid)}</td><td>${money(p.buyerRefunded)}</td><td>${money(p.platformFee)}</td><td>${money(p.net)}</td></tr>`).join('');
    pager('report-pagination', result, page => load(page));
  }
  async function load(page = 1) {
    const id = ++request;
    clear();
    const status = $('report-status');
    const from = $('report-from').value, to = $('report-to').value;
    if (!from || !to || from > to) { status.textContent = 'Vui lòng chọn khoảng ngày hợp lệ (từ ngày không sau đến ngày).'; return; }
    const days = Math.round((Date.parse(to) - Date.parse(from)) / 86400000) + 1;
    if (days > 366 || days > 93 && $('report-group').value === 'day') {
      status.textContent = days > 366 ? 'Khoảng tối đa 366 ngày.' : 'Trên 93 ngày, vui lòng nhóm theo tuần hoặc tháng.'; return;
    }
    status.textContent = 'Đang tải báo cáo…';
    status.setAttribute('aria-busy', 'true');
    try {
      const params = new URLSearchParams({ from, to, group: $('report-group').value, timeZone: zone, page: String(page), pageSize: '10' });
      const result = await api(`seller/finance/report?${params}`);
      if (id !== request) return;
      last = result;
      cards(result.totals);
      chart(result.chart);
      table(result);
      const warnings = result.unreconciledPayments + result.unreconciledRefunds;
      status.textContent = warnings
        ? `Chưa đối soát đủ: ${result.unreconciledPayments} payment và ${result.unreconciledRefunds} refund chưa có bút toán liên kết. Không suy đoán số tiền còn thiếu.`
        : result.chart.every(p => p.paidOrders === 0 && [p.net, p.buyerPaid, p.buyerRefunded, p.platformFee, p.platformSubsidy, p.subsidyReversal, p.shippingAdjustments, p.returnShippingCost].every(x => x === 0))
          ? 'Không có phát sinh trong kỳ; các kỳ được hiển thị bằng 0.'
          : `Đã tải ${result.totalCount} kỳ · ${result.from} đến ${result.to} · ${result.timeZone}.`;
    } catch (error) {
      if (id === request) status.textContent = `${error.message} Bấm Làm mới để thử lại.`;
    } finally {
      if (id === request) status.setAttribute('aria-busy', 'false');
    }
  }
  function init() {
    $('report-zone').textContent = zone;
    preset();
    $('report-preset').onchange = () => { preset(); load(); };
    ['report-from', 'report-to'].forEach(k => $(k).onchange = () => { $('report-preset').value = 'custom'; load(); });
    $('report-group').onchange = () => load();
    $('report-filter').onsubmit = e => { e.preventDefault(); load(); };
    $('report-refresh').onclick = () => load(last?.page || 1);
    document.querySelectorAll('[data-report-series]').forEach(x => x.onchange = () => { if (last) chart(last.chart); });
  }
  G4.SellerReport = { init, load };
})();