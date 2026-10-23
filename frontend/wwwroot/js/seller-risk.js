(() => {
  const { $, api, esc, money, date, safe, pager } = G4;
  let decisionPage = 1,
    blacklistPage = 1,
    decisionsSequence = 0,
    blacklistSequence = 0,
    seller = '';
  const stamp = (v) =>
    date(typeof v === 'string' && !/(Z|[+-]\d{2}:\d{2})$/i.test(v) ? v + 'Z' : v);
  const duration = (v) => (v >= 86400 ? `${v / 86400} ngày` : `${v} giây`);
  function decisionsHtml(items) {
    return items.length
      ? items
          .map(
            (x) =>
              `<article class="finance-row"><div><strong>Quyết định #${Number(x.id)} · Seller #${Number(x.sellerId)} · Khóa bán vĩnh viễn</strong><small>Kỳ nợ #${Number(x.cycleNumber)} · Hệ thống tự động · ${stamp(x.createdAt)}</small><p>${esc(x.reason)}</p><small>Bắt đầu nợ: ${stamp(x.debtStartedAt)} · Hạn trả: ${stamp(x.debtDueAt)}</small><small>Đủ tuổi nợ từ: ${stamp(x.eligibleAt)} · Mốc thu hồi gần nhất/khởi tạo: ${stamp(x.lastRecoveryAt)} · Yêu cầu không thu hồi: ${duration(x.recoveryGraceSeconds)}</small><p>Nợ lúc write-off: ${money(x.originalAmount)} · Đã thu hồi sau quyết định: ${money(x.recoveredAmount)} · Còn trong khoản write-off: ${money(x.remainingWrittenOffAmount)}</p><p>Nợ hiện tại của seller: ${money(x.currentDebt)}</p><small>${x.blacklistedIdentities ? `${Number(x.blacklistedIdentities)} định danh đã xác minh được đưa vào blacklist` : 'Chưa có định danh đã xác minh để đối chiếu blacklist'}</small></div></article>`,
          )
          .join('')
      : '<p class="empty-note">Chưa có quyết định nợ khó thu phù hợp.</p>';
  }
  function blacklistHtml(items) {
    return items.length
      ? items
          .map(
            (x) =>
              `<article class="finance-row"><div><strong>${esc(x.maskedValue)} · ${x.isDemo ? 'Dữ liệu mẫu Development' : `Seller #${Number(x.sourceSellerId)}`}</strong><small>${stamp(x.createdAt)}${x.writeOffId ? ` · Quyết định #${Number(x.writeOffId)}` : ''}</small><p>${esc(x.reason)}</p></div></article>`,
          )
          .join('')
      : '<p class="empty-note">Chưa có định danh blacklist phù hợp.</p>';
  }
  function query(page) {
    const q = new URLSearchParams({ page: String(page), pageSize: '10' });
    if (seller) q.set('sellerId', seller);
    return q;
  }
  async function decisions(page = decisionPage) {
    const ticket = ++decisionsSequence;
    $('seller-write-offs').setAttribute('aria-busy', 'true');
    try {
      const result = await api(`admin/seller-write-offs?${query(page)}`);
      if (ticket !== decisionsSequence) return;
      decisionPage = result.page;
      const p = result.policy,
        s = result.summary;
      $('seller-risk-policy').textContent =
        `Tự xử lý: ${p.enabled ? 'Đang bật' : 'Đang tắt'}. Kỳ mới: nợ liên tục ${duration(p.afterSeconds)}, đã quá hạn và không có khoản thu hồi trong ${duration(p.inactivitySeconds)}. Kỳ đang mở giữ chính sách đã lưu. Quyền xử lý đơn cũ và trả nợ được giữ.`;
      $('seller-risk-summary').innerHTML = [
        ['Nợ lúc write-off', s.originalAmount],
        ['Đã thu hồi', s.recoveredAmount],
        ['Còn trong khoản write-off', Number(s.originalAmount) - Number(s.recoveredAmount)],
      ]
        .map(
          ([title, value]) =>
            `<div class="balance-card"><span>${title}</span><strong>${money(value)}</strong></div>`,
        )
        .join('');
      $('seller-write-offs').innerHTML = decisionsHtml(result.items);
      pager('seller-write-offs-pagination', result, (next) => safe(() => decisions(next)));
    } finally {
      if (ticket === decisionsSequence) $('seller-write-offs').setAttribute('aria-busy', 'false');
    }
  }
  async function blacklist(page = blacklistPage) {
    const ticket = ++blacklistSequence;
    $('seller-blacklist').setAttribute('aria-busy', 'true');
    try {
      const result = await api(`admin/seller-blacklist?${query(page)}`);
      if (ticket !== blacklistSequence) return;
      blacklistPage = result.page;
      $('seller-blacklist').innerHTML = blacklistHtml(result.items);
      pager('seller-blacklist-pagination', result, (next) => safe(() => blacklist(next)));
    } finally {
      if (ticket === blacklistSequence) $('seller-blacklist').setAttribute('aria-busy', 'false');
    }
  }
  async function load() {
    await Promise.all([decisions(), blacklist()]);
  }
  function init() {
    $('refresh-seller-risk').onclick = () => safe(load);
    $('seller-risk-filters').onsubmit = (e) => {
      e.preventDefault();
      seller = $('seller-risk-seller').value;
      decisionPage = blacklistPage = 1;
      safe(load);
    };
  }
  G4.SellerRisk = { init, load, decisionsHtml, blacklistHtml };
})();
