(() => {
  const { $, money, date, esc, label, api, message, safe, pager, patchPagedList, modal, openDetail } = G4;
  let emailPage=1, holdPage=1, currentMails=[], currentHolds=[];
  const mailText={
    PaymentSucceeded: id=>['Thanh toán thành công',`Đơn hàng #${id} đã được thanh toán thành công.`],
    Delivered: id=>['Đơn hàng đã được giao',`Đơn hàng #${id} đã được giao thành công.`],
    DeliveryFailed: id=>['Giao hàng chưa thành công',`Đơn hàng #${id} chưa giao thành công. Vui lòng theo dõi các bước tiếp theo.`],
    Refunded: id=>['Hoàn tiền thành công',`Đơn hàng #${id} đã được hoàn tiền.`]
  };

  function tab(id) {
    document.querySelectorAll('.tab-panel').forEach(x => x.classList.toggle('active', x.id === id));
    document.querySelectorAll('.tabs button').forEach(x => x.classList.toggle('active', x.dataset.tab === id));
    safe(id === 'holds' ? loadHolds : loadInbox);
  }

  function holdCard(x) {
    const actions = x.status === 'OnHold'
      ? `<button data-resolve="seller" data-id="${x.orderId}">Người bán thắng</button><button data-resolve="buyer" data-id="${x.orderId}">Người mua thắng</button>`
      : `<span class="status-note">Đang chờ hoàn tiền cho người mua</span><button data-resolve="buyer" data-id="${x.orderId}">Thử hoàn tiền lại</button>`;
    return `<div class="order-card" data-hold-row="${x.orderId}"><div><strong>Đơn #${x.orderId} · ${esc(label(x.status))}</strong><small>Người bán #${x.sellerId} · ${money(x.amount)} · ${esc(x.reason || 'Không có lý do')} · Tạo: ${date(x.createdAt)}</small></div><div class="actions"><button type="button" data-open-hold="${x.orderId}">Xem chi tiết</button>${actions}</div></div>`;
  }

  async function loadHolds(page=holdPage,changedId=null) {
    const result = await api(`admin/fund-holds/page?page=${page}&pageSize=10`);
    holdPage=result.page;
    currentHolds=result.items;
    const empty='<p class="empty-note">Không có khoản tiền đang giữ.</p>';
    if(changedId===null)$('hold-list').innerHTML=result.items.length?result.items.map(holdCard).join(''):empty;
    else patchPagedList('hold-list',result.items,x=>x.orderId,holdCard,changedId,'data-hold-row',empty);
    pager('hold-pagination',result,p=>safe(()=>loadHolds(p)));
  }

  async function loadInbox(next=emailPage) {
    emailPage=next;
    const result = await api(`inbox/page?page=${emailPage}&pageSize=10`);
    emailPage=result.page;
    currentMails=result.items;
    $('email-list').innerHTML = result.items.length
      ? result.items.map(x => {const copy=mailText[x.eventType]?.(x.orderId)||[x.subject,x.body];return `<article class="email-card"><div class="email-title"><strong>${esc(copy[0])}</strong><span class="status-pill">${esc(label(x.status))}</span></div><p class="email-meta">Từ: Hệ thống G4 · Đến: ${esc(x.recipient)} · ${date(x.createdAt)}</p><button type="button" data-open-mail="${x.id}">Xem chi tiết</button></article>`}).join('')
      : '<p class="empty-note">Chưa có thư điện tử nào.</p>';
    pager('email-pagination',result,p=>safe(()=>loadInbox(p)));
  }

  document.querySelectorAll('.tabs button').forEach(button => {
    button.onclick = () => tab(button.dataset.tab);
  });
  $('refresh-holds').onclick = () => safe(()=>loadHolds(1));
  $('refresh-inbox').onclick = () => safe(()=>loadInbox(1));
  document.body.addEventListener('click', event => {
    const holdButton=event.target.closest('[data-open-hold]');
    if(holdButton){
      const hold=currentHolds.find(x=>x.orderId===Number(holdButton.dataset.openHold));
      if(!hold)return;
      openDetail(`<div class="detail"><h2>Tranh chấp đơn hàng #${hold.orderId}</h2><div class="status-grid"><div class="status-box"><span>Trạng thái</span><strong>${esc(label(hold.status))}</strong></div><div class="status-box"><span>Số tiền đang giữ</span><strong>${money(hold.amount)}</strong></div><div class="status-box"><span>Thời gian tạo</span><strong>${date(hold.createdAt)}</strong></div></div><h3>Lý do tranh chấp</h3><p>${esc(hold.reason||'Chưa có lý do')}</p><p class="small">Người bán #${hold.sellerId}</p></div>`);
      return;
    }
    const mailButton=event.target.closest('[data-open-mail]');
    if(mailButton){
      const mail=currentMails.find(x=>x.id===Number(mailButton.dataset.openMail));
      if(!mail)return;
      const copy=mailText[mail.eventType]?.(mail.orderId)||[mail.subject,mail.body];
      openDetail(`<div class="detail email-detail"><h2>${esc(copy[0])}</h2><p class="small">Trạng thái: ${esc(label(mail.status))}</p><dl><div><dt>Người gửi</dt><dd>Hệ thống G4 (g4@example.test)</dd></div><div><dt>Người nhận</dt><dd>${esc(mail.recipient)}</dd></div><div><dt>Thời gian</dt><dd>${date(mail.createdAt)}</dd></div><div><dt>Đơn hàng</dt><dd>#${mail.orderId}</dd></div></dl><p class="email-body">${esc(copy[1])}</p></div>`);
      return;
    }
    const button = event.target.closest('[data-resolve]');
    if (!button) return;
    safe(async () => {
      const confirmed=await modal({title:'Xử lý tranh chấp',description:`Đơn #${button.dataset.id} · Quyết định ${button.dataset.resolve==='seller'?'người bán thắng':'người mua thắng'}.`,confirm:'Lưu quyết định'});
      if(!confirmed)return;
      const result = await api(`orders/${button.dataset.id}/fund-hold/resolve`, 'POST', {
        releaseToSeller: button.dataset.resolve === 'seller'
      });
      message(result.refundStatus && result.refundStatus !== 'Succeeded'
        ? 'Đã lưu quyết định. Hệ thống sẽ thử hoàn tiền lại.'
        : 'Đã lưu quyết định tranh chấp.', true);
      await loadHolds(holdPage,Number(button.dataset.id));
    });
  });
  safe(loadHolds);
})();
