const {test}=require('node:test');
const assert=require('node:assert/strict');
const fs=require('node:fs');
const vm=require('node:vm');
const context={G4:{esc:v=>String(v??'').replace(/[&<>"']/g,c=>({'&':'&amp;','<':'&lt;','>':'&gt;','"':'&quot;',"'":'&#39;'}[c]))}};
vm.runInNewContext(fs.readFileSync('frontend/wwwroot/js/shipper-ui.js','utf8'),context);
const ui=context.G4.ShipperUI;
test('locations follow the shipment snapshots even for return and reversal',()=>{
 for(const direction of ['Outbound','Return']){
  const s={direction,pickupAddressSnapshot:'Nơi gửi',deliveryAddressSnapshot:'Nơi nhận'};
  assert.equal(ui.location(s,'PickedUp'),'Nơi gửi');
  assert.equal(ui.location(s,'Delivered'),'Nơi nhận');
  assert.equal(ui.location(s,'ReturnedToSeller'),'Nơi gửi');
  assert.match(ui.location(s,'InTransit'),/giả lập/);
 }
 assert.match(ui.location({},'PickedUp'),/giả lập/);
});
test('next actions respect attempt limit and terminal states',()=>{
 assert.deepEqual(Array.from(ui.next({status:'DeliveryFailed',deliveryAttempts:2})),['ReturningToSender']);
 assert.deepEqual(Array.from(ui.next({status:'DeliveryFailed',deliveryAttempts:1})),['OutForDelivery','ReturningToSender']);
 for(const status of ['Delivered','ReturnedToSeller']) assert.equal(ui.next({status}).length,0);
});
test('return and reversal labels name the correct task',()=>{
 assert.match(ui.direction({direction:'Return',status:'InTransit'}),/Trả hàng/);
 assert.match(ui.direction({direction:'Return',status:'ReturningToSender'}),/Chuyển hoàn/);
 assert.match(ui.label('ReturnedToSeller'),/nơi gửi/);
});
test('reversal route swaps original pickup and delivery for both directions',()=>{
 for(const direction of ['Outbound','Return']){
  const html=ui.row({id:1,orderId:2,shipperId:3,direction,status:'ReturningToSender',pickupAddress:'Original sender',deliveryAddress:'Original recipient'});
  assert.match(html,/<b>Lấy<\/b>Original recipient/);
  assert.match(html,/<b>Giao<\/b>Original sender/);
 }
});
test('list escapes external values and exposes only the applicable action',()=>{
 const base={id:12,orderId:4,trackingNumber:'<script>',pickupAddress:'<img>',deliveryAddress:'Hà Nội',status:'LabelCreated',direction:'Outbound',totalWeightKg:1.2};
 const pending=ui.row(base); assert.match(pending,/&lt;script&gt;/); assert.doesNotMatch(pending,/<img>/);
 assert.match(pending,/data-claim-shipment="12"/); assert.doesNotMatch(pending,/data-open-order/);
 const mine=ui.row({...base,shipperId:3}); assert.match(mine,/data-open-order="4"/); assert.match(mine,/data-shipment="12"/); assert.doesNotMatch(mine,/data-claim-shipment/);
});
