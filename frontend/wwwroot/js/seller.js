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
  let orderPage = 1,
    transactionPage = 1,
    payoutPage = 1;
  let orderFilter = 'all',
    needsAction = false,
    orderRequest = 0,
    visibleOrders = new Map();
  let detailOrderId = null,
    detailRequest = 0;
  function tab(id) {
    document
      .querySelectorAll('.tab-panel')
      .forEach((x) => x.classList.toggle('active', x.id === id));
    document
      .querySelectorAll('.tabs button')
      .forEach((x) => x.classList.toggle('active', x.dataset.tab === id));
    if (id === 'orders') safe(loadOrders);
    else if (id === 'disputes') safe(() => G4.Disputes.load());
    else if (id === 'promotions') safe(() => G4.Promotions.load());
    else if (id === 'notifications') safe(() => G4.Notifications.load());
    else if (id === 'integration-logs') safe(() => G4.IntegrationLogs.load());
    else if (id === 'shipping-settings') safe(() => G4.SellerShipping.load());
    else if (id === 'seller-identity') safe(() => G4.SellerIdentity.load());
    else if (id === 'seller-levels') safe(() => G4.SellerLevels.load());
    else if (id === 'seller-returns') safe(() => G4.SellerReturns.load());
    else safe(loadFinance);
  }
  async function loadOrders(page = orderPage, incremental = false) {
    const request = ++orderRequest;
    const filter = orderFilter,
      actionOnly = needsAction;
    const list = $('order-list');
    if (!incremental) {
      list.setAttribute('aria-busy', 'true');
      $('seller-order-summary').textContent = 'Đang tải đơn hàng…';
    }
    try {
      const result = await api(
        `orders/page?page=${page}&pageSize=10&filter=${filter}&needsAction=${actionOnly}`,
      );
      if (request !== orderRequest) return;
      orderPage = result.page;
      const empty = `<p class="empty-note">${filter === 'all' && !actionOnly ? 'Chưa có đơn hàng.' : 'Không có đơn hàng phù hợp với bộ lọc này.'}</p>`;
      if (incremental) {
        patchPagedList(
          'order-list',
          result.items,
          (o) => o.id,
          (o) => orderCard(o, 'seller'),
          null,
          'data-order-row',
          empty,
        );
        for (const o of result.items) {
          if (visibleOrders.has(o.id) && visibleOrders.get(o.id) !== JSON.stringify(o)) {
            const row = list.querySelector(`[data-order-row="${o.id}"]`);
            const focused = row?.contains(document.activeElement);
            if (row) row.outerHTML = orderCard(o, 'seller');
            if (focused)
              list
                .querySelector(`[data-order-row="${o.id}"] [data-open-order]`)
                ?.focus({ preventScroll: true });
          }
        }
      } else
        list.innerHTML = result.items.length
          ? result.items.map((o) => orderCard(o, 'seller')).join('')
          : empty;
      visibleOrders = new Map(result.items.map((o) => [o.id, JSON.stringify(o)]));
      document.querySelectorAll('[data-order-filter]').forEach((b) => {
        const active = b.dataset.orderFilter === filter;
        b.classList.toggle('active', active);
        b.setAttribute('aria-pressed', String(active));
        b.querySelector('[data-filter-count]').textContent =
          result.counts?.[b.dataset.orderFilter] ?? 0;
      });
      $('seller-action-count').textContent = result.actionCount ?? 0;
      $('seller-order-summary').textContent =
        `${result.totalCount} đơn${actionOnly ? ' cần xử lý trong nhóm này' : ''}`;
      pager('order-pagination', result, (p) => safe(() => loadOrders(p)));
    } catch (error) {
      if (request === orderRequest)
        $('seller-order-summary').textContent = 'Chưa tải được danh sách. Vui lòng bấm Làm mới.';
      throw error;
    } finally {
      if (request === orderRequest) list.setAttribute('aria-busy', 'false');
    }
  }
  function sellerActions(o) {
    const b = [];
    if (o.status === 'Paid')
      b.push(`<button data-action="prepare" data-id="${o.id}">Chuẩn bị hàng</button>`);
    if (
      o.status === 'Preparing' &&
      (!o.shipments.some((s) => s.direction === 'Outbound') ||
        o.shipments.some(
          (s) => s.direction === 'Outbound' && s.status === 'ShipmentCreationFailed',
        ))
    )
      b.push(
        `<button data-action="ship" data-id="${o.id}">${o.shipments.some((s) => s.direction === 'Outbound' && s.status === 'ShipmentCreationFailed') ? 'Tạo lại vận đơn' : 'Tạo vận đơn'}</button><button data-action="fail-carrier" data-id="${o.id}">Giả lập lỗi đơn vị vận chuyển</button>`,
      );
    if (o.status === 'CancelRequested')
      b.push(
        `<button data-action="accept-cancel" data-id="${o.id}">Chấp nhận hủy</button><button data-action="reject-cancel" data-id="${o.id}">Từ chối hủy</button>`,
      );
    if (o.returnRequest?.status === 'Requested')
      b.push(
        `<button data-action="approve-return" data-return="${o.returnRequest.id}" data-id="${o.id}">Duyệt trả hàng</button><button data-action="reject-return" data-return="${o.returnRequest.id}" data-id="${o.id}">Từ chối trả hàng</button><button data-action="approve-return-failure" data-return="${o.returnRequest.id}" data-id="${o.id}">Duyệt và giả lập lỗi tạo vận đơn trả</button>`,
      );
    const rs = o.shipments.find((s) => s.direction === 'Return');
    if (o.returnRequest?.status === 'Approved' && rs?.status === 'ShipmentCreationFailed')
      b.push(
        `<button data-action="retry-return" data-return="${o.returnRequest.id}" data-id="${o.id}">Tạo lại vận đơn trả</button>`,
      );
    if (
      ['Approved', 'ReturnShipping'].includes(o.returnRequest?.status) &&
      rs?.status === 'Delivered' &&
      (!o.dispute?.isOpen || o.dispute.status === 'ExecutingAgreement')
    )
      b.push(
        `<button data-action="receive-return" data-return="${o.returnRequest.id}" data-id="${o.id}">Xác nhận hàng hợp lệ và hoàn tiền</button><button data-action="return-issue" data-return="${o.returnRequest.id}" data-id="${o.id}">Báo vấn đề hàng trả</button>`,
      );
    const out = o.shipments.find((s) => s.direction === 'Outbound');
    if (
      out?.status === 'ReturnedToSeller' &&
      !o.refunds.some((r) => r.status === 'Succeeded' && r.reason !== 'shipping-adjustment')
    )
      b.push(
        `<button data-action="refund-delivery" data-id="${o.id}">Hoàn tiền giao thất bại</button>`,
      );
    return b.join('') || '<span class="small">Không có thao tác.</span>';
  }
  async function showOrder(id, preserveScroll = false) {
    const request = ++detailRequest;
    detailOrderId = id;
    const o = await api(`orders/${id}`);
    if (request !== detailRequest || (preserveScroll && !$('detail-dialog')?.open)) return;
    const s = o.settlement;
    const settlement = s
      ? `<section class="settlement-summary"><h3>Đối soát đơn hàng</h3><div class="settlement-grid"><div><span>Tổng tiền</span><strong class="positive">${money(s.grossAmount)}</strong></div><span class="settlement-operator" aria-hidden="true">−</span><div><span>Phí nền tảng</span><strong class="negative">${money(s.platformFeeAmount)}</strong></div><span class="settlement-operator" aria-hidden="true">=</span><div><span>Tiền nhận được</span><strong class="positive">${money(s.netAmount)}</strong></div></div>${s.shippingAdjustmentCost > 0 ? `<p>Chi phí seller chịu thêm do đổi địa chỉ: <strong>${money(s.shippingAdjustmentCost)}</strong></p>` : ''}${s.shippingAdjustmentCost > 0 || o.refunds.some((r) => r.reason === 'shipping-adjustment') ? `<p>Tiền còn đang xử lý: <strong>${money(s.processingAmount)}</strong></p>` : ''}</section>`
      : '';
    const items = o.items
      .map(
        (x) =>
          `<div class="item">${productThumbnail(x.imageUrl, x.productTitleSnapshot)}<div class="item-info"><strong>${esc(x.productTitleSnapshot)}</strong><small>Số lượng: ${x.quantity}</small></div>${G4.Promotions.linePriceHtml(x.originalTotal == null ? null : x, x.unitPrice * x.quantity, o.promotions)}</div>`,
      )
      .join('');
    openDetail(
      `<div class="detail" data-order-detail="${o.id}"><h2>Đơn bán hàng #${o.id} · ${esc(label(o.status))}</h2><p class="small">Tạo: ${date(o.orderDate)} · Cập nhật: ${date(o.updatedAt || o.orderDate)}</p><p class="small">${esc(o.addressSnapshot)} · Khối lượng: ${o.totalWeightKg == null ? 'Chưa có dữ liệu' : Number(o.totalWeightKg) + ' kg'}</p>${G4.AddressChanges.summaryHtml(o, 'seller')}<h3>Sản phẩm</h3>${items}${G4.Promotions.breakdownHtml(o)}${o.platformSubsidy > 0 ? `<p class="promotion-checkout-note">Nền tảng bù ${money(o.platformSubsidy)} cho shop. Doanh thu đối soát: ${money(o.sellerGross)}.</p>` : ''}${G4.disputeBadge(o.dispute)}${settlement}<h3>Tiến trình vận chuyển</h3>${trackingHtml(o)}<p class="small">Các mốc vận chuyển do đơn vị giao hàng cập nhật.</p><button type="button" data-log-order="${o.id}">Xem nhật ký xử lý đơn</button><h3>Thao tác của người bán</h3><div class="actions">${sellerActions(o)}</div>${G4.returnSummary(o)}</div>`,
      preserveScroll,
    );
  }
  async function action(b) {
    const id = Number(b.dataset.id),
      k = b.dataset.action;
    if (k === 'return-issue') {
      const input = await modal({
        title: 'Báo vấn đề hàng trả',
        description: `Admin sẽ xem bằng chứng; tiền của đơn #${id} tiếp tục được giữ.`,
        fields: [
          {
            name: 'description',
            label: 'Mô tả vấn đề',
            type: 'textarea',
            required: true,
            maxLength: 4000,
          },
          {
            name: 'links',
            label: 'Link bằng chứng, mỗi dòng một link',
            type: 'textarea',
            required: true,
          },
        ],
        confirm: 'Chuyển admin xử lý',
      });
      if (!input) return;
      const evidenceLinks = G4.Disputes.parseLinks(input.links);
      await api(`seller/returns/${b.dataset.return}/issue`, 'POST', {
        description: input.description,
        evidenceLinks,
      });
      message('Đã chuyển vấn đề hàng trả cho admin.', true);
    } else {
      const title = b.textContent.trim(),
        needsReason = ['reject-cancel', 'reject-return'].includes(k);
      const input = await modal({
        title,
        description: k === 'fail-carrier' || k === 'approve-return-failure' ? `Đơn #${id}: hệ thống sẽ thử tạo vận đơn và giả lập cả 3 lần gọi đều lỗi. Sau đó bạn có thể tạo lại vận đơn bình thường.` : `Bạn đang cập nhật đơn bán hàng #${id}.`,
        fields: needsReason ? [{ name: 'reason', label: 'Lý do từ chối', required: true }] : [],
        confirm: 'Lưu thay đổi',
      });
      if (!input) return;
      let path,
        body = {};
      if (k === 'prepare') path = `seller/orders/${id}/prepare`;
      else if (k === 'ship') path = `seller/orders/${id}/ship`;
      else if (k === 'fail-carrier') {
        path = `seller/orders/${id}/ship/simulate-failure`;
      } else if (k === 'accept-cancel' || k === 'reject-cancel') {
        path = `seller/orders/${id}/cancel/decision`;
        body = { approve: k === 'accept-cancel', reason: needsReason ? input.reason : null };
      } else if (k === 'approve-return') path = `seller/returns/${b.dataset.return}/approve`;
      else if (k === 'approve-return-failure') path = `seller/returns/${b.dataset.return}/approve/simulate-failure`;
      else if (k === 'retry-return') path = `seller/returns/${b.dataset.return}/ship`;
      else if (k === 'reject-return') {
        path = `seller/returns/${b.dataset.return}/reject`;
        body = { reason: input.reason };
      } else if (k === 'receive-return') path = `seller/returns/${b.dataset.return}/receive`;
      else if (k === 'refund-delivery') {
        path = `seller/orders/${id}/refund`;
        body = { reason: 'delivery-failed' };
      } else return;
      const result = await api(path, 'POST', body);
      const labelFailed = result?.status === 'ShipmentCreationFailed' || result?.shipmentStatus === 'ShipmentCreationFailed';
      message(labelFailed ? 'Tạo vận đơn thất bại sau 3 lần thử. Chưa thu phí nhãn; bấm Tạo lại vận đơn để thử lại.' : 'Đã lưu thay đổi cho đơn hàng.', !labelFailed);
    }
    await loadOrders(orderPage, true);
    await G4.SellerReturns.load();
    await showOrder(id, true);
  }
  const transactionLabels = {
    ShippingAdjustmentCost: 'Chi phí đổi địa chỉ',
    Sale: 'Doanh thu đơn hàng',
    PlatformFee: 'Phí nền tảng',
    DebtRecovery: 'Thu hồi số dư âm',
    DebtRepayment: 'Trả nợ qua ngân hàng',
    DebtWriteOff: 'Ghi nhận nợ khó thu',
    HoldCorrection: 'Điều chỉnh tiền giữ',
    Refund: 'Hoàn tiền cho người mua',
    FeeCredit: 'Hoàn lại phí nền tảng',
    NegativeBalance: 'Số dư âm phát sinh',
    FundHold: 'Tiền giữ do tranh chấp',
    HoldReleased: 'Giải phóng tiền giữ',
    HoldReserved: 'Tiền chờ hoàn',
    FundsReleased: 'Tiền có thể rút',
    Payout: 'Yêu cầu rút tiền',
    PayoutReturned: 'Tiền rút bị trả lại',
  };
  const payoutLabels = {
    Created: 'Đã tạo',
    InProgress: 'Đang xử lý',
    FundsSent: 'Đã gửi ngân hàng',
    Returned: 'Ngân hàng trả lại',
    Completed: 'Hoàn tất',
  };
  const bucketLabels = {
    Audit: 'Nhật ký quyết định',
    Processing: 'Tiền đang xử lý',
    Available: 'Tiền có thể rút',
    OnHold: 'Tiền đang giữ',
    Negative: 'Số dư âm',
    SellerBalance: 'Số dư người bán',
  };
  const transactionDescriptions = {
    ShippingAdjustmentCost: 'Seller chịu phí ship tăng do đổi địa chỉ',
    Sale: 'Ghi nhận tiền bán hàng',
    PlatformFee: 'Đã trừ phí nền tảng',
    DebtRecovery: 'Bù khoản nợ từ giao dịch trước',
    DebtRepayment: 'Ngân hàng xác nhận khoản trả nợ',
    DebtWriteOff: 'Quyết định tự động, không thay đổi số dư',
    HoldCorrection: 'Điều chỉnh khoản tiền đang giữ',
    Refund: 'Đã hoàn tiền cho người mua',
    FeeCredit: 'Hoàn lại một phần phí nền tảng',
    NegativeBalance: 'Khoản hoàn tiền vượt số dư hiện có',
    HoldReleased: 'Tiền tranh chấp đã được giải phóng',
    HoldReserved: 'Giữ tiền để hoàn cho người mua',
    FundsReleased: 'Tiền đã đủ điều kiện rút',
    Payout: 'Đã gửi yêu cầu rút tiền',
    PayoutReturned: 'Ngân hàng trả lại tiền rút',
  };
  function financeDescription(x) {
    if (x.type === 'FundHold')
      return x.description
        ? `Lý do giữ tiền: ${x.description}`
        : 'Tiền được giữ để xử lý tranh chấp';
    return transactionDescriptions[x.type] || 'Số dư đã thay đổi';
  }
  const financeDate = (value) => date(typeof value === 'string' && !/(Z|[+-]\d{2}:\d{2})$/i.test(value) ? value + 'Z' : value);
  async function loadTransactions(page = transactionPage) {
    const result = await api(`seller/finance/transactions/page?page=${page}&pageSize=10`);
    transactionPage = result.page;
    $('finance-transactions').innerHTML = result.items.length
      ? result.items
          .map(
            (x) =>
              `<article class="finance-row"><div><strong>${esc(transactionLabels[x.type] || 'Giao dịch tài chính')}${x.orderId ? ` · đơn #${x.orderId}` : ''}</strong><small>${esc(financeDescription(x))} · ${financeDate(x.createdAt)}</small><small>Khoản tiền: ${esc(bucketLabels[x.bucket] || 'Số dư tài chính')}</small></div><b class="${Number(x.amount) < 0 ? 'negative' : 'positive'}">${Number(x.amount) > 0 ? '+' : ''}${money(x.amount)}</b></article>`,
          )
          .join('')
      : '<p class="empty-note">Chưa có giao dịch tài chính.</p>';
    pager('finance-transactions-pagination', result, (p) => safe(() => loadTransactions(p)));
  }
  async function loadPayouts(page = payoutPage) {
    const result = await api(`seller/finance/payouts/page?page=${page}&pageSize=10`);
    payoutPage = result.page;
    $('finance-payouts').innerHTML = result.items.length
      ? result.items
          .map(
            (x) =>
              `<article class="finance-row"><div><strong>Lần rút tiền #${x.id} · ${esc(payoutLabels[x.status] || label(x.status))}</strong><small>${financeDate(x.createdAt)} · ${esc(x.bankReferenceId || x.destinationMasked || 'Chưa có mã ngân hàng')}</small></div><b>${money(x.amount)}</b></article>`,
          )
          .join('')
      : '<p class="empty-note">Chưa có lần rút tiền.</p>';
    pager('finance-payouts-pagination', result, (p) => safe(() => loadPayouts(p)));
  }
  async function loadFinance() {
    const f = await api('seller/finance');
    const cards = [
      ['Đang xử lý', f.processing],
      ['Ví khả dụng', f.available],
      ['Đang giữ', f.onHold],
      ['Số dư âm', f.negative],
    ];
    $('finance-balances').innerHTML = cards
      .map(
        (x) =>
          `<div class="balance-card"><span>${x[0]}</span><strong class="${x[0] === 'Số dư âm' && Number(x[1]) > 0 ? 'negative' : ''}">${money(x[1])}</strong></div>`,
      )
      .join('');
    const p = f.levelProgress;
    const hold = f.holdSeconds < 86400 ? `${f.holdSeconds} giây` : `${f.holdSeconds / 86400} ngày`;
    $('finance-level').innerHTML =
      `<p><strong>Cấp ${f.level}${f.level >= 3 ? ' · Instant' : ''}</strong></p><p>${f.level >= 3 ? 'Tiền sau phí tự vào ví khi thanh toán thành công, không cần yêu cầu payout.' : `Tiền vào ví sau ${hold} kể từ giao thành công.`}</p><p>Giới hạn doanh số: ${money(f.monthlySalesLimit)}/tháng</p><p>Doanh số tháng này: ${money(f.monthlySalesAmount)}</p><p class="small">Xem tiêu chí và lịch sử chi tiết tại mục Cấp seller.</p>`;
    await Promise.all([loadTransactions(), loadPayouts(), G4.SellerDebt.loadSeller()]);
  }
  document.querySelectorAll('[data-order-filter]').forEach(
    (b) =>
      (b.onclick = () => {
        orderFilter = b.dataset.orderFilter;
        safe(() => loadOrders(1));
      }),
  );
  $('seller-needs-action').onchange = () => {
    needsAction = $('seller-needs-action').checked;
    safe(() => loadOrders(1));
  };
  document
    .querySelectorAll('.role-tabs [data-tab]')
    .forEach((b) => (b.onclick = () => tab(b.dataset.tab)));
  $('refresh-orders').onclick = () => safe(() => loadOrders(orderPage, true));
  $('refresh-finance').onclick = () => safe(loadFinance);
  $('evaluate-level').onclick = () =>
    safe(async () => {
      await api('seller/finance/evaluate-level', 'POST');
      await loadFinance();
      message('Đã cập nhật cấp người bán.', true);
    });
  $('request-payout').onclick = () =>
    safe(async () => {
      await api('seller/payouts', 'POST', {
        amount: Number($('payout-amount').value),
        key: crypto.randomUUID(),
        simulateFailure: $('payout-failure').checked,
      });
      payoutPage = 1;
      await loadFinance();
      message('Đã ghi nhận yêu cầu rút tiền. Bạn có thể theo dõi trạng thái bên dưới.', true);
    });
  G4.Disputes.init('seller', () => loadOrders(orderPage, true));
  G4.SellerDebt.init('seller', loadFinance);
  G4.SellerIdentity.init();
  G4.SellerLevels.init('seller');
  G4.SellerReturns.init();
  safe(() => G4.SellerReturns.load());
  document.body.addEventListener('click', (e) => {
    const open = e.target.closest('[data-open-order]');
    if (open) return safe(() => showOrder(Number(open.dataset.openOrder)));
    const b = e.target.closest('[data-action]');
    if (b) return safe(() => action(b));
  });
  safe(loadOrders);
  G4.Realtime?.subscribe(['orders', 'returns', 'shipments', 'disputes', 'finance', 'levels', 'notifications'], async () => {
    const active = document.querySelector('.tab-panel.active')?.id;
    if (active === 'orders') await loadOrders(orderPage, true);
    if (active === 'finance') await loadFinance();
    if (active === 'seller-levels') await G4.SellerLevels.load();
    if (active === 'notifications') await G4.Notifications.load();
    await G4.SellerReturns.load();
    if ($('detail-dialog')?.open && document.querySelector('#detail-content [data-order-detail]')) await showOrder(detailOrderId, true);
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
  G4.AddressChanges.init('seller', async (id) => {
    await loadOrders(orderPage, true);
    await showOrder(id);
  });
  G4.Promotions.init('seller');
  G4.Notifications.init('seller');
  G4.IntegrationLogs.init('seller');
})();
