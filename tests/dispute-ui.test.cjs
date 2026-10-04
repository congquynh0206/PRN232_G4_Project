const test = require('node:test');
const assert = require('node:assert/strict');
const fs = require('node:fs');
const vm = require('node:vm');

function helpers(g4 = {}, document = {}) {
  const context = { G4: {
    esc: value => String(value ?? '').replace(/[&<>"']/g, c => ({'&':'&amp;','<':'&lt;','>':'&gt;','"':'&quot;',"'":'&#39;'}[c])),
    money: value => '$'+Number(value || 0).toFixed(2), date: value => value || 'Chưa có', label: value => value || 'Chưa có',
    productThumbnail: () => '<div class="product-thumbnail"></div>', trackingHtml: () => '<div class="tracking-list"></div>', ...g4
  }, URL, Date, window: {}, document };
  vm.runInNewContext(fs.readFileSync('frontend/wwwroot/js/disputes.js', 'utf8'), context);
  return context.G4.Disputes;
}

test('trạng thái và kết quả tranh chấp dùng tiếng Việt', () => {
  const d = helpers();
  assert.equal(d.statusText('AwaitingSeller'), 'Chờ người bán phản hồi');
  assert.equal(d.statusText('AwaitingBuyer'), 'Chờ người mua trả lời');
  assert.equal(d.statusText('Closed', 'BuyerSilent'), 'Đã đóng do người mua không phản hồi');
});

function detailFixture(status='Closed', outcome='BuyerWins') {
  return { data: { case: {id:1,orderId:43,buyerId:1,sellerId:2,status,outcome,isOpen:status!=='Closed',
    totalPrice:218.8,heldAmount:0,description:'Tai nghe bị lỗi',proposal:'Refund',resolution:'Đã xem video',
    responseDueAt:new Date(Date.now()+45000).toISOString()}, buyerName:'Nguyễn A',sellerName:'Shop <Camera>',entries:[] },
    order: {id:43,totalPrice:218.8,status:'Delivered',payments:[{status:'Succeeded',method:'PayPal',amount:218.8}],
      refunds:[],items:[{productTitleSnapshot:'Tai nghe',quantity:1,unitPrice:218.8}],shipments:[],events:[],addressSnapshot:'Hà Nội'} };
}

test('chi tiết chia thành ba mục, không coi tiền giữ bằng 0 là đã hoàn tiền', () => {
  const {data,order}=detailFixture();
  const html=helpers().renderDetail(data,order,'buyer');
  assert.match(html,/Tóm tắt/);assert.match(html,/Trao đổi/);assert.match(html,/Đơn hàng/);
  assert.match(html,/Chưa có xác nhận hoàn tiền/);
  assert.doesNotMatch(html,/Đã hoàn \$218.80/);
  assert.match(html,/Shop &lt;Camera&gt;/);
  assert.doesNotMatch(html,/Người bán #2|evidence-warning/);
});

test('đã hoàn tiền lấy số tiền và phương thức từ giao dịch thành công, tách thanh toán ban đầu', () => {
  const {data,order}=detailFixture();
  order.refunds=[{status:'Failed',amount:999},{status:'Succeeded',amount:218.8,completedAt:'2026-10-03T06:50:00Z'}];
  const buyer=helpers().renderDetail(data,order,'buyer');
  assert.match(buyer,/Đã hoàn \$218.80 cho bạn/);assert.match(buyer,/PayPal/);
  assert.match(buyer,/Thanh toán ban đầu/);assert.doesNotMatch(buyer,/\$999.00/);
  const seller=helpers().renderDetail(data,order,'seller');
  assert.match(seller,/Đã hoàn \$218.80 cho người mua/);
});

test('phương án trả hàng không hứa hoàn ngay; giữ thao tác phản hồi và bằng chứng', () => {
  const {data,order}=detailFixture('AwaitingBuyer',null);data.case.proposal='ReturnRefund';
  const buyer=helpers().renderDetail(data,order,'buyer');
  assert.match(buyer,/trả hàng/i);assert.match(buyer,/data-case-action="accept"/);
  assert.match(buyer,/data-case-action="reject"/);assert.match(buyer,/data-case-action="evidence"/);
  const seller=helpers().renderDetail(data,order,'seller');
  assert.doesNotMatch(seller,/data-case-action="accept"/);
  assert.match(seller,/Chờ người mua phản hồi/);
});

test('hồ sơ cũ không biến mô tả chuyển dữ liệu thành lý do khiếu nại, escape nội dung trao đổi', () => {
  const {data,order}=detailFixture();
  data.case.description='Khoản giữ tiền từ trước khi nâng cấp quy trình tranh chấp.';
  data.entries=[{id:1,kind:'Imported',actorRole:'system',description:data.case.description,evidenceLinks:[]},
    {id:2,kind:'Decision',actorRole:'admin',description:'<script>bad()</script>',evidenceLinks:[]}];
  const html=helpers().renderDetail(data,order,'buyer');
  assert.doesNotMatch(html,/nâng cấp quy trình|Chuyển hồ sơ cũ|<script>/);
  assert.match(html,/Chưa có nội dung yêu cầu/);assert.match(html,/&lt;script&gt;/);
});

test('tự cập nhật hồ sơ cũ không hủy thao tác mở hồ sơ khác đang tải', async () => {
  const fixture=detailFixture();let visible=null,release,blocked;
  const dialog={open:false};
  const d=helpers({
    $:()=>dialog,
    api:async route=>{
      if(route==='orders/2')await new Promise(resolve=>{release=resolve;blocked=true});
      if(route.startsWith('orders/'))return fixture.order;
      const id=Number(route.split('/')[1]);return {...fixture.data,case:{...fixture.data.case,id,orderId:id}};
    },
    openDetail:html=>{dialog.open=true;visible=Number(/data-dispute-detail="(\d+)"/.exec(html)[1]);}
  },{querySelector:selector=>selector.includes(`data-dispute-detail="${visible}"`)?{}:null});
  await d.open(1);
  const pending=d.open(2);
  while(!blocked)await Promise.resolve();
  await d.open(1,true);
  release();await pending;
  assert.equal(visible,2);
});

test('link bằng chứng bắt buộc hợp lệ và không nhận địa chỉ thực thi mã', () => {
  const d = helpers();
  assert.deepEqual([...d.parseLinks(' https://example.test/a\nhttps://example.test/a ')], ['https://example.test/a']);
  assert.throws(() => d.parseLinks(''));
  assert.throws(() => d.parseLinks('javascript:alert(1)'));
  assert.throws(() => d.parseLinks('https://user:pass@example.test'));
});

test('đếm ngược deadline UTC không âm và báo chờ cập nhật sau khi hết hạn', () => {
  const d = helpers();
  const now = Date.parse('2026-10-03T00:00:00Z');
  assert.equal(d.countdown('2026-10-03T00:00:45', now), 'Còn 45 giây để phản hồi');
  assert.equal(d.countdown('2026-10-03T00:00:00Z', now), 'Đã hết hạn, đang chờ cập nhật kết quả');
});

test('hồ sơ vừa đóng rời bộ lọc đang mở vẫn cập nhật nhãn trên đơn hàng', async () => {
  const nodes = {
    'dispute-filter': { value: 'open' }, 'dispute-list': { innerHTML: '' },
    'dispute-tab-count': { textContent: '' }, 'refresh-disputes': {}
  };
  let result = { page: 1, pageSize: 10, totalCount: 1, openCount: 1,
    items: [{ id: 7, orderId: 42, status: 'AwaitingBuyer', isOpen: true, description: 'Hàng lỗi' }] };
  const pending = [], updated = [];
  const context = {
    URL, Date, setInterval() {},
    document: { body: { addEventListener() {} }, querySelector() { return null; } },
    G4: {
      $: id => nodes[id], api: async () => result,
      esc: value => value, money: value => value, date: value => value,
      pager() {}, patchPagedList() {}, safe: work => pending.push(work())
    }
  };
  vm.runInNewContext(fs.readFileSync('frontend/wwwroot/js/disputes.js', 'utf8'), context);
  context.G4.Disputes.init('seller', async id => updated.push(id));
  await Promise.all(pending);
  assert.equal(nodes['dispute-tab-count'].textContent, ' (1)');
  result = { page: 1, pageSize: 10, totalCount: 0, openCount: 0, items: [] };
  await context.G4.Disputes.load(1, true);
  assert.deepEqual(updated, [42]);
  assert.equal(nodes['dispute-tab-count'].textContent, '');
});
