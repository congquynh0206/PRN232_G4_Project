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
  const context = { window: {}, document: { getElementById: element }, setTimeout, clearTimeout };
  vm.runInNewContext(fs.readFileSync('frontend/wwwroot/js/common.js', 'utf8'), context);
  return { G4: context.window.G4, element };
}

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
