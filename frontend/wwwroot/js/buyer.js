(() => {
  const {
    $,
    money,
    date,
    esc,
    label,
    api,
    message,
    safe,
    returnStatus,
    trackingHtml,
    productThumbnail,
    orderCard,
    pager,
    patchPagedList,
    modal,
    openDetail,
  } = G4;
  const state = { orders: [], page: 1 };
  let detailOrderId = null,
    detailRequest = 0,
    detailKlarnaInFlight = false,
    klarnaPolling = false,
    nextKlarnaPollAt = 0;
  let orderFilter = 'all',
    orderRequest = 0,
    visibleOrders = new Map();
  function tab(id, loadList = true) {
    if (!G4.Navigation.select(id)) return;
    if (id === 'buyer-offers') safe(() => G4.BuyerOffers.load());
    if (id === 'orders' && loadList) safe(loadOrders);
    if (id === 'best-offers') safe(() => G4.BestOffers.buyerLoad());
    if (id === 'disputes') safe(() => G4.Disputes.load());
    if (id === 'notifications') safe(() => G4.Notifications.load());
  }
  async function loadStrikeNotice() {
    const notice = $('buyer-strike-notice');
    if (!notice) return;
    try {
      const info = await api('buyer/unpaid-strikes');
      notice.hidden = !info.strikes && !info.banned;
      notice.textContent = info.banned
        ? 'Tài khoản đã bị khóa vĩnh viễn do không thanh toán đơn hàng.'
        : info.strikes
          ? `Bạn có ${info.strikes}/${info.limit} điểm phạt chưa thanh toán. Đạt ${info.limit} điểm liên tiếp sẽ bị khóa tài khoản vĩnh viễn.`
          : '';
    } catch {
      notice.hidden = true;
    }
  }
  async function loadOrders(page = state.page, changedId = null) {
    const request = ++orderRequest,
      filter = orderFilter,
      list = $('order-list');
    list.setAttribute('aria-busy', 'true');
    $('buyer-order-summary').textContent = 'Đang tải đơn hàng…';
    try {
      const result = await api(
        `orders/page?page=${page}&pageSize=10&filter=${encodeURIComponent(filter)}`,
      );
      if (request !== orderRequest) return;
      state.page = result.page;
      state.orders = result.items;
      safe(loadStrikeNotice);
      const empty = `<p class="empty-note">${filter === 'all' ? 'Chưa có đơn hàng.' : 'Không có đơn hàng phù hợp với bộ lọc này.'}</p>`;
      if (changedId === null)
        list.innerHTML = result.items.length
          ? result.items.map((o) => orderCard(o, 'buyer')).join('')
          : empty;
      else {
        patchPagedList(
          'order-list',
          result.items,
          (o) => o.id,
          (o) => orderCard(o, 'buyer'),
          null,
          'data-order-row',
          empty,
        );
        for (const o of result.items) {
          if (visibleOrders.has(o.id) && visibleOrders.get(o.id) !== JSON.stringify(o)) {
            const row = list.querySelector(`[data-order-row="${o.id}"]`),
              focused = row?.contains(document.activeElement);
            if (row) row.outerHTML = orderCard(o, 'buyer');
            if (focused)
              list
                .querySelector(`[data-order-row="${o.id}"] [data-open-order]`)
                ?.focus({ preventScroll: true });
          }
        }
      }
      visibleOrders = new Map(result.items.map((o) => [o.id, JSON.stringify(o)]));
      document.querySelectorAll('[data-order-filter]').forEach((b) => {
        const active = b.dataset.orderFilter === filter;
        b.classList.toggle('active', active);
        b.setAttribute('aria-pressed', String(active));
        b.querySelector('[data-filter-count]').textContent =
          result.counts?.[b.dataset.orderFilter] ?? 0;
      });
      $('buyer-order-summary').textContent = `${result.totalCount} đơn`;
      pager('order-pagination', result, (p) => safe(() => loadOrders(p)));
    } catch (error) {
      if (request === orderRequest) {
        $('buyer-order-summary').textContent = 'Chưa tải được danh sách. Vui lòng bấm Làm mới.';
        throw error;
      }
    } finally {
      if (request === orderRequest) list.setAttribute('aria-busy', 'false');
    }
  }
  function actions(o) {
    const buttons = [];
    if (o.payments.some((p) => p.status === 'Succeeded'))
      buttons.push(
        `<button type="button" data-action="product-reviews" data-id="${o.id}">Đánh giá sản phẩm &amp; phản hồi</button>`,
      );
    if (o.status === 'AwaitingPayment' && o.payPalCheckoutId)
      buttons.push(
        `<button data-action="resume-paypal-cart" data-id="${o.payPalCheckoutId}">Xem thanh toán PayPal cả giỏ</button>`,
      );
    const klarnaInFlight = o.payments.some(
      (p) => p.method === 'Klarna' && ['Pending', 'Verifying'].includes(p.status),
    );
    if (o.status === 'AwaitingPayment' && klarnaInFlight)
      buttons.push(
        `<button data-action="reconcile-klarna" data-id="${o.id}">Kiểm tra thanh toán Klarna</button>`,
      );
    if (o.status === 'AwaitingPayment' && !o.payPalCheckoutId && !klarnaInFlight)
      buttons.push(
        `<button data-action="cancel" data-id="${o.id}">Hủy đơn</button><button data-action="retry-card" data-id="${o.id}">Thanh toán bằng credit card</button><button data-action="retry-paypal" data-id="${o.id}">Thanh toán PayPal</button><button data-action="retry-klarna" data-id="${o.id}" data-failed-id="${o.payments.filter((p) => p.method === 'Klarna' && p.status === 'Failed').at(-1)?.id ?? ''}">Thanh toán Klarna</button>`,
      );
    if (['Paid', 'Preparing'].includes(o.status))
      buttons.push(`<button data-action="cancel" data-id="${o.id}">Yêu cầu hủy</button>`);
    if (
      o.status === 'Delivered' &&
      !o.returnRequest &&
      o.canRequestReturn !== false &&
      (G4.Returns.windowOpen?.(o) ?? true)
    )
      buttons.push(`<button data-action="return" data-id="${o.id}">Yêu cầu trả hàng</button>`);
    if (
      o.status === 'Delivered' &&
      o.canOpenDispute !== false &&
      (o.returnRequest?.status === 'Rejected' || (G4.Returns.windowOpen?.(o) ?? true)) &&
      !o.dispute?.isOpen &&
      (!o.returnRequest || ['Rejected', 'Closed'].includes(o.returnRequest.status)) &&
      !o.refunds.some((r) => r.status === 'Succeeded' && r.reason !== 'shipping-adjustment')
    )
      buttons.push(`<button data-action="hold" data-id="${o.id}">Mở yêu cầu giải quyết</button>`);
    return buttons.join('') || '<span class="small">Không có thao tác.</span>';
  }
  async function showOrder(id, preserveScroll = false) {
    const request = ++detailRequest;
    detailOrderId = id;
    const o = await api(`orders/${id}`);
    if (request !== detailRequest || (preserveScroll && !$('detail-dialog')?.open)) return;
    detailKlarnaInFlight = o.status === 'AwaitingPayment' && o.payments.some(
      (p) => p.method === 'Klarna' && ['Pending', 'Verifying'].includes(p.status),
    );
    const klarnaAuthorized = o.payments.some((p) => p.method === 'Klarna' && p.status === 'Authorized');
    const pay = klarnaAuthorized ? 'Authorized' : o.payments.at(-1)?.status ?? 'Chưa thanh toán';
    const status = `<div class="status-grid"><div class="status-box"><span>Đơn hàng</span><strong>${esc(klarnaAuthorized && o.status === 'Paid' ? 'Đã duyệt Klarna · sẵn sàng giao hàng' : label(o.status))}</strong></div><div class="status-box"><span>Thanh toán</span><strong>${esc(label(pay))}</strong></div><div class="status-box"><span>Trả hàng</span><strong>${esc(returnStatus(o.returnRequest?.status))}</strong></div></div>`;
    const lines = o.items
      .map(
        (x) =>
          `<div class="item">${productThumbnail(x.imageUrl, x.productTitleSnapshot)}<div class="item-info"><strong>${esc(x.productTitleSnapshot)}</strong><small>Số lượng: ${x.quantity}${x.unitWeightKgSnapshot == null ? '' : ' · ' + Number(x.unitWeightKgSnapshot) + ' kg/sản phẩm'}</small></div>${G4.Promotions.linePriceHtml(x.originalTotal == null ? null : x, x.unitPrice * x.quantity, o.promotions)}</div>`,
      )
      .join('');
    const collected = o.payments
      .filter((payment) => payment.status === 'Succeeded')
      .reduce((sum, payment) => sum + Number(payment.amount || 0), 0);
    const refunded = o.refunds
      .filter((refund) => refund.status === 'Succeeded')
      .reduce((sum, refund) => sum + Number(refund.amount || 0), 0);
    openDetail(
      `<div class="detail" data-order-detail="${o.id}"><h2>Đơn mua hàng #${o.id}</h2><p class="small">Tạo: ${date(o.orderDate)} · Cập nhật: ${date(o.updatedAt || o.orderDate)}</p><p><strong>Đã thanh toán thành công (đã thu):</strong> ${money(collected)} · <strong>Đã hoàn thành công:</strong> ${money(refunded)} · <strong>Còn lại sau hoàn:</strong> ${money(collected - refunded)}</p><p class="small">${esc(o.addressSnapshot)}${o.deliveryPhone ? ` · SĐT người nhận: ${esc(G4.AddressFields?.formatPhone(o.deliveryPhone) ?? o.deliveryPhone)}` : ''} · Khối lượng: ${o.totalWeightKg == null ? 'Chưa có dữ liệu' : Number(o.totalWeightKg) + ' kg'}</p>${status}${klarnaAuthorized ? '<p class="small">Klarna đã duyệt đơn. Chưa thu tiền; tiền sẽ được thu khi giao hàng.</p>' : ''}${G4.paymentCountdown(o)}${G4.AddressChanges.summaryHtml(o, 'buyer')}${G4.Returns.windowHtml?.(o) || ''}${G4.disputeBadge(o.dispute)}<h3>Sản phẩm</h3>${lines}${G4.Promotions.breakdownHtml(o)}<div class="promotion-order-totals"><p>Phí vận chuyển: <strong>${money(o.shippingFee ?? o.shipping)}</strong></p><p>Tổng thanh toán: <strong>${money(o.totalPrice)}</strong></p></div><h3>Tiến trình vận chuyển</h3>${trackingHtml(o)}<h3>Thao tác</h3><div class="actions">${actions(o)}</div>${G4.SellerFeedback.html(o)}${G4.returnSummary(o)}${o.refunds.length ? `<div class="refund-summary">${o.refunds.map((r) => `<p>${r.reason === 'shipping-adjustment' ? 'Hoàn chênh lệch ship giả lập' : 'Hoàn'} ${money(r.amount)} · ${esc(label(r.status))}</p>`).join('')}</div>` : ''}</div>`,
      preserveScroll,
    );
  }
  async function action(b) {
    const id = Number(b.dataset.id),
      kind = b.dataset.action;
    if (kind === 'resume-paypal-cart') {
      G4.closeDetail();
      return G4.PayPalCart.show(id);
    }
    if (kind === 'reconcile-klarna') {
      const result = await G4.Klarna.reconcile(id);
      await loadOrders(state.page, id);
      await showOrder(id);
      message(
        result.status === 'Authorized'
          ? 'Klarna đã duyệt đơn, sẵn sàng giao hàng. Chưa thu tiền; tiền sẽ được thu khi giao hàng.'
          : result.status === 'Succeeded'
            ? 'Thanh toán Klarna đã được thu thành công.'
              : result.status === 'Failed'
              ? 'Thanh toán Klarna không thành công. Kiểm tra đơn hàng trước khi thử lại.'
              : 'Klarna chưa xác nhận thanh toán. Vui lòng kiểm tra lại sau.',
        ['Authorized', 'Succeeded'].includes(result.status),
      );
      return;
    }
    if (kind === 'review-seller') return G4.SellerFeedback.open(id, () => showOrder(id));
    if (kind === 'product-reviews') return G4.BuyerProductReviews.open(id);
    if (kind === 'hold') return G4.Disputes.openNew(id);
    if (kind === 'return')
      return G4.Returns.open(id, async () => {
        await loadOrders(state.page, id);
        await showOrder(id);
      });
    const titles = {
      cancel: 'Xác nhận hủy đơn',
      return: 'Yêu cầu trả hàng',
      hold: 'Mở tranh chấp',
      'retry-card': 'Thanh toán bằng credit card',
      'retry-paypal': 'Thanh toán PayPal',
      'retry-klarna': 'Thanh toán Klarna',
    };
    const input = await modal({
      title: titles[kind],
      description: `Đơn mua hàng #${id}. Vui lòng kiểm tra trước khi xác nhận.`,
      fields: ['return', 'hold'].includes(kind)
        ? [
          {
            name: 'reason',
            label: kind === 'return' ? 'Lý do trả hàng' : 'Lý do tranh chấp',
            required: true,
          },
        ]
        : [],
      confirm: 'Xác nhận',
    });
    if (!input) return;
    if (kind === 'retry-klarna') return G4.Klarna.begin(id, b.dataset.failedId || null);
    if (kind === 'retry-paypal') {
      const result = await api(`orders/${id}/pay/paypal`, 'POST', { key: crypto.randomUUID() });
      if (result.approvalUrl) {
        location.assign(result.approvalUrl);
        return;
      }
      throw new Error('Chưa có đường dẫn thanh toán PayPal.');
    } else if (kind === 'retry-card') {
      const result = await api(`orders/${id}/pay/card`, 'POST', {
        number: '4111111111111111',
        expiry: '12/30',
        key: crypto.randomUUID(),
      });
      if (result.status !== 'Succeeded')
        throw new Error('Thanh toán chưa thành công. Vui lòng thử lại.');
    } else if (kind === 'cancel') await api(`orders/${id}/cancel`, 'POST');
    message('Đã lưu thay đổi cho đơn hàng.', true);
    await loadOrders(state.page, id);
    await showOrder(id);
  }
  document.querySelectorAll('[data-order-filter]').forEach(
    (b) =>
    (b.onclick = () => {
      orderFilter = b.dataset.orderFilter;
      safe(() => loadOrders(1));
    }),
  );
  $('refresh-orders').onclick = () => safe(() => loadOrders(state.page));
  document.body.addEventListener('click', (e) => {
    const open = e.target.closest('[data-open-order]');
    if (open) return safe(() => showOrder(Number(open.dataset.openOrder)));
    const b = e.target.closest('[data-action]');
    if (b) return safe(() => action(b));
  });
  G4.BuyerCart.init({ tab, loadOrders, showOrder });
  G4.AddressChanges.init('buyer', async (id) => {
    await loadOrders(state.page, id);
    await showOrder(id);
  });
  G4.Notifications.init('buyer');
  G4.Returns.init();
  G4.SellerFeedback.init();
  G4.BuyerProductReviews.init();
  G4.PayPalCart.init(async () => loadOrders(state.page));
  G4.Realtime?.subscribe(
    ['orders', 'returns', 'shipments', 'disputes', 'notifications', 'best-offers'],
    async () => {
      if (document.querySelector('.tab-panel.active')?.id === 'orders')
        await loadOrders(state.page);
      if (document.querySelector('.tab-panel.active')?.id === 'best-offers')
        await G4.BestOffers.buyerLoad();
      if (document.querySelector('.tab-panel.active')?.id === 'notifications')
        await G4.Notifications.load();
      if ($('detail-dialog')?.open && document.querySelector('#detail-content [data-order-detail]'))
        await showOrder(detailOrderId, true);
    },
  );
  G4.Disputes.init('buyer', async (id) => {
    await loadOrders(state.page, id);
  });
  setInterval(() => {
    if (
      document.visibilityState === 'visible' &&
      !G4.Realtime?.connected &&
      $('detail-dialog')?.open &&
      document.querySelector('#detail-content [data-order-detail]')?.dataset.orderDetail ===
      String(detailOrderId)
    )
      safe(() => showOrder(detailOrderId, true));
  }, 5000);
  setInterval(() => {
    if (klarnaPolling || !detailKlarnaInFlight || document.hidden || Date.now() < nextKlarnaPollAt ||
      !$('detail-dialog')?.open ||
      document.querySelector('#detail-content [data-order-detail]')?.dataset.orderDetail !== String(detailOrderId)) return;
    const id = detailOrderId;
    klarnaPolling = true;
    G4.Klarna.reconcile(id).then(async (result) => {
      if (['Authorized', 'Succeeded', 'Failed'].includes(result.status) && id === detailOrderId) {
        await loadOrders(state.page, id);
        await showOrder(id, true);
      }
    }).catch((error) => {
      if (error.status === 429) nextKlarnaPollAt = Date.now() + (error.retryAfterSeconds || 60) * 1000;
    }).finally(() => { klarnaPolling = false; });
  }, 30000);
  for (const kind of ['payment', 'return-window', 'receipt', 'address']) {
    G4.Countdown?.subscribe(kind, async () => {
      if (document.hidden) return;
      if ($('detail-dialog')?.open && document.querySelector('#detail-content [data-order-detail]'))
        await showOrder(detailOrderId, true);
      if (G4.Navigation.current === 'orders') await loadOrders(state.page);
    });
  }
  G4.Navigation.init(tab);
  window.G4BuyerShowOrder = (id) => showOrder(id);
  safe(async () => {
    const p = new URLSearchParams(location.search);
    if (p.has('orderId') && G4.Navigation.current === 'orders') {
      await showOrder(Number(p.get('orderId')));
      if (p.has('paymentResult'))
        message(
          p.get('paymentResult') === 'rate-limited'
            ? `Bạn thao tác quá nhanh. Vui lòng kiểm tra lại thanh toán sau ${Math.max(1, Math.min(3600, Number(p.get('retryAfterSeconds')) || 60))} giây.`
            : p.get('paymentProvider') === 'klarna'
              ? p.get('paymentResult') === 'checked'
                ? 'Đã kiểm tra Klarna. Xem trạng thái thanh toán của đơn bên dưới.'
                : p.get('paymentResult') === 'cancelled'
                  ? 'Bạn đã rời trang Klarna. Chưa xác nhận kết quả thanh toán; hãy kiểm tra đơn trước khi thử lại.'
                  : 'Chưa kiểm tra được Klarna. Vui lòng kiểm tra lại trạng thái đơn.'
              : p.get('paymentResult') === 'checked'
                ? 'Đã kiểm tra thanh toán PayPal. Xem trạng thái đơn hàng bên dưới.'
                : p.get('paymentResult') === 'cancelled'
                  ? 'Bạn đã hủy thanh toán PayPal. Đơn hàng vẫn đang chờ thanh toán.'
                  : 'Chưa xác nhận được thanh toán PayPal. Vui lòng kiểm tra lại đơn hàng.',
          p.get('paymentResult') === 'checked' && p.get('paymentProvider') !== 'klarna',
        );
    }
  });
})();
