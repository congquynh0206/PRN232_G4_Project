(() => {
  const { $, api, esc, money, date, safe, pager } = G4;
  let debt,
    pending,
    busy = false,
    onPaid,
    selectedSeller,
    adminPage = 1;
  let historySequence = 0,
    adminSequence = 0;
  const events = {
    DebtStarted: 'Phát sinh nợ',
    DebtWarning: 'Sắp đến hạn',
    DebtOverdue: 'Đình chỉ do nợ quá hạn',
    DebtCleared: 'Đã hết nợ',
    DebtRestored: 'Gỡ đình chỉ do nợ',
    DebtRepaymentSucceeded: 'Trả nợ thành công',
    DebtRepaymentFailed: 'Trả nợ bị từ chối',
    DebtWriteOff: 'Nợ khó thu và khóa bán vĩnh viễn',
    WriteOffRecovery: 'Thu hồi nợ sau write-off',
  };
  const stamp = (v) =>
    date(typeof v === 'string' && !/(Z|[+-]\d{2}:\d{2})$/i.test(v) ? v + 'Z' : v);
  const age = (seconds) =>
    seconds >= 86400
      ? `${Math.floor(seconds / 86400)} ngày ${Math.floor((seconds % 86400) / 3600)} giờ`
      : seconds >= 3600
        ? `${Math.floor(seconds / 3600)} giờ ${Math.floor((seconds % 3600) / 60)} phút`
        : `${Math.floor(seconds)} giây`;
  function summaryHtml(d, admin = false) {
    const status = d.permanentSalesBlockedAt
      ? 'Khóa bán vĩnh viễn'
      : !Number(d.balance)
        ? 'Không còn nợ'
        : d.overdue
          ? 'Quá hạn · đình chỉ bán hàng do nợ'
          : 'Đang nợ · vẫn được bán trước hạn';
    return (
      `<p><strong>${esc(status)}</strong></p><p>Số nợ: <strong class="negative">${money(d.balance)}</strong></p>` +
      (d.permanentSalesBlockedAt
        ? `<p>Khóa bán từ: ${stamp(d.permanentSalesBlockedAt)}. Trả hết nợ không tự gỡ quyết định này; vẫn được trả nợ và xử lý đơn cũ.</p>`
        : '') +
      (d.earliestWriteOffAt && !d.permanentSalesBlockedAt
        ? `<p class="diagnostic-error">Có thể bị khóa bán vĩnh viễn và đưa định danh vào blacklist sớm nhất từ ${stamp(d.earliestWriteOffAt)} nếu vẫn còn nợ và không có khoản thu hồi mới. Mốc này lùi lại khi có khoản thu hồi; hạn trả ban đầu giữ nguyên.</p>`
        : '') +
      (d.startedAt
        ? `<p>Kỳ #${Number(d.cycleNumber)} · Bắt đầu: ${stamp(d.startedAt)} · Tuổi nợ: ${age(d.ageSeconds)}</p><p>Hạn trả: ${stamp(d.dueAt)}</p><p class="small">Trả một phần hoặc phát sinh thêm nợ không đổi hạn của kỳ này. Còn nợ thì chưa thể rút tiền.</p>`
        : '') +
      (d.overdue
        ? `<p>${admin ? 'Seller' : 'Bạn'} vẫn đăng nhập, trả nợ, giao hàng và xử lý return/dispute của đơn hiện có.</p>`
        : '') +
      (d.accountStatus !== 'Active'
        ? `<p>Hạn chế tài khoản khác: ${esc(d.accountStatus)}. Trả hết nợ chỉ gỡ hạn chế do nợ.</p>`
        : '')
    );
  }
  function historyHtml(items, kind) {
    if (!items.length) return '<p class="empty-note">Chưa có lịch sử thuộc loại này.</p>';
    return items
      .map((x) => {
        if (kind === 'repayments')
          return `<article class="finance-row"><div><strong>Biên nhận #${Number(x.id)} · ${x.status === 'Succeeded' ? 'Thành công' : 'Ngân hàng từ chối'}</strong><small>Kỳ #${Number(x.cycleNumber)} · ${stamp(x.createdAt)}</small><small>${esc(x.bankReferenceId)} · Nợ còn lại lúc ghi nhận: ${money(x.remainingDebt)}</small></div><b>${money(x.amount)}</b></article>`;
        if (kind === 'events')
          return `<article class="finance-row"><div><strong>${esc(events[x.eventType] || x.eventType)}</strong><small>Kỳ #${Number(x.cycleNumber)} · ${stamp(x.createdAt)}</small><p>${esc(x.description)}</p></div><b>Nợ: ${money(x.balance)}</b></article>`;
        if (kind === 'cycles')
          return `<article class="finance-row"><div><strong>Kỳ #${Number(x.number)} · ${x.closedAt ? 'Đã kết thúc' : 'Đang mở'}</strong><small>Bắt đầu: ${stamp(x.startedAt)} · Hạn: ${stamp(x.dueAt)}</small><small>Cảnh báo: ${stamp(x.warnedAt)} · Đình chỉ: ${stamp(x.suspendedAt)} · Kết thúc: ${stamp(x.closedAt)}</small></div><b>Nợ ban đầu: ${money(x.openingBalance)}</b></article>`;
        const types = {
          DebtWriteOff: 'Ghi nhận nợ khó thu (không đổi số dư)',
          DebtRepayment: 'Trả nợ qua ngân hàng',
          DebtRecovery: 'Bù nợ',
          Sale: 'Doanh thu',
          PlatformFee: 'Phí nền tảng',
          Payout: 'Rút tiền',
          PayoutReturned: 'Ngân hàng trả lại tiền rút',
          NegativeBalance: 'Phát sinh nợ',
          Refund: 'Hoàn tiền',
          FundsReleased: 'Giải phóng tiền vào ví',
          FundHold: 'Giữ tiền',
          HoldReleased: 'Giải phóng tiền giữ',
          HoldReserved: 'Giữ tiền chờ hoàn',
          FundHoldConsumed: 'Sử dụng tiền giữ',
          FeeCredit: 'Hoàn phí',
          PlatformSubsidy: 'Tài trợ khuyến mãi',
          PlatformSubsidyReversal: 'Thu hồi tài trợ khuyến mãi',
          ReturnShippingCost: 'Phí trả hàng',
          ShippingAdjustmentCost: 'Phí đổi địa chỉ',
          RefundCreditReleased: 'Hoàn phí vào ví',
        };
        const buckets = {
          Audit: 'Nhật ký quyết định',
          Negative: 'Số dư âm',
          Available: 'Ví khả dụng',
          Processing: 'Đang xử lý',
          OnHold: 'Đang giữ',
          SellerBalance: 'Số dư seller',
        };
        return `<article class="finance-row"><div><strong>${esc(types[x.type] || x.type)}${x.orderId ? ` · Đơn #${Number(x.orderId)}` : ''}</strong><small>${stamp(x.createdAt)} · ${esc(buckets[x.bucket] || x.bucket)}</small><p>${esc(x.description)}</p></div><b>${money(x.amount)}</b></article>`;
      })
      .join('');
  }
  function storageKey() {
    return `g4-debt-repayment-${debt.sellerId}`;
  }
  function remember() {
    if (typeof sessionStorage !== 'undefined')
      sessionStorage.setItem(storageKey(), JSON.stringify(pending));
  }
  function forget() {
    pending = null;
    if (typeof sessionStorage !== 'undefined') sessionStorage.removeItem(storageKey());
  }
  function lockForm() {
    $('debt-repayment-fields').disabled = busy || (!pending && !Number(debt?.balance));
    $('debt-repayment-amount').disabled = !!pending;
    $('debt-repayment-failure').disabled = !!pending;
    $('debt-repayment-all').disabled = !!pending;
    $('debt-repayment-submit').textContent = busy
      ? 'Đang xác nhận…'
      : pending
        ? 'Kiểm tra lại giao dịch đã gửi'
        : 'Trả nợ';
    $('debt-repayment-form').setAttribute('aria-busy', String(busy));
  }
  async function history(admin = false, page = 1) {
    const ticket = ++historySequence;
    const prefix = admin ? 'admin' : 'seller';
    const root = $(`${prefix}-debt-history`),
      kind = $(`${prefix}-debt-history-kind`).value;
    const seller = selectedSeller;
    root.setAttribute('aria-busy', 'true');
    try {
      const path = admin ? `admin/seller-debts/${seller}/history` : 'seller/debt/history';
      const result = await api(`${path}?kind=${kind}&page=${page}&pageSize=10`);
      if (ticket !== historySequence) return;
      root.innerHTML = historyHtml(result.items, kind);
      pager(admin ? 'admin-debt-history-pagination' : 'seller-debt-pagination', result, (p) =>
        safe(() => history(admin, p)),
      );
    } catch (error) {
      if (ticket === historySequence)
        root.innerHTML = '<p class="empty-note">Chưa tải được lịch sử. Hãy làm mới để thử lại.</p>';
      throw error;
    } finally {
      if (ticket === historySequence) root.setAttribute('aria-busy', 'false');
    }
  }
  async function loadSeller() {
    debt = await api('seller/debt');
    if (!pending && typeof sessionStorage !== 'undefined') {
      try {
        pending = JSON.parse(sessionStorage.getItem(storageKey()));
      } catch {
        pending = null;
      }
      if (pending) {
        $('debt-repayment-amount').value = pending.amount;
        $('debt-repayment-failure').checked = pending.simulateFailure;
        $('debt-repayment-error').textContent =
          'Có giao dịch chưa xác định kết quả. Bấm kiểm tra lại để dùng đúng mã giao dịch đã gửi.';
      }
    }
    $('seller-debt-summary').innerHTML = summaryHtml(debt);
    $('debt-repayment-amount').max = String(debt.balance);
    // Keep retry valid even when the first request has already cleared the debt.
    if (pending) $('debt-repayment-amount').max = String(pending.expectedDebt);
    $('request-payout').disabled =
      Number(debt.balance) > 0 || debt.accountStatus !== 'Active' || !!debt.permanentSalesBlockedAt;
    lockForm();
    await history();
  }
  async function submit(e) {
    e.preventDefault();
    if (busy) return;
    const errorBox = $('debt-repayment-error');
    errorBox.textContent = '';
    try {
      if (!pending) {
        const amount = Number($('debt-repayment-amount').value);
        if (
          !debt ||
          !Number.isFinite(amount) ||
          amount <= 0 ||
          amount > Number(debt.balance) ||
          Math.abs(amount * 100 - Math.round(amount * 100)) > 0.000001
        )
          throw new Error(
            'Nhập số tiền lớn hơn 0, tối đa 2 chữ số thập phân và không vượt số nợ đang hiển thị.',
          );
        pending = {
          amount,
          expectedDebt: debt.balance,
          expectedCycleNumber: debt.cycleNumber,
          key: crypto.randomUUID(),
          simulateFailure: $('debt-repayment-failure').checked,
        };
        remember();
      }
      busy = true;
      lockForm();
      const result = await api('seller/debt/repayments', 'POST', pending);
      forget();
      $('debt-repayment-result').textContent =
        `${result.status === 'Succeeded' ? 'Trả nợ thành công' : 'Ngân hàng từ chối; số dư không thay đổi'}. Biên nhận #${result.id}, mã ${result.bankReferenceId}. Nợ còn lại lúc ghi nhận: ${money(result.remainingDebt)}.`;
      $('debt-repayment-amount').value = '';
      if (onPaid) await onPaid();
      else await loadSeller();
    } catch (error) {
      if (error.status >= 400 && error.status < 500) {
        forget();
        try {
          await loadSeller();
        } catch {
          /* Keep the rejection visible even if refreshing also fails. */
        }
      }
      errorBox.textContent =
        error.message + (pending ? ' Kết quả chưa rõ; bấm kiểm tra lại giao dịch đã gửi.' : '');
      errorBox.focus();
    } finally {
      busy = false;
      lockForm();
    }
  }
  async function openSeller(id, focus = false) {
    selectedSeller = id;
    const d = await api(`admin/seller-debts/${id}`);
    if (selectedSeller !== id) return;
    $('admin-debt-detail').hidden = false;
    $('admin-debt-title').textContent = `Seller #${id}`;
    $('admin-debt-summary').innerHTML = summaryHtml(d, true);
    const identities = await api(`admin/seller-identities/${id}`);
    if (selectedSeller !== id) return;
    $('admin-debt-identities').innerHTML = identities.items.length
      ? identities.items
          .map(
            (x) =>
              `<p>${esc(x.maskedValue)} · ${x.current ? 'Hiện tại' : 'Lịch sử'} · ${stamp(x.verifiedAt)}</p>`,
          )
          .join('')
      : '<p class="empty-note">Chưa có định danh đã xác minh.</p>';
    await history(true);
    if (focus && selectedSeller === id) {
      $('admin-debt-title').setAttribute('tabindex', '-1');
      $('admin-debt-title').focus({ preventScroll: true });
      $('admin-debt-detail').scrollIntoView({ block: 'start', behavior: 'auto' });
    }
  }
  async function loadAdmin(page = adminPage) {
    const ticket = ++adminSequence,
      root = $('admin-debt-list');
    root.setAttribute('aria-busy', 'true');
    try {
      const query = new URLSearchParams({
        state: $('admin-debt-state').value,
        page: String(page),
        pageSize: '10',
      });
      if ($('admin-debt-seller').value) query.set('sellerId', $('admin-debt-seller').value);
      const result = await api(`admin/seller-debts?${query}`);
      if (ticket !== adminSequence) return;
      adminPage = result.page;
      $('admin-debt-count').textContent = `${result.totalCount} seller · Trang ${result.page}`;
      root.innerHTML = result.items.length
        ? result.items
            .map(
              (x) =>
                `<article class="finance-row"><div><strong>Seller #${Number(x.sellerId)} · ${esc(x.email)}</strong><small>${x.permanentSalesBlockedAt ? 'Khóa bán vĩnh viễn' : x.overdue ? 'Quá hạn · đình chỉ do nợ' : Number(x.negativeBalance) ? 'Đang nợ' : 'Không còn nợ'} · Trạng thái khác: ${esc(x.status)}</small><small>Bắt đầu: ${stamp(x.debtStartedAt)} · Hạn trả: ${stamp(x.debtDueAt)}</small><button type="button" data-debt-seller="${Number(x.sellerId)}">Xem lịch sử seller #${Number(x.sellerId)}</button></div><b>${money(x.negativeBalance)}</b></article>`,
            )
            .join('')
        : '<p class="empty-note">Không có seller phù hợp.</p>';
      root.querySelectorAll('[data-debt-seller]').forEach((button) => {
        button.onclick = () => safe(() => openSeller(Number(button.dataset.debtSeller), true));
      });
      pager('admin-debt-pagination', result, (p) => safe(() => loadAdmin(p)));
      if (selectedSeller) await openSeller(selectedSeller);
    } finally {
      if (ticket === adminSequence) root.setAttribute('aria-busy', 'false');
    }
  }
  function init(role, refresh) {
    if (role === 'seller') {
      onPaid = refresh;
      $('debt-repayment-fields').disabled = true;
      $('debt-repayment-form').onsubmit = submit;
      $('debt-repayment-all').onclick = () => {
        $('debt-repayment-amount').value = debt?.balance || '';
      };
      $('seller-debt-history-kind').onchange = () => safe(() => history());
    } else {
      $('admin-debt-state').value = 'debt';
      $('admin-debt-history-kind').value = 'repayments';
      $('admin-debt-filters').onsubmit = (e) => {
        e.preventDefault();
        safe(() => loadAdmin(1));
      };
      $('refresh-seller-debts').onclick = () => safe(() => loadAdmin());
      $('admin-debt-history-kind').onchange = () => {
        if (selectedSeller) safe(() => history(true));
      };
    }
  }
  G4.SellerDebt = { init, loadSeller, loadAdmin, openSeller, summaryHtml, historyHtml };
})();
