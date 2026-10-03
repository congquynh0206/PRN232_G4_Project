const test = require('node:test');
const assert = require('node:assert/strict');
const fs = require('node:fs');
const vm = require('node:vm');

function helpers() {
  const context = { G4: {}, URL, Date, window: {}, document: {} };
  vm.runInNewContext(fs.readFileSync('frontend/wwwroot/js/disputes.js', 'utf8'), context);
  return context.G4.Disputes;
}

test('trạng thái và kết quả tranh chấp dùng tiếng Việt', () => {
  const d = helpers();
  assert.equal(d.statusText('AwaitingSeller'), 'Chờ người bán phản hồi');
  assert.equal(d.statusText('AwaitingBuyer'), 'Chờ người mua trả lời');
  assert.equal(d.statusText('Closed', 'BuyerSilent'), 'Đã đóng do người mua không phản hồi');
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
