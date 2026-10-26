const test = require('node:test');
const assert = require('node:assert/strict');
const fs = require('node:fs');
const path = require('node:path');

const root = path.join(__dirname, '..');
const ui = fs.readFileSync(path.join(root, 'frontend/wwwroot/js/buyer-transactions.js'), 'utf8');
const view = fs.readFileSync(path.join(root, 'frontend/Views/Buyer/Index.cshtml'), 'utf8');
const buyer = fs.readFileSync(path.join(root, 'frontend/wwwroot/js/buyer.js'), 'utf8');

test('Buyer transaction history exposes the required filters and period presets', () => {
  for (const text of ['name="orderId"', 'name="from"', 'name="to"', 'name="type"', 'name="status"', 'value="30"', 'value="all"', 'Tìm kiếm', 'Xóa bộ lọc', 'Làm mới']) assert.ok(view.includes(text), `missing ${text}`);
  assert.match(ui, /start\.setDate\(start\.getDate\(\) - 29\)/);
  assert.match(ui, /paidFrom/);
  assert.match(ui, /completedFrom/);
});

test('Rows have stable order links, expandable details, copy action, and do not initiate payments', () => {
  assert.match(ui, /data-transaction-order=/);
  assert.match(ui, /data-transaction-detail=/);
  assert.match(ui, /data-copy=/);
  assert.match(ui, /window\.G4BuyerShowOrder/);
  assert.doesNotMatch(ui, /api\(`orders\/\$\{.*pay\/|api\(`seller\/orders\/\$\{.*refund/);
});

test('Order detail shows lifecycle totals and opens filtered history', () => {
  assert.match(buyer, /Đã thanh toán thành công/);
  assert.match(buyer, /Đã hoàn thành công/);
  assert.match(buyer, /Còn lại sau hoàn/);
  assert.match(buyer, /G4BuyerTransactions\.openForOrder/);
});
