(() => {
  const { $, date, esc, label, api, safe, openDetail } = G4;
  let emailPage = 1, currentMails = [];
  const mailText = {
    PaymentSucceeded: id => ['Thanh toán thành công', `Đơn hàng #${id} đã được thanh toán thành công.`],
    Delivered: id => ['Đơn hàng đã được giao', `Đơn hàng #${id} đã được giao thành công.`],
    DeliveryFailed: id => ['Giao hàng chưa thành công', `Đơn hàng #${id} chưa giao thành công. Vui lòng theo dõi các bước tiếp theo.`],
    Refunded: id => ['Hoàn tiền thành công', `Đơn hàng #${id} đã được hoàn tiền.`]
  };

  function tab(id) {
    document.querySelectorAll('.tab-panel').forEach(x => x.classList.toggle('active', x.id === id));
    document.querySelectorAll('.tabs button').forEach(x => x.classList.toggle('active', x.dataset.tab === id));
    safe(id === 'disputes' ? () => G4.Disputes.load() : loadInbox);
  }

  async function loadInbox(next = emailPage) {
    const result = await api(`inbox/page?page=${next}&pageSize=10`);
    emailPage = result.page;
    currentMails = result.items;
    $('email-list').innerHTML = result.items.length
      ? result.items.map(x => {
          const copy = mailText[x.eventType]?.(x.orderId) || [x.subject, x.body];
          return `<article class="email-card">
            <div class="email-title"><strong>${esc(copy[0])}</strong><span class="status-pill">${esc(label(x.status))}</span></div>
            <p class="email-meta">Từ: Hệ thống G4 · Đến: ${esc(x.recipient)} · ${date(x.createdAt)}</p>
            <button type="button" data-open-mail="${x.id}">Xem chi tiết</button>
          </article>`;
        }).join('')
      : '<p class="empty-note">Chưa có thư điện tử nào.</p>';
    G4.pager('email-pagination', result, p => safe(() => loadInbox(p)));
  }

  document.querySelectorAll('.tabs button').forEach(button => button.onclick = () => tab(button.dataset.tab));
  $('refresh-inbox').onclick = () => safe(() => loadInbox(1));
  document.body.addEventListener('click', event => {
    const button = event.target.closest('[data-open-mail]');
    if (!button) return;
    const mail = currentMails.find(x => x.id === Number(button.dataset.openMail));
    if (!mail) return;
    const copy = mailText[mail.eventType]?.(mail.orderId) || [mail.subject, mail.body];
    openDetail(`<div class="detail email-detail">
      <h2>${esc(copy[0])}</h2><p class="small">Trạng thái: ${esc(label(mail.status))}</p>
      <dl>
        <div><dt>Người gửi</dt><dd>Hệ thống G4 (g4@example.test)</dd></div>
        <div><dt>Người nhận</dt><dd>${esc(mail.recipient)}</dd></div>
        <div><dt>Thời gian</dt><dd>${date(mail.createdAt)}</dd></div>
        <div><dt>Đơn hàng</dt><dd>#${mail.orderId}</dd></div>
      </dl><p class="email-body">${esc(copy[1])}</p>
    </div>`);
  });
  G4.Disputes.init('admin');
})();
