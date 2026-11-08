(() => {
  const { $, api, esc, money } = window.G4;
  let products = [];
  let selected;
  let session;
  let key;
  let busy = false;
  let uncertain = false;
  const prefix = 'buyer/immediate-checkouts';
  const message = (text) => { $('message').textContent = text; };

  function showQuote(snapshot) {
    const q = snapshot.quote;
    $('checkout-quote').innerHTML = `
      <h3>${esc(snapshot.title)}</h3><p>Số lượng: ${snapshot.quantity}</p>
      <p>Giá hàng: ${money(q.subtotal)} · Giảm giá: ${money(q.discount)}</p>
      <p>Phí vận chuyển: ${money(q.shipping)}</p><strong>Tổng: ${money(q.total)}</strong>`;
    $('immediate-payment').hidden = false;
    $('payment-success').hidden = true;
  }

  function succeeded(orderId) {
    uncertain = false;
    $('immediate-payment').hidden = true;
    $('buy-now-form').hidden = true;
    $('payment-success').hidden = false;
    $('paid-order').textContent = `Đơn #${orderId} đã thanh toán và chuyển cho Seller.`;
    message('Payment Success');
    // Keep the paid session in the URL so refresh cannot resubmit the charge.
  }

  async function recover() {
    if (!session) return;
    $('pay-now').disabled = true;
    const state = await api(`${prefix}/${session}`);
    if (state.status === 'Succeeded') {
      succeeded(state.orderId);
      return;
    }
    showQuote(state.snapshot);
    key = state.attempt?.key || key || crypto.randomUUID();
    if (state.attempt?.status === 'Failed') {
      message(`Payment Failed: ${state.attempt.errorCode}. Bạn có thể thử lại.`);
      key = crypto.randomUUID();
    }
    uncertain = ['Pending', 'Verifying'].includes(state.status);
    $('pay-now').disabled = uncertain || busy || state.status === 'Expired';
    $('review-checkout').disabled = uncertain;
    $('check-payment').hidden = !uncertain;
    if (uncertain) message('Đang xác minh. Kiểm tra kết quả trước khi thanh toán lại.');
    if (state.status === 'Expired') message('Checkout đã hết hạn. Chọn sản phẩm để lấy báo giá mới.');
  }

  async function load() {
    products = await api('catalog/products');
    $('products-list').innerHTML = products.map((p) => `
      <article class="immediate-product">
        <h2>${esc(p.title)}</h2><p>${esc(p.description)}</p>
        <p>${money(p.price)} · Còn ${p.available}</p>
        <span class="immediate-badge">${p.immediatePaymentRequired
          ? 'Immediate Payment Required' : 'Normal Payment'}</span>
        <button type="button" data-buy="${p.id}" ${p.available <= 0 ? 'disabled' : ''}>
          ${p.available <= 0 ? 'Sold Out' : 'Buy It Now'}</button>
      </article>`).join('');
    const book = await api('addresses');
    const addresses = Array.isArray(book) ? book : book.addresses;
    $('buy-address').innerHTML = addresses.map((a) =>
      `<option value="${a.id}" ${a.isDefault ? 'selected' : ''}>
        ${esc([a.fullName, a.street, a.city, a.state, a.country].join(', '))}</option>`,
    ).join('');
  }

  $('products-list').addEventListener('click', (event) => {
    const button = event.target.closest('[data-buy]');
    if (!button || busy || uncertain) return;
    selected = products.find((p) => p.id === Number(button.dataset.buy));
    session = null;
    key = null;
    history.replaceState(null, '', '/Buyer/Products');
    $('checkout-title').textContent = selected.title;
    $('buy-now-form').hidden = false;
    $('immediate-payment').hidden = true;
    $('payment-success').hidden = true;
    $('buy-now-form').scrollIntoView({ behavior: 'smooth' });
  });

  $('buy-now-form').addEventListener('submit', async (event) => {
    event.preventDefault();
    if (busy || uncertain || !selected) return;
    busy = true;
    $('review-checkout').disabled = true;
    try {
      const input = {
        productId: selected.id, quantity: Number($('buy-quantity').value),
        addressId: Number($('buy-address').value), couponCode: $('buy-coupon').value || null,
      };
      if (!selected.immediatePaymentRequired) {
        const checkout = {
          addressId: input.addressId,
          items: [{ productId: input.productId, quantity: input.quantity }],
          couponCode: input.couponCode, checkoutKey: crypto.randomUUID(),
        };
        const quote = await api('quote', 'POST', checkout);
        // Normal Payment remains the existing unpaid-order workflow.
        await api('orders', 'POST', {
          ...checkout, expectedTotal: quote.total, pricingFingerprint: quote.pricingFingerprint,
        });
        location.assign('/Buyer?tab=orders');
        return;
      }
      const result = await api(prefix, 'POST', input);
      session = result.id;
      key = crypto.randomUUID();
      history.replaceState(null, '', `?checkout=${encodeURIComponent(session)}`);
      showQuote({ title: result.title, quantity: input.quantity, quote: result.quote });
      $('pay-now').disabled = false;
      $('check-payment').hidden = true;
      message('Kiểm tra tổng tiền rồi nhấn Pay Now.');
    } catch (error) { message(error.message); }
    finally {
      busy = false;
      $('review-checkout').disabled = false;
    }
  });

  $('pay-now-form').addEventListener('submit', async (event) => {
    event.preventDefault();
    if (busy || uncertain || !session) return;
    busy = true;
    $('pay-now').disabled = true;
    $('review-checkout').disabled = true;
    $('pay-now').textContent = 'Đang thanh toán…';
    message('Đang xử lý thanh toán…');
    try {
      const result = await api(`${prefix}/${session}/pay/card`, 'POST', {
        number: $('card-number').value.trim(), expiry: $('card-expiry').value.trim(), key,
      });
      if (result.status === 'Succeeded') {
        succeeded(result.orderId);
        await load();
      } else {
        message(`Payment Failed: ${result.errorCode}. Bạn có thể thử lại.`);
        key = crypto.randomUUID();
      }
    } catch (error) {
      if (error.code === 'OutOfStock') {
        $('immediate-payment').hidden = true;
        message('Out of Stock · Sản phẩm không còn đủ tồn kho. Không có đơn PAID được tạo.');
        await load().catch(() => {});
        return;
      }
      // A failed HTTP response is not proof that the SQL transaction rolled back.
      uncertain = true;
      $('check-payment').hidden = false;
      message(`${error.message} Kiểm tra kết quả trước khi thử lại.`);
    } finally {
      $('card-number').value = '';
      $('card-expiry').value = '';
      busy = false;
      $('pay-now').disabled = uncertain;
      $('review-checkout').disabled = uncertain;
      $('pay-now').textContent = 'Pay Now';
    }
  });
  $('check-payment').addEventListener('click', () => {
    recover().catch((error) => message(error.message));
  });
  (async () => {
    await load();
    session = new URLSearchParams(location.search).get('checkout');
    if (session) {
      uncertain = true;
      await recover();
    }
  })().catch((error) => message(error.message));
})();
