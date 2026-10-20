(() => {
  const { $, esc, money, date, api, message, modal, safe } = G4;
  const busy = new Set(),
    drafts = new Map(), submissions = new Map();
  const stamp = (value) =>
    date(typeof value === 'string' && !/(Z|[+-]\d{2}:\d{2})$/i.test(value) ? value + 'Z' : value);
  const labels = { Pending: 'Chờ seller duyệt', AwaitingShipper: 'Chờ shipper chấp thuận', Approved: 'Địa chỉ mới đã có hiệu lực', Rejected: 'Đã từ chối', Expired: 'Hết hạn xác nhận' };
  function summaryHtml(order, role) {
    const changes = order.addressChanges || [];
    if (!changes.length && !order.canRequestAddressChange) return '';
    return `<section class="address-change-summary"><h3>Đổi địa chỉ giao hàng</h3>${role === 'buyer' && order.canRequestAddressChange ? `<p>Được đổi thành công một lần trước khi bắt đầu giao tới bạn. Xem báo giá, seller duyệt và shipper đã nhận chấp thuận trước khi áp dụng.</p><button type="button" data-address-change="request" data-address-order="${order.id}">Yêu cầu đổi địa chỉ</button>` : ''}${changes
      .map((r) => {
        const increase = Math.max(0, r.newShippingCost - r.oldShippingCost);
        return `<article class="address-change-entry"><p><strong>${esc(labels[r.status] || r.status)}</strong> · ${stamp(r.createdAt)}</p><div class="address-change-grid"><div><strong>Địa chỉ cũ</strong><p>${esc(r.oldAddress)}</p></div><div><strong>Địa chỉ yêu cầu</strong><p>${esc(r.newAddress)}</p></div></div><p>Lý do: ${esc(r.reason)}</p><p>Phí ship theo tuyến: ${money(r.oldShippingCost)} → ${money(r.newShippingCost)}</p>${increase > 0 ? `<p>Seller chịu thêm khi duyệt: <strong>${money(increase)}</strong> · Mức chấp nhận: ${money(r.sellerCap)}</p>` : ''}${r.buyerRefund > 0 ? `<p>${r.status === 'Approved' ? 'Đã hoàn chênh lệch ship bằng giao dịch giả lập' : 'Hoàn chênh lệch ship dự kiến khi đủ xác nhận'}: <strong>${money(r.buyerRefund)}</strong></p>` : ''}${['Pending', 'AwaitingShipper'].includes(r.status) ? `<p>Địa chỉ cũ còn hiệu lực; chưa áp dụng chi phí hoặc hoàn tiền.${r.expiresAt ? ` Hạn chờ: ${esc(stamp(r.expiresAt))}.` : ''}</p>` : ''}${r.sellerConfirmedAt ? `<p>Seller chấp thuận: ${esc(stamp(r.sellerConfirmedAt))}</p>` : ''}${r.appliedAt ? `<p>Có hiệu lực: ${esc(stamp(r.appliedAt))}</p>` : ''}${r.decisionReason ? `<p class="address-change-note">${esc(r.decisionReason)}</p>` : ''}${role === 'seller' && r.status === 'Pending' ? `<div class="actions"><button type="button" class="primary" data-address-change="approve" data-address-request="${r.id}" data-address-order="${order.id}">Duyệt địa chỉ mới</button><button type="button" data-address-change="reject" data-address-request="${r.id}" data-address-order="${order.id}">Từ chối đổi địa chỉ</button></div>` : ''}</article>`;
      })
      .join('')}</section>`;
  }
  function requiresAck(shipment) {
    return shipment.addressChangeRequestId != null && !shipment.addressChangeAcknowledgedAt;
  }
  function pendingChange(shipment, order) {
    if (shipment.direction === 'Return') return null;
    return (order.addressChanges || []).find(x => ['Pending', 'AwaitingShipper'].includes(x.status));
  }
  function blocksDelivery(shipment, order) { return !!pendingChange(shipment, order); }
  function shipperNotice(shipment, order) {
    const pending = pendingChange(shipment, order);
    const request = pending || (order.addressChanges || []).find(x => x.id === shipment.addressChangeRequestId);
    if (!request) return '';
    const awaiting = request.status === 'AwaitingShipper';
    const legacyAck = !pending && requiresAck(shipment);
    return `<section class="address-change-summary"><h3>${pending ? 'Yêu cầu đổi địa chỉ chưa có hiệu lực' : 'Địa chỉ mới đã có hiệu lực'}</h3><div class="address-change-grid"><div><strong>Địa chỉ cũ</strong><p>${esc(request.oldAddress)}</p></div><div><strong>Địa chỉ yêu cầu</strong><p>${esc(request.newAddress)}</p></div></div>${pending ? `<p>Tiếp tục dùng địa chỉ cũ. Chưa được bắt đầu giao tới buyer khi còn chờ xác nhận.${request.expiresAt ? ` Hạn chờ: ${esc(stamp(request.expiresAt))}.` : ''}</p>` : ''}${awaiting ? `<p>Seller đã chấp thuận. Hãy xác nhận khả năng giao trước khi địa chỉ và chi phí được áp dụng.</p><div class="actions"><button type="button" class="primary" data-address-shipper="approve" data-shipment="${shipment.id}" data-request="${request.id}">Có thể giao địa chỉ mới</button><button type="button" data-address-shipper="reject" data-shipment="${shipment.id}" data-request="${request.id}">Không thể giao</button></div>` : pending ? '<p>Đang chờ seller duyệt.</p>' : legacyAck ? `<p>Xác nhận đã đọc địa chỉ đã áp dụng theo quy trình cũ.</p><button type="button" class="primary" data-ack-address="${shipment.id}">Xác nhận địa chỉ mới</button>` : '<p>Đã xác nhận địa chỉ mới.</p>'}</section>`;
  }
  async function decideShipper(button) {
    const approve = button.dataset.addressShipper === 'approve';
    const input = await modal({ title: approve ? 'Chấp thuận khả năng giao' : 'Không thể giao địa chỉ mới',
      description: approve ? 'Xác nhận bạn có thể giao địa chỉ mới đã hiển thị. Server kiểm tra lại điều kiện trước khi áp dụng.' : 'Địa chỉ cũ được giữ nguyên; yêu cầu này không dùng lượt đổi thành công.',
      fields: approve ? [] : [{ name: 'reason', label: 'Lý do không thể giao', type: 'textarea', required: true, maxLength: 1000,
        validate: value => value.trim() ? '' : 'Vui lòng nhập lý do.' }], confirm: approve ? 'Có thể giao' : 'Từ chối' });
    if (!input) return;
    const result = await api(`shipper/shipments/${button.dataset.shipment}/address-change/decision`, 'POST',
      { requestId: Number(button.dataset.request), approve, reason: approve ? null : input.reason.trim() });
    message(result.status === 'Approved' ? 'Địa chỉ mới đã có hiệu lực.' : result.decisionReason || labels[result.status], result.status === 'Approved');
    return result;
  }
  function init(role, refresh) {
    document.body.addEventListener('click', (event) => {
      const button = event.target.closest('[data-address-change]');
      if (!button) return;
      return safe(async () => {
        const orderId = Number(button.dataset.addressOrder),
          action = button.dataset.addressChange;
        if (busy.has(orderId)) return;
        busy.add(orderId);
        document
          .querySelectorAll(`[data-address-order="${orderId}"]`)
          .forEach((x) => (x.disabled = true));
        try {
          if (role === 'buyer' && action === 'request' && submissions.has(orderId)) {
            const result = await api(`orders/${orderId}/address-changes`, 'POST', submissions.get(orderId));
            submissions.delete(orderId); drafts.delete(orderId);
            message(labels[result.status] || result.status, result.status === 'Pending');
          } else if (role === 'buyer' && action === 'request') {
            const addresses = await api('addresses');
            const fields = [
              { name: 'fullName', label: 'Tên người nhận', required: true },
              { name: 'street', label: 'Đường, số nhà', required: true },
              { name: 'city', label: 'Thành phố', required: true },
              { name: 'state', label: 'Tỉnh / vùng', required: true },
              { name: 'country', label: 'Quốc gia', required: true },
              {
                name: 'reason',
                label: 'Lý do đổi địa chỉ',
                type: 'textarea',
                required: true,
                maxLength: 1000,
                validate: (value) => (value.trim() ? '' : 'Vui lòng nhập lý do đổi địa chỉ.'),
              },
            ];
            for (const field of fields)
              if (field.name !== 'reason')
                field.validate = (value) =>
                  !value.trim()
                    ? 'Vui lòng nhập thông tin.'
                    : value.trim().length > (['fullName', 'street'].includes(field.name) ? 100 : 50)
                      ? 'Nội dung quá dài.'
                      : '';
            const pending = modal({
              title: 'Yêu cầu đổi địa chỉ',
              description: `Đơn #${orderId}. Seller sẽ duyệt; phí tăng vượt mức seller chịu sẽ bị từ chối. Địa chỉ mới chỉ áp dụng cho đơn này.`,
              fields,
              confirm: 'Xem báo giá',
            });
            const address = addresses.find((x) => x.isDefault) || addresses[0];
            if (address)
              for (const field of fields.filter((x) => x.name !== 'reason'))
                $(`action-${field.name}`).value = address[field.name] || '';
            if (drafts.has(orderId))
              for (const field of fields)
                $(`action-${field.name}`).value = drafts.get(orderId)[field.name] || '';
            const input = await pending;
            if (!input) return;
            drafts.set(orderId, input);
            const payload = { address: Object.fromEntries(fields.filter(x => x.name !== 'reason').map(x => [x.name, input[x.name].trim()])),
              reason: input.reason.trim(), key: crypto.randomUUID() };
            const quote = await api(`orders/${orderId}/address-changes/quote`, 'POST', payload);
            const confirm = await modal({ title: 'Xác nhận báo giá đổi địa chỉ',
              description: `Địa chỉ cũ: ${quote.oldAddress}\nĐịa chỉ yêu cầu: ${quote.newAddress}\nPhí tuyến: ${money(quote.oldShippingCost)} → ${money(quote.newShippingCost)}. Seller chịu thêm: ${money(Math.max(0, quote.newShippingCost - quote.oldShippingCost))}; cap ${money(quote.sellerCap)}.\nBuyer không trả thêm. Hoàn ship giả lập dự kiến nếu đủ xác nhận: ${money(quote.buyerRefund)}.\n${quote.withinCap ? quote.shipperId ? 'Cần seller và shipper đã nhận chấp thuận.' : 'Chưa có shipper: seller là bên duyệt cuối.' : 'Vượt cap seller: yêu cầu sẽ bị từ chối, giữ địa chỉ cũ.'}`,
              confirm: 'Xác nhận và gửi yêu cầu' });
            if (!confirm) return;
            payload.quoteToken = quote.token;
            submissions.set(orderId, payload);
            const result = await api(`orders/${orderId}/address-changes`, 'POST', payload);
            submissions.delete(orderId);
            message(
              result.status === 'Rejected'
                ? result.decisionReason
                : 'Đã gửi yêu cầu đổi địa chỉ cho seller.',
              result.status !== 'Rejected',
            );
            if (result.status !== 'Rejected') drafts.delete(orderId);
          } else if (role === 'seller' && ['approve', 'reject'].includes(action)) {
            const approve = action === 'approve';
            const input = await modal({
              title: approve ? 'Duyệt địa chỉ mới' : 'Từ chối đổi địa chỉ',
              description: approve
                ? `Đơn #${orderId}: xác nhận địa chỉ mới và chi phí đã hiển thị. Buyer không bị thu thêm tiền.`
                : `Đơn #${orderId} giữ địa chỉ hiện tại.`,
              fields: approve
                ? []
                : [
                    {
                      name: 'reason',
                      label: 'Lý do từ chối',
                      type: 'textarea',
                      required: true,
                      maxLength: 1000,
                    },
                  ],
              confirm: approve ? 'Duyệt địa chỉ' : 'Từ chối',
            });
            if (!input) return;
            const result = await api(
              `seller/address-changes/${button.dataset.addressRequest}/decision`,
              'POST',
              { approve, reason: approve ? null : input.reason.trim() },
            );
            message(
              result.status === 'AwaitingShipper' ? 'Seller đã chấp thuận; đang chờ shipper. Địa chỉ cũ còn hiệu lực.' : result.status === 'Approved'
                ? 'Địa chỉ mới đã có hiệu lực.'
                : result.decisionReason || 'Đã từ chối đổi địa chỉ.',
              ['Approved', 'AwaitingShipper'].includes(result.status),
            );
          }
          await refresh(orderId);
        } catch (error) {
          if (error.status >= 400 && error.status < 500) submissions.delete(orderId);
          await safe(() => refresh(orderId));
          throw error;
        } finally {
          busy.delete(orderId);
          document
            .querySelectorAll(`[data-address-order="${orderId}"]`)
            .forEach((x) => (x.disabled = false));
        }
      });
    });
  }
  G4.AddressChanges = { init, summaryHtml, requiresAck, shipperNotice, blocksDelivery, decideShipper };
})();
