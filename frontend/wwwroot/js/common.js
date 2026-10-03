(() => {
  const $ = id => document.getElementById(id);
  const money = value => { const amount=Number(value || 0); return (amount<0?'-':'')+'$'+Math.abs(amount).toFixed(2); };
  const date = value => value ? new Date(value).toLocaleString('vi-VN', {dateStyle:'short', timeStyle:'short'}) : 'Chưa có';
  const esc = value => String(value ?? '').replace(/[&<>"']/g, c => ({'&':'&amp;','<':'&lt;','>':'&gt;','"':'&quot;',"'":'&#39;'}[c]));
  const labels = {
    AwaitingPayment:'Chờ thanh toán',Paid:'Đã thanh toán',Preparing:'Đang chuẩn bị hàng',Shipping:'Đang giao hàng',Delivered:'Đã giao hàng',Closed:'Hoàn tất',Cancelled:'Đã hủy',Expired:'Hết hạn thanh toán',CancelRequested:'Đang chờ duyệt hủy',
    Pending:'Đang chờ',Processing:'Đang xử lý',Succeeded:'Thành công',Failed:'Thất bại',Verifying:'Đang xác minh',Refunded:'Đã hoàn tiền',PartiallyRefunded:'Đã hoàn một phần',RefundPending:'Đang hoàn tiền',RefundFailed:'Hoàn tiền thất bại',
    OnHold:'Đang giữ tiền',Available:'Có thể rút',Negative:'Số dư âm',Released:'Đã giải ngân',Held:'Đang giữ',Completed:'Hoàn tất',Approved:'Đã duyệt',Rejected:'Đã từ chối',Requested:'Đang chờ duyệt',
    Created:'Đã tạo',Sent:'Đã gửi',Captured:'Đã ghi nhận',Retrying:'Đang thử lại',Queued:'Đang chờ gửi',PaidOut:'Đã chuyển tiền',Returned:'Đã trả lại',InProgress:'Đang xử lý',FundsSent:'Đã gửi ngân hàng',Active:'Đang hoạt động',
    Outbound:'Chiều giao hàng',Return:'Chiều trả hàng',Card:'Thẻ giả lập',PayPal:'PayPal',
    Level1:'Cấp 1',Level2:'Cấp 2',Level3:'Cấp 3',Starter:'Khởi đầu',Growth:'Tăng trưởng',Trusted:'Uy tín'
  };
  const label = value => labels[value] || value || 'Chưa có';
  function friendlyError(status, raw) {
    const value=String(raw||'').toLowerCase();
    if(/coupon|promotion|discount/.test(value))return 'Mã giảm giá chưa áp dụng được. Vui lòng kiểm tra điều kiện sử dụng hoặc thử mã khác.';
    if(/card|expiry|payment|paypal/.test(value))return 'Thanh toán chưa hoàn tất. Vui lòng kiểm tra phương thức thanh toán rồi thử lại.';
    if(/stock|inventory|product/.test(value))return 'Sản phẩm hoặc số lượng đã thay đổi. Vui lòng cập nhật giỏ hàng rồi thử lại.';
    if(/address|shipping fee/.test(value))return 'Địa chỉ giao hàng chưa hợp lệ. Vui lòng kiểm tra rồi thử lại.';
    if(/balance|payout|limit/.test(value))return 'Chưa thể rút số tiền này. Vui lòng kiểm tra số dư và giới hạn hiện tại.';
    if(/refund|return/.test(value))return 'Yêu cầu trả hàng hoặc hoàn tiền chưa thể xử lý. Vui lòng kiểm tra trạng thái đơn.';
    if(status===401)return 'Phiên đăng nhập đã hết hạn. Vui lòng đăng nhập lại.';
    if(status===403)return 'Tài khoản của bạn chưa có quyền thực hiện thao tác này.';
    if(status===404)return 'Không tìm thấy thông tin cần xem. Vui lòng làm mới danh sách.';
    if(status===409)return 'Trạng thái đã thay đổi hoặc thao tác chưa thể thực hiện. Vui lòng kiểm tra lại đơn hàng.';
    return 'Thao tác chưa thành công. Vui lòng kiểm tra thông tin và thử lại.';
  }
  async function api(path, method = 'GET', body = null) {
    let response;
    try { response = await fetch('/api/proxy/' + path, {method, headers:method === 'POST' ? {'Content-Type':'application/json'} : {}, body:method === 'POST' ? JSON.stringify(body ?? {}) : undefined}); }
    catch { throw new Error('Chưa thể kết nối. Vui lòng kiểm tra mạng rồi thử lại.'); }
    if (response.redirected && response.url.includes('/Account/Login')) { location.assign(response.url); throw new Error('Phiên đăng nhập đã hết hạn. Vui lòng đăng nhập lại.'); }
    const data = await response.json().catch(() => ({}));
    if (!response.ok) {
      throw new Error(friendlyError(response.status,data.error || data.title));
    }
    return data;
  }
  let messageTimer;
  function message(value, success = false) {
    const box = $('message'); if (!box) return;
    box.textContent = value; box.className = 'message show' + (success ? ' success' : '');
    clearTimeout(messageTimer);
    messageTimer=setTimeout(()=>box.classList.remove('show'), success?6000:10000);
  }
  async function safe(work) { try { await work(); } catch (error) { message(error.message || 'Có lỗi xảy ra. Vui lòng thử lại.'); } }
  function shippingStatus(status, direction) {
    const back = direction === 'Return';
    return ({NotCreated:'Chưa tạo vận đơn',ShipmentCreationFailed:'Tạo vận đơn thất bại',LabelCreated:'Đã tạo vận đơn',
      PickedUp:back?'Đã lấy hàng từ người mua':'Đã lấy hàng từ người bán',InTransit:'Đang vận chuyển',
      OutForDelivery:back?'Đang giao tới người bán':'Đang giao tới người mua',Delivered:back?'Người bán đã nhận hàng trả':'Người mua đã nhận hàng',
      DeliveryFailed:'Giao hàng thất bại',ReturningToSender:'Đang chuyển hoàn',ReturnedToSeller:'Đã chuyển hoàn cho người bán'})[status] ?? label(status);
  }
  function returnStatus(status) {
    return ({Requested:'Chờ người bán duyệt',Approved:'Đã duyệt',Rejected:'Đã từ chối',ReturnShipping:'Đang trả hàng về người bán',
      ReceivedBySeller:'Người bán đã nhận hàng',RefundPending:'Đang hoàn tiền',Refunded:'Đã hoàn tiền',RefundFailed:'Hoàn tiền thất bại'})[status] ?? label(status);
  }
  const eventText = value => ({'Label created':'Đã tạo vận đơn giao hàng','Return label created':'Đã tạo vận đơn trả hàng','Distribution hub':'Trung tâm phân phối','Recipient unavailable':'Không liên hệ được người nhận'})[value] || value;
  function shipmentHtml(o, shipment, direction, actions = '') {
    const events = shipment ? o.events.filter(e => e.shippingInfoId === shipment.id).sort((a,b) => new Date(a.occurredAt)-new Date(b.occurredAt)) : [];
    const timeline = events.length ? `<div class="timeline-scroll"><ol class="timeline">${events.map(e => `<li><strong>${esc(shippingStatus(e.status,direction))}</strong><small>${date(e.occurredAt)}</small>${e.location?`<span>${esc(eventText(e.location))}</span>`:''}${e.note?`<span>${esc(eventText(e.note))}</span>`:''}</li>`).join('')}</ol></div>` : '<p class="empty-note">Chưa có mốc vận chuyển.</p>';
    return `<section class="tracking-card${direction==='Return'?' tracking-return':''}"><div class="tracking-head"><h4>${direction==='Return'?'Người mua → Người bán':'Người bán → Người mua'}</h4><strong class="tracking-status">${esc(shippingStatus(shipment?.status,direction))}</strong></div><p class="tracking-number">Mã vận đơn: <strong>${esc(shipment?.trackingNumber ?? 'Chưa có')}</strong></p>${timeline}${actions?`<div class="tracking-actions"><div class="actions">${actions}</div></div>`:''}</section>`;
  }
  function trackingHtml(o, actionFactory) {
    const outbound = o.shipments.find(s => s.direction === 'Outbound');
    const returned = o.shipments.find(s => s.direction === 'Return');
    return `<div class="tracking-list">${shipmentHtml(o,outbound,'Outbound',actionFactory?.(o,outbound,'Outbound')||'')}${returned?shipmentHtml(o,returned,'Return',actionFactory?.(o,returned,'Return')||''):''}</div>`;
  }
  function orderCard(o, source = 'orders') { return `<article class="order-card" data-order-row="${o.id}"><div class="order-card-main"><span class="overline">${source==='seller'?'Đơn bán hàng':'Đơn mua hàng'} · #${o.id}</span><strong>${esc(label(o.status))}</strong><small>Tạo: ${date(o.orderDate)} · Cập nhật: ${date(o.updatedAt || o.orderDate)}</small></div><div class="order-card-side"><b>${money(o.totalPrice)}</b><button data-open-order="${o.id}" data-source="${source}">Xem chi tiết</button></div></article>`; }
  function pager(id, result, onPage) {
    const host = $(id), count = Math.max(1, Math.ceil(result.totalCount/result.pageSize));
    const current = Math.min(Math.max(1, result.page), count);
    const pages = new Set([1, count]);
    for (let page = Math.max(1, current - 2); page <= Math.min(count, current + 2); page++) pages.add(page);
    const numbers = [...pages].sort((a,b) => a-b);
    const controls = [];
    for (let index = 0; index < numbers.length; index++) {
      if (index && numbers[index] - numbers[index-1] > 1) controls.push('<span class="page-gap" aria-hidden="true">…</span>');
      const page = numbers[index];
      controls.push(`<button type="button" data-page="${page}" ${page===current?'aria-current="page" disabled':''} aria-label="Trang ${page}">${page}</button>`);
    }
    host.innerHTML = `<span>Trang ${current}/${count} · ${result.totalCount} mục</span><div class="page-controls"><button type="button" data-page="${current-1}" ${current<=1?'disabled':''}>Trước</button>${controls.join('')}<button type="button" data-page="${current+1}" ${current>=count?'disabled':''}>Sau</button></div>`;
    host.querySelectorAll('[data-page]').forEach(b => b.onclick = () => onPage(Number(b.dataset.page)));
  }
  function pageChanges(currentIds, nextIds, changedId) {
    const current = new Set(currentIds);
    const next = new Set(nextIds);
    return {
      remove: currentIds.filter(id => !next.has(id)),
      upsert: nextIds.filter(id => !current.has(id) || id === changedId),
      order: nextIds
    };
  }
  function patchPagedList(id, items, keyOf, render, changedId, attribute, emptyHtml) {
    const host = $(id);
    const selector = `[${attribute}]`;
    const currentIds = [...host.querySelectorAll(selector)].map(node => Number(node.getAttribute(attribute)));
    const nextIds = items.map(item => Number(keyOf(item)));
    const changes = pageChanges(currentIds, nextIds, Number(changedId));
    changes.remove.forEach(key => host.querySelector(`[${attribute}="${key}"]`)?.remove());
    host.querySelector('.empty-note')?.remove();
    for (const item of items) {
      const key = Number(keyOf(item));
      if (!changes.upsert.includes(key)) continue;
      const row = host.querySelector(`[${attribute}="${key}"]`);
      if (row) row.outerHTML = render(item);
      else host.insertAdjacentHTML('beforeend', render(item));
    }
    let next = host.firstElementChild;
    for (const key of changes.order) {
      const row = host.querySelector(`[${attribute}="${key}"]`);
      if (row !== next) host.insertBefore(row, next);
      next = row.nextElementSibling;
    }
    if (!items.length) host.innerHTML = emptyHtml;
  }
  function openDetail(html) {
    const dialog = $('detail-dialog');
    $('detail-content').innerHTML = html;
    if (!dialog.open) dialog.showModal();
    dialog.scrollTop = 0;
    layoutTimelines();
    $('detail-close')?.focus?.();
  }
  function closeDetail() {
    const dialog = $('detail-dialog');
    if (dialog.open) dialog.close();
  }
  function layoutTimeline(timeline) {
    const nodes=[...timeline.children];
    const width=timeline.parentElement?.clientWidth || 320;
    const columns=Math.min(4,nodes.length,Math.max(1,Math.floor(width/180))) || 1;
    timeline.dataset.columns=String(columns);
    timeline.style.setProperty('--timeline-columns',String(columns));
    nodes.forEach((node,index)=>{
      const row=Math.floor(index/columns);
      const column=index%columns;
      node.style.gridRow=String(row+1);
      node.style.gridColumn=String(row%2?columns-column:column+1);
      node.classList.toggle('timeline-reverse',row%2===1);
      node.classList.toggle('timeline-turn',columns>1 && index===Math.min(nodes.length,(row+1)*columns)-1 && index<nodes.length-1);
      node.classList.toggle('timeline-turn-left',row%2===1);
    });
  }
  function layoutTimelines() {
    $('detail-content')?.querySelectorAll?.('.timeline').forEach(layoutTimeline);
  }
  function modal({title, description, fields = [], confirm = 'Xác nhận'}) {
    const dialog = $('action-dialog');
    $('action-title').textContent = title;
    $('action-description').textContent = description || '';
    $('action-fields').innerHTML = fields.map(f => `<label for="action-${esc(f.name)}">${esc(f.label)}</label><input id="action-${esc(f.name)}" name="${esc(f.name)}" ${f.required?'required':''} placeholder="${esc(f.placeholder || '')}"/>`).join('');
    $('action-confirm').textContent = confirm;
    return new Promise(resolve => {
      const form = $('action-form');
      const done = ok => { dialog.close(); form.onsubmit = null; dialog.oncancel = null; $('action-cancel').onclick = null; resolve(ok ? Object.fromEntries(new FormData(form)) : null); };
      form.onsubmit = e => { e.preventDefault(); done(true); };
      $('action-cancel').onclick = () => done(false);
      dialog.oncancel = e => { e.preventDefault(); done(false); };
      dialog.showModal();
      (form.querySelector('input') || $('action-confirm')).focus();
    });
  }
  $('detail-close').onclick = closeDetail;
  if(typeof ResizeObserver!=='undefined')new ResizeObserver(layoutTimelines).observe($('detail-content'));
  window.addEventListener?.('resize',layoutTimelines);
  window.G4 = {$,money,date,esc,label,api,message,safe,shippingStatus,returnStatus,trackingHtml,orderCard,pager,pageChanges,patchPagedList,modal,openDetail,closeDetail,layoutTimeline};
})();
