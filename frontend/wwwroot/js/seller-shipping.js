(() => {
  const {$, esc, api, safe, message, pager} = G4;
  let page = 1, initialized = false;
  async function load(next = page) {
    const result = await api(`seller/shipping/settings?page=${next}&pageSize=10`);
    page = result.page;
    if (!initialized) {
      for (const key of ['fullName', 'street', 'city', 'state', 'country']) $('pickup-' + key).value = result.pickup?.[key] || '';
      initialized = true;
    }
    $('product-weights').innerHTML = result.items.length ? result.items.map(p => `<form class="product-weight-row" data-weight-product="${p.id}"><div><strong>${esc(p.title)}</strong><small>#${p.id}${p.weightKg == null ? ' · Chưa khai báo khối lượng' : ''}</small></div><label>Khối lượng (kg)<input name="weight" type="number" min="0.001" max="10000" step="0.001" required value="${p.weightKg == null ? '' : Number(p.weightKg)}" /></label><button type="submit">Lưu</button></form>`).join('') : '<p class="empty-note">Chưa có sản phẩm.</p>';
    pager('product-weights-pagination', result, n => safe(() => load(n)));
  }
  $('pickup-form').onsubmit = e => {
    e.preventDefault();
    safe(async () => {
      const body = Object.fromEntries(['fullName', 'street', 'city', 'state', 'country'].map(k => [k, $('pickup-' + k).value.trim()]));
      await api('seller/shipping/pickup', 'POST', body);
      message('Đã lưu địa chỉ lấy hàng cho các đơn mới.', true);
    });
  };
  $('product-weights').addEventListener('submit', e => {
    const form = e.target.closest('[data-weight-product]'); if (!form) return;
    e.preventDefault();
    safe(async () => {
      await api(`seller/shipping/products/${form.dataset.weightProduct}/weight`, 'POST', {weightKg: Number(form.elements.weight.value)});
      message('Đã lưu khối lượng sản phẩm.', true);
      await load();
    });
  });
  G4.SellerShipping = {load};
})();
