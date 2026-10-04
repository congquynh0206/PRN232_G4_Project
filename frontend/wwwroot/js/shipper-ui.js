(() => {
  const {esc} = G4;
  const labels={LabelCreated:'Chờ lấy hàng',PickedUp:'Đã lấy hàng',InTransit:'Đang vận chuyển',OutForDelivery:'Đang giao hàng',Delivered:'Giao thành công',DeliveryFailed:'Giao thất bại',ReturningToSender:'Đang chuyển hoàn',ReturnedToSeller:'Đã hoàn về nơi gửi'};
  const actionLabels={PickedUp:'Đã lấy hàng',InTransit:'Chuyển đến trung tâm',OutForDelivery:'Bắt đầu giao hàng',Delivered:'Giao thành công',DeliveryFailed:'Giao thất bại',ReturningToSender:'Chuyển hoàn về nơi gửi',ReturnedToSeller:'Đã hoàn về nơi gửi'};
  const next=s=>({LabelCreated:['PickedUp'],PickedUp:['InTransit'],InTransit:['OutForDelivery'],OutForDelivery:['Delivered','DeliveryFailed'],DeliveryFailed:['OutForDelivery','ReturningToSender'],ReturningToSender:['ReturnedToSeller']}[s.status]||[]).filter(x=>x!=='OutForDelivery'||Number(s.deliveryAttempts||0)<2);
  const reverse=s=>['ReturningToSender','ReturnedToSeller'].includes(s.status);
  const direction=s=>reverse(s)?'Chuyển hoàn'+(s.direction==='Return'?' · hàng trả':''):s.direction==='Return'?'Trả hàng về người bán':'Giao hàng cho người mua';
  const label=status=>labels[status]||'Chưa có trạng thái';
  const tone=status=>['Delivered','ReturnedToSeller'].includes(status)?'success':status==='DeliveryFailed'?'danger':['ReturningToSender','OutForDelivery'].includes(status)?'warning':'info';
  function location(s,status){
    if(status==='PickedUp'||status==='ReturnedToSeller') return s.pickupAddressSnapshot||s.pickupAddress||'Điểm gửi VNPost — giả lập';
    if(status==='InTransit') return 'Trung tâm khai thác VNPost — giả lập';
    return s.deliveryAddressSnapshot||s.deliveryAddress||'Điểm nhận VNPost — giả lập';
  }
  function row(s){
    const action=s.shipperId==null?`<button class="primary" data-claim-shipment="${s.id}" data-order="${s.orderId}">Nhận vận đơn</button>`:`<button data-open-order="${s.orderId}" data-shipment="${s.id}">Xem / cập nhật</button>`;
    const pickup=reverse(s)?s.deliveryAddress:s.pickupAddress,delivery=reverse(s)?s.pickupAddress:s.deliveryAddress;
    return `<tr data-shipment-row="${s.id}"><td data-label="Vận đơn"><strong class="vn-tracking">${esc(s.trackingNumber||'Chưa có mã')}</strong><small>Đơn #${s.orderId}</small><span class="vn-direction">${esc(direction(s))}</span></td><td data-label="Tuyến giao nhận"><div class="vn-route"><span><b>Lấy</b>${esc(pickup||'Chưa có địa chỉ')}</span><span><b>Giao</b>${esc(delivery||'Chưa có địa chỉ')}</span></div></td><td data-label="Khối lượng">${s.totalWeightKg==null?'Chưa có':`${esc(Number(s.totalWeightKg))} kg`}</td><td data-label="Trạng thái"><span class="vn-status vn-${tone(s.status)}">${esc(label(s.status))}</span>${s.deliveryAttempts?`<small>Đã giao ${Number(s.deliveryAttempts)}/2 lần</small>`:''}</td><td data-label="Thao tác">${action}</td></tr>`;
  }
  G4.ShipperUI={labels,actionLabels,next,reverse,direction,label,tone,location,row};
})();
