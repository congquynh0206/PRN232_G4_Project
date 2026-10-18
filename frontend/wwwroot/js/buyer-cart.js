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
            quoteKey: null,
            baseline: null,
        };
        let quoteRequest = 0,
            busy = false,
            addressBusy = false;
        // The order may only be created from a quote that belongs to the address selected right now.
        const canPay = () =>
            !busy &&
            !addressBusy &&
            !!state.quote &&
            book.selectedId() != null &&
            state.quoteKey === book.selectedKey();
        const book = G4.BuyerAddresses.create({
            lock(value) {
                addressBusy = value;
                $('pay').disabled = !canPay();
            },
            onChange() {
                // Address chosen/edited/deleted: drop the old quote at once, then ask the server again.
                reset();
                render();
                safe(recalculate);
            },
        });
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
                addressId: book.selectedId(),
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
            state.quoteKey = null;
            $('checkout-promotions').innerHTML = '';
            for (const id of ['subtotal', 'discount', 'shipping', 'total'])
                $(id).textContent = 'Chưa có báo giá';
            $('checkout-weight').textContent = '—';
        }
        function quoteStatus(kind, detail = '') {
            const box = $('address-quote-status');
            box.className = 'address-quote-status' + (kind === 'ok' ? '' : ' ' + kind);
            if (kind === 'loading')
                box.innerHTML =
                    'Đang tính lại phí ship và tổng thanh toán theo địa chỉ đã chọn… Nút đặt hàng tạm khóa để tránh dùng giá cũ.';
            else if (kind === 'warn')
                box.textContent = 'Chưa có địa chỉ giao hàng. Hãy thêm địa chỉ trước khi thanh toán.';
            else if (kind === 'error')
                box.innerHTML = `Không tính được báo giá cho địa chỉ này: ${esc(detail)} Nút đặt hàng đang bị khóa. Hãy đổi địa chỉ, kiểm tra mã giảm giá hoặc bấm “Cập nhật báo giá”.`;
            else if (kind === 'ok') {
                const a = book.selected();
                box.innerHTML =
                    `Báo giá đã cập nhật cho: <strong>${esc(a?.fullName)}</strong>, ${esc(a?.city)}, ${esc(a?.state)}, ${esc(a?.country)}.` +
                    (detail ? `<ul>${detail}</ul>` : '');
            } else box.textContent = '';
        }
        function snapshot(q, addressKey) {
            return {
                addressKey,
                context: JSON.stringify([state.version, [...state.coupons.entries()]]),
                sellers: new Map(
                    q.sellers.map((s) => [
                        s.sellerId,
                        {
                            name: s.sellerName,
                            base: s.quote.shippingBase,
                            discount: s.quote.shippingDiscount,
                            promotions: new Map(s.quote.promotions.map((p) => [p.id, p])),
                        },
                    ]),
                ),
            };
        }
        // Explain what the new address did to promotions (same cart and coupons, different address only).
        function addressNotes(q, addressKey) {
            const before = state.baseline,
                now = snapshot(q, addressKey);
            if (!before || before.addressKey === addressKey || before.context !== now.context) return '';
            const notes = [];
            for (const [sellerId, old] of before.sellers) {
                const next = now.sellers.get(sellerId);
                if (!next) continue;
                if (old.base !== next.base)
                    notes.push(
                        `<li>${esc(next.name)}: phí ship gốc đổi từ ${money(old.base)} sang ${money(next.base)} theo khu vực giao hàng mới.</li>`,
                    );
                for (const [id, promo] of old.promotions) {
                    const kept = next.promotions.get(id);
                    if (!kept)
                        notes.push(
                            `<li class="note-warn">${esc(next.name)}: khuyến mãi “${esc(promo.name)}” không còn áp dụng với địa chỉ mới` +
                            (promo.type === 'Shipping'
                                ? ` (không còn giảm được phí ship ${money(next.base)} của khu vực này).`
                                : ' vì điều kiện áp dụng đã thay đổi.') +
                            '</li>',
                        );
                    else if (kept.amount !== promo.amount)
                        notes.push(
                            `<li class="note-warn">${esc(next.name)}: “${esc(promo.name)}” giảm ${money(kept.amount)} thay vì ${money(promo.amount)} do phí ship của địa chỉ mới khác.</li>`,
                        );
                }
            }
            return notes.join('');
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
                        return `<section class="cart-seller"><h3>${esc(items[0].sellerName)}</h3>${items.map((x) => itemHtml(x, editable)).join('')}${!editable ? `<div class="seller-coupon"><label for="coupon-${sellerId}">Mã giảm giá của ${esc(items[0].sellerName)}</label><div class="coupon-row"><input id="coupon-${sellerId}" data-seller-coupon="${sellerId}" maxlength="50" value="${esc(state.coupons.get(sellerId) || '')}" ${busy ? 'disabled' : ''}/><button type="button" data-apply-coupon="${sellerId}" ${busy ? 'disabled' : ''}>Áp dụng</button></div></div><div class="seller-quote">${q ? `Phí ship: <strong>${money(q.shipping)}</strong>${q.shippingDiscount > 0 ? ` <span class="ship-detail">(gốc ${money(q.shippingBase)}, giảm ${money(q.shippingDiscount)})</span>` : ''} · Tổng đơn: <strong>${money(q.total)}</strong>${G4.Promotions.breakdownHtml(q)}` : 'Cập nhật báo giá để xem phí ship và tổng của seller.'}</div>` : ''}</section>`;
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
            $('pay').disabled = !canPay();
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
                'refresh-quote',
            ])
                $(id).disabled = value;
            book.setDisabled(value);
            document
                .querySelectorAll(
                    '[data-quantity],[data-remove],[data-seller-coupon],[data-apply-coupon],input[name="method"],#card-number,#card-expiry',
                )
                .forEach((x) => (x.disabled = value));
            $('continue').disabled = value || !state.items.length;
            $('pay').disabled = !canPay();
        }
        function accept(cart) {
            state.items = cart.items;
            state.version = cart.version;
            state.baseline = null;
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
            if (!state.items.length) return;
            const addressKey = book.selectedKey();
            if (book.selectedId() == null) {
                reset();
                quoteStatus('warn');
                return;
            }
            // Only the newest request may write the result; older responses are ignored.
            const token = ++quoteRequest;
            state.quote = null;
            state.quoteKey = null;
            $('pay').disabled = true;
            $('checkout-items').setAttribute('aria-busy', 'true');
            quoteStatus('loading');
            for (const id of ['subtotal', 'discount', 'shipping', 'total'])
                $(id).textContent = 'Đang tính…';
            try {
                const q = await api('cart/quote', 'POST', request());
                if (token !== quoteRequest) return;
                state.quote = q;
                state.quoteKey = addressKey;
                for (const id of ['subtotal', 'shipping', 'total']) $(id).textContent = money(q[id]);
                $('discount').textContent = '-' + money(q.discount);
                $('checkout-weight').textContent = q.totalWeightKg + ' kg';
                const notes = addressNotes(q, addressKey);
                state.baseline = snapshot(q, addressKey);
                quoteStatus('ok', notes);
                render();
            } catch (error) {
                if (token === quoteRequest) {
                    state.quote = null;
                    state.quoteKey = null;
                    for (const id of ['subtotal', 'discount', 'shipping', 'total'])
                        $(id).textContent = 'Chưa có báo giá';
                    quoteStatus('error', error.message);
                    render();
                    throw error;
                }
            } finally {
                if (token === quoteRequest) $('checkout-items').setAttribute('aria-busy', 'false');
            }
        }
        async function pay() {
            if (!canPay()) return;
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
            if (!book.count()) book.openManage(true);
        };
        $('pay').onclick = () => safe(pay);
        $('refresh-quote').onclick = () =>
            safe(async () => {
                reset();
                await recalculate();
            });
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
        return safe(async () => {
            setBusy(true);
            try {
                await book.load();
                accept(await api('cart'));
                await recalculate();
            } finally {
                setBusy(false);
            }
        });
    }
})();
