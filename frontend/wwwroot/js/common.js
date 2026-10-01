(() => {
  const $ = id => document.getElementById(id);
  const money = value => '$' + Number(value || 0).toFixed(2);
  const esc = value => String(value ?? '').replace(/[&<>"']/g, c => ({'&':'&amp;','<':'&lt;','>':'&gt;','"':'&quot;',"'":'&#39;'}[c]));
  async function api(path, method = 'GET', body = null) {
    const response = await fetch('/api/proxy/' + path, {
      method, headers: method === 'POST' ? {'Content-Type':'application/json'} : {},
      body: method === 'POST' ? JSON.stringify(body ?? {}) : undefined
    });
    if (response.redirected && response.url.includes('/Account/Login')) { location.assign(response.url); throw new Error('Phiên đăng nhập đã hết hạn'); }
    const data = await response.json().catch(() => ({}));
    if (!response.ok) throw new Error(data.error || data.title || `HTTP ${response.status}`);
    return data;
  }
  function message(value, success = false) {
    const box = $('message'); if (!box) return;
    box.textContent = value; box.className = 'message show' + (success ? ' success' : '');
    window.scrollTo({top:0,behavior:'smooth'});
  }
  async function safe(work) { try { await work(); } catch (error) { message(error.message || String(error)); } }
  function shippingStatus(status, direction) {
    const back = direction === 'Return';
    return ({NotCreated:'Chưa tạo vận đơn',ShipmentCreationFailed:'Tạo vận đơn thất bại',LabelCreated:'Đã tạo vận đơn',
      PickedUp:back?'Đã lấy từ buyer':'Đã lấy từ seller',InTransit:'Đang vận chuyển',
      OutForDelivery:back?'Đang giao tới seller':'Đang giao tới buyer',Delivered:back?'Seller đã nhận hàng trả':'Buyer đã nhận hàng',
      DeliveryFailed:'Giao hàng thất bại',ReturningToSender:'Đang chuyển hoàn',ReturnedToSeller:'Đã chuyển hoàn'})[status] ?? status ?? 'Chưa tạo';
  }
  function returnStatus(status) {
    return ({Requested:'Chờ seller duyệt',Approved:'Đã duyệt',Rejected:'Đã từ chối',ReturnShipping:'Đang trả về seller',
      ReceivedBySeller:'Seller đã nhận',RefundPending:'Đang hoàn tiền',Refunded:'Đã hoàn tiền',RefundFailed:'Hoàn tiền thất bại'})[status] ?? status ?? 'Không có';
  }
  function shipmentHtml(o, shipment, direction, actions = '') {
    const events = shipment ? o.events.filter(e => e.shippingInfoId === shipment.id).sort((a,b) => new Date(a.occurredAt)-new Date(b.occurredAt)) : [];
    const timeline = events.length ? `<ol class="timeline">${events.map(e => `<li><strong>${esc(shippingStatus(e.status,direction))}</strong>${e.location?` · ${esc(e.location)}`:''}<small>${new Date(e.occurredAt).toLocaleString('vi-VN')}${e.note?` · ${esc(e.note)}`:''}</small></li>`).join('')}</ol>` : '<p class="empty-note">Chưa có sự kiện.</p>';
    return `<section class="tracking-card${direction==='Return'?' tracking-return':''}"><div class="tracking-head"><div><h4>${direction==='Return'?'Buyer → Seller':'Seller → Buyer'}</h4></div><strong class="tracking-status">${esc(shippingStatus(shipment?.status,direction))}</strong></div><p class="tracking-number">Mã vận đơn: <strong>${esc(shipment?.trackingNumber ?? 'Chưa có')}</strong></p>${timeline}${actions?`<div class="tracking-actions"><div class="actions">${actions}</div></div>`:''}</section>`;
  }
  function trackingHtml(o, actionFactory) {
    const outbound = o.shipments.find(s => s.direction === 'Outbound');
    const returned = o.shipments.find(s => s.direction === 'Return');
    return `<div class="tracking-list">${shipmentHtml(o,outbound,'Outbound',actionFactory?.(o,outbound,'Outbound')||'')}${returned?shipmentHtml(o,returned,'Return',actionFactory?.(o,returned,'Return')||''):''}</div>`;
  }
  function orderCard(o, source = 'orders') { return `<div class="order-card"><div><strong>Đơn #${o.id} · ${esc(o.status)}</strong><small>${new Date(o.orderDate).toLocaleString('vi-VN')} · ${money(o.totalPrice)}</small></div><button data-open-order="${o.id}" data-source="${source}">Xem chi tiết</button></div>`; }
  window.G4 = {$,money,esc,api,message,safe,shippingStatus,returnStatus,trackingHtml,orderCard};
})();
