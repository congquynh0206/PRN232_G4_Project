(() => {
  const { $, api, esc, date, productThumbnail } = G4;
  const pending = new Map(), drafts = new Map(); let orderId, busy = false, request = 0;
  const keyFor = (productId) => `${orderId}:${productId}`;
  function itemHtml(item) {
    const id = Number(item.productId), r = item.review, key = keyFor(id), previous = pending.get(key), draft = previous || drafts.get(key) || {};
    if (r && previous?.rating === r.rating && previous?.comment === r.comment) { pending.delete(key); drafts.delete(key); }
    const head = `<header>${productThumbnail(item.imageUrl, item.title, 'product-feedback-thumb')}<strong>${esc(item.title)}</strong></header>`;
    const body = r ? `<p class="product-review-rating">${Number(r.rating)}/5 sao · ${esc(date(r.createdAt))}</p><p class="product-review-comment">${esc(r.comment || 'Bạn không để lại nhận xét.')}</p>${r.reply ? `<section class="product-review-reply"><strong>Shop trả lời</strong><p>${esc(r.reply.comment)}</p><small>${esc(date(r.reply.createdAt))}</small></section>` : '<p class="small">Shop chưa trả lời đánh giá này.</p>'}`
      : item.canReview ? `<form data-product-review="${id}"><fieldset ${previous ? 'disabled' : ''}><label for="product-rating-${id}">Số sao</label><select id="product-rating-${id}" name="rating" required><option value="">Chọn số sao</option>${[5, 4, 3, 2, 1].map((n) => `<option value="${n}" ${Number(draft.rating) === n ? 'selected' : ''}>${n} sao</option>`).join('')}</select><label for="product-comment-${id}">Nhận xét (không bắt buộc)</label><textarea id="product-comment-${id}" name="comment" maxlength="2000" rows="3">${esc(draft.comment || '')}</textarea></fieldset><p role="alert" class="product-review-error" tabindex="-1">${previous ? 'Kết quả lần gửi trước chưa rõ; hãy thử lại cùng đánh giá.' : ''}</p><button type="submit" class="primary">${previous ? 'Thử lại cùng đánh giá' : 'Gửi đánh giá sản phẩm'}</button></form>` : '<p class="small">Bạn có thể đánh giá sau khi đơn đã thanh toán và giao thành công.</p>';
    return `<article class="product-review-card">${head}${body}</article>`;
  }
  async function load() {
    if (busy) return;
    const token = ++request, id = orderId;
    $('buyer-product-reviews-error').textContent = ''; $('buyer-product-reviews-list').setAttribute('aria-busy', 'true');
    $('buyer-product-reviews-list').innerHTML = '<p role="status">Đang tải đánh giá…</p>';
    try {
      const data = await api(`orders/${id}/product-reviews`);
      if (token !== request || id !== orderId) return;
      $('buyer-product-reviews-list').innerHTML = data.items.length ? data.items.map(itemHtml).join('') : '<p>Đơn không có sản phẩm phù hợp để đánh giá.</p>';
    } catch (e) { if (token === request) { $('buyer-product-reviews-list').innerHTML = ''; $('buyer-product-reviews-error').textContent = e.message; } }
    finally { if (token === request) $('buyer-product-reviews-list').setAttribute('aria-busy', 'false'); }
  }
  async function open(id) {
    if (busy) return; orderId = id;
    $('buyer-product-reviews-context').textContent = `Sản phẩm trong đơn #${id}`;
    if (!$('buyer-product-reviews-dialog').open) $('buyer-product-reviews-dialog').showModal();
    $('buyer-product-reviews-close').focus(); await load();
  }
  async function submit(e) {
    const form = e.target.closest('[data-product-review]'); if (!form) return;
    e.preventDefault(); if (busy) return;
    const product = Number(form.dataset.productReview), key = keyFor(product), fieldset = form.querySelector('fieldset'), button = form.querySelector('button'), alert = form.querySelector('[role=alert]');
    const rating = Number(form.querySelector('select').value), comment = form.querySelector('textarea').value.trim();
    if (!pending.has(key) && (!Number.isInteger(rating) || rating < 1 || rating > 5 || comment.length > 2000)) { alert.textContent = 'Chọn 1–5 sao, nhận xét tối đa 2.000 ký tự.'; alert.focus(); return; }
    if (!pending.has(key)) pending.set(key, { rating, comment, key: crypto.randomUUID() });
    busy = true; fieldset.disabled = true; button.disabled = true; button.textContent = 'Đang gửi…'; alert.textContent = '';
    $('buyer-product-reviews-close').disabled = true; $('buyer-product-reviews-refresh').disabled = true; let saved = false;
    try {
      await api(`orders/${orderId}/products/${product}/review`, 'POST', pending.get(key));
      pending.delete(key); drafts.delete(key); saved = true; G4.message('Đã gửi đánh giá sản phẩm.', true);
    } catch (err) {
      if (err.status >= 400 && err.status < 500) pending.delete(key);
      alert.textContent = err.message + (pending.has(key) ? ' Kết quả chưa rõ, hãy thử lại cùng đánh giá.' : ''); alert.focus();
    } finally {
      busy = false; fieldset.disabled = pending.has(key); button.disabled = false; button.textContent = pending.has(key) ? 'Thử lại cùng đánh giá' : 'Gửi đánh giá sản phẩm';
      $('buyer-product-reviews-close').disabled = false; $('buyer-product-reviews-refresh').disabled = false;
    }
    if (saved) await load();
  }
  function init() {
    $('buyer-product-reviews-list').addEventListener('submit', submit);
    $('buyer-product-reviews-list').addEventListener('input', (e) => {
      const f = e.target.closest('[data-product-review]'); if (!f) return;
      drafts.set(keyFor(Number(f.dataset.productReview)), { rating: Number(f.querySelector('select').value), comment: f.querySelector('textarea').value });
    });
    $('buyer-product-reviews-close').onclick = () => $('buyer-product-reviews-dialog').close();
    $('buyer-product-reviews-refresh').onclick = load;
    $('buyer-product-reviews-dialog').addEventListener('cancel', (e) => { if (busy) e.preventDefault(); });
    G4.Realtime?.subscribe(['product-feedback'], async () => {
      if (!$('buyer-product-reviews-dialog').open) return;
      if (busy || pending.size || [...drafts.values()].some((d) => d.rating || d.comment.trim())) return false;
      await load();
    });
  }
  G4.BuyerProductReviews = { init, open, load, submit, itemHtml };
})();
