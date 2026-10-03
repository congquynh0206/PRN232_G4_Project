(() => {
  const { $, api, esc, money, date, label, safe, message, pager, patchPagedList, modal, openDetail, trackingHtml } = G4;
  const proposals = { Refund: 'Hoàn tiền toàn bộ', ReturnRefund: 'Trả hàng để hoàn tiền', KeepOrder: 'Giữ nguyên đơn hàng' };
  const outcomes = {
    BuyerSilent: 'Đã đóng do người mua không phản hồi', SellerWins: 'Kết quả: người bán thắng',
    BuyerWins: 'Kết quả: người mua thắng', AgreedRefund: 'Đã hoàn tiền theo thỏa thuận',
    AgreedReturn: 'Đã trả hàng và hoàn tiền', AgreedKeepOrder: 'Hai bên thống nhất giữ nguyên đơn',
    RefundCompleted: 'Đơn hàng đã được hoàn tiền'
  };
  const warning = 'Bằng chứng không thể sửa hoặc xóa sau khi gửi. Vui lòng kiểm tra trước khi xác nhận.';
  let role, onOrderChange, page = 1, previous = [], activeId = null, revision = '', busy = false, loading = false;

  function statusText(status, outcome) {
    if (status === 'Closed') return outcomes[outcome] || 'Đã giải quyết';
    if (status === 'ExecutingAgreement' && outcome === 'BuyerWins') return 'Đang hoàn tiền theo quyết định';
    return { AwaitingSeller: 'Chờ người bán phản hồi', AwaitingBuyer: 'Chờ người mua trả lời',
      Escalated: 'Chờ admin xử lý', ExecutingAgreement: 'Đang thực hiện thỏa thuận' }[status] || 'Đang xử lý yêu cầu';
  }

  function utcTime(value) {
    return Date.parse(/(?:Z|[+-]\d{2}:\d{2})$/i.test(value) ? value : value + 'Z');
  }

  function countdown(value, now = Date.now()) {
    const remaining = Math.max(0, Math.ceil((utcTime(value) - now) / 1000));
    return remaining > 0 ? `Còn ${remaining} giây để phản hồi` : 'Đã hết hạn, đang chờ cập nhật kết quả';
  }

  function parseLinks(value) {
    const links = [...new Set(String(value).split(/\r?\n/).map(x => x.trim()).filter(Boolean))];
    if (links.length < 1 || links.length > 10) throw new Error('Vui lòng nhập từ 1 đến 10 link, mỗi link một dòng.');
    for (const link of links) {
      let url;
      try { url = new URL(link); } catch { throw new Error('Có link chưa hợp lệ. Vui lòng dùng địa chỉ http hoặc https đầy đủ.'); }
      if (!['http:', 'https:'].includes(url.protocol) || !url.hostname || url.username || url.password || link.length > 2000)
        throw new Error('Link bằng chứng phải là địa chỉ http hoặc https hợp lệ, không chứa thông tin đăng nhập.');
    }
    return links;
  }

  function evidenceFields() {
    return [
      { name: 'description', label: 'Mô tả bằng chứng', type: 'textarea', required: true,
        validate: value => value.trim() ? '' : 'Vui lòng nhập mô tả bằng chứng.' },
      { name: 'links', label: 'Link bằng chứng', type: 'textarea', rows: 3, maxLength: 20000, required: true,
        placeholder: 'https://...', help: 'Mỗi link một dòng. Hãy cấp quyền xem để bên còn lại và admin mở được.',
        validate: value => { try { parseLinks(value); return ''; } catch (error) { return error.message; } } }
    ];
  }

  function deadlineHtml(c) {
    return c.responseDueAt ? `<p class="case-deadline" data-case-deadline="${esc(c.responseDueAt)}">${countdown(c.responseDueAt)}</p>` : '';
  }

  function card(c) {
    return `<article class="order-card dispute-card" data-case-row="${c.id}">
      <div class="order-card-main"><span class="overline">Yêu cầu #${c.id} · Đơn hàng #${c.orderId}</span>
        <strong>${esc(statusText(c.status, c.outcome))}</strong><small>${esc(c.description)}</small>
        <small>Mở: ${date(c.createdAt)} · Cập nhật: ${date(c.updatedAt)}</small>${deadlineHtml(c)}
      </div><div class="order-card-side"><b>${money(c.totalPrice)}</b>
        <button type="button" data-open-case="${c.id}">Xem chi tiết</button></div></article>`;
  }

  async function load(next = page, incremental = false) {
    if (loading) return;
    loading = true;
    try {
      const result = await api(`disputes/page?page=${next}&pageSize=10&filter=${$('dispute-filter').value}`);
      const empty = '<p class="empty-note">Chưa có yêu cầu phù hợp.</p>';
      const changed = result.items.filter(c => JSON.stringify(c) !== JSON.stringify(previous.find(x => x.id === c.id)));
      const removed = previous.filter(c => !result.items.some(x => x.id === c.id));
      if (incremental) {
        patchPagedList('dispute-list', result.items, c => c.id, card, -1, 'data-case-row', empty);
        for (const c of changed) {
          const row = document.querySelector(`[data-case-row="${c.id}"]`);
          if (row) row.outerHTML = card(c);
        }
      } else $('dispute-list').innerHTML = result.items.length ? result.items.map(card).join('') : empty;
      page = result.page;
      pager('dispute-pagination', result, n => safe(() => load(n)));
      $('dispute-tab-count').textContent = result.openCount ? ` (${result.openCount})` : '';
      previous = result.items;
      if (incremental && onOrderChange) {
        for (const orderId of new Set([...changed, ...removed].map(c => c.orderId))) await onOrderChange(orderId);
      }
    } finally { loading = false; }
  }

  function renderDetail(data, order) {
    const c = data.case;
    const canRespond = c.responseDueAt && utcTime(c.responseDueAt) > Date.now();
    const buttons = [];
    if (role !== 'admin' && c.isOpen && !c.outcome)
      buttons.push(`<button type="button" data-case-action="evidence" data-case-id="${c.id}">Bổ sung bằng chứng</button>`);
    if (role === 'seller' && c.status === 'AwaitingSeller')
      buttons.push(`<button type="button" class="primary" data-case-action="proposal" data-case-id="${c.id}" ${canRespond ? '' : 'disabled'}>Gửi phương án giải quyết</button>`);
    if (role === 'buyer' && c.status === 'AwaitingBuyer')
      buttons.push(`<button type="button" class="primary" data-case-action="accept" data-case-id="${c.id}" ${canRespond ? '' : 'disabled'}>Chấp nhận phương án</button>
        <button type="button" data-case-action="reject" data-case-id="${c.id}" ${canRespond ? '' : 'disabled'}>Không đồng ý, nhờ admin xử lý</button>`);
    if (role === 'admin' && c.status === 'Escalated')
      buttons.push(`<button type="button" data-case-action="buyer-wins" data-case-id="${c.id}">Người mua thắng — hoàn tiền</button>
        <button type="button" data-case-action="seller-wins" data-case-id="${c.id}">Người bán thắng — giải phóng tiền</button>`);
    const names = { buyer: 'Người mua', seller: 'Người bán', admin: 'Admin', system: 'Hệ thống' };
    const kinds = { Evidence: 'Bằng chứng', Proposal: 'Phương án giải quyết', Response: 'Phản hồi',
      Decision: 'Quyết định', Escalated: 'Chuyển admin', Closed: 'Kết quả', Imported: 'Chuyển hồ sơ cũ' };
    const history = data.entries.map(e => `<article class="case-entry">
      <header><strong>${esc(names[e.actorRole] || 'Hệ thống')} · ${esc(kinds[e.kind] || 'Cập nhật')}</strong><small>${date(e.createdAt)}</small></header>
      <p>${esc(e.description)}</p>${e.evidenceLinks.length ? `<ul>${e.evidenceLinks.map((url, index) =>
        `<li><a href="${esc(url)}" target="_blank" rel="noopener noreferrer">Bằng chứng ${index + 1}: ${esc(url)}</a></li>`).join('')}</ul>` : ''}</article>`).join('');
    const pay = order.payments.at(-1);
    return `<div class="detail case-detail" data-dispute-detail="${c.id}">
      <h2>Yêu cầu #${c.id} · Đơn hàng #${c.orderId}</h2>
      <p class="case-status">${esc(statusText(c.status, c.outcome))}</p>${deadlineHtml(c)}
      <div class="status-grid"><div class="status-box"><span>Tổng tiền đơn</span><strong>${money(c.totalPrice)}</strong></div>
        <div class="status-box"><span>Tiền đang giữ của đơn</span><strong>${money(c.heldAmount)}</strong></div>
        <div class="status-box"><span>Thanh toán</span><strong>${esc(label(pay?.status))} · ${esc(label(pay?.method))}</strong></div></div>
      <p class="small">Người mua #${c.buyerId} · Người bán #${c.sellerId} · Mở: ${date(c.createdAt)}</p>
      <h3>Nội dung yêu cầu</h3><p class="case-description">${esc(c.description)}</p>
      ${c.proposal ? `<div class="case-proposal"><strong>Phương án người bán: ${esc(proposals[c.proposal])}</strong>
        ${c.status === 'AwaitingBuyer' ? '<p>Người mua cần phản hồi trước thời hạn. Nếu không phản hồi, yêu cầu sẽ tự đóng và khoản giữ được giải phóng theo quy tắc tài chính.</p>' : ''}</div>` : ''}
      ${c.resolution ? `<div class="case-result"><strong>Giải thích kết quả</strong><p>${esc(c.resolution)}</p></div>` : ''}
      ${c.status === 'AwaitingSeller' ? '<p class="small">Người bán cần gửi phương án giải quyết. Chỉ bổ sung bằng chứng sẽ không dừng thời hạn phản hồi.</p>' : ''}
      ${c.status === 'ExecutingAgreement' ? '<p class="small">Yêu cầu chỉ kết thúc khi việc trả hàng/hoàn tiền cần thiết hoàn tất. Bạn có thể theo dõi vận chuyển và thao tác trả hàng trong chi tiết đơn.</p>' : ''}
      <div class="actions">${buttons.join('')}</div><h3>Lịch sử trao đổi và bằng chứng</h3>
      <p class="evidence-warning">${warning}</p><div class="case-history">${history}</div>
      <h3>Thông tin đơn hàng</h3><p>${esc(order.addressSnapshot)}</p>
      ${order.items.map(x => `<p>${esc(x.productTitleSnapshot)} · Số lượng ${x.quantity}</p>`).join('')}
      <h3>Tiến trình vận chuyển</h3>${trackingHtml(order)}</div>`;
  }

  async function open(id, preserve = false) {
    const data = await api(`disputes/${id}`);
    const order = await api(`orders/${data.case.orderId}`);
    const nextRevision = JSON.stringify(data) + order.updatedAt;
    if (preserve && revision === nextRevision) return;
    activeId = id;
    revision = nextRevision;
    openDetail(renderDetail(data, order), preserve);
  }

  async function openNew(orderId) {
    const input = await modal({ title: `Mở yêu cầu cho đơn #${orderId}`, description: warning,
      fields: evidenceFields(), confirm: 'Gửi yêu cầu' });
    if (!input) return;
    busy = true;
    try {
      const result = await api(`orders/${orderId}/disputes`, 'POST', { description: input.description, evidenceLinks: parseLinks(input.links) });
      $('dispute-filter').value = 'open';
      await load(1);
      await onOrderChange?.(orderId);
      message('Đã mở yêu cầu và thông báo cho người bán. Bạn có thể theo dõi trong tab Tranh chấp.', true);
      await open(result.id);
    } finally { busy = false; }
  }

  async function action(button) {
    const id = Number(button.dataset.caseId), kind = button.dataset.caseAction;
    let input, path, body;
    if (kind === 'evidence' || kind === 'proposal') {
      const fields = evidenceFields();
      if (kind === 'proposal') fields.unshift({ name: 'proposal', label: 'Phương án giải quyết', type: 'select', required: true,
        options: Object.entries(proposals).map(([value, text]) => ({ value, label: text })) });
      input = await modal({ title: kind === 'proposal' ? 'Gửi phương án giải quyết' : 'Bổ sung bằng chứng', description: warning, fields, confirm: 'Gửi' });
      if (!input) return;
      path = kind;
      body = { description: input.description, evidenceLinks: parseLinks(input.links), ...(kind === 'proposal' ? { proposal: input.proposal } : {}) };
    } else if (kind === 'accept' || kind === 'reject') {
      input = await modal({ title: kind === 'accept' ? 'Chấp nhận phương án' : 'Yêu cầu admin xử lý',
        description: kind === 'accept' ? 'Bạn đồng ý thực hiện phương án người bán đã gửi.' : 'Hồ sơ và bằng chứng của hai bên sẽ được chuyển cho admin.',
        fields: kind === 'reject' ? [{ name: 'description', label: 'Lý do chưa thống nhất', type: 'textarea', required: true }] : [], confirm: 'Xác nhận' });
      if (!input) return;
      path = 'response'; body = { accept: kind === 'accept', description: input.description || 'Đồng ý phương án' };
    } else {
      input = await modal({ title: 'Quyết định tranh chấp', description: kind === 'buyer-wins' ? 'Người mua thắng: hoàn tiền về phương thức thanh toán ban đầu.' : 'Người bán thắng: giải phóng tiền theo lịch rút tiền hiện có.',
        fields: [{ name: 'reason', label: 'Lý do quyết định', type: 'textarea', required: true }], confirm: 'Lưu quyết định' });
      if (!input) return;
      path = 'resolution'; body = { buyerWins: kind === 'buyer-wins', reason: input.reason };
    }
    busy = true;
    try {
      message('Đang lưu yêu cầu, vui lòng đợi trong giây lát…');
      await api(`disputes/${id}/${path}`, 'POST', body);
      await load(page, true);
      await open(id, true);
      message('Đã lưu. Trạng thái và lịch sử yêu cầu đã được cập nhật.', true);
    } finally { busy = false; }
  }

  function updateCountdowns() {
    document.querySelectorAll('[data-case-deadline]').forEach(node => { node.textContent = countdown(node.dataset.caseDeadline); });
    const detail = document.querySelector('[data-dispute-detail]');
    const deadline = detail?.querySelector('[data-case-deadline]')?.dataset.caseDeadline;
    if (deadline && utcTime(deadline) <= Date.now())
      detail.querySelectorAll('[data-case-action="proposal"],[data-case-action="accept"],[data-case-action="reject"]').forEach(b => b.disabled = true);
  }

  function init(actorRole, afterOrderChange) {
    role = actorRole;
    onOrderChange = afterOrderChange;
    $('refresh-disputes').onclick = () => safe(() => load(1));
    $('dispute-filter').onchange = () => safe(() => load(1));
    document.body.addEventListener('click', event => {
      const openButton = event.target.closest('[data-open-case]');
      if (openButton) return safe(() => open(Number(openButton.dataset.openCase)));
      const actionButton = event.target.closest('[data-case-action]');
      if (actionButton && !busy) return safe(() => action(actionButton));
    });
    safe(() => load());
    setInterval(updateCountdowns, 1000);
    setInterval(() => safe(async () => {
      if (busy || loading || document.hidden || $('action-dialog').open || document.activeElement?.closest('.pagination')) return;
      await load(page, true);
      if (activeId && $('detail-dialog').open && document.querySelector(`[data-dispute-detail="${activeId}"]`)) await open(activeId, true);
    }), 4000);
  }

  G4.Disputes = { init, load, open, openNew, statusText, parseLinks, countdown };
})();
