(() => {
  const $ = id => document.getElementById(id);
  const money = value => '$' + Number(value || 0).toFixed(2);
  const esc = value => String(value ?? '').replace(/[&<>"']/g, c => ({'&':'&amp;','<':'&lt;','>':'&gt;','"':'&quot;',"'":'&#39;'}[c]));
  const state = {cart: [], addresses: [], orders: [], quote: null, checkoutKey: null, orderId: null, role: 'buyer'};

  function message(value, success = false) {
    const box = $('message'); box.textContent = value; box.className = 'message show' + (success ? ' success' : '');
    window.scrollTo({top: 0, behavior: 'smooth'});
  }
  async function api(path, method = 'GET', body = null) {
    const response = await fetch('/demo/api/' + path, {
      method, headers: {'X-Demo-Role': state.role, ...(method === 'POST' ? {'Content-Type': 'application/json'} : {})},
      body: method === 'POST' ? JSON.stringify(body ?? {}) : undefined
    });
    const data = await response.json().catch(() => ({}));
    if (!response.ok) throw new Error(data.error || `HTTP ${response.status}`);
    return data;
  }
  async function safe(work) { try { await work(); } catch (error) { message(error.message || String(error)); } }
  function tab(id) {
    document.querySelectorAll('.tab-panel').forEach(p => p.classList.toggle('active', p.id === id));
    document.querySelectorAll('.tabs button').forEach(b => b.classList.toggle('active', b.dataset.tab === id));
    if (id === 'seller' && state.role !== 'seller') { state.role = 'seller'; $('role').value = 'seller'; }
    if (['summary', 'checkout', 'orders'].includes(id) && state.role !== 'buyer') { state.role = 'buyer'; $('role').value = 'buyer'; }
    if (id === 'orders') safe(loadOrders);
    if (id === 'seller') safe(loadSeller);
    if (id === 'inbox') safe(loadInbox);
  }
  function request() { return {addressId: Number($('address').value), items: state.cart.map(x => ({productId: x.productId, quantity: x.quantity})), couponCode: $('coupon').value.trim() || null, checkoutKey: state.checkoutKey}; }
  function itemHtml(item, editable) {
    const quantity = editable ? `<label>Số lượng <select data-quantity="${item.productId}">${Array.from({length: Math.min(item.available, 5)}, (_, i) => `<option value="${i + 1}" ${item.quantity === i + 1 ? 'selected' : ''}>${i + 1}</option>`).join('')}</select></label>` : `<span>SL ${item.quantity}</span>`;
    return `<div class="item"><div class="thumb">▣</div><div class="item-info"><strong>${esc(item.title)}</strong><small>G4 Demo Store · Còn ${item.available ?? '—'} sản phẩm</small></div>${quantity}<span class="item-price">${money(item.price * item.quantity)}</span></div>`;
  }
  function renderCart() {
    $('cart').classList.toggle('empty', state.cart.length === 0);
    $('cart').innerHTML = state.cart.length ? state.cart.map(x => itemHtml(x, true)).join('') : 'Chọn số sản phẩm rồi bấm “Tạo giỏ ngẫu nhiên”.';
    $('checkout-items').innerHTML = state.cart.map(x => itemHtml(x, false)).join('');
    $('cart-subtotal').textContent = 'Tạm tính: ' + money(state.cart.reduce((n, x) => n + x.price * x.quantity, 0));
    $('continue').disabled = state.cart.length === 0;
    $('pay').disabled = state.cart.length === 0 || !state.quote;
    document.querySelectorAll('[data-quantity]').forEach(select => select.onchange = () => {
      const item = state.cart.find(x => x.productId === Number(select.dataset.quantity));
      if (item) item.quantity = Number(select.value);
      state.orderId = null; state.checkoutKey = crypto.randomUUID(); state.quote = null;
      renderCart(); safe(recalculate);
    });
  }
  async function randomCart() {
    state.role = 'buyer'; $('role').value = 'buyer';
    state.cart = await api('catalog/random?count=' + $('count').value);
    state.checkoutKey = crypto.randomUUID(); state.orderId = null; state.quote = null;
    renderCart(); await recalculate(); message(`Đã tạo ${state.cart.length} sản phẩm ngẫu nhiên.`, true);
  }
  async function recalculate() {
    if (!state.cart.length || !$('address').value) return;
    state.quote = await api('quote', 'POST', request());
    $('subtotal').textContent = money(state.quote.subtotal);
    $('discount').textContent = '-' + money(state.quote.discount);
    $('shipping').textContent = money(state.quote.shipping);
    $('total').textContent = money(state.quote.total);
    $('pay').disabled = false;
  }
  async function ensureOrder() {
    if (state.orderId) return state.orderId;
    const created = await api('orders', 'POST', request());
    state.orderId = created.id; return created.id;
  }
  async function pay() {
    $('pay').disabled = true;
    try {
      const id = await ensureOrder();
      const method = document.querySelector('input[name="method"]:checked').value;
      if (method === 'paypal') {
        const result = await api(`orders/${id}/pay/paypal`, 'POST', {key: crypto.randomUUID()});
        window.location.assign(result.approvalUrl);
        return;
      }
      const result = await api(`orders/${id}/pay/card`, 'POST', {number: $('card-number').value, expiry: $('card-expiry').value, key: crypto.randomUUID()});
      message(result.status === 'Succeeded' ? `Thanh toán thành công. Mã đơn #${id}.` : `Thẻ thử trả về: ${result.status}. Bạn có thể thử lại trên đơn #${id}.`, result.status === 'Succeeded');
      tab('orders'); await showOrder(id, 'orders');
    } finally { $('pay').disabled = false; }
  }
  async function loadOrders() {
    state.orders = await api('orders');
    renderOrders();
  }
  function renderOrders() {
    const filter = $('order-filter').value;
    const groups = {pending:['AwaitingPayment','Paid','Preparing','CancelRequested'], shipping:['Shipping'], complete:['Delivered','Closed'], cancelled:['Cancelled','Expired']};
    const orders = filter === 'all' ? state.orders : state.orders.filter(o => groups[filter]?.includes(o.status));
    $('order-list').innerHTML = orders.length ? orders.map(o => orderCard(o, 'orders')).join('') : '<p class="empty-note">Không có đơn trong nhóm này.</p>';
  }
  async function loadSeller() {
    const orders = await api('orders');
    $('seller-list').innerHTML = orders.length ? orders.map(o => orderCard(o, 'seller')).join('') : '<p class="empty-note">Chưa có đơn hàng.</p>';
  }
  function orderCard(o, source) { return `<div class="order-card"><div><strong>Đơn #${o.id} · ${esc(o.status)}</strong><small>${new Date(o.orderDate).toLocaleString('vi-VN')} · ${money(o.totalPrice)}</small></div><button data-open-order="${o.id}" data-source="${source}">Xem chi tiết</button></div>`; }
  async function showOrder(id, source) {
    const o = await api(`orders/${id}`); const buyer = source === 'orders';
    const latestPayment = o.payments.at(-1)?.status ?? 'Chưa thanh toán';
    const outward = o.shipments.find(s => s.direction === 'Outbound');
    const returned = o.shipments.find(s => s.direction === 'Return');
    const status = `<div class="status-grid"><div class="status-box"><span>Đơn hàng</span><strong>${esc(o.status)}</strong></div><div class="status-box"><span>Thanh toán</span><strong>${esc(latestPayment)}</strong></div><div class="status-box"><span>Giao hàng</span><strong>${esc(outward?.status ?? 'Chưa tạo')}</strong></div><div class="status-box"><span>Trả hàng</span><strong>${esc(o.returnRequest?.status ?? 'Không có')}</strong></div></div>`;
    const lines = o.items.map(x => `<div class="item"><div class="thumb">▣</div><div class="item-info"><strong>${esc(x.productTitleSnapshot)}</strong><small>SL ${x.quantity}</small></div><b>${money(x.unitPrice * x.quantity)}</b></div>`).join('');
    const timeline = o.events.length ? `<ol class="timeline">${o.events.map(e => `<li><strong>${esc(e.status)}</strong> ${esc(e.location || '')}<small>${new Date(e.occurredAt).toLocaleString('vi-VN')} ${esc(e.note || '')}</small></li>`).join('')}</ol>` : '<p class="empty-note">Chưa có sự kiện tracking.</p>';
    const moneyBlock = `<div class="money"><span>Tiền hàng</span><span>${money(o.subtotal)}</span><span>Giảm giá</span><span>-${money(o.discountAmount)}</span><span>Phí giao</span><span>${money(o.shippingFee)}</span><strong>Tổng</strong><strong>${money(o.totalPrice)}</strong></div>`;
    const actions = buyer ? buyerActions(o) : sellerActions(o);
    const detail = `<div class="detail"><h2>Đơn #${o.id}</h2><p class="small">Giao tới: ${esc(o.addressSnapshot)}</p>${status}<h3>Sản phẩm</h3>${lines}<h3>Thanh toán</h3>${moneyBlock}<h3>Tracking</h3><p>Mã chiều đi: <strong>${esc(outward?.trackingNumber ?? 'Chưa có')}</strong>${returned ? ` · Mã hàng trả: <strong>${esc(returned.trackingNumber)}</strong>` : ''}</p>${timeline}<h3>Thao tác</h3><div class="actions">${actions}</div>${o.cancelDecisionReason ? `<p>Lý do từ chối hủy: ${esc(o.cancelDecisionReason)}</p>` : ''}${o.returnRequest ? `<p>Yêu cầu trả: ${esc(o.returnRequest.reason)} · ${esc(o.returnRequest.status)} ${esc(o.returnRequest.decisionReason || '')}</p>` : ''}${o.refunds.length ? `<p>Hoàn tiền: ${o.refunds.map(r => `${money(r.amount)} (${esc(r.status)})`).join(', ')}</p>` : ''}</div>`;
    $(buyer ? 'order-detail' : 'seller-detail').innerHTML = detail;
    $(buyer ? 'order-detail' : 'seller-detail').scrollIntoView({behavior: 'smooth', block: 'start'});
  }
  function buyerActions(o) {
    if (o.status === 'AwaitingPayment') return `<button data-action="cancel" data-id="${o.id}">Hủy đơn</button><button data-action="retry-card" data-id="${o.id}">Trả bằng thẻ thử</button><button data-action="retry-paypal" data-id="${o.id}">Trả bằng PayPal Sandbox</button>`;
    if (o.status === 'Paid' || o.status === 'Preparing') return `<button data-action="cancel" data-id="${o.id}">Yêu cầu hủy</button>`;
    if (o.status === 'Delivered' && !o.returnRequest) return `<input id="return-reason" placeholder="Lý do trả hàng" aria-label="Lý do trả hàng" /><button data-action="return" data-id="${o.id}">Yêu cầu trả toàn bộ</button>`;
    return '<span class="small">Không có thao tác nào ở trạng thái này.</span>';
  }
  function sellerActions(o) {
    const buttons = [];
    if (o.status === 'Paid') buttons.push(`<button data-action="prepare" data-id="${o.id}">Chuẩn bị hàng</button>`);
    if (o.status === 'Preparing') buttons.push(`<button data-action="ship" data-id="${o.id}">Tạo vận đơn</button><button data-action="fail-carrier" data-id="${o.id}">Lỗi carrier 2 lần</button>`);
    if (o.status === 'CancelRequested') buttons.push(`<button data-action="accept-cancel" data-id="${o.id}">Chấp nhận hủy + hoàn tiền</button><button data-action="reject-cancel" data-id="${o.id}">Từ chối hủy</button>`);
    const outbound = o.shipments.find(s => s.direction === 'Outbound');
    const ret = o.shipments.find(s => s.direction === 'Return');
    for (const s of [outbound, ret].filter(Boolean)) {
      if (s.status === 'ShipmentCreationFailed') buttons.push(`<button data-action="ship" data-id="${o.id}">Thử tạo vận đơn lại</button>`);
      const options = {LabelCreated:['PickedUp'],PickedUp:['InTransit'],InTransit:['OutForDelivery'],OutForDelivery:['Delivered','DeliveryFailed'],DeliveryFailed:['OutForDelivery','ReturningToSender'],ReturningToSender:['ReturnedToSeller']}[s.status] ?? [];
      options.forEach(next => buttons.push(`<button data-action="event" data-shipment="${s.id}" data-next="${next}" data-id="${o.id}">${s.direction === 'Return' ? 'Hàng trả: ' : ''}${next}</button>`));
    }
    if (outbound?.status === 'ReturnedToSeller' && !o.refunds.some(r => r.status === 'Succeeded')) buttons.push(`<button data-action="refund-delivery" data-id="${o.id}">Hoàn tiền do giao thất bại</button>`);
    if (o.status === 'Cancelled' && o.payments.some(p => p.status === 'Succeeded') && !o.refunds.some(r => r.status === 'Succeeded')) buttons.push(`<button data-action="refund-cancel" data-id="${o.id}">Thử hoàn tiền hủy đơn</button>`);
    if (o.returnRequest?.status === 'Requested') buttons.push(`<button data-action="approve-return" data-return="${o.returnRequest.id}" data-id="${o.id}">Duyệt trả hàng</button><button data-action="reject-return" data-return="${o.returnRequest.id}" data-id="${o.id}">Từ chối trả hàng</button>`);
    if ((o.returnRequest?.status === 'ReturnShipping' || o.returnRequest?.status === 'Approved') && o.shipments.some(s => s.direction === 'Return' && s.status === 'Delivered')) buttons.push(`<button data-action="receive-return" data-return="${o.returnRequest.id}" data-id="${o.id}">Xác nhận nhận hàng trả</button>`);
    if (o.returnRequest?.status === 'ReceivedBySeller' || o.returnRequest?.status === 'RefundFailed') buttons.push(`<button data-action="refund-return" data-id="${o.id}">Hoàn tiền đơn trả</button>`);
    return buttons.join('') || '<span class="small">Không có thao tác nào ở trạng thái này.</span>';
  }
  async function action(button) {
    const id = Number(button.dataset.id); const kind = button.dataset.action; let path; let body = {};
    if (kind === 'cancel') path = `orders/${id}/cancel`;
    else if (kind === 'retry-card') {
      const result = await api(`orders/${id}/pay/card`, 'POST', {number: '4111111111111111', expiry: '12/30', key: crypto.randomUUID()});
      message(`Thanh toán thẻ thử: ${result.status}`, result.status === 'Succeeded'); await showOrder(id, 'orders'); return;
    }
    else if (kind === 'retry-paypal') { const result = await api(`orders/${id}/pay/paypal`, 'POST', {key: crypto.randomUUID()}); location.assign(result.approvalUrl); return; }
    else if (kind === 'return') { path = `orders/${id}/returns`; body = {reason: $('return-reason').value.trim()}; }
    else if (kind === 'prepare') path = `seller/orders/${id}/prepare`;
    else if (kind === 'ship') path = `seller/orders/${id}/ship`;
    else if (kind === 'fail-carrier') { path = 'carrier/fail-next'; body = {orderId:id,direction:'Outbound',count:2}; }
    else if (kind === 'accept-cancel' || kind === 'reject-cancel') { path = `seller/orders/${id}/cancel/decision`; body = {approve:kind === 'accept-cancel', reason: kind === 'reject-cancel' ? prompt('Lý do từ chối hủy') || '' : null}; }
    else if (kind === 'event') { path = `shipments/${button.dataset.shipment}/events`; body = {status:button.dataset.next,eventId:crypto.randomUUID(),location:'Demo hub',note:button.dataset.next === 'DeliveryFailed' ? 'Recipient unavailable' : null}; }
    else if (kind === 'refund-delivery' || kind === 'refund-return' || kind === 'refund-cancel') { path = `seller/orders/${id}/refund`; body = {reason:kind === 'refund-return' ? 'return' : kind === 'refund-cancel' ? 'cancel' : 'delivery-failed'}; }
    else if (kind === 'approve-return') path = `seller/returns/${button.dataset.return}/approve`;
    else if (kind === 'reject-return') { path = `seller/returns/${button.dataset.return}/reject`; body = {reason: prompt('Lý do từ chối') || ''}; }
    else if (kind === 'receive-return') path = `seller/returns/${button.dataset.return}/receive`;
    else return;
    await api(path, 'POST', body); message('Đã cập nhật trạng thái.', true);
    await showOrder(id, state.role === 'buyer' ? 'orders' : 'seller');
    if (state.role === 'buyer') await loadOrders(); else await loadSeller();
  }
  async function loadInbox() {
    const mails = await api('inbox');
    $('email-list').innerHTML = mails.length ? mails.map(x => `<div class="order-card"><div><strong>${esc(x.subject)} · ${esc(x.status)}</strong><small>Đơn #${x.orderId} → ${esc(x.recipient)} · ${new Date(x.createdAt).toLocaleString('vi-VN')}</small><p>${esc(x.body)}</p></div></div>`).join('') : '<p class="empty-note">Chưa có thông báo.</p>';
  }
  document.querySelectorAll('.tabs button').forEach(button => button.onclick = () => tab(button.dataset.tab));
  $('role').onchange = () => { state.role = $('role').value; tab(state.role === 'seller' ? 'seller' : 'orders'); };
  $('random').onclick = () => safe(randomCart);
  $('continue').onclick = () => { tab('checkout'); safe(recalculate); };
  $('address').onchange = () => { state.orderId = null; state.checkoutKey = crypto.randomUUID(); safe(recalculate); };
  $('edit-address').onclick = () => {
    const address = state.addresses.find(a => a.id === Number($('address').value));
    if (!address) return;
    for (const [field, value] of Object.entries({name:address.fullName,street:address.street,city:address.city,state:address.state,country:address.country}))
      $('address-' + field).value = value || '';
    $('address-fields').classList.toggle('hidden');
  };
  $('save-address').onclick = () => safe(async () => {
    const id = Number($('address').value);
    const updated = await api(`addresses/${id}/edit`, 'POST', {
      fullName:$('address-name').value, street:$('address-street').value, city:$('address-city').value,
      state:$('address-state').value, country:$('address-country').value
    });
    state.addresses = state.addresses.map(a => a.id === id ? updated : a);
    $('address').selectedOptions[0].textContent = `${updated.fullName} — ${updated.street}, ${updated.city}, ${updated.state}`;
    $('address-fields').classList.add('hidden');
    state.orderId = null; state.checkoutKey = crypto.randomUUID();
    await recalculate(); message('Đã lưu địa chỉ nhận hàng.', true);
  });
  $('apply-coupon').onclick = () => { state.orderId = null; state.checkoutKey = crypto.randomUUID(); safe(recalculate); };
  $('coupon').oninput = () => { state.quote = null; $('pay').disabled = true; };
  document.querySelectorAll('input[name="method"]').forEach(r => r.onchange = () => $('card-fields').classList.toggle('hidden', document.querySelector('input[name="method"]:checked').value !== 'card'));
  $('pay').onclick = () => safe(pay);
  $('refresh-orders').onclick = () => safe(loadOrders);
  $('order-filter').onchange = renderOrders;
  $('refresh-seller').onclick = () => safe(loadSeller);
  $('refresh-inbox').onclick = () => safe(loadInbox);
  document.body.addEventListener('click', event => {
    const open = event.target.closest('[data-open-order]');
    if (open) return safe(() => showOrder(Number(open.dataset.openOrder), open.dataset.source));
    const button = event.target.closest('[data-action]'); if (button) return safe(() => action(button));
  });
  safe(async () => {
    await api('bootstrap', 'POST');
    state.addresses = await api('addresses');
    $('address').innerHTML = state.addresses.map(a => `<option value="${a.id}" ${a.isDefault ? 'selected' : ''}>${esc(a.fullName)} — ${esc(a.street)}, ${esc(a.city)}, ${esc(a.state)}</option>`).join('');
    const params = new URLSearchParams(location.search);
    if (params.has('orderId')) {
      tab('orders'); await showOrder(Number(params.get('orderId')), 'orders');
      if (params.has('paymentResult')) message(`PayPal: ${params.get('paymentResult') === 'checked' ? 'đã kiểm tra kết quả; xem trạng thái đơn bên dưới' : 'chưa hoàn tất'}.`, params.get('paymentResult') === 'checked');
    }
  });
})();
