(() => {
  const {$,money,date,esc,label,api,message,safe,returnStatus,trackingHtml,productThumbnail,orderCard,pager,patchPagedList,modal,openDetail}=G4;
  const state={cart:[],addresses:[],orders:[],page:1,quote:null,checkoutKey:crypto.randomUUID(),orderId:null};
  let detailOrderId=null, detailRequest=0;
  let quoteRequest=0, paying=false, orderPaid=false;
  let orderFilter='all', orderRequest=0, visibleOrders=new Map();
  function tab(id,loadList=true){document.querySelectorAll('.tab-panel').forEach(x=>x.classList.toggle('active',x.id===id));document.querySelectorAll('.tabs button').forEach(x=>x.classList.toggle('active',x.dataset.tab===id));if(id==='orders'&&loadList)safe(loadOrders);if(id==='disputes')safe(()=>G4.Disputes.load());}
  function request(checkout=false){return{addressId:Number($('address').value),items:state.cart.map(x=>({productId:x.productId,quantity:x.quantity})),couponCode:$('coupon').value.trim()||null,checkoutKey:state.checkoutKey,...(checkout?{expectedTotal:state.quote.total,pricingFingerprint:state.quote.pricingFingerprint}:{})};}
  function itemHtml(x,editable){const quantity=editable?`<label>SL <select data-quantity="${x.productId}">${Array.from({length:Math.min(x.available,5)},(_,i)=>`<option value="${i+1}" ${x.quantity===i+1?'selected':''}>${i+1}</option>`).join('')}</select></label>`:`<span>SL ${x.quantity}</span>`;const line=state.quote?.lines?.find(l=>l.productId===x.productId);return `<div class="item">${productThumbnail(x.imageUrl,x.title)}<div class="item-info"><strong>${esc(x.title)}</strong><small>Còn ${x.available??'—'} · ${x.weightKg == null ? 'Chưa có khối lượng' : `${Number(x.weightKg)} kg / sản phẩm`}</small></div>${quantity}${G4.Promotions.linePriceHtml(line,x.price*x.quantity,state.quote?.promotions)}</div>`;}
  function renderCart(){$('cart').classList.toggle('empty',!state.cart.length);$('cart').innerHTML=state.cart.length?state.cart.map(x=>itemHtml(x,true)).join(''):'Chọn số sản phẩm rồi tạo giỏ.';$('checkout-items').innerHTML=state.cart.map(x=>itemHtml(x,false)).join('');$('cart-subtotal').textContent=state.quote?'Tiền hàng sau giảm: '+money(state.quote.subtotal-state.quote.discount):'Tạm tính: '+money(state.cart.reduce((n,x)=>n+x.price*x.quantity,0));$('continue').disabled=!state.cart.length;$('pay').disabled=!state.cart.length||!state.quote;document.querySelectorAll('[data-quantity]').forEach(s=>s.onchange=()=>{const x=state.cart.find(i=>i.productId===Number(s.dataset.quantity));if(x)x.quantity=Number(s.value);resetOrder();renderCart();safe(recalculate);});}
  function resetOrder(){quoteRequest++;state.orderId=null;orderPaid=false;state.checkoutKey=crypto.randomUUID();state.quote=null;$('pay').disabled=true;$('checkout-promotions').innerHTML='';$('checkout-promotion-note').hidden=true;}
  async function randomCart(){state.cart=await api('catalog/random?count='+$('count').value);resetOrder();renderCart();await recalculate();message(`Đã tạo ${state.cart.length} sản phẩm.`,true);}
  async function recalculate(){if(!state.cart.length||!$('address').value)return;const token=++quoteRequest;state.quote=null;$('pay').disabled=true;$('checkout-promotions').innerHTML='';$('checkout-promotion-note').hidden=true;for(const key of ['subtotal','discount','shipping','total'])$(key).textContent='Đang tính…';renderCart();try{const quote=await api('quote','POST',request());if(token!==quoteRequest)return;state.quote=quote;$('subtotal').textContent=money(quote.subtotal);$('discount').textContent='-'+money(quote.discount);$('shipping').textContent=money(quote.shipping);$('checkout-weight').textContent=quote.totalWeightKg+' kg';$('total').textContent=money(quote.total);$('checkout-promotions').innerHTML=G4.Promotions.breakdownHtml(quote);const coupon=quote.promotions?.some(p=>p.type==='Coupon');$('checkout-promotion-note').hidden=!coupon;$('checkout-promotion-note').textContent='Mã giảm giá có thể kết hợp giảm giá sản phẩm và phí vận chuyển. Giảm theo số lượng hoặc theo đơn được thay thế khi dùng mã.';renderCart();}catch(error){if(token===quoteRequest){for(const key of ['subtotal','discount','shipping','total'])$(key).textContent='Chưa có báo giá';throw error;}}}
  async function ensureOrder(){if(state.orderId)return state.orderId;if(!state.quote)throw new Error('Vui lòng cập nhật báo giá trước khi thanh toán.');try{const o=await api('orders','POST',request(true));state.orderId=o.id;orderPaid=o.status==='Paid';return o.id;}catch(error){state.quote=null;quoteRequest++;$('pay').disabled=true;renderCart();$('checkout-promotions').innerHTML='';$('total').textContent='Cần cập nhật báo giá';throw new Error(error.message+' Vui lòng bấm Cập nhật báo giá và kiểm tra tổng tiền trước khi thanh toán lại.');}}
  function freezePriceInputs(){
    const controls=[...['random','count','continue','coupon','address','apply-coupon','refresh-quote','edit-address','save-address'].map($),...document.querySelectorAll('[data-quantity],#address-fields input')];
    const previous=controls.map(control=>[control,control.disabled]);
    controls.forEach(control=>control.disabled=true);
    return ()=>previous.forEach(([control,disabled])=>control.disabled=disabled);
  }
  async function pay(){if(paying)return;paying=true;const unfreeze=freezePriceInputs();$('pay').disabled=true;const method=document.querySelector('input[name="method"]:checked').value;if(method==='paypal')$('paypal-loading').classList.remove('hidden');try{const id=await ensureOrder();let result;if(orderPaid){result={status:'Succeeded'};}else if(method==='paypal'){result=await api(`orders/${id}/pay/paypal`,'POST',{key:crypto.randomUUID()});if(result.approvalUrl){location.assign(result.approvalUrl);return;}}else{result=await api(`orders/${id}/pay/card`,'POST',{number:$('card-number').value,expiry:$('card-expiry').value,key:crypto.randomUUID()});}message(result?.status==='Succeeded'?(Number(state.quote?.total)===0?'Đơn đã được thanh toán bằng khuyến mãi.':'Thanh toán thành công. Bạn có thể theo dõi đơn hàng bên dưới.'):'Thanh toán chưa được chấp nhận. Vui lòng kiểm tra và thử lại.',result?.status==='Succeeded');tab('orders',false);await loadOrders(1);await showOrder(id);}finally{paying=false;unfreeze();$('pay').disabled=!state.quote;$('paypal-loading').classList.add('hidden');}}
  async function loadOrders(page=state.page,changedId=null){
    const request=++orderRequest, filter=orderFilter, list=$('order-list');
    list.setAttribute('aria-busy','true');
    $('buyer-order-summary').textContent='Đang tải đơn hàng…';
    try{
      const result=await api(`orders/page?page=${page}&pageSize=10&filter=${encodeURIComponent(filter)}`);
      if(request!==orderRequest)return;
      state.page=result.page;state.orders=result.items;
      const empty=`<p class="empty-note">${filter==='all'?'Chưa có đơn hàng.':'Không có đơn hàng phù hợp với bộ lọc này.'}</p>`;
      if(changedId===null)list.innerHTML=result.items.length?result.items.map(o=>orderCard(o,'buyer')).join(''):empty;
      else{
        patchPagedList('order-list',result.items,o=>o.id,o=>orderCard(o,'buyer'),null,'data-order-row',empty);
        for(const o of result.items){
          if(visibleOrders.has(o.id)&&visibleOrders.get(o.id)!==JSON.stringify(o)){
            const row=list.querySelector(`[data-order-row="${o.id}"]`), focused=row?.contains(document.activeElement);
            if(row)row.outerHTML=orderCard(o,'buyer');
            if(focused)list.querySelector(`[data-order-row="${o.id}"] [data-open-order]`)?.focus({preventScroll:true});
          }
        }
      }
      visibleOrders=new Map(result.items.map(o=>[o.id,JSON.stringify(o)]));
      document.querySelectorAll('[data-order-filter]').forEach(b=>{
        const active=b.dataset.orderFilter===filter;
        b.classList.toggle('active',active);b.setAttribute('aria-pressed',String(active));
        b.querySelector('[data-filter-count]').textContent=result.counts?.[b.dataset.orderFilter]??0;
      });
      $('buyer-order-summary').textContent=`${result.totalCount} đơn`;
      pager('order-pagination',result,p=>safe(()=>loadOrders(p)));
    }catch(error){
      if(request===orderRequest){$('buyer-order-summary').textContent='Chưa tải được danh sách. Vui lòng bấm Làm mới.';throw error;}
    }finally{if(request===orderRequest)list.setAttribute('aria-busy','false');}
  }
  function actions(o) {
    const buttons = [];
    if (o.status === 'AwaitingPayment') buttons.push(`<button data-action="cancel" data-id="${o.id}">Hủy đơn</button><button data-action="retry-card" data-id="${o.id}">Thanh toán bằng thẻ thử</button>`);
    if (['Paid', 'Preparing'].includes(o.status)) buttons.push(`<button data-action="cancel" data-id="${o.id}">Yêu cầu hủy</button>`);
    if (o.status === 'Delivered' && !o.returnRequest) buttons.push(`<button data-action="return" data-id="${o.id}">Yêu cầu trả hàng</button>`);
    if (o.status === 'Delivered' && !o.dispute?.isOpen && (!o.returnRequest||['Rejected','Closed'].includes(o.returnRequest.status)) && !o.refunds.some(r => r.status === 'Succeeded')) buttons.push(`<button data-action="hold" data-id="${o.id}">Mở yêu cầu giải quyết</button>`);
    return buttons.join('') || '<span class="small">Không có thao tác.</span>';
  }
  async function showOrder(id,preserveScroll=false){
    const request=++detailRequest; detailOrderId=id;
    const o=await api(`orders/${id}`);
    if(request!==detailRequest||preserveScroll&&!$('detail-dialog')?.open)return;
    const pay=o.payments.at(-1)?.status??'Chưa thanh toán';
    const status=`<div class="status-grid"><div class="status-box"><span>Đơn hàng</span><strong>${esc(label(o.status))}</strong></div><div class="status-box"><span>Thanh toán</span><strong>${esc(label(pay))}</strong></div><div class="status-box"><span>Trả hàng</span><strong>${esc(returnStatus(o.returnRequest?.status))}</strong></div></div>`;
    const lines=o.items.map(x=>`<div class="item">${productThumbnail(x.imageUrl,x.productTitleSnapshot)}<div class="item-info"><strong>${esc(x.productTitleSnapshot)}</strong><small>Số lượng: ${x.quantity}${x.unitWeightKgSnapshot == null ? '' : ' · '+Number(x.unitWeightKgSnapshot)+' kg/sản phẩm'}</small></div>${G4.Promotions.linePriceHtml(x.originalTotal==null?null:x,x.unitPrice*x.quantity,o.promotions)}</div>`).join('');
    openDetail(`<div class="detail" data-order-detail="${o.id}"><h2>Đơn mua hàng #${o.id}</h2><p class="small">Tạo: ${date(o.orderDate)} · Cập nhật: ${date(o.updatedAt || o.orderDate)}</p><p class="small">${esc(o.addressSnapshot)} · Khối lượng: ${o.totalWeightKg == null ? 'Chưa có dữ liệu' : Number(o.totalWeightKg)+' kg'}</p>${status}${G4.disputeBadge(o.dispute)}<h3>Sản phẩm</h3>${lines}${G4.Promotions.breakdownHtml(o)}<div class="promotion-order-totals"><p>Phí vận chuyển: <strong>${money(o.shippingFee??o.shipping)}</strong></p><p>Tổng thanh toán: <strong>${money(o.totalPrice)}</strong></p></div><h3>Tiến trình vận chuyển</h3>${trackingHtml(o)}<h3>Thao tác</h3><div class="actions">${actions(o)}</div>${G4.returnSummary(o)}${o.refunds.length?`<div class="refund-summary">${o.refunds.map(r=>`<p>Hoàn ${money(r.amount)} · ${esc(label(r.status))}</p>`).join('')}</div>`:''}</div>`,preserveScroll);
  }
  async function action(b){const id=Number(b.dataset.id),kind=b.dataset.action;if(kind==='hold')return G4.Disputes.openNew(id);const titles={cancel:'Xác nhận hủy đơn',return:'Yêu cầu trả hàng',hold:'Mở tranh chấp','retry-card':'Thanh toán bằng thẻ thử'};const input=await modal({title:titles[kind],description:`Đơn mua hàng #${id}. Vui lòng kiểm tra trước khi xác nhận.`,fields:['return','hold'].includes(kind)?[{name:'reason',label:kind==='return'?'Lý do trả hàng':'Lý do tranh chấp',required:true}]:[],confirm:'Xác nhận'});if(!input)return;if(kind==='retry-card'){await api(`orders/${id}/pay/card`,'POST',{number:'4111111111111111',expiry:'12/30',key:crypto.randomUUID()});}else if(kind==='cancel')await api(`orders/${id}/cancel`,'POST');else if(kind==='return')await api(`orders/${id}/returns`,'POST',{reason:input.reason});message('Đã lưu thay đổi cho đơn hàng.',true);await loadOrders(state.page,id);await showOrder(id);}
  document.querySelectorAll('[data-order-filter]').forEach(b=>b.onclick=()=>{orderFilter=b.dataset.orderFilter;safe(()=>loadOrders(1));});
  document.querySelectorAll('.tabs button').forEach(b=>b.onclick=()=>tab(b.dataset.tab));$('random').onclick=()=>safe(randomCart);$('continue').onclick=()=>{tab('checkout');safe(recalculate);};$('address').onchange=()=>{resetOrder();safe(recalculate);};$('apply-coupon').onclick=()=>{resetOrder();safe(recalculate);};$('pay').onclick=()=>safe(pay);$('refresh-orders').onclick=()=>safe(()=>loadOrders(state.page));document.querySelectorAll('input[name="method"]').forEach(r=>r.onchange=()=>$('card-fields').classList.toggle('hidden',document.querySelector('input[name="method"]:checked').value!=='card'));
  $('edit-address').onclick=()=>{const a=state.addresses.find(x=>x.id===Number($('address').value));if(!a)return;['name','street','city','state','country'].forEach(k=>$('address-'+k).value=k==='name'?a.fullName:a[k]||'');$('address-fields').classList.toggle('hidden');};$('save-address').onclick=()=>safe(async()=>{const id=Number($('address').value);const a=await api(`addresses/${id}/edit`,'POST',{fullName:$('address-name').value,street:$('address-street').value,city:$('address-city').value,state:$('address-state').value,country:$('address-country').value});state.addresses=state.addresses.map(x=>x.id===id?a:x);$('address-fields').classList.add('hidden');resetOrder();await recalculate();message('Đã lưu địa chỉ.',true);});document.body.addEventListener('click',e=>{const open=e.target.closest('[data-open-order]');if(open)return safe(()=>showOrder(Number(open.dataset.openOrder)));const b=e.target.closest('[data-action]');if(b)return safe(()=>action(b));});
  G4.Disputes.init('buyer', async id => {
    await loadOrders(state.page,id);
  });
  $('refresh-quote').onclick=()=>safe(()=>{resetOrder();return recalculate();});
  $('coupon').oninput=()=>{resetOrder();renderCart();$('total').textContent='Áp dụng mã để cập nhật báo giá';};
  setInterval(()=>{if(document.visibilityState==='visible'&&$('detail-dialog')?.open&&document.querySelector('#detail-content [data-order-detail]')?.dataset.orderDetail===String(detailOrderId))safe(()=>showOrder(detailOrderId,true));},5000);
  safe(async()=>{state.addresses=await api('addresses');$('address').innerHTML=state.addresses.map(a=>`<option value="${a.id}" ${a.isDefault?'selected':''}>${esc(a.fullName)} — ${esc(a.street)}, ${esc(a.city)}</option>`).join('');const p=new URLSearchParams(location.search);if(p.has('orderId')){tab('orders');await showOrder(Number(p.get('orderId')));message(p.get('paymentResult')==='checked'?'Đã kiểm tra thanh toán PayPal. Xem trạng thái đơn hàng bên dưới.':p.get('paymentResult')==='cancelled'?'Bạn đã hủy thanh toán PayPal. Đơn hàng vẫn đang chờ thanh toán.':'Chưa xác nhận được thanh toán PayPal. Vui lòng kiểm tra lại đơn hàng.',p.get('paymentResult')==='checked');}});
})();
