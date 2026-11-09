(() => {
  const { $, api, esc, money } = window.G4;
  const message = (text) => { $('message').textContent = text; };

  async function load() {
    const products = await api('catalog/products');
    if (!products.length) {
      $('products-list').innerHTML = '<p>Chưa có sản phẩm đang bán.</p>';
      return;
    }
    $('products-list').innerHTML = products.map((p) => `
      <article class="immediate-product">
        <h2>${esc(p.title)}</h2><p>${esc(p.description)}</p>
        <p>${money(p.price)} · Còn ${p.available}</p>
        <span class="immediate-badge">${p.immediatePaymentRequired
          ? 'Immediate Payment Required' : 'Normal Payment'}</span>
        <button type="button" data-buy="${p.id}" ${p.available <= 0 ? 'disabled' : ''}>
          ${p.available <= 0 ? 'Sold Out' : 'Buy It Now'}</button>
      </article>`).join('');
  }

  // Buy It Now opens its own checkout page instead of a form under the list.
  $('products-list').addEventListener('click', (event) => {
    const button = event.target.closest('[data-buy]');
    if (!button || button.disabled) return;
    location.assign(`/Buyer/Checkout?product=${encodeURIComponent(button.dataset.buy)}`);
  });

  load().catch((error) => message(error.message));
})();
