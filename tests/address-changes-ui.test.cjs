const test=require('node:test');
const assert=require('node:assert/strict');
const fs=require('node:fs');
const vm=require('node:vm');

function helpers(){
  const elements=new Map(),listeners=[],calls=[],messages=[];
  const element=id=>{if(!elements.has(id))elements.set(id,{value:'',disabled:false});return elements.get(id);};
  const G4={$:element,esc:value=>String(value??'').replace(/[<>&"]/g,x=>({'<':'&lt;','>':'&gt;','&':'&amp;','"':'&quot;'}[x])),
    money:value=>'$'+Number(value||0).toFixed(2),date:value=>value,
    safe:fn=>fn(),message:(...args)=>messages.push(args),modal:async()=>({reason:'Rejected reason'}),api:async(route,verb,body)=>{calls.push({route,body});return{status:'Approved'};}};
  const context={G4,crypto:{randomUUID:()=> 'key'},document:{body:{addEventListener:(type,fn)=>listeners.push(fn)},querySelectorAll:()=>[]}};
  vm.runInNewContext(fs.readFileSync('frontend/wwwroot/js/address-changes.js','utf8'),context);
  return{...G4.AddressChanges,G4,listeners,calls,messages};
}
const row={id:2,orderId:1,status:'Pending',oldAddress:'Old address',newAddress:'<script>New</script>',reason:'Move',oldShippingCost:2.1,newShippingCost:5.2,sellerCap:4,buyerRefund:0,createdAt:'2026-10-13T09:00:00'};

test('buyer sees request button only when server says change is allowed',()=>{
  const h=helpers();assert.match(h.summaryHtml({id:1,canRequestAddressChange:true},'buyer'),/Yêu cầu đổi địa chỉ/);
  assert.equal(h.summaryHtml({id:1,canRequestAddressChange:false},'buyer'),'');
  assert.doesNotMatch(h.summaryHtml({id:1,canRequestAddressChange:true},'seller'),/data-address-change="request"/);
});
test('history escapes addresses and reasons; seller sees pending decision and its cost',()=>{
  const html=helpers().summaryHtml({id:1,addressChanges:[row]},'seller');
  assert.match(html,/&lt;script&gt;/);assert.doesNotMatch(html,/<script>/);
  assert.match(html,/\$3.10/);assert.match(html,/data-address-change="approve"/);assert.match(html,/data-address-change="reject"/);
  assert.doesNotMatch(helpers().summaryHtml({id:1,addressChanges:[{...row,status:'Approved'}]},'seller'),/data-address-change="approve"/);
});
test('refund presentation distinguishes proposed refund and completed simulated refund',()=>{
  const h=helpers();
  assert.match(h.summaryHtml({id:1,addressChanges:[{...row,buyerRefund:3.1}]},'buyer'),/dự kiến khi duyệt/);
  assert.match(h.summaryHtml({id:1,addressChanges:[{...row,buyerRefund:3.1,status:'Approved'}]},'buyer'),/Đã hoàn.*giả lập/);
});
test('shipper must acknowledge approved changed address before tracking',()=>{
  const h=helpers(),shipment={id:5,addressChangeRequestId:2,addressChangeAcknowledgedAt:null};
  assert.equal(h.requiresAck(shipment),true);
  assert.match(h.shipperNotice(shipment,{addressChanges:[row]}),/data-ack-address="5"/);
  assert.equal(h.requiresAck({...shipment,addressChangeAcknowledgedAt:'2026-10-13'}),false);
  assert.match(h.shipperNotice({...shipment,addressChangeAcknowledgedAt:'2026-10-13'},{addressChanges:[row]}),/Đã xác nhận/);
});
test('seller decision uses request ID and refreshes the correct order',async()=>{
  const h=helpers(),refreshed=[];h.init('seller',id=>refreshed.push(id));
  const button={dataset:{addressOrder:'8',addressRequest:'12',addressChange:'approve'}};
  await h.listeners[0]({target:{closest:()=>button}});
  assert.equal(h.calls[0].route,'seller/address-changes/12/decision');assert.equal(h.calls[0].body.approve,true);
  assert.deepEqual(refreshed,[8]);
});
