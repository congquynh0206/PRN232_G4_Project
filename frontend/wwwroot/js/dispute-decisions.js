(() => {
  const { $, api, esc, money, message } = G4;
  let id, quote, previewInput, sequence = 0, saving = false, refresh;
  const drafts = new Map();
  function error(text) {
    $('decision-error').textContent = text;
    $('decision-error').hidden = !text;
    if (text) $('decision-error').focus();
  }
  function input() {
    const outcome = $('decision-outcome').value;
    const amount = $('decision-amount').value;
    if (outcome === 'PartialRefund' && (!amount || !Number.isFinite(Number(amount)) || Number(amount) <= 0))
      throw new Error('Nhập khoản buyer thực nhận lớn hơn 0 rồi xem dự kiến.');
    return { outcome, responsibility: $('decision-responsibility').value, refundAmount: outcome === 'PartialRefund' ? Number(amount) : null };
  }
  function invalidate() {
    sequence++; quote = null; previewInput = null;
    $('decision-confirm').disabled = true;
    const outcome = $('decision-outcome').value;
    $('decision-amount-row').hidden = outcome !== 'PartialRefund';
    $('decision-amount').required = outcome === 'PartialRefund';
    $('decision-amount').disabled = outcome !== 'PartialRefund';
    if (outcome === 'SellerWins') $('decision-responsibility').value = 'Buyer';
    $('decision-responsibility').disabled = outcome === 'SellerWins';
    $('decision-quote').setAttribute('aria-busy', 'false');
    $('decision-quote').innerHTML = '<p class="small">Xem dự kiến tài chính sau khi chọn kết luận và khoản hoàn.</p>';
  }
  function quoteHtml(q) {
    const rows = [
      ['Khoản buyer thực nhận', q.buyerRefund], ['Mức hoàn tối đa theo kết luận', q.maximumRefund],
      ['Ship chiều đi không hoàn', q.outboundShippingRetained], ['Chi phí trả hàng đã ghi nhận', q.returnShippingCost],
      ['Phí trả trừ vào khoản hoàn', q.buyerShippingRecovery], ['Tài trợ coupon được đảo riêng', q.subsidyReversal],
      ['Phí nền tảng được hoàn cho seller', q.feeCredit],
      [q.sellerNetDebit < 0 ? 'Seller được cộng thêm' : 'Seller bị trừ thêm khi refund thành công', Math.abs(q.sellerNetDebit)],
    ];
    return `<section class="decision-quote"><h3>Dự kiến theo phán quyết</h3><p>Trách nhiệm: <strong>${q.responsibility === 'Seller' ? 'Người bán' : 'Người mua'}</strong>. ${q.outcome === 'SellerWins' ? 'Không thực hiện refund hoặc thu thêm phí từ buyer.' : q.shippingPayer ? `Người ${q.shippingPayer === 'Seller' ? 'bán' : 'mua'} chịu phí trả hàng.` : 'Không phát sinh phí trả hàng.'}</p><dl>${rows.map(([name, amount]) => `<div><dt>${esc(name)}</dt><dd>${money(amount)}</dd></div>`).join('')}</dl><p class="small">Chi phí trả hàng ở trên đã được ứng trước, không thu lần nữa. Coupon không cộng vào tiền mặt buyer nhận. Số dư thiếu sẽ thành nợ âm; phần tiền giữ còn lại tiếp tục theo lịch của đơn.</p></section>`;
  }
  async function preview() {
    if (saving) return;
    const currentSequence = ++sequence;
    quote = null; $('decision-confirm').disabled = true; error('');
    try {
      const current = input();
      $('decision-quote').setAttribute('aria-busy', 'true');
      $('decision-quote').innerHTML = '<p>Đang tính dự kiến…</p>';
      const result = await api(`disputes/${id}/decision-preview`, 'POST', current);
      if (currentSequence !== sequence || !$('decision-dialog').open) return;
      quote = result; previewInput = JSON.stringify(current);
      $('decision-quote').innerHTML = quoteHtml(result);
      $('decision-confirm').disabled = false;
    } catch (e) { if (currentSequence === sequence) error(e.message); }
    finally { if (currentSequence === sequence) $('decision-quote').setAttribute('aria-busy', 'false'); }
  }
  async function open(caseId, onSaved) {
    if (saving) return;
    id = caseId; refresh = onSaved;
    const data = await api(`disputes/${id}`);
    if (data.case.status !== 'Escalated') throw new Error('Hồ sơ đã thay đổi. Hãy xem lại kết quả.');
    $('decision-context').textContent = `Tranh chấp #${id} · Đơn #${data.case.orderId}`;
    const evidence = data.entries.filter(e => !['Imported', 'Decision', 'Closed'].includes(e.kind));
    $('decision-evidence').innerHTML = evidence.map(e => {
      const links = (e.evidenceLinks || []).map((url, index) => {
        try { G4.Disputes.parseLinks(url); } catch { return ''; }
        return `<a href="${esc(url)}" target="_blank" rel="noopener noreferrer">Mở bằng chứng ${index + 1}</a>`;
      }).filter(Boolean).join(' · ');
      return `<label><input type="checkbox" class="decision-evidence-check" value="${e.id}" /><span>#${e.id} · ${e.actorRole === 'buyer' ? 'Người mua' : e.actorRole === 'seller' ? 'Người bán' : 'Hệ thống'}: ${esc(e.description)}${links ? `<br />${links}` : ''}</span></label>`;
    }).join('') || '<p>Hồ sơ chưa có căn cứ. Cần bổ sung bằng chứng trước khi quyết định.</p>';
    $('decision-outcome').value = 'FullRefund'; $('decision-responsibility').value = 'Seller';
    $('decision-amount').value = ''; $('decision-reason').value = '';
    $('decision-fields').disabled = false; $('decision-close').disabled = false;
    $('decision-dialog').showModal(); invalidate(); error(''); $('decision-outcome').focus();
  }
  function close() { if (!saving) { sequence++; $('decision-dialog').close(); } }
  function init() {
    if (!$('decision-dialog')) return;
    $('decision-outcome').onchange = invalidate;
    $('decision-responsibility').onchange = invalidate;
    $('decision-amount').oninput = invalidate;
    $('decision-preview').onclick = preview;
    $('decision-close').onclick = close;
    $('decision-dialog').oncancel = e => { e.preventDefault(); close(); };
    $('decision-form').onsubmit = async e => {
      e.preventDefault();
      if (saving || !quote) return;
      let body;
      try {
        const current = input();
        if (JSON.stringify(current) !== previewInput) throw new Error('Xem lại dự kiến tài chính trước khi xác nhận.');
        const evidenceEntryIds = [...document.querySelectorAll('.decision-evidence-check:checked')].map(x => Number(x.value));
        if (!evidenceEntryIds.length) throw new Error('Chọn ít nhất một căn cứ trong hồ sơ.');
        const reason = $('decision-reason').value.trim();
        if (!reason || reason.length > 4000) throw new Error('Nhập lý do và nhận định từ bằng chứng, tối đa 4.000 ký tự.');
        const payload = { ...current, evidenceEntryIds, reason, pricingFingerprint: quote.pricingFingerprint };
        const fingerprint = JSON.stringify(payload), previous = drafts.get(id);
        const key = previous?.fingerprint === fingerprint ? previous.key : crypto.randomUUID();
        drafts.set(id, { fingerprint, key }); body = { ...payload, key };
      } catch (e) { error(e.message); return; }
      saving = true; $('decision-fields').disabled = true; $('decision-close').disabled = true;
      $('decision-confirm').disabled = true; $('decision-confirm').textContent = 'Đang lưu…'; error('');
      try {
        await api(`disputes/${id}/resolution`, 'POST', body);
        drafts.delete(id); $('decision-dialog').close(); sequence++;
        message('Đã lưu phán quyết. Khoản hoàn được xử lý theo quyết định đã xác nhận.', true);
        await refresh?.();
      } catch (e) { error(e.message); }
      finally {
        saving = false; $('decision-fields').disabled = false; $('decision-close').disabled = false;
        $('decision-confirm').disabled = !quote; $('decision-confirm').textContent = 'Xác nhận và lưu phán quyết';
      }
    };
  }
  G4.DisputeDecisions = { init, open, quoteHtml };
})();
