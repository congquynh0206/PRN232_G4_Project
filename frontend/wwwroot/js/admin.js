(() => {
  const { $, money, esc, api, message, safe } = G4;

  function tab(id) {
    document.querySelectorAll('.tab-panel').forEach(x => x.classList.toggle('active', x.id === id));
    document.querySelectorAll('.tabs button').forEach(x => x.classList.toggle('active', x.dataset.tab === id));
    safe(id === 'holds' ? loadHolds : loadInbox);
  }

  async function loadHolds() {
    const holds = await api('admin/fund-holds');
    $('hold-list').innerHTML = holds.length
      ? holds.map(x => {
          const actions = x.status === 'OnHold'
            ? `<div class="actions"><button data-resolve="seller" data-id="${x.orderId}">Seller thắng</button><button data-resolve="buyer" data-id="${x.orderId}">Buyer thắng</button></div>`
            : '<span class="status-note">Đang chờ hoàn tiền cho buyer</span>';
          return `<div class="order-card"><div><strong>Đơn #${x.orderId} · ${esc(x.status)}</strong><small>Seller #${x.sellerId} · ${money(x.amount)} · ${esc(x.reason || 'Không có lý do')}</small></div>${actions}</div>`;
        }).join('')
      : '<p class="empty-note">Không có khoản tiền đang giữ.</p>';
  }

  async function loadInbox() {
    const mails = await api('inbox');
    $('email-list').innerHTML = mails.length
      ? mails.map(x => `<div class="order-card"><div><strong>${esc(x.subject)} · ${esc(x.status)}</strong><small>Đơn #${x.orderId} → ${esc(x.recipient)} · ${new Date(x.createdAt).toLocaleString('vi-VN')}</small><p>${esc(x.body)}</p></div></div>`).join('')
      : '<p class="empty-note">Chưa có email.</p>';
  }

  document.querySelectorAll('.tabs button').forEach(button => {
    button.onclick = () => tab(button.dataset.tab);
  });
  $('refresh-holds').onclick = () => safe(loadHolds);
  $('refresh-inbox').onclick = () => safe(loadInbox);
  document.body.addEventListener('click', event => {
    const button = event.target.closest('[data-resolve]');
    if (!button) return;
    safe(async () => {
      await api(`orders/${button.dataset.id}/fund-hold/resolve`, 'POST', {
        releaseToSeller: button.dataset.resolve === 'seller'
      });
      message('Đã lưu quyết định tranh chấp.', true);
      await loadHolds();
    });
  });
  safe(loadHolds);
})();
