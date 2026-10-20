(() => {
  const { $, api, esc, money, message } = G4;
  let orderId, quote = null, quoteSequence = 0, saving = false, loading = false, refresh;
  const drafts = new Map();
  const utc = value => typeof value === 'string' && !/(Z|[+-]\d{2}:\d{2})$/i.test(value) ? value + 'Z' : value;
  const windowOpen = order => !order.returnWindowClosesAt || Date.now() < new Date(utc(order.returnWindowClosesAt)).getTime();
  function windowHtml(order) {
    if (!order.returnWindowClosesAt) return '';
    const deadline = utc(order.returnWindowClosesAt);
    return `<p class="return-window" role="status">Hạn mở yêu cầu trả hàng: <time datetime="${esc(deadline)}">${esc(G4.date?.(deadline) || deadline)}</time>. ${windowOpen(order) ? 'Gửi yêu cầu trước hạn này.' : 'Đã hết hạn mở yêu cầu trả hàng mới.'}${order.returnRequest ? ' Yêu cầu đã gửi đúng hạn tiếp tục được xử lý; hạn kiểm tra hàng trả là riêng.' : ''}</p>`;
  }
  function expireQuote() {
    if (!quote?.returnWindowClosesAt || windowOpen(quote) || saving || quote.windowExpired) return false;
    quote = { ...quote, canRequest: false, windowExpired: true, blockReason: 'Đã hết hạn mở yêu cầu trả hàng mới. Yêu cầu đã gửi đúng hạn vẫn được xử lý.' };
    $('return-quote').innerHTML = quoteHtml(quote);
    $('return-confirm').disabled = true;
    error(quote.blockReason);
    return true;
  }
  function quoteHtml(q) {
    const rows = [
      ['Tiền hàng sau giảm giá', q.goodsPaid],
      ['Phí ship chiều đi không hoàn', q.outboundShippingRetained],
      [q.shippingPayer === 'Buyer' ? 'Phí trả hàng trừ vào khoản hoàn' : 'Phí trả hàng do người bán chịu', q.returnShippingFee],
    ];
    return `<section class="return-price"><h3>Khoản hoàn dự kiến</h3>${windowHtml(q)}${rows.map(([name, amount]) => `<p><span>${esc(name)}</span><strong>${money(amount)}</strong></p>`).join('')}<p class="return-total">${q.canRequest ? `<span>Bạn được hoàn</span><strong>${money(q.buyerRefund)}</strong>` : q.windowExpired ? 'Đã hết hạn mở yêu cầu trả hàng mới.' : 'Phí trả hàng vượt số tiền có thể hoàn.'}</p>${q.alreadyRefunded > 0 ? `<small>Đã hoàn chênh lệch ship ${money(q.alreadyRefunded)} trước đó; không hoàn lại khoản này.</small>` : ''}<small>${q.shippingPayer === 'Buyer' ? 'Tiền hàng được hoàn sau khi trừ phí chiều về; phí ship chiều đi không hoàn.' : 'Hoàn số tiền bạn đã trả còn lại, gồm phí ship chiều đi; bạn không chịu phí trả hàng.'}</small>${q.platformSubsidyReversal > 0 ? '<small>Coupon do nền tảng tài trợ được điều chỉnh riêng, không cộng thêm vào tiền hoàn cho bạn.</small>' : ''}${!q.canRequest ? `<p role="alert">${esc(q.blockReason)}</p>` : ''}</section>`;
  }
  function savedHtml(order) {
    const r = order.returnRequest;
    if (!r || r.pricingPolicyVersion !== 1) return '';
    const completed = (order.refunds || []).find(x => x.returnRequestId === r.id && x.status === 'Succeeded');
    return `<div class="return-price"><p>Phí trả hàng: <strong>${money(r.returnShippingFee)}</strong> · ${r.shippingPayer === 'Buyer' ? 'Người mua chịu, trừ vào khoản hoàn' : 'Người bán chịu'}</p><p>Phí ship chiều đi không hoàn theo yêu cầu: <strong>${money(r.outboundShippingRetained)}</strong></p><p>Khoản hoàn theo báo giá đã xác nhận: <strong>${money(r.buyerRefundAmount)}</strong></p>${completed ? `<p>Đã hoàn thành công: <strong>${money(completed.amount)}</strong></p>` : ''}${r.description ? `<p>Mô tả: ${esc(r.description)}</p>` : ''}</div>`;
  }
  function error(text) {
    $('return-error').textContent = text;
    $('return-error').hidden = !text;
    if (text) $('return-error').focus();
  }
  async function requote() {
    const sequence = ++quoteSequence, id = orderId, reason = $('return-reason').value;
    quote = null; loading = true;
    $('return-confirm').disabled = true;
    $('return-quote').setAttribute('aria-busy', 'true');
    $('return-quote').innerHTML = '<p>Đang tính khoản hoàn…</p>';
    error('');
    try {
      const result = await api(`orders/${id}/returns/quote?reasonCode=${encodeURIComponent(reason)}`);
      if (sequence !== quoteSequence || !$('return-dialog').open) return;
      quote = result;
      $('return-quote').innerHTML = quoteHtml(result);
    } catch (e) { if (sequence === quoteSequence && $('return-dialog').open) error(e.message); }
    finally {
      if (sequence === quoteSequence) {
        loading = false;
        $('return-quote').setAttribute('aria-busy', 'false');
        $('return-confirm').disabled = saving || !quote?.canRequest;
      }
    }
  }
  async function open(id, onSaved) {
    if (saving) return;
    orderId = id; refresh = onSaved; quote = null;
    const draft = drafts.get(id);
    $('return-reason').value = draft?.reasonCode || 'WrongItem';
    $('return-description').value = draft?.description || '';
    $('return-context').textContent = `Đơn #${id}. Kiểm tra khoản hoàn và bên chịu phí trước khi gửi yêu cầu.`;
    $('return-fields').disabled = false;
    $('return-close').disabled = false;
    $('return-dialog').showModal();
    $('return-reason').focus();
    await requote();
  }
  function close() {
    if (saving) return;
    const draft = drafts.get(orderId) || {};
    drafts.set(orderId, { ...draft, reasonCode: $('return-reason').value, description: $('return-description').value });
    quoteSequence++;
    $('return-dialog').close();
  }
  function init() {
    if (!$('return-dialog')) return;
    $('return-reason').onchange = requote;
    $('return-requote').onclick = requote;
    $('return-close').onclick = close;
    $('return-dialog').oncancel = (event) => { event.preventDefault(); close(); };
    $('return-form').onsubmit = async (event) => {
      event.preventDefault();
      if (expireQuote()) return;
      if (saving || loading || !quote?.canRequest || quote.reasonCode !== $('return-reason').value) return;
      const input = { reasonCode: quote.reasonCode, description: $('return-description').value.trim(), pricingFingerprint: quote.pricingFingerprint };
      const fingerprint = JSON.stringify(input), previous = drafts.get(orderId);
      const key = previous?.fingerprint === fingerprint ? previous.key : crypto.randomUUID();
      drafts.set(orderId, { ...input, fingerprint, key });
      saving = true;
      $('return-fields').disabled = true; $('return-close').disabled = true; $('return-confirm').disabled = true;
      $('return-confirm').textContent = 'Đang gửi…'; error('');
      try {
        await api(`orders/${orderId}/returns`, 'POST', { ...input, key });
        drafts.delete(orderId);
        $('return-dialog').close(); quoteSequence++;
        message('Đã gửi yêu cầu trả hàng với khoản hoàn đã xác nhận.', true);
        await refresh?.();
      } catch (e) { error(e.message); }
      finally {
        saving = false;
        $('return-fields').disabled = false; $('return-close').disabled = false;
        $('return-confirm').disabled = !quote?.canRequest;
        $('return-confirm').textContent = 'Xác nhận khoản hoàn và gửi';
      }
    };
  }
  globalThis.setInterval?.(() => {
    if (quote && $('return-dialog')?.open) expireQuote();
  }, 1000);
  G4.Returns = { init, open, quoteHtml, savedHtml, windowHtml, windowOpen };
})();
