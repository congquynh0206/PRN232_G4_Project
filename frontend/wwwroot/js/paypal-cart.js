(() => {
  const { $, api, esc, money, safe } = G4;
  const storageKey = 'g4-paypal-cart-attempt'; let current, attempt, busy = false, afterChange, generation = 0;
  try { attempt = JSON.parse(sessionStorage.getItem(storageKey) || 'null'); } catch {}
  const save = () => { try { sessionStorage.setItem(storageKey, JSON.stringify(attempt)); } catch {} };
  function html(s) {
    const labels = { Succeeded: 'Đã thanh toán', Failed: 'Chưa thanh toán thành công', Pending: 'Chờ xác nhận PayPal', Verifying: 'Đang xác minh', Cancelled: 'Đã hủy lượt PayPal', PartiallyPaid: 'Một số đơn đã thanh toán', Creating: 'Đang khởi tạo PayPal' };
    const unpaid = s.orders.some(o => o.orderStatus === 'AwaitingPayment' && o.paymentStatus !== 'Succeeded');
    return `<h2>Thanh toán PayPal cả giỏ</h2><p><strong>${esc(labels[s.status] || s.status)}</strong></p>${s.message ? `<p>${esc(s.message)}</p>` : ''}${s.orders.map(o => `<p>Đơn #${Number(o.id)} · ${money(o.amount)} · ${esc(labels[o.paymentStatus] || o.paymentStatus)} <button type="button" data-open-order="${Number(o.id)}">Xem đơn</button></p>`).join('')}<div class="actions"><button type="button" id="paypal-cart-refresh">Kiểm tra kết quả</button>${s.status === 'Pending' && s.approvalUrl ? '<button type="button" id="paypal-cart-approve" class="primary">Tiếp tục xác nhận PayPal</button>' : ''}${['Pending', 'Creating'].includes(s.status) ? '<button type="button" id="paypal-cart-cancel">Hủy lượt PayPal này</button>' : ''}${['Failed', 'PartiallyPaid', 'Cancelled'].includes(s.status) && unpaid ? '<button type="button" id="paypal-cart-retry">Thanh toán các đơn còn lại</button>' : ''}</div><p class="small">Đơn đã thanh toán được giữ nguyên. Khi đang xác minh, hãy chờ kết quả trước khi thanh toán lại.</p>`;
  }
  function render(s) {
    generation++;
    current = s; attempt = { ...attempt, id: s.id, checkoutKey: s.checkoutKey }; save();
    const panel = $('paypal-cart-result'); panel.hidden = false; panel.innerHTML = html(s);
    $('paypal-cart-refresh').onclick = () => safe(() => refresh(true));
    if ($('paypal-cart-approve')) $('paypal-cart-approve').onclick = () => safe(async () => {
      await refresh(); if (current.status === 'Pending' && current.approvalUrl) location.assign(current.approvalUrl);
    });
    if ($('paypal-cart-cancel')) $('paypal-cart-cancel').onclick = () => safe(() => mutate('cancel'));
    if ($('paypal-cart-retry')) $('paypal-cart-retry').onclick = () => safe(() => begin(s.checkoutKey, true));
  }
  async function begin(checkoutKey, fresh = false, redirect = true) {
    if (busy) return; busy = true; generation++;
    try {
      if (fresh || !attempt || attempt.checkoutKey !== checkoutKey) attempt = { checkoutKey, key: crypto.randomUUID() };
      save();
      const s = await api('buyer/paypal-checkouts', 'POST', { checkoutKey, key: attempt.key }); render(s);
      if (redirect && s.status === 'Pending' && s.approvalUrl) location.assign(s.approvalUrl);
      else await afterChange?.();
    } catch (e) {
      $('paypal-cart-result').hidden = false;
      const known = e.status >= 400 && e.status < 500;
      $('paypal-cart-result').innerHTML = `<p>${esc(e.message)} ${known ? 'Chưa thể bắt đầu lượt PayPal này.' : 'Kết quả khởi tạo chưa rõ.'} Đơn đã tạo vẫn nằm trong Đơn của tôi.</p><button type="button" id="paypal-cart-create-retry">Kiểm tra lại lượt PayPal</button>`;
      $('paypal-cart-create-retry').onclick = () => safe(() => begin(checkoutKey)); throw e;
    } finally { busy = false; }
  }
  async function refresh(reconcile = false) {
    if (!attempt?.id || busy) return;
    const ticket = ++generation;
    const s = await api(`buyer/paypal-checkouts/${attempt.id}${reconcile ? '/reconcile' : ''}`, reconcile ? 'POST' : 'GET');
    if (ticket !== generation) return;
    render(s);
    if (reconcile) await afterChange?.();
  }
  async function mutate(action) {
    if (busy || !current) return; busy = true; generation++;
    try { render(await api(`buyer/paypal-checkouts/${current.id}/${action}`, 'POST')); await afterChange?.(); }
    finally { busy = false; }
  }
  function init(onChange) {
    afterChange = onChange;
    const p = new URLSearchParams(location.search);
    if (p.has('paypalCheckoutId')) { attempt = { id: Number(p.get('paypalCheckoutId')) }; save(); }
    if (attempt?.id) safe(async () => {
      await refresh();
      if (p.get('paymentResult') === 'cancelled' && current?.status === 'Pending') await mutate('cancel');
    });
    else if (attempt?.checkoutKey && attempt?.key) safe(() => begin(attempt.checkoutKey, false, false));
    G4.Realtime?.subscribe(['orders'], async () => { if (attempt?.id) await refresh(); });
  }
  async function show(id) { attempt = { id }; save(); await refresh(); $('paypal-cart-result').scrollIntoView({ behavior: 'smooth', block: 'start' }); }
  G4.PayPalCart = { init, begin, refresh, html, show };
})();
