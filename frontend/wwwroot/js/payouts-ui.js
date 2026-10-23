(() => {
  const { esc, money, api } = G4;
  const labels = {
    Created: 'Đã tạo yêu cầu',
    InProgress: 'Đang xử lý',
    FundsSent: 'Đã gửi ngân hàng, chờ xác nhận',
    Completed: 'Hoàn tất',
    Returned: 'Ngân hàng trả lại',
  };
  let draft = null,
    sending = false;
  const stamp = (value) => {
    const utc =
      typeof value === 'string' && !/(Z|[+-]\d{2}:\d{2})$/i.test(value) ? value + 'Z' : value;
    return G4.date(utc);
  };
  function card(p) {
    const history = (p.history || [])
      .map(
        (e) =>
          `<li><strong>${esc(labels[e.toStatus] || e.toStatus)}</strong><time>${esc(stamp(e.occurredAt))}</time><span>${e.actorRole === 'seller' ? `Seller #${esc(e.actorUserId)}` : 'Hệ thống'}</span>${e.bankReferenceId ? `<small>${esc(e.bankReferenceId)}</small>` : ''}</li>`,
      )
      .join('');
    return `<article class="finance-row payout-row"><div><strong>Lần rút tiền #${p.id} · ${esc(labels[p.status] || p.status)}</strong><small>${esc(stamp(p.createdAt))} · ${esc(p.destinationMasked || 'Ngân hàng giả lập')}${p.bankReferenceId ? ` · ${esc(p.bankReferenceId)}` : ''}</small>${p.status === 'FundsSent' ? '<p>Khoản tiền đang chuyển; ngân hàng chưa xác nhận hoàn tất.</p>' : p.status === 'Returned' ? '<p>Tiền được trả về hệ thống và bù nợ nếu có; phần còn lại vào ví khả dụng.</p>' : ''}<details><summary>Lịch sử chuyển trạng thái</summary>${p.workflowVersion !== 1 ? '<p>Dữ liệu cũ chưa ghi nhận đầy đủ các bước trước nâng cấp.</p>' : ''}${history ? `<ol class="payout-history">${history}</ol>` : '<p>Chưa ghi nhận lịch sử từng bước.</p>'}</details></div><b>${money(p.amount)}</b></article>`;
  }
  async function request(amount, simulateFailure) {
    if (sending) return null;
    if (!Number.isFinite(amount) || amount <= 0 || Math.round(amount * 100) / 100 !== amount)
      throw new Error('Nhập số tiền lớn hơn 0, tối đa hai chữ số thập phân.');
    if (draft && (draft.amount !== amount || draft.simulateFailure !== simulateFailure))
      throw new Error(
        'Kết quả yêu cầu trước chưa rõ. Giữ số tiền và lựa chọn cũ để thử lại, rồi kiểm tra lịch sử rút tiền.',
      );
    draft ||= { amount, simulateFailure, key: crypto.randomUUID() };
    sending = true;
    try {
      const result = await api('seller/payouts', 'POST', draft);
      draft = null;
      return result;
    } catch (error) {
      if (error.status >= 400 && error.status < 500) draft = null;
      throw error;
    } finally {
      sending = false;
    }
  }
  G4.Payouts = { card, request };
})();
