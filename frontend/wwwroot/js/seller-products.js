(() => {
  const { $, api, esc, money } = window.G4;
  let products = [];
  const message = (text) => { $('message').textContent = text; };
  async function load() {
    products = await api('seller/products');
    $('products-list').innerHTML = products.map((p) => `
      <form class="immediate-product" data-product="${p.id}">
        <h3>${esc(p.title)} · ${money(p.price)}</h3>
        <p>Tồn kho: ${p.available} · ${p.immediatePaymentRequired
          ? 'Immediate Payment Required' : 'Normal Payment'}</p>
        <label>Giá USD
          <input name="price" type="number" min="0.01" step="0.01" value="${p.price}" required>
        </label>
        <label>Thanh toán <select name="immediatePaymentRequired">
          <option value="false" ${!p.immediatePaymentRequired ? 'selected' : ''}>Normal Payment</option>
          <option value="true" ${p.immediatePaymentRequired ? 'selected' : ''}>Immediate Payment</option>
        </select></label>
        <label>Hiển thị <select name="isListed">
          <option value="true" ${p.isListed ? 'selected' : ''}>Đang bán</option>
          <option value="false" ${!p.isListed ? 'selected' : ''}>Ngừng bán</option>
        </select></label>
        <button type="submit">Lưu cấu hình</button>
      </form>`).join('');
  }
  $('publish-product').addEventListener('submit', async (event) => {
    event.preventDefault();
    const form = event.currentTarget;
    const button = form.querySelector('button');
    if (button.disabled) return;
    button.disabled = true;
    try {
      const data = Object.fromEntries(new FormData(form));
      for (const field of ['price', 'weightKg', 'quantity']) data[field] = Number(data[field]);
      data.immediatePaymentRequired = data.immediatePaymentRequired === 'true';
      await api('seller/products', 'POST', data);
      form.reset();
      await load();
      message('Đã đăng sản phẩm.');
    } catch (error) { message(error.message); }
    finally { button.disabled = false; }
  });
  $('products-list').addEventListener('submit', async (event) => {
    event.preventDefault();
    const form = event.target;
    const product = products.find((p) => p.id === Number(form.dataset.product));
    const button = form.querySelector('button');
    if (button.disabled) return;
    button.disabled = true;
    try {
      const data = Object.fromEntries(new FormData(form));
      await api(`seller/products/${product.id}/payment-settings`, 'POST', {
        price: Number(data.price), expectedPrice: product.price,
        immediatePaymentRequired: data.immediatePaymentRequired === 'true',
        isListed: data.isListed === 'true',
      });
      await load();
      message('Đã lưu cấu hình.');
    } catch (error) { message(error.message); }
    finally { button.disabled = false; }
  });
  load().catch((error) => message(error.message));
})();
