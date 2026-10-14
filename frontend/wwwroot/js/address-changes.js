(() => {
  const { $, esc, money, date, api, message, modal, safe } = G4;
  const busy = new Set(),
    drafts = new Map();
  const stamp = (value) =>
    date(typeof value === 'string' && !/(Z|[+-]\d{2}:\d{2})$/i.test(value) ? value + 'Z' : value);
  const labels = { Pending: 'Chờ seller duyệt', Approved: 'Đã duyệt', Rejected: 'Đã từ chối' };
  function summaryHtml(order, role) {
    const changes = order.addressChanges || [];
    if (!changes.length && !order.canRequestAddressChange) return '';
    return `<section class="address-change-summary"><h3>Đổi địa chỉ giao hàng</h3>${role === 'buyer' && order.canRequestAddressChange ? `<p>Được đổi thành công một lần trước khi bắt đầu giao tới bạn. Seller cần duyệt địa chỉ mới.</p><button type="button" data-address-change="request" data-address-order="${order.id}">Yêu cầu đổi địa chỉ</button>` : ''}${changes
      .map((r) => {
        const increase = Math.max(0, r.newShippingCost - r.oldShippingCost);
        return `<article class="address-change-entry"><p><strong>${esc(labels[r.status] || r.status)}</strong> · ${stamp(r.createdAt)}</p><div class="address-change-grid"><div><strong>Địa chỉ cũ</strong><p>${esc(r.oldAddress)}</p></div><div><strong>Địa chỉ yêu cầu</strong><p>${esc(r.newAddress)}</p></div></div><p>Lý do: ${esc(r.reason)}</p><p>Phí ship theo tuyến: ${money(r.oldShippingCost)} → ${money(r.newShippingCost)}</p>${increase > 0 ? `<p>Seller chịu thêm khi duyệt: <strong>${money(increase)}</strong> · Mức chấp nhận: ${money(r.sellerCap)}</p>` : ''}${r.buyerRefund > 0 ? `<p>${r.status === 'Approved' ? 'Đã hoàn chênh lệch ship bằng giao dịch giả lập' : 'Hoàn chênh lệch ship dự kiến khi duyệt'}: <strong>${money(r.buyerRefund)}</strong></p>` : ''}${r.decisionReason ? `<p class="address-change-note">${esc(r.decisionReason)}</p>` : ''}${role === 'seller' && r.status === 'Pending' ? `<div class="actions"><button type="button" class="primary" data-address-change="approve" data-address-request="${r.id}" data-address-order="${order.id}">Duyệt địa chỉ mới</button><button type="button" data-address-change="reject" data-address-request="${r.id}" data-address-order="${order.id}">Từ chối đổi địa chỉ</button></div>` : ''}</article>`;
      })
      .join('')}</section>`;
  }
  function requiresAck(shipment) {
    return shipment.addressChangeRequestId != null && !shipment.addressChangeAcknowledgedAt;
  }
  function shipperNotice(shipment, order) {
    const request = (order.addressChanges || []).find(
      (x) => x.id === shipment.addressChangeRequestId,
    );
    if (!request) return '';
    return `<section class="address-change-summary"><h3>Địa chỉ mới đã được seller duyệt</h3><div class="address-change-grid"><div><strong>Địa chỉ cũ</strong><p>${esc(request.oldAddress)}</p></div><div><strong>Địa chỉ mới</strong><p>${esc(request.newAddress)}</p></div></div>${requiresAck(shipment) ? `<p>Xác nhận đã đọc địa chỉ mới trước khi cập nhật vận chuyển.</p><button type="button" class="primary" data-ack-address="${shipment.id}">Xác nhận địa chỉ mới</button>` : '<p>Đã xác nhận địa chỉ mới.</p>'}</section>`;
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
          if (role === 'buyer' && action === 'request') {
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
              confirm: 'Gửi yêu cầu',
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
            const result = await api(`orders/${orderId}/address-changes`, 'POST', {
              address: Object.fromEntries(
                fields
                  .filter((x) => x.name !== 'reason')
                  .map((x) => [x.name, input[x.name].trim()]),
              ),
              reason: input.reason.trim(),
              key: crypto.randomUUID(),
            });
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
              result.status === 'Approved'
                ? 'Đã duyệt địa chỉ mới.'
                : result.decisionReason || 'Đã từ chối đổi địa chỉ.',
              result.status === 'Approved',
            );
          }
          await refresh(orderId);
        } catch (error) {
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
  G4.AddressChanges = { init, summaryHtml, requiresAck, shipperNotice };
})();
