const test = require('node:test');
const assert = require('node:assert/strict');
const { estimate } = require('../scripts/api-budget.cjs');

test('budget reserves funds, includes fixed cost and caps API count at four', () => {
  const result = estimate({ budget: 1000, fixed: 100, hourly: 0.25, hours: 730 });
  assert.equal(result.usable, 900);
  assert.equal(result.fourApiCost, 830);
  assert.equal(result.maxApiReplicas, 4);
  assert.equal(result.maxReadReplicas, 3);
  assert.equal(result.remainingAfterFour, 70);
});

test('insufficient budgets do not silently permit the minimum deployment', () => {
  const result = estimate({ budget: 100, fixed: 95, hourly: 1 });
  assert.equal(result.maxApiReplicas, 0);
  assert.equal(result.topologyAffordable, false);
});

test('budget may allow only three APIs; invalid inputs fail closed', () => {
  assert.equal(estimate({ budget: 800, fixed: 100, hourly: 0.25 }).maxApiReplicas, 3);
  for (const bad of [NaN, Infinity, -1]) {
    assert.throws(() => estimate({ budget: bad, fixed: 0, hourly: 1 }));
  }
  assert.throws(() => estimate({ budget: 100, fixed: 0, hourly: 0 }));
  assert.throws(() => estimate({ budget: 100, fixed: 0, hourly: 1, reserve: 1 }));
});
