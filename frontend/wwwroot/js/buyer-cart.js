(() => {
  const { $, money, esc, api, message, safe, productThumbnail } = G4;
  G4.BuyerCart = { init };
  function init({ tab, loadOrders, showOrder }) {
    const state = {
      items: [],
      version: null,
      quote: null,
      key: crypto.randomUUID(),
      coupons: new Map(),
      attempt: null,
    };
    let quoteRequest = 0,
      busy = false,
      addresses = [];
    function groups() {
      const result = new Map();
      for (const item of state.items) {
        if (!result.has(item.sellerId)) result.set(item.sellerId, []);
        result.get(item.sellerId).push(item);
      }
      return [...result.entries()];
    }
    function request(confirmed = false) {
      return {
        addressId: Number($('address').value),
        cartVersion: state.version,
        checkoutKey: state.key,
        sellers: groups().map(([sellerId]) => {
          const q = state.quote?.sellers.find((x) => x.sellerId === sellerId)?.quote;
          return {
            sellerId,
            couponCode: state.coupons.get(sellerId) || null,
            ...(confirmed
              ? { expectedTotal: q.total, pricingFingerprint: q.pricingFingerprint }
              : {}),
          };
        }),
      };
    }
    function reset() {
      quoteRequest++;
      state.quote = null;
      state.key = crypto.randomUUID();
      state.attempt = null;
      $('pay').disabled = true;
      $('checkout-promotions').innerHTML = '';
      for (const id of ['subtotal', 'discount', 'shipping', 'total'])
        $(id).textContent = 'Chưa có báo giá';
      $('checkout-weight').textContent = '—';
    }
    function quoteFor(item) {
      return state.quote?.sellers.find((x) => x.sellerId === item.sellerId)?.quote;
    }
    function itemHtml(item, editable) {
      const q = quoteFor(item),
        line = q?.lines?.find((x) => x.productId === item.productId);
      const limit = Math.max(item.quantity, Math.min(item.available, 5));
      const quantity = editable
        ? `<label for="quantity-${item.productId}">SL <select id="quantity-${item.productId}" data-quantity="${item.productId}" ${busy ? 'disabled' : ''}>${Array.from({ length: limit }, (_, i) => `<option value="${i + 1}" ${item.quantity === i + 1 ? 'selected' : ''}>${i + 1}</option>`).join('')}</select></label><button type="button" data-remove="${item.productId}" aria-label="Xóa ${esc(item.title)} khỏi giỏ" ${busy ? 'disabled' : ''}>Xóa</button>`
        : `<span>SL ${item.quantity}</span>`;
      return `<div class="item">${productThumbnail(item.imageUrl, item.title)}<div class="item-info"><strong>${esc(item.title)}</strong><small>Còn ${item.available} · ${item.weightKg ?? 'Chưa có'} kg/sản phẩm</small>${item.quantity > item.available ? '<p class="cart-stock-error">Không đủ tồn kho. Giảm số lượng hoặc xóa sản phẩm.</p>' : ''}</div>${quantity}${G4.Promotions.linePriceHtml(line, (item.price ?? 0) * item.quantity, q?.promotions)}</div>`;
    }
    function render() {
      const html = (editable) =>
        groups()
          .map(([sellerId, items]) => {
            const q = quoteFor(items[0]);
            return `<section class="cart-seller"><h3>${esc(items[0].sellerName)}</h3>${items.map((x) => itemHtml(x, editable)).join('')}${!editable ? `<div class="seller-coupon"><label for="coupon-${sellerId}">Mã giảm giá của ${esc(items[0].sellerName)}</label><div class="coupon-row"><input id="coupon-${sellerId}" data-seller-coupon="${sellerId}" maxlength="50" value="${esc(state.coupons.get(sellerId) || '')}" ${busy ? 'disabled' : ''}/><button type="button" data-apply-coupon="${sellerId}" ${busy ? 'disabled' : ''}>Áp dụng</button></div></div><div class="seller-quote">${q ? `Phí ship: <strong>${money(q.shipping)}</strong> · Tổng đơn: <strong>${money(q.total)}</strong>${G4.Promotions.breakdownHtml(q)}` : 'Cập nhật báo giá để xem phí ship và tổng của seller.'}</div>` : ''}</section>`;
          })
          .join('');
      $('cart').classList.toggle('empty', !state.items.length);
      $('cart').innerHTML = state.items.length
        ? html(true)
        : 'Giỏ đang trống. Tạo giỏ ngẫu nhiên để bắt đầu.';
      $('checkout-items').innerHTML = html(false);
      $('cart-subtotal').textContent = state.quote
        ? 'Tiền hàng sau giảm: ' + money(state.quote.subtotal - state.quote.discount)
        : 'Tạm tính: ' + money(state.items.reduce((n, x) => n + (x.price ?? 0) * x.quantity, 0));
      $('continue').disabled = busy || !state.items.length;
      $('pay').disabled = busy || !state.quote;
      document.querySelectorAll('[data-quantity]').forEach(
        (control) =>
          (control.onchange = () =>
            safe(() =>
              save(
                state.items.map((x) => ({
                  productId: x.productId,
                  quantity:
                    x.productId === Number(control.dataset.quantity)
                      ? Number(control.value)
                      : x.quantity,
                })),
              ),
            )),
      );
      document
        .querySelectorAll('[data-remove]')
        .forEach(
          (control) =>
            (control.onclick = () =>
              safe(() =>
                save(
                  state.items
                    .filter((x) => x.productId !== Number(control.dataset.remove))
                    .map((x) => ({ productId: x.productId, quantity: x.quantity })),
                ),
              )),
        );
      document.querySelectorAll('[data-seller-coupon]').forEach(
        (control) =>
          (control.oninput = () => {
            state.coupons.set(Number(control.dataset.sellerCoupon), control.value.trim());
            reset();
            $('cart-subtotal').textContent =
              'Tạm tính: ' +
              money(state.items.reduce((n, x) => n + (x.price ?? 0) * x.quantity, 0));
            document
              .querySelectorAll('.seller-quote')
              .forEach((x) => (x.textContent = 'Áp dụng mã hoặc cập nhật báo giá để tính lại.'));
          }),
      );
      document.querySelectorAll('[data-apply-coupon]').forEach(
        (control) =>
          (control.onclick = () =>
            safe(async () => {
              reset();
              await recalculate();
            })),
      );
    }
    function setBusy(value) {
      busy = value;
      $('cart').setAttribute('aria-busy', String(value));
      for (const id of [
        'random',
        'count',
        'reload-cart',
        'address',
        'edit-address',
        'save-address',
        'refresh-quote',
      ])
        $(id).disabled = value;
      document
        .querySelectorAll(
          '[data-quantity],[data-remove],[data-seller-coupon],[data-apply-coupon],#address-fields input,input[name="method"],#card-number,#card-expiry',
        )
        .forEach((x) => (x.disabled = value));
      $('continue').disabled = value || !state.items.length;
      $('pay').disabled = value || !state.quote;
    }
    function accept(cart) {
      state.items = cart.items;
      state.version = cart.version;
      reset();
      render();
    }
    async function reload() {
      if (busy) return;
      setBusy(true);
      try {
        accept(await api('cart'));
        await recalculate();
      } finally {
        setBusy(false);
      }
    }
    async function save(items) {
      if (busy) return;
      setBusy(true);
      reset();
      try {
        accept(await api('cart/save', 'POST', { version: state.version, items }));
        await recalculate();
        message('Đã lưu giỏ hàng.', true);
      } catch (error) {
        accept(await api('cart'));
        throw error;
      } finally {
        setBusy(false);
        render();
      }
    }
    async function random() {
      if (busy) return;
      setBusy(true);
      reset();
      try {
        accept(
          await api('cart/random', 'POST', {
            version: state.version,
            count: Number($('count').value),
          }),
        );
        state.coupons.clear();
        await recalculate();
        message(`Đã lưu ${state.items.length} sản phẩm từ 2 seller.`, true);
      } catch (error) {
        accept(await api('cart'));
        throw error;
      } finally {
        setBusy(false);
        render();
      }
    }
    async function recalculate() {
      if (!state.items.length || !$('address').value) return;
      const token = ++quoteRequest;
      state.quote = null;
      $('pay').disabled = true;
      $('checkout-items').setAttribute('aria-busy', 'true');
      for (const id of ['subtotal', 'discount', 'shipping', 'total'])
        $(id).textContent = 'Đang tính…';
      try {
        const q = await api('cart/quote', 'POST', request());
        if (token !== quoteRequest) return;
        state.quote = q;
        for (const id of ['subtotal', 'shipping', 'total']) $(id).textContent = money(q[id]);
        $('discount').textContent = '-' + money(q.discount);
        $('checkout-weight').textContent = q.totalWeightKg + ' kg';
        render();
      } catch (error) {
        if (token === quoteRequest) {
          state.quote = null;
          for (const id of ['subtotal', 'discount', 'shipping', 'total'])
            $(id).textContent = 'Chưa có báo giá';
          render();
          throw error;
        }
      } finally {
        if (token === quoteRequest) $('checkout-items').setAttribute('aria-busy', 'false');
      }
    }
    async function pay() {
      if (busy || !state.quote) return;
      setBusy(true);
      $('pay').textContent = 'Đang tạo đơn…';
      let orders = null;
      const method = document.querySelector('input[name="method"]:checked').value;
      try {
        state.attempt ??= request(true);
        orders = await api('cart/checkout', 'POST', state.attempt);
        const checkoutKey = state.attempt.checkoutKey;
        accept(await api('cart'));
        tab('orders', false);
        await loadOrders(1);
        const pending = orders.filter((x) => x.status === 'AwaitingPayment');
        if (method === 'paypal' && pending.length) {
          message(`Đã tạo ${orders.length} đơn. PayPal sẽ xác nhận thanh toán cả giỏ trong một lượt.`, true);
          $('paypal-loading').classList.remove('hidden');
          await G4.PayPalCart.begin(checkoutKey);
          return;
        }
        const results = [];
        for (const order of pending) {
          try {
            const result = await api(`orders/${order.id}/pay/card`, 'POST', {
              number: $('card-number').value,
              expiry: $('card-expiry').value,
              key: crypto.randomUUID(),
            });
            results.push({ id: order.id, status: result.status });
          } catch (error) {
            results.push({ id: order.id, status: 'Failed' });
          }
        }
        const failed = results.filter((x) => x.status !== 'Succeeded');
        message(
          failed.length
            ? `Đã tạo ${orders.length} đơn. Đơn ${failed.map((x) => '#' + x.id).join(', ')} chưa thanh toán thành công; thử lại trong Đơn của tôi.`
            : `Đã thanh toán thành công ${orders.length} đơn.`,
          !failed.length,
        );
        await loadOrders(1);
        if (orders.length) await showOrder(orders[0].id);
      } catch (error) {
        if (orders) {
          tab('orders', false);
          await loadOrders(1);
          throw new Error(
            `Đã tạo đơn ${orders.map((x) => '#' + x.id).join(', ')}. ${error.message} Tiếp tục thanh toán trong Đơn của tôi.`,
          );
        }
        if (error.status === 400 || error.status === 409) {
          reset();
          render();
          throw new Error(error.message + ' Vui lòng tải lại giỏ và cập nhật báo giá.');
        }
        throw new Error(error.message + ' Hãy kiểm tra Đơn của tôi hoặc thử lại với giỏ hiện tại.');
      } finally {
        setBusy(false);
        $('pay').textContent = 'Đặt hàng & thanh toán';
        $('paypal-loading').classList.add('hidden');
      }
    }
    $('random').onclick = () => safe(random);
    $('reload-cart').onclick = () => safe(reload);
    $('continue').onclick = () => {
      tab('checkout');
      safe(recalculate);
    };
    $('pay').onclick = () => safe(pay);
    $('refresh-quote').onclick = () =>
      safe(async () => {
        reset();
        await recalculate();
      });
    $('address').onchange = () => {
      reset();
      safe(recalculate);
    };
    document
      .querySelectorAll('input[name="method"]')
      .forEach(
        (r) =>
          (r.onchange = () =>
            $('card-fields').classList.toggle(
              'hidden',
              document.querySelector('input[name="method"]:checked').value !== 'card',
            )),
      );
    $('edit-address').onclick = () => {
      const a = addresses.find((x) => x.id === Number($('address').value));
      if (!a) return;
      ['name', 'street', 'city', 'state', 'country'].forEach(
        (k) => ($('address-' + k).value = k === 'name' ? a.fullName : a[k] || ''),
      );
      $('address-fields').classList.toggle('hidden');
    };
    $('save-address').onclick = () =>
      safe(async () => {
        if (busy) return;
        setBusy(true);
        try {
          const id = Number($('address').value);
          const a = await api(`addresses/${id}/edit`, 'POST', {
            fullName: $('address-name').value,
            street: $('address-street').value,
            city: $('address-city').value,
            state: $('address-state').value,
            country: $('address-country').value,
          });
          addresses = addresses.map((x) => (x.id === id ? a : x));
          $('address-fields').classList.add('hidden');
          reset();
          await recalculate();
          message('Đã lưu địa chỉ.', true);
        } finally {
          setBusy(false);
        }
      });
    return safe(async () => {
      setBusy(true);
      try {
        addresses = await api('addresses');
        $('address').innerHTML = addresses
          .map(
            (a) =>
              `<option value="${a.id}" ${a.isDefault ? 'selected' : ''}>${esc(a.fullName)} — ${esc(a.street)}, ${esc(a.city)}</option>`,
          )
          .join('');
        accept(await api('cart'));
        await recalculate();
      } finally {
        setBusy(false);
      }
    });
  }
})();
