(() => {
  const { $, api, esc, money, date, label, safe, message, pager, patchPagedList, modal, openDetail, trackingHtml, productThumbnail } = G4;
  const proposals = { Refund: 'Hoàn tiền toàn bộ', ReturnRefund: 'Trả hàng để hoàn tiền', KeepOrder: 'Giữ nguyên đơn hàng' };
  const outcomes = {
    BuyerSilent: 'Đã đóng do người mua không phản hồi', SellerWins: 'Đã kết thúc, giữ nguyên đơn hàng',
    BuyerWins: 'Đã quyết định hoàn tiền cho người mua', AgreedRefund: 'Đã hoàn tiền theo thỏa thuận',
    AgreedReturn: 'Đã trả hàng và hoàn tiền', AgreedKeepOrder: 'Hai bên thống nhất giữ nguyên đơn',
    RefundCompleted: 'Đơn hàng đã được hoàn tiền'
  };
  const warning = 'Bằng chứng không thể sửa hoặc xóa sau khi gửi. Vui lòng kiểm tra trước khi xác nhận.';
  let role, onOrderChange, page = 1, previous = [], activeId = null, revision = '', busy = false, loading = false;
  let detailRequest = 0, detailLoading = false, detailTab = 'summary', paymentOpen = false, proposalOpen = false;
  const caseDate = value => value ? date(new Date(utcTime(value)).toISOString()) : 'Chưa có';

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
        <strong>${esc(statusText(c.status, c.outcome))}</strong><small>${esc(c.description === 'Khoản giữ tiền từ trước khi nâng cấp quy trình tranh chấp.' ? 'Chưa có nội dung yêu cầu.' : c.description)}</small>
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

  function caseHero(c, order, actorRole) {
    const buyer = actorRole === 'buyer', seller = actorRole === 'seller';
    const recipient = buyer ? 'bạn' : 'người mua';
    const paid = order.payments.findLast(p => p.status === 'Succeeded');
    const refunds = order.refunds.filter(r => r.status === 'Succeeded');
    const amount = refunds.reduce((sum, r) => sum + Number(r.amount || 0), 0);
    const completed = refunds.map(r => r.completedAt).filter(Boolean).sort((a,b) => utcTime(a)-utcTime(b)).at(-1);
    let title, description, next, tone = 'waiting';
    if (c.status === 'AwaitingSeller') {
      title = seller ? 'Bạn cần gửi phương án giải quyết' : 'Chờ người bán phản hồi';
      description = seller ? 'Hãy xem yêu cầu và bằng chứng của người mua trước khi đưa ra phương án.' : 'Người bán đang xem yêu cầu và sẽ gửi phương án giải quyết.';
      next = seller ? 'Gửi phương án trước thời hạn bên dưới. Chỉ gửi thêm bằng chứng sẽ không thay thế việc phản hồi.' : 'Bạn có thể bổ sung bằng chứng trong mục Trao đổi. Nếu người bán không phản hồi đúng hạn, admin sẽ xử lý.';
    } else if (c.status === 'AwaitingBuyer') {
      title = buyer ? 'Bạn cần phản hồi phương án' : 'Chờ người mua phản hồi';
      description = `Người bán đề nghị: ${proposals[c.proposal] || 'Chưa có phương án'}.`;
      next = buyer ? 'Hãy đồng ý hoặc nhờ admin xử lý trước thời hạn bên dưới. Không phản hồi sẽ khiến yêu cầu đóng mà chưa thực hiện phương án.' : 'Người mua đang xem phương án. Bạn có thể bổ sung bằng chứng trong mục Trao đổi.';
    } else if (c.status === 'Escalated') {
      title = actorRole === 'admin' ? 'Cần bạn xem xét và quyết định' : 'Chờ admin giải quyết';
      description = 'Admin sẽ xem yêu cầu, phản hồi và bằng chứng của hai bên.';
      next = actorRole === 'admin' ? 'Xem mục Trao đổi trước khi đưa ra quyết định.' : 'Bạn có thể bổ sung bằng chứng trong mục Trao đổi. Tiền đang được giữ trong lúc chờ quyết định.';
    } else if (c.status === 'ExecutingAgreement' && c.outcome === 'AgreedReturn' && !amount &&
      !['ReceivedBySeller','RefundPending','RefundFailed'].includes(order.returnRequest?.status)) {
      title = 'Đang trả hàng để hoàn tiền';
      description = order.shipments.some(s => s.direction === 'Return' && s.status === 'Delivered')
        ? 'Hàng trả đã giao tới người bán; đang chờ xác nhận nhận hàng.' : 'Hai bên đã đồng ý trả hàng rồi hoàn tiền.';
      next = buyer ? 'Theo dõi vận đơn trả hàng trong mục Đơn hàng. Tiền sẽ được hoàn sau khi xác nhận nhận hàng.' : 'Xem chi tiết đơn hàng để theo dõi và xác nhận hàng trả.';
    } else if (amount > 0) {
      title = `Đã hoàn ${money(amount)} cho ${recipient}`;
      description = `Phương thức: ${label(paid?.method)}${completed ? ' · '+caseDate(completed) : ''}`;
      next = c.isOpen ? 'Hoàn tiền đã thành công; hệ thống đang cập nhật kết quả tranh chấp.' : buyer ? 'Bạn không cần làm gì thêm. Có thể kiểm tra giao dịch hoàn tiền theo phương thức thanh toán ban đầu.' : 'Bạn không cần thao tác thêm cho tranh chấp này.';
      tone = 'success';
    } else if (c.status === 'ExecutingAgreement') {
      title = order.refunds.some(r => r.status === 'Failed') ? 'Hoàn tiền chưa thành công' : 'Đang xử lý hoàn tiền';
      description = `Hoàn tiền cho ${recipient} theo phương thức thanh toán ban đầu.`;
      next = 'Hệ thống đang xử lý và sẽ thử lại nếu gặp lỗi. Bạn không cần gửi lại yêu cầu.';
    } else if (c.outcome === 'BuyerWins' || ['AgreedRefund','AgreedReturn','RefundCompleted'].includes(c.outcome)) {
      title = 'Đã quyết định hoàn tiền'; description = 'Chưa có xác nhận hoàn tiền trong dữ liệu giao dịch.';
      next = 'Kiểm tra mục Tiền thanh toán và hoàn tiền bên dưới để theo dõi.';
    } else {
      tone = 'neutral';
      title = c.outcome === 'BuyerSilent' ? 'Yêu cầu đã đóng vì người mua không phản hồi' : c.outcome === 'SellerWins' ? 'Quyết định giữ nguyên đơn hàng' : 'Hai bên thống nhất giữ nguyên đơn hàng';
      description = 'Không thực hiện hoàn tiền theo yêu cầu này.';
      next = 'Đọc kết quả trong mục Tóm tắt; chi tiết trao đổi nằm ở mục Trao đổi.';
      if (!['BuyerSilent','SellerWins','AgreedKeepOrder'].includes(c.outcome)) {
        title = 'Tranh chấp đã kết thúc'; description = 'Xem kết quả và thông tin giao dịch ở bên dưới.';
      }
    }
    return {title, description, next, tone};
  }

  function renderDetail(data, order, actorRole = role, selectedTab = 'summary', expandedPayment = false, expandedProposal = false) {
    const c = data.case;
    const hero = caseHero(c, order, actorRole);
    const canRespond = c.responseDueAt && utcTime(c.responseDueAt) > Date.now();
    const canEvidence = actorRole !== 'admin' && c.isOpen && !c.outcome;
    const buttons = [];
    if (actorRole === 'seller' && c.status === 'AwaitingSeller')
      buttons.push(`<button type="button" class="primary" data-case-action="proposal" data-case-id="${c.id}" ${canRespond ? '' : 'disabled'}>Gửi phương án giải quyết</button>`);
    if (actorRole === 'buyer' && c.status === 'AwaitingBuyer')
      buttons.push(`<button type="button" class="primary" data-case-action="accept" data-case-id="${c.id}" ${canRespond ? '' : 'disabled'}>Chấp nhận phương án</button>
        <button type="button" data-case-action="reject" data-case-id="${c.id}" ${canRespond ? '' : 'disabled'}>Không đồng ý, nhờ admin xử lý</button>`);
    if (actorRole === 'admin' && c.status === 'Escalated')
      buttons.push(`<button type="button" data-case-action="buyer-wins" data-case-id="${c.id}">Người mua thắng — hoàn tiền</button>
        <button type="button" data-case-action="seller-wins" data-case-id="${c.id}">Người bán thắng — giải phóng tiền</button>`);
    if (actorRole !== 'admin' && c.status === 'ExecutingAgreement' && c.outcome === 'AgreedReturn')
      buttons.push(`<button type="button" data-open-order="${c.orderId}">Xem đơn để theo dõi trả hàng</button>`);
    const names = { buyer: 'Người mua', seller: 'Người bán', admin: 'Admin', system: 'Hệ thống' };
    const kinds = { Evidence: 'Bằng chứng', Proposal: 'Phương án giải quyết', Response: 'Phản hồi',
      Decision: 'Quyết định', Escalated: 'Nhờ admin xử lý', Closed: 'Kết quả', ReturnIssue: 'Vấn đề hàng trả' };
    const history = data.entries.filter(e => e.kind !== 'Imported').map(e => `<li class="case-history-entry" data-case-entry="${e.id}">
      <header><strong>${esc(names[e.actorRole] || 'Hệ thống')} · ${esc(kinds[e.kind] || 'Cập nhật')}</strong><time>${esc(caseDate(e.createdAt))}</time></header>
      <p>${esc(e.description)}</p>${e.evidenceLinks.length ? `<ul class="case-evidence-links">${e.evidenceLinks.map((url, index) => {
        try { parseLinks(url); } catch { return `<li>Link bằng chứng ${index+1} không hợp lệ.</li>`; }
        return `<li><a data-case-evidence="${index}" href="${esc(url)}" target="_blank" rel="noopener noreferrer">Mở bằng chứng ${index + 1}</a></li>`;
      }).join('')}</ul>` : ''}</li>`).join('');
    const pay = order.payments.findLast(p => p.status === 'Succeeded') || order.payments.at(-1);
    const refunded = order.refunds.filter(r => r.status === 'Succeeded').reduce((sum,r) => sum+Number(r.amount||0),0);
    const imported = data.entries.some(e => e.kind === 'Imported') && c.description === 'Khoản giữ tiền từ trước khi nâng cấp quy trình tranh chấp.';
    const proposalCopy = { Refund: `Hoàn toàn bộ ${money(c.totalPrice)}; không yêu cầu trả hàng.`,
      ReturnRefund: 'Người mua gửi trả hàng; hoàn tiền sau khi xác nhận nhận hàng.', KeepOrder: 'Giữ nguyên đơn hàng, không hoàn tiền.' };
    const proposalEntry = data.entries.findLast(e => e.kind === 'Proposal');
    const payment = `<details class="case-payment" ${expandedPayment?'open':''}><summary>Tiền thanh toán và hoàn tiền</summary><dl class="case-ledger">
      <div><dt>Thanh toán ban đầu</dt><dd>${money(pay?.amount)} · ${esc(label(pay?.status))}</dd></div>
      <div><dt>Phương thức</dt><dd>${esc(label(pay?.method))}</dd></div>
      <div><dt>Đã hoàn thành công</dt><dd>${money(refunded)}</dd></div>
      <div><dt>Tiền đang giữ</dt><dd>${money(c.heldAmount)}</dd></div></dl>
      ${order.refunds.length?`<ul class="case-refunds">${order.refunds.map(r=>`<li>${esc(label(r.status))}${r.status==='Succeeded'?' · '+money(r.amount):''}${r.completedAt?' · '+esc(caseDate(r.completedAt)):''}</li>`).join('')}</ul>`:'<p class="small">Chưa có giao dịch hoàn tiền.</p>'}</details>`;
    const tabs = [['summary','Tóm tắt'],['history','Trao đổi'],['order','Đơn hàng']];
    const parties = actorRole === 'buyer' ? `Bạn mua từ ${data.sellerName || 'Người bán'}` : actorRole === 'seller'
      ? `Người mua: ${data.buyerName || 'Chưa có tên'}` : `Người mua: ${data.buyerName || 'Chưa có tên'} · Người bán: ${data.sellerName || 'Chưa có tên'}`;
    return `<div class="detail case-detail" data-dispute-detail="${c.id}">
      <h2>Tranh chấp #${c.id} · Đơn #${c.orderId}</h2><p class="case-parties">${esc(parties)} · Mở: ${esc(caseDate(c.createdAt))}</p>
      <section class="case-hero case-hero-${hero.tone}" aria-label="Trạng thái và việc cần làm">
        <span class="case-hero-status">${c.isOpen?'Đang xử lý tranh chấp':'Tranh chấp đã kết thúc'}</span>
        <h3>${esc(hero.title)}</h3><p>${esc(hero.description)}</p><p class="case-next">${esc(hero.next)}</p>${deadlineHtml(c)}
        ${buttons.length?`<div class="actions">${buttons.join('')}</div>`:''}</section>
      <div class="case-tabs" role="tablist" aria-label="Nội dung tranh chấp">${tabs.map(([key,text])=>`<button type="button" id="case-${c.id}-tab-${key}" role="tab" data-case-tab="${key}" aria-controls="case-${c.id}-panel-${key}" aria-selected="${selectedTab===key}" tabindex="${selectedTab===key?0:-1}">${text}</button>`).join('')}</div>
      <section class="case-tab-panel" id="case-${c.id}-panel-summary" role="tabpanel" tabindex="0" aria-labelledby="case-${c.id}-tab-summary" ${selectedTab==='summary'?'':'hidden'}>
        <h3>${imported?'Nội dung yêu cầu':'Lý do yêu cầu giải quyết'}</h3><p class="case-description">${imported?'Chưa có nội dung yêu cầu.':esc(c.description)}</p>
        ${c.resolution?`<section class="case-decision"><h3>${data.entries.some(e=>e.kind==='Decision')?'Quyết định của admin':'Kết quả giải quyết'}</h3><p>${esc(c.resolution)}</p></section>`:''}
        ${c.proposal?`<${c.status==='AwaitingBuyer'?'section':'details'} class="case-proposal-summary" ${expandedProposal&&c.status!=='AwaitingBuyer'?'open':''}>${c.status==='AwaitingBuyer'?'<h3>Phương án người bán đề nghị</h3>':'<summary>Phương án người bán đã gửi</summary>'}<p>${esc(proposalCopy[c.proposal] || proposals[c.proposal] || c.proposal)}</p>${proposalEntry?`<p class="case-description">${esc(proposalEntry.description)}</p>`:''}</${c.status==='AwaitingBuyer'?'section':'details'}>`:''}${payment}
      </section>
      <section class="case-tab-panel" id="case-${c.id}-panel-history" role="tabpanel" tabindex="0" aria-labelledby="case-${c.id}-tab-history" ${selectedTab==='history'?'':'hidden'}>
        <h3>Trao đổi và bằng chứng</h3>${canEvidence?`<div class="case-evidence-action"><p class="small">${warning}</p><button type="button" data-case-action="evidence" data-case-id="${c.id}">Bổ sung bằng chứng</button></div>`:''}
        ${history?`<ol class="case-history-timeline">${history}</ol>`:'<p class="empty-note">Chưa có trao đổi hoặc bằng chứng để hiển thị.</p>'}
      </section>
      <section class="case-tab-panel" id="case-${c.id}-panel-order" role="tabpanel" tabindex="0" aria-labelledby="case-${c.id}-tab-order" ${selectedTab==='order'?'':'hidden'}>
        <h3>Sản phẩm · Đơn #${c.orderId}</h3>${order.items.map(x=>`<div class="item">${productThumbnail(x.imageUrl,x.productTitleSnapshot)}<div class="item-info"><strong>${esc(x.productTitleSnapshot)}</strong><small>Số lượng: ${x.quantity}</small></div><b>${money(x.unitPrice*x.quantity)}</b></div>`).join('')}
        <p class="case-order-total">Tổng đơn: <strong>${money(c.totalPrice)}</strong></p><h3>Địa chỉ nhận hàng</h3><p class="case-description">${esc(order.addressSnapshot)}</p><h3>Tiến trình vận chuyển</h3>${trackingHtml(order)}</section></div>`;
  }

  async function open(id, preserve = false) {
    if (preserve && detailLoading) return;
    const request = ++detailRequest;
    detailLoading = true;
    try {
      const data = await api(`disputes/${id}`);
      const order = await api(`orders/${data.case.orderId}`);
      if (request !== detailRequest || preserve && (!$('detail-dialog').open || !document.querySelector(`[data-dispute-detail="${id}"]`))) return;
      const nextRevision = JSON.stringify(data) + JSON.stringify(order);
      if (preserve && revision === nextRevision) return;
      if (!preserve) { detailTab = 'summary'; paymentOpen = false; proposalOpen = false; }
      else {
        paymentOpen = Boolean(document.querySelector('.case-payment')?.open);
        proposalOpen = Boolean(document.querySelector('details.case-proposal-summary')?.open);
      }
      const focused = document.activeElement;
      let focusSelector = null;
      if (preserve && focused?.closest('[data-dispute-detail]')) {
        if (focused.dataset.caseTab) focusSelector = `[data-case-tab="${focused.dataset.caseTab}"]`;
        else if (focused.dataset.caseAction) focusSelector = `[data-case-action="${focused.dataset.caseAction}"]`;
        else if (focused.dataset.openOrder) focusSelector = `[data-open-order="${focused.dataset.openOrder}"]`;
        else if (focused.matches('.case-payment summary')) focusSelector = '.case-payment summary';
        else if (focused.matches('.case-proposal-summary summary')) focusSelector = '.case-proposal-summary summary';
        else if (focused.matches('.case-tab-panel')) focusSelector = `#${focused.id}`;
        else if (focused.dataset.caseEvidence !== undefined) focusSelector = `[data-case-entry="${focused.closest('[data-case-entry]').dataset.caseEntry}"] [data-case-evidence="${focused.dataset.caseEvidence}"]`;
      }
      activeId = id;
      revision = nextRevision;
      openDetail(renderDetail(data, order, role, detailTab, paymentOpen, proposalOpen), preserve);
      if (focusSelector) (document.querySelector(focusSelector) || document.querySelector('[data-case-tab][aria-selected="true"]'))?.focus({preventScroll:true});
    } finally { if (request === detailRequest) detailLoading = false; }
  }

  function selectTab(button, focus = false) {
    const detail = button.closest('[data-dispute-detail]');
    detailTab = button.dataset.caseTab;
    detail.querySelectorAll('[data-case-tab]').forEach(tab => {
      const selected = tab === button;
      tab.setAttribute('aria-selected',String(selected)); tab.tabIndex = selected ? 0 : -1;
      document.getElementById(tab.getAttribute('aria-controls')).hidden = !selected;
    });
    if (focus) button.focus();
    G4.layoutTimeline && detail.querySelectorAll('.timeline').forEach(G4.layoutTimeline);
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
      const tab = event.target.closest('[data-case-tab]');
      if (tab) return selectTab(tab);
      const openButton = event.target.closest('[data-open-case]');
      if (openButton) return safe(() => open(Number(openButton.dataset.openCase)));
      const actionButton = event.target.closest('[data-case-action]');
      if (actionButton && !busy) return safe(() => action(actionButton));
    });
    document.body.addEventListener('keydown', event => {
      const tab = event.target.closest('[data-case-tab]');
      if (!tab || !['ArrowLeft','ArrowRight','Home','End'].includes(event.key)) return;
      event.preventDefault();
      const tabs = [...tab.parentElement.querySelectorAll('[data-case-tab]')], index = tabs.indexOf(tab);
      selectTab(tabs[event.key==='Home'?0:event.key==='End'?tabs.length-1:(index+(event.key==='ArrowRight'?1:-1)+tabs.length)%tabs.length],true);
    });
    safe(() => load());
    setInterval(updateCountdowns, 1000);
    setInterval(() => safe(async () => {
      if (busy || loading || document.hidden || $('action-dialog').open || document.activeElement?.closest('.pagination')) return;
      await load(page, true);
      if (activeId && $('detail-dialog').open && document.querySelector(`[data-dispute-detail="${activeId}"]`)) await open(activeId, true);
    }), 4000);
  }

  G4.Disputes = { init, load, open, openNew, statusText, parseLinks, countdown, renderDetail };
})();
