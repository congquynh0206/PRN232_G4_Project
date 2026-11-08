(() => {
  const { $, api, esc, money, date, message } = G4;
  const state = { addresses: [] };
  const active = (offer) => ['PendingSeller', 'PendingBuyer'].includes(offer.status);
  const statusName = (status) =>
    ({
      PendingSeller: 'Chờ người bán',
      PendingBuyer: 'Chờ người mua',
      Accepted: 'Đã chấp nhận, đơn đã thanh toán',
      Declined: 'Đã từ chối',
      Expired: 'Đã hết hạn',
    })[status] || status;

  async function buyerLoad() {
    const catalog = $('offer-catalog');
    const offers = $('buyer-offer-list');
    if (!catalog || !offers) return;
    catalog.setAttribute('aria-busy', 'true');
    offers.setAttribute('aria-busy', 'true');
    try {
      const [products, result, addressBook] = await Promise.all([
        api('best-offers/catalog'),
        api('best-offers/buyer'),
        api('addresses'),
      ]);
      state.addresses = Array.isArray(addressBook)
        ? addressBook
        : addressBook.addresses || [];
      catalog.innerHTML = products.length
        ? products.map(productCard).join('')
        : '<p class="empty-state">Chưa có sản phẩm bật trả giá.</p>';
      offers.innerHTML = result.length
        ? result.map((offer) => offerCard(offer, 'buyer')).join('')
        : '<p class="empty-state">Bạn chưa có phiên trả giá nào.</p>';
    } finally {
      catalog.setAttribute('aria-busy', 'false');
      offers.setAttribute('aria-busy', 'false');
    }
  }

  async function sellerLoad() {
    const products = $('seller-offer-products');
    const offers = $('seller-offer-list');
    if (!products || !offers) return;
    products.setAttribute('aria-busy', 'true');
    offers.setAttribute('aria-busy', 'true');
    try {
      const [items, result] = await Promise.all([
        api('best-offers/seller/products'),
        api('best-offers/seller'),
      ]);
      products.innerHTML = items.length
        ? items.map(settingsCard).join('')
        : '<p class="empty-state">Chưa có sản phẩm giá cố định trong cửa hàng.</p>';
      offers.innerHTML = result.length
        ? result.map((offer) => offerCard(offer, 'seller')).join('')
        : '<p class="empty-state">Chưa có đề nghị trả giá.</p>';
    } finally {
      products.setAttribute('aria-busy', 'false');
      offers.setAttribute('aria-busy', 'false');
    }
  }

  function productCard(product) {
    const options = state.addresses
      .map(
        (address) =>
          `<option value="${address.id}" ${address.isDefault ? 'selected' : ''}>` +
          `${esc(address.fullName)} — ${esc(address.city)}</option>`,
      )
      .join('');
    return `<article class="offer-card">
      <div class="offer-card-head">
        <strong>${esc(product.title)}</strong><span>Giá ${money(product.listPrice)}</span>
      </div>
      <p>Còn ${product.available} sản phẩm · Đề xuất phải thấp hơn giá niêm yết.</p>
      <form data-offer-create="${product.productId}" class="offer-form">
        <label>Giá đề nghị
          <input name="amount" type="number" min="0.01" max="${product.listPrice}"
            step="0.01" required />
        </label>
        <label>Địa chỉ giao hàng
          <select name="addressId" required>
            ${options || '<option value="">Hãy thêm địa chỉ trước</option>'}
          </select>
        </label>
        <label>Thẻ thử đã lưu
          <select name="cardProfile">
            <option value="Success">4111…1111 — thành công</option>
            <option value="Declined">4000…0002 — từ chối</option>
          </select>
        </label>
        <label>Hạn thẻ<input name="cardExpiry" value="12/30" maxlength="5" required /></label>
        <button class="primary" ${state.addresses.length ? '' : 'disabled'}>Gửi trả giá</button>
      </form>
      <small>Khi người bán chấp nhận, hệ thống sẽ thử thu tiền ngay bằng thẻ mô phỏng.</small>
    </article>`;
  }

  function settingsCard(product) {
    return `<article class="offer-card">
      <form data-offer-settings="${product.productId}" class="offer-form">
        <div class="offer-card-head">
          <strong>${esc(product.title)}</strong><span>Giá ${money(product.price)}</span>
        </div>
        <label class="offer-check">
          <input type="checkbox" name="allowOffers" ${product.allowOffers ? 'checked' : ''} />
          Cho phép người mua trả giá
        </label>
        <label>Tự động chấp nhận từ mức giá
          <input name="autoAcceptPrice" type="number" min="0.01" step="0.01"
            max="${product.price}" value="${product.autoAcceptPrice ?? ''}" />
        </label>
        <label>Tự động từ chối dưới mức giá
          <input name="autoDeclineBelow" type="number" min="0" step="0.01"
            max="${product.price}" value="${product.autoDeclineBelow ?? ''}" />
        </label>
        <button>Lưu cấu hình</button>
      </form>
    </article>`;
  }

  function offerCard(offer, role) {
    const turns = (offer.turns || [])
      .map(
        (turn) =>
          `<li>${esc(turn.actorRole)} · ${esc(turn.action)} · ${money(turn.amount)}` +
          `${turn.result ? ` · ${esc(turn.result)}` : ''}</li>`,
      )
      .join('');
    let actions = '';
    if (active(offer) && offer.viewerRole === 'seller' && offer.status === 'PendingSeller') {
      actions = `<div class="offer-actions">
        <button data-offer-action="seller/accept" data-id="${offer.id}">
          Chấp nhận và thu tiền
        </button>
        <button data-offer-action="seller/decline" data-id="${offer.id}">Từ chối</button>
        <form data-offer-counter="seller" data-id="${offer.id}">
          <input name="amount" type="number" min="0.01" step="0.01"
            max="${offer.listPrice}" placeholder="Giá phản hồi" required />
          <button>Trả giá lại</button>
        </form>
      </div>`;
    } else if (active(offer) && offer.viewerRole === 'buyer' && offer.status === 'PendingBuyer') {
      const counterForm =
        offer.buyerOfferCount < offer.buyerOfferLimit
          ? `<form data-offer-counter="buyer" data-id="${offer.id}">
              <input name="amount" type="number" min="0.01" step="0.01"
                max="${offer.listPrice}" placeholder="Giá trả lại" required />
              <button>Trả giá lại</button>
            </form>`
          : '<small>Đã dùng hết lượt trả giá.</small>';
      actions = `<div class="offer-actions">
        <button data-offer-action="buyer/accept" data-id="${offer.id}">
          Chấp nhận và thanh toán
        </button>
        <button data-offer-action="buyer/decline" data-id="${offer.id}">Từ chối</button>
        ${counterForm}
      </div>`;
    }
    if (active(offer) && offer.viewerRole === 'buyer') {
      actions += `<form data-offer-payment="${offer.id}" class="offer-form">
        <label>Thẻ thử dùng khi chốt đơn
          <select name="cardProfile">
            <option value="Success">4111…1111 — thành công</option>
            <option value="Declined">4000…0002 — từ chối</option>
          </select>
        </label>
        <label>Hạn thẻ<input name="cardExpiry" value="12/30" maxlength="5" required /></label>
        <button>Cập nhật thẻ thử</button>
      </form>`;
    }
    return `<article class="offer-card">
      <div class="offer-card-head">
        <strong>${esc(offer.productTitle)}</strong><span>${statusName(offer.status)}</span>
      </div>
      <p>Đề nghị hiện tại: <strong>${money(offer.currentAmount)}</strong>
        · Hết hạn ${date(offer.expiresAt)}</p>
      ${role === 'buyer'
        ? `<p>Lượt trả giá của bạn: ${offer.buyerOfferCount}/${offer.buyerOfferLimit}</p>`
        : ''}
      <ul class="offer-turns">${turns}</ul>${actions}
      ${offer.orderId ? `<p>Đơn hàng #${offer.orderId} đã được tạo và thanh toán.</p>` : ''}
    </article>`;
  }

  document.addEventListener('submit', async (event) => {
    const create = event.target.closest('[data-offer-create]');
    const settings = event.target.closest('[data-offer-settings]');
    const counter = event.target.closest('[data-offer-counter]');
    const payment = event.target.closest('[data-offer-payment]');
    if (!create && !settings && !counter && !payment) return;
    event.preventDefault();
    const form = event.target;
    const values = new FormData(form);
    try {
      if (create) {
        await api('best-offers', 'POST', {
          productId: Number(create.dataset.offerCreate),
          amount: Number(values.get('amount')),
          addressId: Number(values.get('addressId')),
          paymentMethod: 'Card',
          cardProfile: values.get('cardProfile'),
          cardExpiry: values.get('cardExpiry'),
          key: crypto.randomUUID(),
        });
        message('Đã gửi đề nghị trả giá.', true);
        await buyerLoad();
      } else if (settings) {
        await api('best-offers/seller/settings', 'POST', {
          productId: Number(settings.dataset.offerSettings),
          allowOffers: values.has('allowOffers'),
          autoAcceptPrice: values.get('autoAcceptPrice')
            ? Number(values.get('autoAcceptPrice'))
            : null,
          autoDeclineBelow: values.get('autoDeclineBelow')
            ? Number(values.get('autoDeclineBelow'))
            : null,
        });
        message('Đã lưu cấu hình Best Offer.', true);
        await sellerLoad();
      } else if (payment) {
        await api(`best-offers/${payment.dataset.offerPayment}/buyer/payment-profile`, 'POST', {
          cardProfile: values.get('cardProfile'),
          cardExpiry: values.get('cardExpiry'),
        });
        message('Đã cập nhật thẻ mô phỏng cho phiên.', true);
        await buyerLoad();
      } else {
        await api(`best-offers/${counter.dataset.id}/${counter.dataset.offerCounter}/counter`, 'POST', {
          amount: Number(values.get('amount')),
          key: crypto.randomUUID(),
        });
        message('Đã gửi giá phản hồi.', true);
        await (counter.dataset.offerCounter === 'buyer' ? buyerLoad() : sellerLoad());
      }
    } catch (error) {
      message(error.message || 'Không thể hoàn tất thao tác.');
    }
  });

  document.addEventListener('click', async (event) => {
    const button = event.target.closest('[data-offer-action]');
    if (!button) return;
    button.disabled = true;
    try {
      await api(`best-offers/${button.dataset.id}/${button.dataset.offerAction}`, 'POST', {});
      message('Đã cập nhật phiên trả giá.', true);
      await (button.dataset.offerAction.startsWith('buyer/') ? buyerLoad() : sellerLoad());
    } catch (error) {
      message(error.message || 'Không thể hoàn tất thao tác.');
      button.disabled = false;
    }
  });

  $('buyer-offers-refresh')?.addEventListener('click', () => G4.safe(buyerLoad));
  $('seller-offers-refresh')?.addEventListener('click', () => G4.safe(sellerLoad));
  $('buyer-offers-addresses')?.addEventListener('click', () => {
    G4.BuyerCart.addressBook?.openManage(state.addresses.length === 0);
  });
  $('address-dialog')?.addEventListener('close', () => {
    if ($('best-offers')?.classList.contains('active')) G4.safe(buyerLoad);
  });
  G4.BestOffers = { buyerLoad, sellerLoad };
})();
