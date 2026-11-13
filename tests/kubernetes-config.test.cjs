const test = require('node:test');
const assert = require('node:assert/strict');
const fs = require('node:fs');
const api = JSON.parse(fs.readFileSync('deploy/k8s/api.json', 'utf8')).items;
const find = (kind, name) => api.find(x => x.kind === kind && x.metadata.name === name);

test('API capacity includes main, autoscaling and rollout under a hard quota', () => {
  const main = find('Deployment', 'api-main');
  const read = find('Deployment', 'api-read');
  const hpa = find('HorizontalPodAutoscaler', 'api-read');
  const quota = find('ResourceQuota', 'api-budget');
  assert.equal(main.spec.replicas + hpa.spec.maxReplicas, Number(quota.spec.hard.pods));
  assert.equal(Number(quota.spec.hard.pods), 4);
  assert.equal(main.spec.strategy.type, 'Recreate');
  assert.equal(read.spec.strategy.rollingUpdate.maxSurge, 0);
  assert.equal(hpa.spec.behavior.scaleDown.stabilizationWindowSeconds, 300);
  for (const [deployment, enabled] of [[main, 'true'], [read, 'false']]) {
    const container = deployment.spec.template.spec.containers[0];
    assert.equal(container.env.find(x => x.name === 'Workers__Enabled').value, enabled);
    assert.ok(container.resources.requests.cpu);
    assert.ok(container.resources.limits.memory);
  }
});

test('gateway ConfigMap matches source and does not retry writes', () => {
  const gateway = JSON.parse(fs.readFileSync('deploy/k8s/gateway.json', 'utf8')).items;
  const config = gateway.find(x => x.kind === 'ConfigMap').data['nginx.conf'];
  assert.equal(config, fs.readFileSync('deploy/k8s/gateway.conf', 'utf8'));
  assert.match(config, /proxy_next_upstream off;/);
  assert.match(config, /Retry-After 5 always/);
  assert.match(config, /proxy_set_header Upgrade/);
});
