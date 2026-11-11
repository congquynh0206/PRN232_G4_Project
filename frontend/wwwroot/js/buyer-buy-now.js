// Buy It Now inside the "Thanh toán" tab (/Buyer?tab=checkout&product={id}#checkout).
// One product, never touches the cart. The cart module owns the shared checkout UI (address
// book, payment methods, totals, pay button) and hands control here while this mode is on.
// Normal products: "Xác nhận và thanh toán" creates the order (fixed checkout key) and pays
// at once by credit card, PayPal or Klarna; a declined card leaves the order in "Đơn hàng".
// Immediate products: credit card only; the order exists only after the payment is captured.
(() => {
  const { $, api, esc, money, message, productThumbnail } = G4;
  const prefix = 'buyer/immediate-checkouts';
  G4.BuyNow = { create };

  function create({ book, tab, loadOrders, showOrder, quoteStatus, onChange }) {
    const params = new URLSearchParams(location.search);
    let productId = Number(params.get('product')) || null;
    let product = null;
    let loadError = '';
    let quantity = 1;
    let coupon = '';
    let quote = null;
    let quoteKey = null;
    let quoteRequest = 0;
    // A retry of the same quote reuses the key, so the server returns the same order.
    let checkoutKey = crypto.randomUUID();
    // Immediate session: kept in the URL so a refresh can recover an unclear payment.
    let session = params.get('checkout');
    let sessionInputs = null;
    let sessionExpiresAt = 0;
    let payKey = null;
    let uncertain = false;

    const active = () => productId != null || session != null;
    const immediate = () => !!product?.immediatePaymentRequired;
    const soldOut = () => !!product && product.available <= 0;
    const maxQuantity = () => Math.max(1, Math.min(5, product?.available || 1));
    const inputsKey = () => JSON.stringify([book.selectedKey(), quantity, coupon]);
    const ready = (addressKey) =>
      !!quote && quoteKey === addressKey && !uncertain && !soldOut();

    function body(code = coupon) {
      return {
        addressId: book.selectedId(),
        items: [{ productId: product.id, quantity }],
        couponCode: code || null,
        checkoutKey,
      };
    }

    function saveUrl() {
      const url = new URL(location.href);
      for (const name of ['product', 'checkout']) url.searchParams.delete(name);
      if (productId) url.searchParams.set('product', productId);
      if (session) url.searchParams.set('checkout', session);
      history.replaceState(null, '', url);
    }

    // Only cart pages show these; Buy It Now has its own back link and method rules.
    function applyMode() {
      const on = active();
      document.querySelectorAll('[data-cart-only]').forEach((x) => (x.hidden = on));
      document.querySelectorAll('[data-buy-now-only]').forEach((x) => (x.hidden = !on));
      const only = on && immediate();
      for (const id of ['cart-method-paypal', 'cart-method-klarna']) {
        const option = $(id)?.closest('label');
        if (option) option.hidden = only;
      }
      $('immediate-method-note').hidden = !only;
      if (only && !$('cart-method-card').checked) {
        $('cart-method-card').checked = true;
        $('card-fields').classList.toggle('hidden', false);
      }
      $('buy-now-check').hidden = !on || !uncertain;
    }

    function html(busy) {
      if (!product)
        return loadError
          ? `<p class="cart-stock-error">${esc(loadError)}</p>`
          : '<p class="small">Đang tải sản phẩm…</p>';
      const off = busy || uncertain ? ' disabled' : '';
      const options = Array.from({ length: maxQuantity() }, (_, i) => i + 1)
        .map((n) => `<option value="${n}" ${n === quantity ? 'selected' : ''}>${n}</option>`)
        .join('');
      const line = quote?.lines?.find((x) => x.productId === product.id);
      const badge = immediate()
        ? '<span class="product-badge">Yêu cầu thanh toán ngay</span>'
        : '';
      const removeCoupon = coupon
        ? `<button type="button" data-buy-now-input data-buy-now-remove-coupon${off}>
            Bỏ mã</button>`
        : '';
      const quoteHtml = quote
        ? `Phí ship: <strong>${money(quote.shipping)}</strong>`
          + (quote.shippingDiscount > 0
            ? ` <span class="ship-detail">(gốc ${money(quote.shippingBase)}, `
              + `giảm ${money(quote.shippingDiscount)})</span>`
            : '')
          + ` · Tổng đơn: <strong>${money(quote.total)}</strong>`
          + G4.Promotions.breakdownHtml(quote)
        : 'Chọn địa chỉ để xem phí ship và tổng tiền.';
      return `<section class="cart-seller buy-now-item">
        <h3>${esc(product.sellerName || 'Người bán')}</h3>
        <div class="item">
          ${productThumbnail(product.imageUrl, product.title)}
          <div class="item-info">
            <strong>${esc(product.title)}</strong>
            <small>Còn ${product.available} · ${product.weightKg ?? 'Chưa có'} kg/sản phẩm</small>
            ${badge}
            ${soldOut() ? '<p class="cart-stock-error">Sản phẩm đã hết hàng.</p>' : ''}
          </div>
          <label for="buy-now-quantity">SL
            <select id="buy-now-quantity" data-buy-now-input${off}>${options}</select>
          </label>
          ${G4.Promotions.linePriceHtml(line, (product.price ?? 0) * quantity, quote?.promotions)}
        </div>
        <div class="seller-coupon">
          <label for="buy-now-coupon">Mã giảm giá của ${esc(product.sellerName || 'người bán')}</label>
          <div class="coupon-row">
            <input id="buy-now-coupon" data-buy-now-input maxlength="50"
              value="${esc(coupon)}"${off} />
            <button type="button" data-buy-now-input data-buy-now-apply${off}>Áp dụng</button>
          </div>
          <div class="toolbar">
            <button type="button" data-buy-now-input data-buy-now-pick${off}>
              Chọn mã giảm giá</button>
            ${removeCoupon}
          </div>
        </div>
        <div class="seller-quote">${quoteHtml}</div>
      </section>`;
    }

    // Wire the controls rendered by html(); called after every render.
    function bind(requote) {
      const select = $('buy-now-quantity');
      if (!select) return;
      select.onchange = () => {
        quantity = Number(select.value);
        requote();
      };
      // Typing only drops the old quote; re-rendering here would steal the input focus.
      $('buy-now-coupon').oninput = (event) => {
        coupon = event.target.value.trim();
        clear();
        for (const id of ['subtotal', 'discount', 'shipping', 'total'])
          $(id).textContent = 'Chưa có báo giá';
        document.querySelector('.buy-now-item .seller-quote').textContent =
          'Bấm “Áp dụng” để tính lại với mã này.';
      };
      document.querySelector('[data-buy-now-apply]').onclick = requote;
      document.querySelector('[data-buy-now-pick]').onclick = () =>
        G4.safe(() => pickCoupon(requote));
      const remove = document.querySelector('[data-buy-now-remove-coupon]');
      if (remove)
        remove.onclick = () => {
          coupon = '';
          requote();
        };
    }

    async function pickCoupon(requote) {
      const addressKey = book.selectedKey();
      if (!ready(addressKey))
        throw new Error('Chọn địa chỉ và chờ báo giá trước khi kiểm tra mã.');
      const before = quote.total;
      const token = quoteRequest;
      const ensureCurrent = () => {
        if (token !== quoteRequest || addressKey !== book.selectedKey())
          throw new Error('Báo giá đã thay đổi. Hãy mở lại bộ chọn mã.');
      };
      await G4.BuyerOffers.pick(product.sellerId, async (code) => {
        ensureCurrent();
        const result = await api('quote', 'POST', body(code));
        ensureCurrent();
        return { before, after: result.total };
      }, (code) => {
        ensureCurrent();
        coupon = code;
        requote();
      });
    }

    function showTotals(q) {
      for (const id of ['subtotal', 'shipping', 'total']) $(id).textContent = money(q[id]);
      $('discount').textContent = '-' + money(q.discount);
      $('checkout-weight').textContent = q.totalWeightKg + ' kg';
    }

    // Drop the quote; the cart's reset() already cleared the totals on screen.
    function clear() {
      quoteRequest++;
      quote = null;
      quoteKey = null;
      if (!uncertain) payKey = null;
      checkoutKey = crypto.randomUUID();
      $('pay').disabled = true;
    }

    async function load() {
      if (!productId) return;
      try {
        product = await api(`catalog/products/${productId}`);
        quantity = Math.min(quantity, maxQuantity());
        if (soldOut()) message('Sản phẩm đã hết hàng.');
      } catch (error) {
        loadError = error.message || 'Không tải được sản phẩm.';
        message(loadError);
      }
    }

    async function start() {
      await load();
      applyMode();
      if (session) {
        uncertain = true;
        applyMode();
        await recover();
      }
    }

    async function requestQuote() {
      if (!product || soldOut()) return;
      const addressKey = book.selectedKey();
      if (book.selectedId() == null) {
        clear();
        quoteStatus('warn');
        return;
      }
      const token = ++quoteRequest;
      quote = null;
      quoteKey = null;
      $('pay').disabled = true;
      quoteStatus('loading');
      for (const id of ['subtotal', 'discount', 'shipping', 'total'])
        $(id).textContent = 'Đang tính…';
      try {
        const q = await api('quote', 'POST', body());
        if (token !== quoteRequest) return;
        quote = q;
        quoteKey = addressKey;
        showTotals(q);
        quoteStatus('ok');
        onChange();
      } catch (error) {
        if (token !== quoteRequest) return;
        for (const id of ['subtotal', 'discount', 'shipping', 'total'])
          $(id).textContent = 'Chưa có báo giá';
        quoteStatus('error', error.message);
        onChange();
        throw error;
      }
    }

    function card() {
      return { number: $('card-number').value, expiry: $('card-expiry').value.trim() };
    }

    // Leave Buy It Now, then show the paid/created order like the cart does.
    async function finish(orderId) {
      exit();
      tab('orders', false);
      await loadOrders(1);
      if (orderId) await showOrder(orderId);
    }

    async function payNormal(method) {
      const confirmed = {
        ...body(),
        expectedTotal: quote.total,
        pricingFingerprint: quote.pricingFingerprint,
      };
      let order;
      try {
        order = await api('orders', 'POST', confirmed);
      } catch (error) {
        if (error.status === 400 || error.status === 409) {
          clear();
          onChange();
          throw new Error(error.message + ' Hãy cập nhật báo giá rồi thử lại.');
        }
        throw new Error(error.message + ' Hãy kiểm tra Đơn hàng trước khi thử lại.');
      }
      const id = order.id;
      try {
        if (method === 'paypal') {
          const result = await api(`orders/${id}/pay/paypal`, 'POST', {
            key: crypto.randomUUID(),
          });
          if (!result.approvalUrl) throw new Error('Chưa có đường dẫn PayPal.');
          message(`Đã tạo đơn #${id}. Đang chuyển sang PayPal…`, true);
          $('paypal-loading').classList.remove('hidden');
          location.assign(result.approvalUrl);
          return;
        }
        if (method === 'klarna') {
          message(`Đã tạo đơn #${id}. Đang chuyển sang Klarna…`, true);
          await G4.Klarna.begin(id);
          return;
        }
        const result = await api(`orders/${id}/pay/card`, 'POST', {
          ...card(),
          key: crypto.randomUUID(),
        });
        await finish(id);
        if (result.status === 'Succeeded') {
          message(`Đã thanh toán thành công đơn #${id}.`, true);
          return;
        }
        message(
          `Đơn #${id} đã được tạo nhưng chưa thanh toán thành công`
            + `${result.errorCode ? ` (${result.errorCode})` : ''}. `
            + 'Thử lại trong đơn trước hạn giữ hàng.',
        );
      } catch (error) {
        await finish(id);
        throw new Error(`Đã tạo đơn #${id}. ${error.message} Tiếp tục thanh toán trong đơn.`);
      }
    }

    // The session freezes the quote; a new one is needed after any change or expiry.
    async function ensureSession() {
      const inputs = inputsKey();
      if (session && sessionInputs === inputs && Date.now() < sessionExpiresAt) return true;
      const result = await api(prefix, 'POST', {
        productId: product.id,
        quantity,
        addressId: book.selectedId(),
        couponCode: coupon || null,
      });
      session = result.id;
      sessionInputs = inputs;
      sessionExpiresAt = Date.parse(result.expiresAt) || Date.now() + 25 * 60 * 1000;
      payKey = crypto.randomUUID();
      saveUrl();
      if (result.quote.total === quote.total) return true;
      quote = result.quote;
      showTotals(quote);
      onChange();
      message('Tổng tiền vừa thay đổi. Kiểm tra lại rồi bấm “Xác nhận và thanh toán”.');
      return false;
    }

    async function payImmediate() {
      if (!(await ensureSession())) return;
      payKey ??= crypto.randomUUID();
      let result;
      try {
        result = await api(`${prefix}/${session}/pay/card`, 'POST', { ...card(), key: payKey });
      } catch (error) {
        if (error.code === 'OutOfStock') return outOfStock();
        // A failed HTTP response is not proof that the payment was rolled back.
        setUncertain(true);
        throw new Error(`${error.message} Kiểm tra kết quả trước khi thử lại.`);
      }
      handleImmediate(result);
    }

    function setUncertain(value) {
      uncertain = value;
      applyMode();
      onChange();
    }

    async function outOfStock() {
      session = null;
      sessionInputs = null;
      saveUrl();
      message('Out of Stock · Sản phẩm không còn đủ tồn kho. Không có đơn hàng được tạo.');
      await load();
      clear();
      onChange();
    }

    function handleImmediate(result) {
      if (result.status === 'Succeeded') {
        return finish(result.orderId).then(() =>
          message(`Thanh toán thành công. Đơn #${result.orderId} đã chuyển cho người bán.`, true),
        );
      }
      if (['Pending', 'Verifying'].includes(result.status)) {
        setUncertain(true);
        message('Đang đối soát thanh toán. Chưa được thanh toán lại.');
        return;
      }
      payKey = crypto.randomUUID();
      if (result.errorCode === 'OutOfStock') return outOfStock();
      if (result.stage === 'Refunded') {
        session = null;
        sessionInputs = null;
        saveUrl();
        message('Giao dịch đã được hoàn tiền. Không có đơn hàng được tạo. Hãy thử lại.');
        return;
      }
      message(`Thanh toán thất bại: ${result.errorCode}. Bạn có thể thử lại.`);
    }

    // Read the session from the server; reconcile first while the provider is unclear.
    async function recover() {
      if (!session) return;
      let state = await api(`${prefix}/${session}`);
      if (state.attempt && ['Pending', 'Verifying'].includes(state.status)) {
        await api(`${prefix}/${session}/reconcile`, 'POST');
        state = await api(`${prefix}/${session}`);
      }
      if (state.status === 'Succeeded') {
        await finish(state.orderId);
        message(`Đơn #${state.orderId} đã được thanh toán.`, true);
        return;
      }
      if (['Pending', 'Verifying'].includes(state.status)) {
        setUncertain(true);
        message('Đang xác minh thanh toán. Kiểm tra kết quả trước khi thanh toán lại.');
        return;
      }
      // Ready, Failed or Expired: no charge is in flight, so a fresh quote is safe.
      session = null;
      sessionInputs = null;
      saveUrl();
      setUncertain(false);
      if (state.attempt?.status === 'Failed')
        message(`Thanh toán trước đó thất bại: ${state.attempt.errorCode}. Bạn có thể thử lại.`);
    }

    async function pay(method) {
      if (immediate()) await payImmediate();
      else await payNormal(method);
    }

    function exit() {
      if (!active()) return;
      productId = null;
      product = null;
      session = null;
      sessionInputs = null;
      uncertain = false;
      quote = null;
      quoteKey = null;
      coupon = '';
      quantity = 1;
      saveUrl();
      applyMode();
    }

    $('buy-now-check').onclick = () =>
      G4.safe(async () => {
        $('buy-now-check').disabled = true;
        try {
          await recover();
        } finally {
          $('buy-now-check').disabled = false;
        }
      });

    return {
      active,
      immediate,
      ready,
      html,
      bind,
      start,
      clear,
      quote: requestQuote,
      pay,
      exit,
    };
  }
})();
