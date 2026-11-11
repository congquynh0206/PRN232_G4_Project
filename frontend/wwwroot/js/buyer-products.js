(() => {
  const { $, api, esc, money, productThumbnail } = window.G4;
  const products = new Map();
  let busy = false;

  function message(html, ok = true) {
    const box = $('message');
    box.className = 'products-message ' + (ok ? 'ok' : 'error');
    box.innerHTML = html;
  }

  function card(p) {
    const soldOut = p.available <= 0;
    const immediate = p.immediatePaymentRequired;
    const offer = p.allowOffers && !soldOut
      ? `<a class="product-action" href="/Buyer?tab=best-offers&offer=${p.id}">Trả giá</a>`
      : '';
    return `<article class="product-card" data-product-card="${p.id}">
      ${productThumbnail(p.imageUrl, p.title, 'product-card-thumb')}
      <div class="product-card-body">
        <h2>${esc(p.title)}</h2>
        <p class="product-card-seller">Người bán: ${esc(p.sellerName || 'Không rõ')}</p>
        <p class="product-card-price">${money(p.price)}</p>
        <p class="product-card-stock">${soldOut ? 'Hết hàng' : `Còn ${p.available}`}</p>
        ${immediate
          ? '<span class="product-badge">Yêu cầu thanh toán ngay</span>'
          : ''}
        <div class="product-card-actions">
          <button type="button" class="primary" data-buy="${p.id}" ${soldOut ? 'disabled' : ''}>
            Buy It Now</button>
          <button type="button" data-add-cart="${p.id}"
            ${soldOut || immediate ? 'disabled' : ''}>Thêm vào giỏ</button>
          ${offer}
        </div>
        ${immediate && !soldOut
          ? '<p class="product-card-note">Sản phẩm cần thanh toán ngay, hãy dùng Buy It Now.</p>'
          : ''}
      </div>
    </article>`;
  }

  async function load() {
    const list = await api('catalog/products');
    products.clear();
    list.forEach((p) => products.set(p.id, p));
    $('products-list').setAttribute('aria-busy', 'false');
    $('products-list').innerHTML = list.length
      ? list.map(card).join('')
      : '<p class="empty-state">Chưa có sản phẩm đang bán.</p>';
  }

  // Add one unit; the server checks stock, listing state and the cart size.
  async function addToCart(product) {
    const cart = await api('cart');
    const items = cart.items.map((x) => ({ productId: x.productId, quantity: x.quantity }));
    const line = items.find((x) => x.productId === product.id);
    if (line) {
      if (line.quantity >= Math.min(5, product.available)) {
        message(`“${esc(product.title)}” đã có số lượng tối đa trong giỏ.`, false);
        return;
      }
      line.quantity += 1;
    } else {
      items.push({ productId: product.id, quantity: 1 });
    }
    await api('cart/save', 'POST', { version: cart.version, items });
    message(
      `Đã thêm “${esc(product.title)}” vào giỏ. ` +
        '<a href="/Buyer?tab=summary">Xem giỏ hàng</a>',
    );
  }

  $('products-list').addEventListener('click', (event) => {
    const buy = event.target.closest('[data-buy]');
    if (buy && !buy.disabled) {
      const product = encodeURIComponent(buy.dataset.buy);
      location.assign(`/Buyer?tab=checkout&product=${product}#checkout`);
      return;
    }
    const add = event.target.closest('[data-add-cart]');
    if (!add || add.disabled || busy) return;
    const product = products.get(Number(add.dataset.addCart));
    if (!product) return;
    busy = true;
    add.disabled = true;
    addToCart(product)
      .catch((error) => message(esc(error.message), false))
      .finally(() => {
        busy = false;
        add.disabled = false;
      });
  });

  load().catch((error) => {
    $('products-list').setAttribute('aria-busy', 'false');
    $('products-list').textContent = '';
    message(esc(error.message), false);
  });
})();
