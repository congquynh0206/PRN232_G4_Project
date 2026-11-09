(() => {
  const { $, api, esc, money } = window.G4;
  const params = new URLSearchParams(location.search);
  const productId = Number(params.get('product')) || null;
  const fields = ['buy-quantity', 'buy-address', 'buy-coupon'];
  let selected;
  let session = params.get('checkout');
  let key;
  let busy = false;
  let uncertain = false;
  let expired = false;
  const prefix = 'buyer/immediate-checkouts';
  const message = (text) => { $('message').textContent = text; };

  // Keep product and session in the URL so refresh restores the same checkout.
  function saveUrl() {
    const query = new URLSearchParams();
    if (productId) query.set('product', productId);
    if (session) query.set('checkout', session);
    history.replaceState(null, '', `/Buyer/Checkout?${query}`);
  }

  function showProduct(product) {
    const soldOut = product.available <= 0;
    $('checkout-product').innerHTML = `
      <article class="immediate-product">
        <h3>${esc(product.title)}</h3><p>${esc(product.description)}</p>
        <p>${money(product.price)} · Còn ${product.available}</p>
        <span class="immediate-badge">${product.immediatePaymentRequired
          ? 'Immediate Payment Required' : 'Normal Payment'}</span>
      </article>`;
    $('review-checkout').textContent = product.immediatePaymentRequired
      ? 'Xem báo giá' : 'Đặt hàng (Normal Payment)';
    if (soldOut && !session) {
      $('review-checkout').disabled = true;
      message('Sold Out · Sản phẩm đã hết hàng.');
    }
  }

  function showQuote(snapshot) {
    const q = snapshot.quote;
    $('checkout-quote').innerHTML = `
      <p>${esc(snapshot.title)} · Số lượng: ${snapshot.quantity}</p>
      <dl>
        <div><dt>Tiền hàng</dt><dd>${money(q.subtotal)}</dd></div>
        <div><dt>Giảm giá</dt><dd>-${money(q.discount)}</dd></div>
        <div><dt>Phí vận chuyển</dt><dd>${money(q.shipping)}</dd></div>
        <div class="grand"><dt>Tổng</dt><dd>${money(q.total)}</dd></div>
      </dl>`;
    $('immediate-payment').hidden = false;
    $('payment-success').hidden = true;
  }

  function succeeded(orderId) {
    uncertain = false;
    $('immediate-payment').hidden = true;
    $('buy-now-form').hidden = true;
    $('checkout-content').hidden = true;
    $('payment-success').hidden = false;
    $('paid-order').textContent = `Đơn #${orderId} đã thanh toán và chuyển cho Seller.`;
    message('Payment Success');
    // Keep the paid session in the URL so refresh cannot resubmit the charge.
  }

  async function recover() {
    if (!session) return;
    $('pay-now').disabled = true;
    let state = await api(`${prefix}/${session}`);
    if (state.attempt && ['Pending', 'Verifying'].includes(state.status)) {
      await api(`${prefix}/${session}/reconcile`, 'POST');
      state = await api(`${prefix}/${session}`);
    }
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
    expired = state.status === 'Expired';
    $('pay-now').disabled = uncertain || busy || expired;
    $('review-checkout').disabled = uncertain;
    for (const id of fields) $(id).disabled = uncertain;
    $('check-payment').hidden = !uncertain;
    if (uncertain) message('Đang xác minh. Kiểm tra kết quả trước khi thanh toán lại.');
    if (expired) message('Checkout đã hết hạn. Bấm “Xem báo giá” để lấy báo giá mới.');
  }

  async function loadProduct() {
    if (!productId) return;
    selected = await api(`catalog/products/${productId}`);
    showProduct(selected);
  }

  async function load() {
    const book = await api('addresses');
    const addresses = Array.isArray(book) ? book : book.addresses;
    $('buy-address').innerHTML = addresses.map((a) =>
      `<option value="${a.id}" ${a.isDefault ? 'selected' : ''}>
        ${esc([a.fullName, a.street, a.city, a.state, a.country].join(', '))}</option>`,
    ).join('');
    if (!addresses.length) message('Bạn chưa có địa chỉ giao hàng. Hãy thêm địa chỉ trước.');
    if (!productId && !session) {
      $('review-checkout').disabled = true;
      message('Chưa chọn sản phẩm. Quay lại danh sách để chọn Buy It Now.');
    }
    await loadProduct();
  }

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
      expired = false;
      key = crypto.randomUUID();
      saveUrl();
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

  for (const id of fields) {
    $(id).addEventListener('input', () => {
      if (busy || uncertain) return;
      session = null;
      key = null;
      $('immediate-payment').hidden = true;
      saveUrl();
      message('Thông tin checkout đã thay đổi. Lấy báo giá mới trước khi Pay Now.');
    });
  }

  $('pay-now-form').addEventListener('submit', async (event) => {
    event.preventDefault();
    if (busy || uncertain || expired || !session) return;
    busy = true;
    $('pay-now').disabled = true;
    $('review-checkout').disabled = true;
    $('pay-now').textContent = 'Đang thanh toán…';
    for (const id of fields) $(id).disabled = true;
    message('Đang xử lý thanh toán…');
    try {
      const result = await api(`${prefix}/${session}/pay/card`, 'POST', {
        number: $('card-number').value.trim(), expiry: $('card-expiry').value.trim(), key,
      });
      if (result.status === 'Succeeded') {
        succeeded(result.orderId);
      } else if (['Pending', 'Verifying'].includes(result.status)) {
        uncertain = true;
        $('check-payment').hidden = false;
        message('Đang đối soát authorization/Capture/Void/Refund. Chưa được thanh toán lại.');
      } else if (result.errorCode === 'OutOfStock') {
        $('immediate-payment').hidden = true;
        message('Out of Stock · Authorization đã được Void. Không có đơn hàng được tạo.');
        key = crypto.randomUUID();
        await loadProduct().catch(() => {});
      } else if (result.stage === 'Refunded') {
        message('Giao dịch đã được hoàn tiền. Không có đơn hàng được tạo. Vui lòng lấy báo giá mới.');
        key = crypto.randomUUID();
        $('immediate-payment').hidden = true;
      } else {
        message(`Payment Failed: ${result.errorCode}. Bạn có thể thử lại.`);
        key = crypto.randomUUID();
      }
    } catch (error) {
      if (error.code === 'OutOfStock') {
        $('immediate-payment').hidden = true;
        message('Out of Stock · Sản phẩm không còn đủ tồn kho. Không có đơn PAID được tạo.');
        await loadProduct().catch(() => {});
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
      for (const id of fields) $(id).disabled = uncertain;
      $('pay-now').textContent = 'Pay Now';
    }
  });
  $('check-payment').addEventListener('click', () => {
    recover().catch((error) => message(error.message));
  });
  (async () => {
    await load();
    if (session) {
      uncertain = true;
      await recover();
    }
  })().catch((error) => message(error.message));
})();
