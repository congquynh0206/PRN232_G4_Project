(() => {
  const {$,esc,api,message,safe,pager,modal,openDetail,date,ShipperUI:ui} = G4;
  let page = 1, filter = 'pending', search = '', status = 'all', direction = 'all', listRequest = 0, detailRequest = 0;
  let active = null, detailTab = 'info', order = null;
  const busy = new Set();
  const localDate=value=>date(typeof value==='string'&&!/(Z|[+-]\d{2}:\d{2})$/i.test(value)?value+'Z':value);
  $('shipment-status').insertAdjacentHTML('beforeend',Object.entries(ui.labels).map(([value,label])=>`<option value="${value}">${esc(label)}</option>`).join(''));
  async function load(next = page) {
    const request = ++listRequest;
    const requestedFilter = filter;
    const query = new URLSearchParams({page:next,pageSize:10,filter,search,status,direction});
    $('shipment-list').setAttribute('aria-busy', 'true');
    $('shipment-summary').textContent = 'Đang tải vận đơn…';
    try {
      const result = await api(`shipper/shipments/page?${query}`);
      if (request !== listRequest) return;
      page = result.page;
      $('pending-shipment-count').textContent = result.pendingCount;
      $('mine-shipment-count').textContent = result.mineCount;
      document.querySelectorAll('[data-shipment-filter]').forEach(b => {
        b.classList.toggle('active', b.dataset.shipmentFilter === requestedFilter);
        b.setAttribute('aria-pressed', String(b.dataset.shipmentFilter === requestedFilter));
      });
      $('shipment-summary').textContent = `${result.totalCount} vận đơn${search||status!=='all'||direction!=='all'?' phù hợp bộ lọc':''} · ${requestedFilter==='pending'?'Chờ nhận vận đơn':'Vận đơn của tôi'}`;
      $('shipment-list').innerHTML = result.items.length ? `<table class="vn-table" aria-label="Danh sách vận đơn"><thead><tr><th scope="col">Vận đơn</th><th scope="col">Tuyến giao nhận</th><th scope="col">Khối lượng</th><th scope="col">Trạng thái</th><th scope="col">Thao tác</th></tr></thead><tbody>${result.items.map(ui.row).join('')}</tbody></table>` : `<p class="empty-note">${search||status!=='all'||direction!=='all'?'Không tìm thấy vận đơn phù hợp. Hãy thử đổi hoặc xóa bộ lọc.':requestedFilter==='pending'?'Không có vận đơn chờ nhận.':'Bạn chưa nhận vận đơn nào.'}</p>`;
      pager('shipment-pagination', result, n => safe(() => load(n)));
    } catch(error) {
      if(request!==listRequest) return;
      $('shipment-summary').textContent = 'Chưa tải được danh sách. Bấm Làm mới để thử lại.';
      $('shipment-list').innerHTML = '<p class="empty-note">Chưa thể kết nối để tải vận đơn.</p>';
      $('shipment-pagination').innerHTML = '';
      throw error;
    } finally { if (request === listRequest) $('shipment-list').setAttribute('aria-busy', 'false'); }
  }
  function actions(s) {
    const next=ui.next(s), disabled=busy.has(s.id)?'disabled':'';
    return `<section class="vn-next"><h3>${next.length?'Thao tác tiếp theo':'Vận đơn đã kết thúc'}</h3>${s.status==='DeliveryFailed'?`<p>Đã giao ${Number(s.deliveryAttempts||0)}/2 lần. ${s.deliveryAttempts>=2?'Đã hết lượt giao lại. Hãy chuyển hoàn về nơi gửi.':'Bạn có thể giao lại hoặc chuyển hoàn về nơi gửi.'}</p>`:''}${next.map(value=>`<div class="vn-action-row"><button ${disabled} class="${value==='DeliveryFailed'?'vn-fail':'primary'}" data-event="${value}" data-shipment="${s.id}">${esc(ui.actionLabels[value])}</button>${value!=='DeliveryFailed'?`<button ${disabled} class="vn-other" data-event="${value}" data-shipment="${s.id}" data-custom="true" aria-label="Nhập thông tin khác: ${esc(ui.actionLabels[value])}">Nhập thông tin khác</button>`:''}<small>Địa điểm: ${esc(ui.location(s,value))}</small></div>`).join('')}<p class="vn-save-state" role="status" aria-live="polite">${busy.has(s.id)?'Đang lưu cập nhật…':next.length?'Địa điểm được điền sẵn. Bạn có thể đổi nếu cần.':'Bạn vẫn có thể xem thông tin và hành trình bên dưới.'}</p></section>`;
  }
  function renderDetail(preserveScroll=false){
    const s=order.shipments.find(x=>x.id===active.shipmentId)||order.shipments[0];
    if(!s) { message('Không còn vận đơn thuộc quyền xử lý của bạn.'); return; }
    active.shipmentId=s.id;
    const events=(order.events||[]).filter(x=>x.shippingInfoId===s.id).sort((a,b)=>new Date(b.occurredAt)-new Date(a.occurredAt));
    const timeline=events.length?`<ol class="vn-history">${events.map(x=>`<li><strong>${esc(ui.label(x.status))}</strong><time>${esc(localDate(x.occurredAt))}</time>${x.location?`<p>${esc(x.location)}</p>`:''}${x.note?`<p>${esc(x.note)}</p>`:''}</li>`).join('')}</ol>`:'<p class="empty-note">Chưa có mốc vận chuyển.</p>';
    openDetail(`<div class="vn-detail">${order.shipments.length>1?`<div class="vn-shipment-select" role="group" aria-label="Chọn vận đơn của đơn hàng">${order.shipments.map(x=>`<button data-select-shipment="${x.id}" aria-pressed="${x.id===s.id}">${esc(x.trackingNumber||'Chưa có mã')} · ${esc(ui.direction(x))}</button>`).join('')}</div>`:''}<span class="eyebrow">Chi tiết vận đơn</span><h2>${esc(s.trackingNumber||'Chưa có mã')}</h2><div class="vn-detail-meta"><span>Đơn #${order.id}</span><span>· ${esc(ui.direction(s))}</span><span>· ${order.totalWeightKg==null?'Chưa có khối lượng':`${esc(Number(order.totalWeightKg))} kg`}</span><span class="vn-status vn-${ui.tone(s.status)}">${esc(ui.label(s.status))}</span><span>Đã giao ${Number(s.deliveryAttempts||0)}/2 lần</span></div>${actions(s)}<div class="vn-detail-tabs" role="tablist" aria-label="Thông tin vận đơn"><button id="vn-tab-info" role="tab" aria-selected="${detailTab==='info'}" aria-controls="vn-pane-info" tabindex="${detailTab==='info'?0:-1}" data-detail-tab="info">Thông tin giao nhận</button><button id="vn-tab-history" role="tab" aria-selected="${detailTab==='history'}" aria-controls="vn-pane-history" tabindex="${detailTab==='history'?0:-1}" data-detail-tab="history">Hành trình vận chuyển</button></div><section id="vn-pane-info" class="vn-pane" role="tabpanel" aria-labelledby="vn-tab-info" ${detailTab!=='info'?'hidden':''}>${ui.reverse(s)?'<p class="vn-info-note">Hàng đang chuyển hoàn từ điểm nhận về điểm gửi ban đầu.</p>':''}<div class="vn-address-grid"><div class="vn-address"><h3>Điểm gửi ban đầu</h3><p>${esc(s.pickupAddressSnapshot||'Chưa có địa chỉ')}</p></div><div class="vn-address"><h3>Điểm nhận ban đầu</h3><p>${esc(s.deliveryAddressSnapshot||'Chưa có địa chỉ')}</p></div></div><p class="vn-info-note">${s.direction==='Return'?'Hàng trả: lấy từ người mua, giao về người bán.':'Giao hàng: lấy từ người bán, giao tới người mua.'}</p></section><section id="vn-pane-history" class="vn-pane" role="tabpanel" aria-labelledby="vn-tab-history" ${detailTab!=='history'?'hidden':''}>${timeline}</section></div>`,preserveScroll);
  }
  async function detail(id,shipmentId,preserveScroll=false) {
    if(preserveScroll&&(!active||active.orderId!==id||!$('detail-dialog').open)) return;
    if(!preserveScroll){active={orderId:id,shipmentId};detailTab='info';}
    const request = ++detailRequest;
    const o = await api(`orders/${id}`);
    if (request !== detailRequest || !active || (preserveScroll&&!$('detail-dialog').open)) return;
    order=o;
    renderDetail(preserveScroll);
  }
  function selectTab(value,focus=false){
    detailTab=value;
    $('detail-content').querySelectorAll('[data-detail-tab]').forEach(b=>{const selected=b.dataset.detailTab===value;b.setAttribute('aria-selected',String(selected));b.tabIndex=selected?0:-1;if(selected&&focus)b.focus();});
    $('vn-pane-info').hidden=value!=='info';$('vn-pane-history').hidden=value!=='history';
  }
  $('detail-dialog').addEventListener('close',()=>{active=null;order=null;detailRequest++;});
  $('detail-content').addEventListener('keydown',e=>{
    if(!e.target.closest('[data-detail-tab]')||!['ArrowLeft','ArrowRight','Home','End'].includes(e.key)) return;
    e.preventDefault(); selectTab(e.key==='Home'?'info':e.key==='End'?'history':detailTab==='info'?'history':'info',true);
  });
  async function update(button){
    const id=Number(button.dataset.shipment),s=order?.shipments.find(x=>x.id===id);
    if(!s||busy.has(id)||!ui.next(s).includes(button.dataset.event)) return;
    const orderId=order.id, event=button.dataset.event;
    busy.add(id);
    $('detail-content').querySelectorAll(`[data-event][data-shipment="${id}"]`).forEach(b=>b.disabled=true);
    const saving=$('detail-content').querySelector('.vn-save-state');
    try{
      let input={location:ui.location(s,event),note:null};
      if(event==='DeliveryFailed'||button.dataset.custom==='true'){
        const failed=event==='DeliveryFailed';
        const fields=failed?[{name:'reason',label:'Lý do giao thất bại',type:'select',required:true,options:[{value:'',label:'Chọn lý do'},{value:'Không liên hệ được người nhận',label:'Không liên hệ được người nhận'},{value:'Người nhận hẹn giao lại',label:'Người nhận hẹn giao lại'},{value:'Người nhận từ chối nhận hàng',label:'Người nhận từ chối nhận hàng'},{value:'other',label:'Lý do khác'}]}]:[];
        fields.push({name:'location',label:'Địa điểm',required:true,validate:v=>v.trim()?'':'Vui lòng nhập địa điểm.'},{name:'note',type:'textarea',label:failed?'Ghi chú / lý do khác':'Ghi chú (không bắt buộc)',maxLength:1000,validate:v=>failed&&$('action-reason').value==='other'&&!v.trim()?'Vui lòng nhập lý do khác.':''});
        const answer=modal({title:ui.actionLabels[event],description:`${s.trackingNumber} · Đơn #${orderId}`,fields,confirm:'Lưu cập nhật'});
        $('action-location').value=input.location;
        if(failed) $('action-reason').onchange=()=>{$('action-note').setCustomValidity('');};
        input=await answer;
        if(!input) return;
        input.note=failed?(input.reason==='other'?input.note.trim():input.reason+(input.note.trim()?` · ${input.note.trim()}`:'')):input.note.trim()||null;
      }
      if(saving) saving.textContent='Đang lưu cập nhật…';
      const result=await api(`shipments/${id}/events`,'POST',{status:event,eventId:crypto.randomUUID(),location:input.location.trim(),note:input.note});
      message(result.applied?`Đã cập nhật: ${ui.label(event)}.`:'Trạng thái đã thay đổi. Vận đơn đã được làm mới.',result.applied);
      await Promise.all([safe(()=>load()),safe(()=>detail(orderId,id,true))]);
    }finally{
      busy.delete(id);
      $('detail-content').querySelectorAll(`[data-event][data-shipment="${id}"]`).forEach(b=>b.disabled=false);
      const state=$('detail-content').querySelector('.vn-save-state');if(state&&active?.shipmentId===id&&ui.next(order?.shipments.find(x=>x.id===id)||{}).length)state.textContent='Địa điểm được điền sẵn. Bạn có thể đổi nếu cần.';
    }
  }
  document.body.addEventListener('click', e => {
    const tab=e.target.closest('[data-detail-tab]'); if(tab) return selectTab(tab.dataset.detailTab);
    const selected=e.target.closest('[data-select-shipment]');if(selected&&active){active.shipmentId=Number(selected.dataset.selectShipment);renderDetail(true);return;}
    const open = e.target.closest('[data-open-order]');
    if (open) return safe(() => detail(Number(open.dataset.openOrder),Number(open.dataset.shipment)));
    const claim = e.target.closest('[data-claim-shipment]');
    if (claim) return safe(async () => {
      const shipmentId=Number(claim.dataset.claimShipment);
      if(busy.has(shipmentId))return;
      busy.add(shipmentId);claim.disabled=true;
      try {
        const input = await modal({title:'Nhận vận đơn',description:`Bạn sẽ phụ trách vận đơn của đơn #${claim.dataset.order}.`,confirm:'Nhận vận đơn'});
        if (!input) return;
        try {
          await api(`shipper/shipments/${claim.dataset.claimShipment}/claim`,'POST');
          filter = 'mine';
          message('Đã nhận vận đơn. Mở chi tiết để cập nhật giao nhận.',true);
        } finally { await load(1); }
      } finally {busy.delete(shipmentId);claim.disabled=false;}
    });
    const b = e.target.closest('[data-event]');
    if (!b) return;
    safe(()=>update(b));
  });
  document.querySelectorAll('[data-shipment-filter]').forEach(b => b.onclick = () => { filter = b.dataset.shipmentFilter; safe(() => load(1)); });
  $('refresh-shipments').onclick = () => safe(() => load());
  $('shipment-search-form').onsubmit=e=>{e.preventDefault();search=$('shipment-search').value.trim();safe(()=>load(1));};
  $('shipment-status').onchange=()=>{status=$('shipment-status').value;safe(()=>load(1));};
  $('shipment-direction').onchange=()=>{direction=$('shipment-direction').value;safe(()=>load(1));};
  $('shipment-reset').onclick=()=>{search='';status=direction='all';$('shipment-search').value='';$('shipment-status').value=$('shipment-direction').value='all';safe(()=>load(1));};
  safe(load);
})();
