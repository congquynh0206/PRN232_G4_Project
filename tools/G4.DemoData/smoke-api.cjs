// Read-only HTTP smoke against an imported, isolated G4_Demo_QA_* database.
const assert = require('node:assert/strict');
const fs = require('node:fs');
const path = require('node:path');
const { spawn } = require('node:child_process');

async function main() {
  const report = fs.readFileSync('database/demo/validation.txt', 'utf8');
  const database = report.match(/Imported SQL database: (G4_Demo_QA_[0-9_]+)/)?.[1];
  assert.ok(database, 'only the QA database recorded by the generator is allowed');
  const base = 'http://127.0.0.1:5258';
  const profile = {
    ASPNETCORE_ENVIRONMENT: 'Development',
    DOTNET_ENVIRONMENT: 'Development',
    ConnectionStrings__DefaultConnection: `Server=localhost;Database=${database};Integrated Security=True;TrustServerCertificate=True`,
    SellerIdentity__HmacKey: 'AQIDBAUGBwgJCgsMDQ4PEBESExQVFhcYGRobHB0eHyA=',
    SellerIdentity__KeyId: 'v1',
    Returns__WindowSeconds: '7200',
    Returns__ReceiptSeconds: '7200',
    Disputes__SellerResponseSeconds: '7200',
    Disputes__BuyerResponseSeconds: '7200',
    Disputes__ReturnReceiptSeconds: '7200',
    AddressChanges__ConfirmationSeconds: '7200',
    Finance__Levels__1__HoldSeconds: '1800',
    Finance__Levels__2__HoldSeconds: '900',
    Finance__LevelPromotion__EvaluationSeconds: '86400',
    Finance__Debt__DueSeconds: '3600',
    Finance__Debt__WarningBeforeSeconds: '300',
    Finance__WriteOff__AfterSeconds: '7200',
    Finance__WriteOff__InactivitySeconds: '1800',
    Mail__Mode: 'Capture',
    Carrier__BaseUrl: base,
  };
  const dll = path.resolve('artifacts/rate-limit-validation/build/G4.Api.dll');
  const api = spawn('dotnet', [dll, '--urls', base], {
    cwd: path.resolve('backend/G4.Api'),
    windowsHide: true,
    env: { ...process.env, ...profile },
    stdio: ['ignore', 'pipe', 'pipe'],
  });
  let logs = '';
  api.stdout.on('data', (chunk) => {
    logs += chunk;
  });
  api.stderr.on('data', (chunk) => {
    logs += chunk;
  });
  try {
    for (let i = 0; ; i++) {
      try {
        if ((await fetch(base + '/swagger/index.html')).ok) break;
      } catch {}
      if (i > 100) throw new Error('API did not start: ' + logs);
      await new Promise((resolve) => setTimeout(resolve, 100));
    }
    async function login(email) {
      const reply = await fetch(base + '/api/auth/login', {
        method: 'POST',
        headers: { 'Content-Type': 'application/json' },
        body: JSON.stringify({ email, password: 'G4@123456' }),
      });
      assert.equal(reply.status, 200, email);
      return (await reply.json()).token;
    }
    async function get(route, token) {
      const reply = await fetch(base + route, { headers: { Authorization: 'Bearer ' + token } });
      const text = await reply.text();
      assert.equal(reply.status, 200, route + ': ' + text);
      return JSON.parse(text);
    }
    const buyer = await login('buyer@demo.test');
    const admin = await login('admin@demo.test');
    const sellers = {};
    for (const [email, level] of [
      ['seller.l1@demo.test', 1],
      ['seller.l2@demo.test', 2],
      ['seller.l3@demo.test', 3],
    ]) {
      const token = await login(email);
      sellers[level] = token;
      const finance = await get('/api/seller/finance', token);
      assert.equal(finance.level, level, email);
      await get('/api/seller/level', token);
      await get('/api/seller/debt', token);
      if (level === 3) assert.ok(finance.available > 0);
    }
    const writeoffs = await get('/api/admin/seller-write-offs?page=1&pageSize=10', admin);
    assert.ok(writeoffs.totalCount >= 2);
    const blacklist = await get('/api/admin/seller-blacklist?page=1&pageSize=10', admin);
    assert.ok(blacklist.totalCount >= 3);
    await get('/api/disputes/page?page=1&pageSize=10&filter=all', admin);
    const guide = fs.readFileSync('database/demo/HUONG_DAN_DEMO.md', 'utf8');
    const ids = [...guide.matchAll(/\| [A-Z0-9-]+ \| Order #(\d+) \|/g)].map((match) =>
      Number(match[1]),
    );
    for (const id of ids) await get('/api/orders/' + id, buyer);
    async function post(route, token, body) {
      const response = await fetch(base + route, {
        method: 'POST',
        headers: { 'Content-Type': 'application/json', Authorization: 'Bearer ' + token },
        body: JSON.stringify(body),
      });
      const text = await response.text();
      assert.equal(response.status, 200, route + ': ' + text);
      return JSON.parse(text);
    }
    const orderId = (code) =>
      Number(guide.match(new RegExp(`\\| ${code} \\| Order #(\\d+) \\|`))[1]);
    await post(`/api/orders/${orderId('PAY-READY')}/pay/card`, buyer, {
      number: '4111111111111111',
      expiry: '12/30',
      key: 'qa-ready-card',
    });
    const returned = await get('/api/orders/' + orderId('RETURN-REQUEST-CHANGEDMIND'), buyer);
    await post(`/api/seller/returns/${returned.returnRequest.id}/approve`, sellers[1], {});
    for (const level of [1, 2, 3])
      await post('/api/seller/payouts', sellers[level], {
        amount: 1,
        key: `qa-ready-payout-${level}`,
        simulateFailure: false,
      });
    const up2 = await login('seller.up2@demo.test');
    const up3 = await login('seller.up3@demo.test');
    assert.equal((await post('/api/seller/finance/evaluate-level', up2, {})).level, 2);
    assert.equal((await post('/api/seller/finance/evaluate-level', up3, {})).level, 3);
    console.log(
      `HTTP smoke passed: three seller levels, finance/debt/level, admin write-offs/blacklist, buyer history/disputes, ${ids.length} prepared order details; card payment, return approval/label, all-level payout and level upgrades executed on QA.`,
    );
  } finally {
    fs.writeFileSync('artifacts/demo-api-smoke.log', logs);
    if (api.exitCode === null) {
      const exited = new Promise((resolve) => api.once('exit', resolve));
      api.kill();
      await exited;
    }
  }
}
main().catch((error) => {
  console.error(error);
  process.exitCode = 1;
});
