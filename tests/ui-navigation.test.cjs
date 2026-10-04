const test = require('node:test');
const assert = require('node:assert/strict');
const fs = require('node:fs');
const vm = require('node:vm');

function loadCommon() {
  const elements = new Map();
  const element = id => {
    if (!elements.has(id)) elements.set(id, { innerHTML: '', textContent: '' });
    return elements.get(id);
  };
  const context = { window: {}, document: { getElementById: element }, URL, setTimeout, clearTimeout };
  vm.runInNewContext(fs.readFileSync('frontend/wwwroot/js/common.js', 'utf8'), context);
  return { G4: context.window.G4, element };
}

test('hộp thoại có trường mô tả không trùng ID với phần hướng dẫn', () => {
  const {G4, element} = loadCommon();
  element('action-dialog').showModal = () => {};
  element('action-form').querySelector = () => ({focus() {}});
  G4.modal({title:'Báo vấn đề hàng trả', description:'Tiền tiếp tục được giữ',
    fields:[{name:'description',label:'Mô tả',type:'textarea',required:true}]});
  const layout = fs.readFileSync('frontend/Views/Shared/_RoleLayout.cshtml','utf8');
  const staticIds = [...layout.matchAll(/\bid="([^"]+)"/g)].map(m=>m[1]);
  const fieldIds = [...element('action-fields').innerHTML.matchAll(/\bid="([^"]+)"/g)].map(m=>m[1]);
  assert.deepEqual(fieldIds.filter(id=>staticIds.includes(id)),[], 'label và validation phải trỏ đến đúng control');
});

test('vận đơn chờ nhận hiển thị địa chỉ và chỉ có nút nhận, không có nút cập nhật tracking', () => {
  const {G4} = loadCommon();
  const html = G4.shipperShipmentCard({id: 12, orderId: 46, direction: 'Return', status: 'LabelCreated',
    trackingNumber: 'RET-12', pickupAddress: '<script>buyer</script>', deliveryAddress: 'Seller street', totalWeightKg: 1.2});
  assert.match(html, /data-claim-shipment="12"/);
  assert.doesNotMatch(html, /data-open-order|data-event/);
  assert.match(html, /&lt;script&gt;buyer&lt;\/script&gt;/);
  assert.match(html, /1.2 kg/);
  const owned = G4.shipperShipmentCard({id: 12, orderId: 46, direction: 'Return', shipperId: 7, status: 'InTransit'});
  assert.match(owned, /data-open-order="46"/);
  assert.doesNotMatch(owned, /data-claim-shipment/);
});

test('phân trang hiện số trang và cho chọn trực tiếp trang khác', () => {
  const { G4, element } = loadCommon();
  const host = element('orders-pagination');
  const selected = [];
  host.querySelectorAll = () => host.buttons = [...host.innerHTML.matchAll(/data-page="(\d+)"/g)].map(match => ({
    dataset: { page: match[1] },
    set onclick(handler) { this.click = handler; }
  }));
  G4.pager('orders-pagination', { page: 2, pageSize: 10, totalCount: 35 }, page => selected.push(page));
  assert.match(host.innerHTML, /Trang 2\/4/);
  assert.match(host.innerHTML, /data-page="4"/);
  assert.ok(host.buttons.length >= 4, 'có thể chọn các trang cụ thể');
  host.buttons.find(button => button.dataset.page === '4').click();
  assert.deepEqual(selected, [4]);
});

test('chi tiết mở trong hộp thoại và đóng không điều hướng trang', () => {
  const { G4, element } = loadCommon();
  const dialog = element('detail-dialog');
  dialog.open = false;
  dialog.showModal = () => { dialog.open = true; };
  dialog.close = () => { dialog.open = false; };
  G4.openDetail('<h2>Đơn #42</h2>');
  assert.equal(dialog.open, true);
  assert.match(element('detail-content').innerHTML, /Đơn #42/);
  G4.closeDetail();
  assert.equal(dialog.open, false);
});

test('timeline dài nối sang hàng kế tiếp trong chiều rộng hiện có', () => {
  const { G4 } = loadCommon();
  const nodes = Array.from({ length: 7 }, () => ({
    style: {},
    classList: { values: new Set(), toggle(name, enabled) {
      if (enabled) this.values.add(name); else this.values.delete(name);
    } }
  }));
  const timeline = {
    children: nodes,
    parentElement: { clientWidth: 580 },
    style: { setProperty(name, value) { this[name] = value; } },
    dataset: {}
  };
  G4.layoutTimeline(timeline);
  assert.equal(timeline.dataset.columns, '3');
  assert.equal(nodes[2].style.gridRow, '1');
  assert.equal(nodes[3].style.gridRow, '2');
  assert.equal(nodes[3].style.gridColumn, '3');
  assert.equal(nodes[2].classList.values.has('timeline-turn'), true);
});

test('danh sách đã lọc loại đơn đổi trạng thái và bổ sung mục từ trang kế', () => {
  const { G4 } = loadCommon();
  const changes = G4.pageChanges([1, 2, 3], [2, 3, 4], 1);
  assert.deepEqual([...changes.remove], [1]);
  assert.deepEqual([...changes.upsert], [4]);
  assert.deepEqual([...changes.order], [2, 3, 4]);
  const retained = G4.pageChanges([1, 2], [1, 2], 1);
  assert.deepEqual([...retained.upsert], [1]);
});

test('thẻ seller nhận diện sản phẩm, người mua và refund mà không render HTML dữ liệu', () => {
  const { G4 } = loadCommon();
  const html = G4.orderCard({ id: 46, status: 'Closed', totalPrice: 191.89,
    productTitle: '<img src=x onerror=alert(1)>', buyerName: 'A & B',
    productCount: 3, itemCount: 5, hasRefund: true, attention: null }, 'seller');
  assert.match(html, /&lt;img src=x onerror=alert\(1\)&gt;/);
  assert.match(html, /A &amp; B/);
  assert.match(html, /\+2 sản phẩm khác/);
  assert.match(html, /5 món/);
  assert.match(html, /Đã hoàn tiền/);
  assert.doesNotMatch(html, />Hoàn tất</);
  assert.match(html, /Tổng đơn/);
  assert.match(html, /\$191\.89/);
  assert.match(html, /data-open-order="46"/);
});

test('thẻ buyer dùng bố cục seller, ảnh sản phẩm và thông tin người bán', () => {
  const {G4} = loadCommon();
  const html = G4.orderCard({id:46,status:'Closed',totalPrice:191.89,productTitle:'Camera',
    imageUrl:'https://example.test/camera.jpg',sellerName:'Shop A & B',buyerName:'Không hiển thị',
    productCount:2,itemCount:3,hasRefund:true},'buyer');
  assert.match(html,/seller-order-head/);
  assert.match(html,/seller-order-body/);
  assert.match(html,/seller-order-foot/);
  assert.match(html,/<img[^>]+src="https:\/\/example.test\/camera.jpg"/);
  assert.match(html,/Người bán: Shop A &amp; B/);
  assert.doesNotMatch(html,/Người mua:|Không hiển thị/);
  assert.match(html,/Đã hoàn tiền/);
  assert.match(html,/data-source="buyer"/);
});

test('ảnh sản phẩm dùng URL HTTP(S), escape tên và có placeholder khi không có ảnh', () => {
  const {G4} = loadCommon();
  const photo = G4.productThumbnail('https://example.test/photo.jpg','Camera "A" <test>');
  assert.match(photo,/alt="Camera &quot;A&quot; &lt;test&gt;"/);
  assert.match(photo,/loading="lazy"/);
  assert.match(photo,/product-image-placeholder/);
  for(const url of [null,'','javascript:alert(1)','data:text/html,test']) {
    const html = G4.productThumbnail(url,'Camera');
    assert.match(html,/product-image-placeholder/);
    assert.doesNotMatch(html,/<img/);
  }
});
