(() => {
  const { esc } = G4;
  const listeners = new Map();
  const announced = new Set();
  const monotonic = () => globalThis.performance?.now?.() ?? Date.now();
  let serverTime = Date.now(),
    sampledAt = monotonic();
  const now = () => serverTime + monotonic() - sampledAt;

  function timestamp(value) {
    if (typeof value !== 'string' || !value.trim()) return NaN;
    return Date.parse(/(?:Z|[+-]\d{2}:\d{2})$/i.test(value) ? value : value + 'Z');
  }

  function sync(value, elapsed = 0) {
    const time = Date.parse(value);
    if (!Number.isFinite(time)) return;
    serverTime = time + Math.max(0, elapsed) / 2;
    sampledAt = monotonic();
  }

  function format(seconds) {
    const total = Math.max(0, Math.ceil(seconds));
    const hours = Math.floor(total / 3600);
    const minutes = Math.floor((total % 3600) / 60);
    const pad = (value) => String(value).padStart(2, '0');
    return `${hours ? pad(hours) + ':' : ''}${pad(minutes)}:${pad(total % 60)}`;
  }

  function html(deadline, options = {}) {
    const time = timestamp(deadline);
    if (!Number.isFinite(time)) return '';
    const {
      label = 'Thời gian còn lại',
      expired = 'Đã đến hạn — đang chờ hệ thống cập nhật',
      kind = '',
      scope = '',
    } = options;
    const seconds = Math.max(0, Math.ceil((time - now()) / 1000));
    return `<span class="deadline-countdown"
      data-countdown="${new Date(time).toISOString()}" data-countdown-kind="${esc(kind)}"
      data-countdown-scope="${esc(scope)}" data-countdown-expired="${esc(expired)}"
      aria-live="off"><span>${esc(label)}</span>
      <strong role="timer" aria-live="off" data-countdown-value>
        ${seconds ? format(seconds) : esc(expired)}
      </strong></span>`;
  }

  function subscribe(kind, callback) {
    if (!listeners.has(kind)) listeners.set(kind, new Set());
    listeners.get(kind).add(callback);
  }

  function update() {
    document.querySelectorAll('[data-countdown]').forEach((node) => {
      const time = timestamp(node.dataset.countdown);
      if (!Number.isFinite(time)) return;
      const seconds = Math.max(0, Math.ceil((time - now()) / 1000));
      const value = seconds ? format(seconds) : node.dataset.countdownExpired;
      const target = node.querySelector('[data-countdown-value]');
      if (target && target.textContent !== value) target.textContent = value;
      node.classList.toggle('is-urgent', seconds > 0 && seconds <= 15);
      node.classList.toggle('is-expired', seconds === 0);
      if (!seconds) {
        const container = node.closest('.detail, .address-change-entry, .address-change-summary');
        const selectors = {
          'return-window': '[data-action="return"]',
          payment: '[data-action="retry-card"], [data-action="retry-paypal"]',
          address: '[data-address-change], [data-address-shipper]',
        };
        if (container && selectors[node.dataset.countdownKind])
          container
            .querySelectorAll(selectors[node.dataset.countdownKind])
            .forEach((button) => (button.disabled = true));
      }
      const key = `${node.dataset.countdownKind}:${node.dataset.countdownScope}:${time}`;
      if (seconds || announced.has(key)) return;
      announced.add(key);
      for (const callback of listeners.get(node.dataset.countdownKind) || []) {
        Promise.resolve()
          .then(() => callback(node.dataset.countdownScope))
          .catch(() => {});
      }
    });
  }

  function payment(order) {
    return order.status === 'AwaitingPayment'
      ? html(order.paymentExpiresAt, {
          label: 'Thanh toán trong',
          expired: 'Đã hết hạn thanh toán — chờ hệ thống đối soát và cập nhật',
          kind: 'payment',
          scope: order.id,
        })
      : '';
  }

  function receipt(request, blocked = false, scope = request?.id) {
    if (!request?.confirmationDueAt || !['Approved', 'ReturnShipping'].includes(request.status))
      return '';
    return blocked
      ? '<p class="small">Đang có tranh chấp — chờ xử lý hồ sơ trước khi hoàn tiền.</p>'
      : html(request.confirmationDueAt, {
          label: 'Seller kiểm tra hàng trả, tự hoàn tiền sau',
          expired: 'Đã đến hạn — đang chờ xác nhận và kết quả hoàn tiền từ hệ thống',
          kind: 'receipt',
          scope,
        });
  }

  function address(request) {
    return ['Pending', 'AwaitingShipper'].includes(request.status)
      ? html(request.expiresAt, {
          label: 'Hoàn tất xác nhận đổi địa chỉ trong',
          expired: 'Đã hết hạn xác nhận — chờ hệ thống cập nhật, địa chỉ cũ còn hiệu lực',
          kind: 'address',
          scope: request.id,
        })
      : '';
  }

  function dispute(item) {
    if (!item.isOpen) return '';
    if (item.returnConfirmationDueAt)
      return html(item.returnConfirmationDueAt, {
        label: 'Kiểm tra hàng trả theo thỏa thuận, tự hoàn tiền sau',
        expired: 'Đã đến hạn — chờ hệ thống thực hiện thỏa thuận và xác nhận refund',
        kind: 'dispute',
        scope: item.id,
      });
    return html(item.responseDueAt, {
      label:
        item.status === 'AwaitingSeller' ? 'Seller gửi phương án trong' : 'Buyer trả lời trong',
      expired:
        item.status === 'AwaitingSeller'
          ? 'Đã hết hạn — chờ chuyển admin xử lý'
          : 'Đã hết hạn — chờ đóng hồ sơ do buyer không phản hồi',
      kind: 'dispute',
      scope: item.id,
    });
  }

  function debt(item) {
    if (!(Number(item.balance) > 0)) return '';
    const options = { kind: 'debt', scope: item.sellerId };
    return (
      (item.permanentSalesBlockedAt
        ? ''
        : html(item.warningAt, {
            ...options,
            label: 'Tới mốc cảnh báo nợ',
            expired: 'Đã đến mốc cảnh báo nợ',
          })) +
      (item.suspended
        ? ''
        : html(item.dueAt, {
            ...options,
            label: 'Thời gian trả nợ còn lại',
            expired: 'Đã quá hạn — chờ hệ thống cập nhật hạn chế bán hàng',
          })) +
      html(item.earliestWriteOffAt, {
        ...options,
        label: 'Mốc sớm nhất có thể write-off nếu không có thu hồi mới',
        expired: 'Đã tới mốc dự kiến — chờ hệ thống kiểm tra lại điều kiện write-off',
      })
    );
  }

  G4.Countdown = {
    html,
    format,
    now,
    timestamp,
    sync,
    subscribe,
    update,
    payment,
    receipt,
    address,
    dispute,
    debt,
  };
  setInterval(() => {
    if (!document.hidden) update();
  }, 1000);
  document.addEventListener('visibilitychange', update);
  if (typeof MutationObserver !== 'undefined') {
    new MutationObserver((records) => {
      if (
        records.some((record) =>
          [...record.addedNodes].some(
            (node) =>
              node.nodeType === 1 &&
              (node.matches('[data-countdown]') || node.querySelector('[data-countdown]')),
          ),
        )
      )
        update();
    }).observe(document.body, { childList: true, subtree: true });
  }
})();
