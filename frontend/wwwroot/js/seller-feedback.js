(() => {
  const { $, api, esc, date } = G4;
  const pending = new Map(); let orderId, savedCallback, busy = false;
  function html(o) {
    if (o.sellerFeedback) return `<section class="finance-block"><h3>Đánh giá seller đã gửi</h3><p>${Number(o.sellerFeedback.rating)}/5 sao</p><p class="seller-feedback-comment">${esc(o.sellerFeedback.comment)}</p><small>${date(o.sellerFeedback.createdAt)}</small></section>`;
    return o.canReviewSeller ? `<button type="button" data-action="review-seller" data-id="${Number(o.id)}">Đánh giá seller</button>` : '';
  }
  function lock() {
    $('seller-feedback-fields').disabled = busy || pending.has(orderId);
    $('seller-feedback-submit').disabled = busy;
    $('seller-feedback-submit').textContent = busy ? 'Đang gửi…' : pending.has(orderId) ? 'Thử lại cùng đánh giá' : 'Gửi đánh giá';
    $('seller-feedback-close').disabled = busy; $('seller-feedback-form').setAttribute('aria-busy', String(busy));
  }
  function open(id, callback) {
    if (busy) return; orderId = id; savedCallback = callback;
    const previous = pending.get(id);
    $('seller-feedback-context').textContent = `Đơn mua hàng #${id}. Kiểm tra nội dung trước khi gửi.`;
    $('seller-feedback-rating').value = previous?.rating || '';
    $('seller-feedback-comment').value = previous?.comment || '';
    $('seller-feedback-error').textContent = previous ? 'Kết quả lần gửi trước chưa rõ; hãy thử lại cùng đánh giá.' : '';
    lock(); $('seller-feedback-dialog').showModal(); (previous ? $('seller-feedback-submit') : $('seller-feedback-rating')).focus();
  }
  async function submit(e) {
    e.preventDefault(); if (busy) return;
    if (!pending.has(orderId)) {
      const rating = Number($('seller-feedback-rating').value);
      if (!Number.isInteger(rating) || rating < 1 || rating > 5) { $('seller-feedback-error').textContent = 'Chọn từ 1 đến 5 sao.'; return; }
      pending.set(orderId, { rating, comment: $('seller-feedback-comment').value.trim(), key: crypto.randomUUID() });
    }
    busy = true; lock(); $('seller-feedback-error').textContent = '';
    try {
      await api(`orders/${orderId}/seller-feedback`, 'POST', pending.get(orderId));
      pending.delete(orderId); $('seller-feedback-dialog').close();
      G4.message('Đã gửi đánh giá seller.', true); if (savedCallback) await G4.safe(savedCallback);
    } catch (err) {
      if (err.status >= 400 && err.status < 500) pending.delete(orderId);
      $('seller-feedback-error').textContent = err.message + (pending.has(orderId) ? ' Kết quả chưa rõ; hãy thử lại cùng đánh giá.' : '');
      $('seller-feedback-error').focus();
    } finally { busy = false; lock(); }
  }
  function init() {
    $('seller-feedback-form').onsubmit = submit;
    $('seller-feedback-close').onclick = () => $('seller-feedback-dialog').close();
    $('seller-feedback-dialog').addEventListener('cancel', (e) => { if (busy) e.preventDefault(); });
  }
  G4.SellerFeedback = { init, open, html, submit };
})();
