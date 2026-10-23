(() => {
  const { $, api, esc, date, safe, pager, money } = G4;
  let role,
    selectedSeller,
    policy,
    request = 0,
    evaluating = false,
    simulating = false,
    page = 1;
  const stamp = (v) =>
    v
      ? date(typeof v === 'string' && !/(Z|[+-]\d{2}:\d{2})$/i.test(v) ? v + 'Z' : v)
      : 'Chưa đánh giá';
  const number = (v) =>
    v == null ? 'Chưa có dữ liệu' : Number(v).toLocaleString('vi-VN', { maximumFractionDigits: 2 });
  const value = (c, v) =>
    v == null
      ? 'Chưa có dữ liệu'
      : c.code === 'restriction'
        ? v
          ? 'Đang bị hạn chế'
          : 'Không có hạn chế'
        : c.code === 'debt'
          ? money(v)
          : number(v) +
            (['feedback', 'delivery', 'cancel', 'return', 'dispute'].includes(c.code) ? '%' : '');
  function criteriaHtml(t) {
    return `<section class="level-tier"><h3>Điều kiện cấp ${Number(t.level)}${t.level === 3 ? ' · Instant' : ''}</h3><ul class="level-criteria">${t.criteria.map((c) => `<li><span>${esc(c.label)}<small>Yêu cầu ${c.code === 'restriction' ? '' : c.maximum ? '≤' : '≥'} ${value(c, c.required)}</small></span><strong>${value(c, c.actual)}<small>${c.passed == null ? 'Chưa đủ dữ liệu' : c.passed ? 'Đạt' : 'Chưa đạt'}</small></strong></li>`).join('')}</ul></section>`;
  }
  function decisionHtml(d) {
    const m = d.metrics;
    return `<p class="level-result"><strong>Cấp ${Number(d.previousLevel)} → cấp ${Number(d.resultLevel)}</strong><br>${esc(d.reason)}</p><p class="small">Kỳ ${stamp(m.windowStart)} – ${stamp(m.windowEnd)} · ${Number(m.paidOrders)} đơn thanh toán · ${Number(m.feedbackCount)} đánh giá · ${Number(m.deliveryCount)} đơn có hạn giao.</p><div class="level-tier-grid">${d.tiers.map(criteriaHtml).join('')}</div>`;
  }
  function detailHtml(d) {
    return `<h2>Seller #${Number(d.sellerId)} · Cấp ${Number(d.level)}</h2><p>Lần xét gần nhất: ${stamp(d.lastEvaluatedAt)} · Lần tự xét tiếp theo sớm nhất: ${stamp(d.nextEvaluationAt)}</p><p class="small">Bên dưới là dữ liệu hiện tại và kết quả nếu xét ngay. Mỗi đánh giá 4–5 sao được tính là tích cực; thiếu đánh giá không tính là phản hồi xấu.</p>${decisionHtml(d.preview)}<h2>Lịch sử đánh giá gần nhất</h2><div class="level-history">${d.history.length ? d.history.map((h) => `<details><summary>${stamp(h.createdAt)} · ${h.source === 'Worker' ? 'Hệ thống tự xét' : 'Seller yêu cầu'} · ${Number(h.decision.previousLevel)} → ${Number(h.decision.resultLevel)}</summary><p class="small">Số liệu và chính sách tại thời điểm xét, không thay đổi theo dữ liệu hiện tại.</p>${decisionHtml(h.decision)}</details>`).join('') : '<p>Chưa có lịch sử đánh giá.</p>'}</div>`;
  }
  async function loadDetail(id) {
    const token = ++request;
    selectedSeller = id;
    $('seller-level-detail').setAttribute('aria-busy', 'true');
    try {
      const d = await api(role === 'admin' ? `admin/seller-levels/${id}` : 'seller/level');
      if (token !== request) return;
      policy = d.preview.policy;
      $('seller-level-detail').innerHTML = detailHtml(d);
      $('seller-level-status').textContent = `Đã tải cấp và tiêu chí của seller #${d.sellerId}.`;
    } catch (e) {
      if (token === request) {
        $('seller-level-detail').innerHTML = '';
        $('seller-level-status').textContent = 'Chưa tải được dữ liệu. Hãy thử Làm mới.';
      }
      throw e;
    } finally {
      if (token === request) $('seller-level-detail').setAttribute('aria-busy', 'false');
    }
  }
  async function load(p = page) {
    if (role !== 'admin') return loadDetail();
    const token = ++request;
    const query = new URLSearchParams({ page: p, pageSize: 10 });
    if ($('level-seller-id').value) query.set('sellerId', $('level-seller-id').value);
    if ($('level-filter-tier').value) query.set('level', $('level-filter-tier').value);
    const data = await api(`admin/seller-levels?${query}`);
    if (token !== request) return;
    policy = data.policy;
    page = data.page;
    $('seller-level-list').innerHTML = data.items.length
      ? data.items
          .map(
            (s) =>
              `<article class="finance-row"><div><strong>${esc(s.sellerName)} · Seller #${Number(s.sellerId)} · Cấp ${Number(s.level)}</strong><small>Hạn mức ${money(s.monthlySalesLimit)}/tháng · Nợ ${money(s.negativeBalance)} · Lần xét ${stamp(s.lastLevelEvaluatedAt)}</small></div><button type="button" data-level-seller="${Number(s.sellerId)}">Xem tiêu chí và lịch sử</button></article>`,
          )
          .join('')
      : '<p>Không có seller phù hợp.</p>';
    pager('seller-level-pagination', data, (next) => safe(() => load(next)));
  }
  async function evaluate() {
    if (evaluating) return;
    evaluating = true;
    $('evaluate-seller-level').disabled = true;
    $('seller-level-status').textContent = 'Đang đánh giá…';
    try {
      await api('seller/finance/evaluate-level', 'POST');
      await loadDetail();
    } catch (e) {
      $('seller-level-status').textContent =
        'Chưa xác nhận được kết quả xét cấp. Hãy làm mới để xem lịch sử.';
      throw e;
    } finally {
      evaluating = false;
      $('evaluate-seller-level').disabled = false;
    }
  }
  function scenario(name) {
    const p = policy || {
      minimumPaidOrders: 20,
      level2: { completedOrders: 20, activityDays: 30 },
      level3: { completedOrders: 100, activityDays: 90 },
    };
    const t = name === 'level2' ? p.level2 : p.level3;
    const values = {
      current: name === 'level2' || name === 'level3' ? 1 : 3,
      completed: t.completedOrders,
      paid: Math.max(100, p.minimumPaidOrders),
      'feedback-count': 100,
      feedback: 100,
      'delivery-count': 100,
      delivery: 100,
      age: t.activityDays,
      cancel: name === 'demote' ? 100 : 0,
      return: 0,
      dispute: 0,
      debt: name === 'debt' ? 25 : 0,
    };
    if (name === 'sample') values.paid = Math.max(0, p.minimumPaidOrders - 1);
    if (name === 'missing') {
      values['feedback-count'] = 0;
      values['delivery-count'] = 0;
    }
    Object.entries(values).forEach(([key, value]) => {
      $('sim-' + key).value = value;
    });
    $('sim-restricted').checked = false;
    $('level-simulation-result').innerHTML = '';
    $('level-simulation-error').textContent = '';
  }
  async function simulate(e) {
    e.preventDefault();
    if (simulating) return;
    simulating = true;
    $('level-simulate').disabled = true;
    $('level-simulation-fields').disabled = true;
    $('level-scenario').disabled = true;
    $('level-simulation-error').textContent = '';
    $('level-simulation-result').textContent = 'Đang chạy mô phỏng…';
    const n = (id) => Number($('sim-' + id).value);
    try {
      const now = new Date(),
        start = new Date(now.getTime() - (policy?.windowDays || 90) * 86400000);
      const d = await api('dev/seller-levels/simulate', 'POST', {
        currentLevel: n('current'),
        metrics: {
          completedOrders: n('completed'),
          paidOrders: n('paid'),
          feedbackCount: n('feedback-count'),
          positiveFeedbackRate: n('feedback-count') ? n('feedback') : null,
          deliveryCount: n('delivery-count'),
          onTimeDeliveryRate: n('delivery-count') ? n('delivery') : null,
          activityDays: n('age'),
          cancellationRate: n('cancel'),
          returnRate: n('return'),
          disputeRate: n('dispute'),
          negativeBalance: n('debt'),
          restricted: $('sim-restricted').checked,
          windowStart: start.toISOString(),
          windowEnd: now.toISOString(),
        },
      });
      policy = d.policy;
      $('level-simulation-result').innerHTML = `<h2>Kết quả mô phỏng</h2>${decisionHtml(d)}`;
    } catch (err) {
      $('level-simulation-result').textContent = '';
      $('level-simulation-error').textContent = err.message;
      $('level-simulation-error').focus();
    } finally {
      simulating = false;
      $('level-simulate').disabled = false;
      $('level-simulation-fields').disabled = false;
      $('level-scenario').disabled = false;
    }
  }
  function init(r) {
    role = r;
    $('refresh-seller-levels').onclick = () =>
      safe(() => (role === 'admin' && selectedSeller ? loadDetail(selectedSeller) : load()));
    if (role === 'admin') {
      $('seller-level-filter').onsubmit = (e) => {
        e.preventDefault();
        request++;
        selectedSeller = null;
        $('seller-level-detail').innerHTML = '';
        safe(() => load(1));
      };
      $('seller-level-list').onclick = (e) => {
        const b = e.target.closest('[data-level-seller]');
        if (b) safe(() => loadDetail(Number(b.dataset.levelSeller)));
      };
    } else $('evaluate-seller-level').onclick = () => safe(evaluate);
    if ($('level-simulation-form')) {
      scenario('level2');
      $('level-scenario').onchange = () => scenario($('level-scenario').value);
      $('level-simulation-form').onsubmit = simulate;
    }
  }
  async function refresh() {
    const id = selectedSeller;
    await load();
    if (role === 'admin' && id) await loadDetail(id);
  }
  G4.SellerLevels = {
    init,
    load,
    refresh,
    criteriaHtml,
    detailHtml,
    decisionHtml,
    simulate,
    scenario,
  };
})();
