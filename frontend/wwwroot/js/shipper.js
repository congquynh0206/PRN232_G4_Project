(() => {
  const {$,esc,date,label,api,message,safe,shippingStatus,trackingHtml,pager,modal,openDetail}=G4;
  let page=1;
  async function load(next=page){
    const result=await api(`shipper/shipments/page?page=${next}&pageSize=10`);
    page=result.page;
    $('shipment-list').innerHTML=result.items.length?result.items.map(o=>`<article class="order-card" data-order-row="${o.id}"><div class="order-card-main"><span class="overline">Đơn vận chuyển · #${o.id}</span><strong>${esc(label(o.status))}</strong><small>Tạo: ${date(o.orderDate)} · Cập nhật: ${date(o.updatedAt||o.orderDate)}</small><small>${o.shipments.map(s=>`${s.direction==='Return'?'Chiều trả':'Chiều giao'}: ${shippingStatus(s.status,s.direction)}`).join(' · ')}</small></div><button data-open-order="${o.id}">Xem chi tiết</button></article>`).join(''):'<p class="empty-note">Chưa có vận đơn.</p>';
    pager('shipment-pagination',result,n=>safe(()=>load(n)));
  }
  function eventActions(o,s,direction){
    if(!s)return '';
    const next={LabelCreated:['PickedUp'],PickedUp:['InTransit'],InTransit:['OutForDelivery'],OutForDelivery:['Delivered','DeliveryFailed'],DeliveryFailed:['OutForDelivery','ReturningToSender'],ReturningToSender:['ReturnedToSeller']}[s.status]||[];
    return next.filter(x=>x!=='OutForDelivery'||s.deliveryAttempts<2).map(x=>`<button data-event="${x}" data-shipment="${s.id}" data-order="${o.id}" data-direction="${direction}">${esc(shippingStatus(x,direction))}</button>`).join('');
  }
  async function detail(id){
    const o=await api(`orders/${id}`);
    openDetail(`<div class="detail"><h2>Đơn vận chuyển #${o.id}</h2><p class="small">Tạo: ${date(o.orderDate)} · Cập nhật: ${date(o.updatedAt||o.orderDate)}</p>${trackingHtml(o,eventActions)}</div>`);
  }
  document.body.addEventListener('click',e=>{
    const open=e.target.closest('[data-open-order]');if(open)return safe(()=>detail(Number(open.dataset.openOrder)));
    const b=e.target.closest('[data-event]');if(!b)return;
    safe(async()=>{
      const input=await modal({title:'Cập nhật trạng thái vận chuyển',description:`Đơn #${b.dataset.order} · ${shippingStatus(b.dataset.event,b.dataset.direction)}`,fields:[{name:'location',label:'Địa điểm',required:true},{name:'note',label:'Ghi chú'}],confirm:'Lưu trạng thái'});
      if(!input)return;
      await api(`shipments/${b.dataset.shipment}/events`,'POST',{status:b.dataset.event,eventId:crypto.randomUUID(),location:input.location,note:input.note||null});
      message('Đã cập nhật tiến trình vận chuyển.',true);
      const id=Number(b.dataset.order);
      const row=document.querySelector(`[data-order-row="${id}"]`);
      if(row){
        const o=await api(`orders/${id}`);
        row.querySelector('.order-card-main strong').textContent=label(o.status);
        row.querySelector('.order-card-main small').textContent=`Tạo: ${date(o.orderDate)} · Cập nhật: ${date(o.updatedAt||o.orderDate)}`;
        row.querySelectorAll('.order-card-main small')[1].textContent=o.shipments.map(s=>`${s.direction==='Return'?'Chiều trả':'Chiều giao'}: ${shippingStatus(s.status,s.direction)}`).join(' · ');
      }
      await detail(id);
    });
  });
  $('refresh-shipments').onclick=()=>safe(()=>load(1));
  safe(load);
})();
