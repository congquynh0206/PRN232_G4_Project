(() => {
  const { $, api, esc, date, pager, productThumbnail } = G4;
  let page = 1, reviewPage = 1, selected = null, request = 0, reviewRequest = 0, busy = false;
  let filters = new URLSearchParams();
  const drafts = new Map(), pending = new Map();
  const error = (message) => { $('product-feedback-error').textContent = message; };
  function readFilters() {
    const result = new URLSearchParams();
    for (const key of ['search', 'rating', 'reply']) {
      const value = $('product-feedback-' + key).value.trim(); if (value) result.set(key, value);
    }
    const from = $('product-feedback-from').value, to = $('product-feedback-to').value;
    if (from && to && from > to) throw new Error('Ngày bắt đầu không được sau ngày kết thúc.');
    if (from) result.set('from', new Date(from + 'T00:00:00').toISOString());
    if (to) { const end = new Date(to + 'T00:00:00'); end.setDate(end.getDate() + 1); result.set('to', end.toISOString()); }
    return result;
  }
  function productHtml(p) {
    return `<button type="button" class="product-feedback-product" data-feedback-product="${Number(p.productId)}" aria-pressed="${p.productId === selected}">${productThumbnail(p.imageUrl, p.title, 'product-feedback-thumb')}<span><strong>${esc(p.title)}</strong><small>#${Number(p.productId)} · ${Number(p.averageRating).toFixed(1)}/5 sao · ${Number(p.reviewCount)} đánh giá</small><span>${Number(p.unansweredCount)} chưa trả lời</span></span></button>`;
  }
  function replyHtml(reply) {
    return `<section class="product-review-reply"><strong>Phản hồi từ shop</strong><p>${esc(reply.comment)}</p><small>${esc(date(reply.createdAt))}</small></section>`;
  }
  function reviewHtml(r) {
    const id = Number(r.id), previous = pending.get(id);
    if (r.reply && previous?.comment === r.reply.comment) { pending.delete(id); drafts.delete(id); }
    return `<article class="product-review-card"><header><div><strong>${esc(r.buyerName)}</strong><p class="small">${esc(date(r.createdAt))}${r.verifiedPurchase ? ' · Đã mua hàng' : ''}</p></div><span class="product-review-rating">${Number(r.rating)}/5 sao</span></header><p class="product-review-comment">${esc(r.comment || 'Khách không để lại nhận xét.')}</p>${r.reply ? replyHtml(r.reply) : `<form data-feedback-reply="${id}"><fieldset ${previous ? 'disabled' : ''}><label for="product-reply-${id}">Trả lời khách hàng</label><textarea id="product-reply-${id}" name="comment" rows="3" maxlength="2000" required placeholder="Cảm ơn và giải đáp góp ý của khách…">${esc(previous?.comment ?? drafts.get(id) ?? '')}</textarea><small>Khách hàng sẽ thấy phản hồi này. Tối đa 2.000 ký tự; nội dung đã gửi không sửa/xóa.</small></fieldset><p class="product-review-error" role="alert" tabindex="-1">${previous ? 'Kết quả gửi trước chưa rõ. Hãy thử lại cùng phản hồi.' : ''}</p><button type="submit" class="primary">${previous ? 'Thử lại cùng phản hồi' : 'Gửi phản hồi'}</button></form>`}</article>`;
  }
  async function reviews(p = 1) {
    if (busy) return;
    const token = ++reviewRequest, product = selected;
    $('product-feedback-reviews').innerHTML = '';
    $('product-feedback-review-pages').innerHTML = '';
    $('product-feedback-reviews').setAttribute('aria-busy', 'false');
    if (!product) { $('product-feedback-review-title').textContent = 'Chọn sản phẩm để xem'; $('product-feedback-review-status').textContent = ''; return; }
    $('product-feedback-reviews').setAttribute('aria-busy', 'true');
    $('product-feedback-review-status').textContent = 'Đang tải đánh giá…';
    try {
      const data = await api(`seller/product-feedback/products/${product}/reviews?${filters}&page=${p}&pageSize=10`);
      if (token !== reviewRequest || product !== selected) return;
      reviewPage = data.page; $('product-feedback-review-title').textContent = data.title;
      $('product-feedback-review-status').textContent = `${data.totalCount} đánh giá phù hợp`;
      $('product-feedback-reviews').innerHTML = data.items.length ? data.items.map(reviewHtml).join('') : '<p class="empty-note">Không có đánh giá phù hợp.</p>';
      pager('product-feedback-review-pages', data, reviews);
    } catch (e) { if (token === reviewRequest) { error(e.message); $('product-feedback-review-status').textContent = 'Chưa tải được đánh giá. Bấm Làm mới để thử lại.'; } }
    finally { if (token === reviewRequest) $('product-feedback-reviews').setAttribute('aria-busy', 'false'); }
  }
  async function load(p = page) {
    if (busy) return;
    const token = ++request; ++reviewRequest; error('');
    $('product-feedback-products').setAttribute('aria-busy', 'true');
    $('product-feedback-product-status').textContent = 'Đang tải sản phẩm…';
    $('product-feedback-summary').innerHTML = '';
    $('product-feedback-products').innerHTML = '';
    $('product-feedback-product-pages').innerHTML = '';
    $('product-feedback-reviews').innerHTML = '';
    $('product-feedback-review-pages').innerHTML = '';
    try {
      const data = await api(`seller/product-feedback/products?${filters}&page=${p}&pageSize=8`);
      if (token !== request) return;
      page = data.page;
      if (!data.items.some((x) => x.productId === selected)) selected = data.items[0]?.productId ?? null;
      const s = data.summary;
      $('product-feedback-summary').innerHTML = [['Sản phẩm có đánh giá', s.productCount], ['Đánh giá', s.reviewCount], ['Điểm trung bình', s.averageRating == null ? 'Chưa có' : Number(s.averageRating).toFixed(1) + '/5'], ['Chưa trả lời', s.unansweredCount]]
        .map(([label, value]) => `<div class="product-feedback-stat"><span>${label}</span><strong>${value}</strong></div>`).join('');
      $('product-feedback-product-status').textContent = `${data.totalCount} sản phẩm phù hợp`;
      $('product-feedback-products').innerHTML = data.items.length ? data.items.map(productHtml).join('') : '<p class="empty-note">Chưa có đánh giá phù hợp. Thử xóa bộ lọc hoặc chờ khách đánh giá sau khi nhận hàng.</p>';
      pager('product-feedback-product-pages', data, load);
      await reviews(1);
    } catch (e) { if (token === request) { error(e.message); $('product-feedback-product-status').textContent = 'Chưa tải được dữ liệu. Bấm Làm mới để thử lại.'; } }
    finally { if (token === request) $('product-feedback-products').setAttribute('aria-busy', 'false'); }
  }
  async function submit(event) {
    const form = event.target.closest('[data-feedback-reply]'); if (!form) return;
    event.preventDefault(); if (busy) return;
    const id = Number(form.dataset.feedbackReply), fieldset = form.querySelector('fieldset'), button = form.querySelector('button[type=submit]'), alert = form.querySelector('[role=alert]');
    const comment = form.querySelector('textarea').value.trim();
    if (!pending.has(id) && (!comment || comment.length > 2000)) { alert.textContent = 'Nhập phản hồi từ 1 đến 2.000 ký tự.'; alert.focus(); return; }
    if (!pending.has(id)) pending.set(id, { comment, key: crypto.randomUUID() });
    busy = true; fieldset.disabled = true; button.disabled = true; button.textContent = 'Đang gửi…'; alert.textContent = '';
    $('product-feedback-filters').disabled = true;
    form.setAttribute('aria-busy', 'true'); let saved = false;
    try {
      await api(`seller/product-feedback/reviews/${id}/reply`, 'POST', pending.get(id));
      pending.delete(id); drafts.delete(id); saved = true; G4.message('Đã gửi phản hồi cho khách hàng.', true);
    } catch (e) {
      if (e.status >= 400 && e.status < 500) pending.delete(id);
      alert.textContent = e.message + (pending.has(id) ? ' Kết quả chưa rõ, thử lại cùng phản hồi.' : ''); alert.focus();
    } finally {
      busy = false; $('product-feedback-filters').disabled = false;
      fieldset.disabled = pending.has(id); button.disabled = false; button.textContent = pending.has(id) ? 'Thử lại cùng phản hồi' : 'Gửi phản hồi'; form.setAttribute('aria-busy', 'false');
    }
    if (saved) await load(page);
  }
  function init() {
    $('product-feedback-filter').onsubmit = (e) => { e.preventDefault(); if (busy) return; try { filters = readFilters(); selected = null; load(1); } catch (err) { error(err.message); $('product-feedback-error').focus(); } };
    $('product-feedback-reset').onclick = () => { if (busy) return; $('product-feedback-filter').reset(); filters = new URLSearchParams(); selected = null; load(1); };
    $('product-feedback-refresh').onclick = () => load(page);
    $('product-feedback-products').onclick = (e) => { const b = e.target.closest('[data-feedback-product]'); if (!b || busy) return; selected = Number(b.dataset.feedbackProduct); document.querySelectorAll('[data-feedback-product]').forEach((x) => x.setAttribute('aria-pressed', String(x === b))); reviews(1); };
    $('product-feedback-reviews').addEventListener('submit', submit);
    $('product-feedback-reviews').addEventListener('input', (e) => { const f = e.target.closest('[data-feedback-reply]'); if (f) drafts.set(Number(f.dataset.feedbackReply), e.target.value); });
    G4.Realtime?.subscribe(['product-feedback'], async () => {
      if (document.querySelector('.tab-panel.active')?.id !== 'product-feedback') return;
      if (busy || pending.size || [...drafts.values()].some((x) => x.trim())) return false;
      await load(page);
    });
  }
  G4.ProductFeedback = { init, load, reviews, submit, reviewHtml, productHtml, replyHtml, readFilters };
})();
