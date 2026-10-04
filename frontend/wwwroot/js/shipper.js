(() => {
  const {$,esc,api,message,safe,shippingStatus,pager,modal,openDetail,shipmentHtml,shipperShipmentCard} = G4;
  let page = 1, filter = 'pending', listRequest = 0, detailRequest = 0;
  async function load(next = page) {
    const request = ++listRequest;
    $('shipment-list').setAttribute('aria-busy', 'true');
    try {
      const result = await api(`shipper/shipments/page?page=${next}&pageSize=10&filter=${filter}`);
      if (request !== listRequest) return;
      page = result.page;
      $('pending-shipment-count').textContent = result.pendingCount;
      $('mine-shipment-count').textContent = result.mineCount;
      document.querySelectorAll('[data-shipment-filter]').forEach(b => {
        b.classList.toggle('active', b.dataset.shipmentFilter === filter);
        b.setAttribute('aria-pressed', String(b.dataset.shipmentFilter === filter));
      });
      $('shipment-list').innerHTML = result.items.length ? result.items.map(shipperShipmentCard).join('') : `<p class="empty-note">${filter === 'pending' ? 'Không có vận đơn chờ nhận.' : 'Bạn chưa nhận vận đơn nào.'}</p>`;
      pager('shipment-pagination', result, n => safe(() => load(n)));
    } finally { if (request === listRequest) $('shipment-list').setAttribute('aria-busy', 'false'); }
  }
  function eventActions(o, s) {
    const next = {LabelCreated:['PickedUp'],PickedUp:['InTransit'],InTransit:['OutForDelivery'],OutForDelivery:['Delivered','DeliveryFailed'],DeliveryFailed:['OutForDelivery','ReturningToSender'],ReturningToSender:['ReturnedToSeller']}[s.status] || [];
    return next.filter(x => x !== 'OutForDelivery' || s.deliveryAttempts < 2).map(x => `<button data-event="${x}" data-shipment="${s.id}" data-order="${o.id}" data-direction="${s.direction}">${esc(shippingStatus(x,s.direction))}</button>`).join('');
  }
  async function detail(id, preserveScroll = false) {
    const request = ++detailRequest;
    const o = await api(`orders/${id}`);
    if (request !== detailRequest) return;
    const cards = o.shipments.map(s => `<p class="small">Lấy hàng: ${esc(s.pickupAddressSnapshot || 'Chưa có địa chỉ')}<br>Giao tới: ${esc(s.deliveryAddressSnapshot || 'Chưa có địa chỉ')}</p>${shipmentHtml(o,s,s.direction,eventActions(o,s))}`).join('');
    openDetail(`<div class="detail"><h2>Vận chuyển đơn #${o.id}</h2><p class="small">Khối lượng: ${o.totalWeightKg == null ? 'Chưa có dữ liệu' : `${Number(o.totalWeightKg)} kg`}</p>${cards}</div>`,preserveScroll);
  }
  document.body.addEventListener('click', e => {
    const open = e.target.closest('[data-open-order]');
    if (open) return safe(() => detail(Number(open.dataset.openOrder)));
    const claim = e.target.closest('[data-claim-shipment]');
    if (claim) return safe(async () => {
      const input = await modal({title:'Nhận vận đơn',description:`Bạn sẽ phụ trách vận đơn của đơn #${claim.dataset.order}.`,confirm:'Nhận vận đơn'});
      if (!input) return;
      try {
        await api(`shipper/shipments/${claim.dataset.claimShipment}/claim`,'POST');
        filter = 'mine';
        message('Đã nhận vận đơn. Bạn có thể cập nhật tracking.',true);
      } finally { await load(1); }
    });
    const b = e.target.closest('[data-event]');
    if (!b) return;
    safe(async () => {
      const failed = b.dataset.event === 'DeliveryFailed';
      const input = await modal({title:'Cập nhật trạng thái vận chuyển',description:`Đơn #${b.dataset.order} · ${shippingStatus(b.dataset.event,b.dataset.direction)}`,fields:[{name:'location',label:'Địa điểm',required:true},{name:'note',label:failed?'Lý do giao thất bại':'Ghi chú',required:failed}],confirm:'Lưu trạng thái'});
      if (!input) return;
      const result = await api(`shipments/${b.dataset.shipment}/events`,'POST',{status:b.dataset.event,eventId:crypto.randomUUID(),location:input.location,note:input.note || null});
      message(result.applied?'Đã cập nhật tiến trình vận chuyển.':'Trạng thái đã thay đổi. Danh sách đã được làm mới.',result.applied);
      await load();
      await detail(Number(b.dataset.order),true);
    });
  });
  document.querySelectorAll('[data-shipment-filter]').forEach(b => b.onclick = () => { filter = b.dataset.shipmentFilter; safe(() => load(1)); });
  $('refresh-shipments').onclick = () => safe(() => load());
  safe(load);
})();
