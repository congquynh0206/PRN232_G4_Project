(() => {
  const { $, api, esc, money, date, message } = G4;
  const state = { addresses: [] };
  // One idempotency key per click/submit, kept until it succeeds so a retry reuses it.
  const pendingKeys = new WeakMap();
  const keyFor = (element) => {
    if (!pendingKeys.has(element)) pendingKeys.set(element, crypto.randomUUID());
    return pendingKeys.get(element);
  };
  const active = (offer) => ['PendingSeller', 'PendingBuyer'].includes(offer.status);
  const payLater = (offer) => offer.paymentMethod === 'PayLater';
  const cents = (value) => (Math.round(Number(value) * 100) / 100).toFixed(2);
  const statusName = (offer) =>
    ({
      PendingSeller: 'Chờ người bán',
      PendingBuyer: 'Chờ người mua',
      Accepted: payLater(offer)
        ? 'Đã chấp nhận, đơn chờ thanh toán'
        : 'Đã chấp nhận, đã thanh toán',
      Declined: 'Đã từ chối',
      Expired: 'Đã hết hạn',
    })[offer.status] || offer.status;
  const paymentName = (offer) =>
    payLater(offer) ? 'Thanh toán sau (giữ hàng chờ trả)' : 'Auto-Pay bằng thẻ thử';
  const cardFields = (prefix) => `
    <label>${prefix}
      <select name="cardProfile">
        <option value="Success">4111…1111 — thành công</option>
        <option value="Declined">4000…0002 — từ chối</option>
      </select>
    </label>
    <label>Hạn thẻ<input name="cardExpiry" value="12/30" maxlength="5" /></label>`;

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
    const exhausted = product.buyerOffersUsed >= product.buyerOfferLimit;
    const canSend = state.addresses.length && !exhausted;
    return `<article class="offer-card">
      <div class="offer-card-head">
        <strong>${esc(product.title)}</strong><span>Giá ${money(product.listPrice)}</span>
      </div>
      <p>Còn ${product.available} sản phẩm · Đề xuất phải thấp hơn giá niêm yết.</p>
      <p>Đã dùng ${product.buyerOffersUsed}/${product.buyerOfferLimit} lượt
        cho sản phẩm này.</p>
      <form data-offer-create="${product.productId}" class="offer-form">
        <label>Giá đề nghị
          <input name="amount" type="number" min="0.01" max="${cents(product.listPrice - 0.01)}"
            step="0.01" required ${exhausted ? 'disabled' : ''} />
        </label>
        <label>Địa chỉ giao hàng
          <select name="addressId" required>
            ${options || '<option value="">Hãy thêm địa chỉ trước</option>'}
          </select>
        </label>
        <fieldset class="offer-payment">
          <legend>Khi được chấp nhận</legend>
          <label class="offer-check">
            <input type="radio" name="paymentMethod" value="PayLater" checked />
            Thanh toán sau: tạo đơn chờ thanh toán, trả trong tab Đơn hàng trước hạn
          </label>
          <label class="offer-check">
            <input type="radio" name="paymentMethod" value="Card" />
            Auto-Pay: thu tiền thẻ thử ngay khi được chấp nhận
          </label>
        </fieldset>
        <div data-card-fields hidden>${cardFields('Thẻ thử đã lưu')}</div>
        <button class="primary" ${canSend ? '' : 'disabled'}>Gửi trả giá</button>
      </form>
      ${exhausted
        ? '<small>Bạn đã dùng hết lượt trả giá cho sản phẩm này.</small>'
        : ''}
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

  // Valid counter range: seller between the buyer's offer and the list price; buyer between
  // their previous offer and the seller's counter. The server enforces the same rule.
  function counterRange(offer) {
    if (offer.status === 'PendingSeller')
      return { min: offer.currentAmount + 0.01, max: offer.listPrice - 0.01 };
    const buyerTurns = (offer.turns || []).filter(
      (turn) => turn.actorRole === 'buyer' && ['Offer', 'Counter'].includes(turn.action),
    );
    const previous = buyerTurns.length ? buyerTurns.at(-1).amount : 0;
    return { min: previous + 0.01, max: offer.currentAmount - 0.01 };
  }

  function counterForm(offer, role) {
    const range = counterRange(offer);
    if (range.min > range.max)
      return '<small>Không còn khoảng giá để trả giá lại; hãy chấp nhận hoặc từ chối.</small>';
    return `<form data-offer-counter="${role}" data-id="${offer.id}">
      <input name="amount" type="number" step="0.01" min="${cents(range.min)}"
        max="${cents(range.max)}" placeholder="Giá ${role === 'seller' ? 'phản hồi' : 'trả lại'}"
        required />
      <button>Trả giá lại</button>
      <small>Từ ${money(cents(range.min))} đến ${money(cents(range.max))}</small>
    </form>`;
  }

  function offerCard(offer, role) {
    const turns = (offer.turns || [])
      .map(
        (turn) =>
          `<li>${esc(turn.actorRole)} · ${esc(turn.action)} · ${money(turn.amount)}` +
          `${turn.result ? ` · ${esc(turn.result)}` : ''}</li>`,
      )
      .join('');
    const acceptLabel = payLater(offer)
      ? 'Chấp nhận (tạo đơn chờ thanh toán)'
      : role === 'seller'
        ? 'Chấp nhận và thu tiền'
        : 'Chấp nhận và thanh toán';
    let actions = '';
    if (active(offer) && offer.viewerRole === 'seller' && offer.status === 'PendingSeller') {
      actions = `<div class="offer-actions">
        <button data-offer-action="seller/accept" data-id="${offer.id}">${acceptLabel}</button>
        <button data-offer-action="seller/decline" data-id="${offer.id}">Từ chối</button>
        ${counterForm(offer, 'seller')}
      </div>`;
    } else if (active(offer) && offer.viewerRole === 'buyer' && offer.status === 'PendingBuyer') {
      const counter =
        offer.buyerOfferCount < offer.buyerOfferLimit
          ? counterForm(offer, 'buyer')
          : '<small>Đã dùng hết lượt trả giá cho sản phẩm này.</small>';
      actions = `<div class="offer-actions">
        <button data-offer-action="buyer/accept" data-id="${offer.id}">${acceptLabel}</button>
        <button data-offer-action="buyer/decline" data-id="${offer.id}">Từ chối</button>
        ${counter}
      </div>`;
    }
    if (active(offer) && offer.viewerRole === 'buyer' && !payLater(offer)) {
      actions += `<form data-offer-payment="${offer.id}" class="offer-form">
        ${cardFields('Thẻ thử dùng khi chốt đơn')}
        <button>Cập nhật thẻ thử</button>
      </form>`;
    }
    let orderNote = '';
    if (offer.orderId)
      orderNote = payLater(offer)
        ? `<p>Đơn hàng #${offer.orderId} đang chờ thanh toán. Người mua thanh toán trong tab
            Đơn hàng trước hạn; quá hạn người bán có thể hủy và người mua bị cộng điểm phạt.</p>`
        : `<p>Đơn hàng #${offer.orderId} đã được tạo và thanh toán.</p>`;
    return `<article class="offer-card">
      <div class="offer-card-head">
        <strong>${esc(offer.productTitle)}</strong><span>${statusName(offer)}</span>
      </div>
      <p>Đề nghị hiện tại: <strong>${money(offer.currentAmount)}</strong>
        · Hết hạn ${date(offer.expiresAt)}</p>
      <p>Thanh toán: ${paymentName(offer)}</p>
      ${role === 'buyer'
        ? `<p>Lượt trả giá đã dùng cho sản phẩm:
            ${offer.buyerOfferCount}/${offer.buyerOfferLimit}</p>`
        : ''}
      <ul class="offer-turns">${turns}</ul>${actions}
      ${orderNote}
    </article>`;
  }

  document.addEventListener('change', (event) => {
    const radio = event.target.closest('[data-offer-create] input[name="paymentMethod"]');
    if (!radio) return;
    const form = radio.closest('form');
    form.querySelector('[data-card-fields]').hidden = radio.value !== 'Card' || !radio.checked;
  });

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
        const autoPay = values.get('paymentMethod') === 'Card';
        await api('best-offers', 'POST', {
          productId: Number(create.dataset.offerCreate),
          amount: Number(values.get('amount')),
          addressId: Number(values.get('addressId')),
          paymentMethod: autoPay ? 'Card' : 'PayLater',
          cardProfile: autoPay ? values.get('cardProfile') : null,
          cardExpiry: autoPay ? values.get('cardExpiry') : null,
          key: keyFor(form),
        });
        pendingKeys.delete(form);
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
        const path = `best-offers/${counter.dataset.id}/${counter.dataset.offerCounter}/counter`;
        await api(path, 'POST', {
          amount: Number(values.get('amount')),
          key: keyFor(form),
        });
        pendingKeys.delete(form);
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
      await api(`best-offers/${button.dataset.id}/${button.dataset.offerAction}`, 'POST', {
        key: keyFor(button),
      });
      pendingKeys.delete(button);
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
