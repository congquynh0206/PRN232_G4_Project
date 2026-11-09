(() => {
  const dialog = document.getElementById('password-confirmation-dialog');
  if (!dialog || !window.G4) return;
  const $ = (id) => document.getElementById('password-confirmation-' + id);
  const input = $('input');
  const submit = $('submit');
  const cancel = $('cancel');
  const errorBox = $('error');
  let pending = null;
  let busy = false;
  let cooldownTimer;

  function finish(error, result) {
    const operation = pending;
    pending = null;
    clearTimeout(cooldownTimer);
    cooldownTimer = null;
    input.value = '';
    dialog.close();
    if (operation?.focus?.isConnected) operation.focus.focus({ preventScroll: true });
    if (error) operation?.reject(error);
    else operation?.resolve(result);
  }
  function abort() {
    if (busy || !pending) return;
    const error = new Error('Đã hủy thao tác.');
    error.code = 'PasswordConfirmationCancelled';
    // A precondition was cancelled before the server could run the decision.
    error.status = 428;
    finish(error);
  }
  function cooldown(seconds) {
    const until = Date.now() + seconds * 1000;
    function tick() {
      if (!pending) return;
      const remaining = Math.max(0, Math.ceil((until - Date.now()) / 1000));
      submit.disabled = remaining > 0;
      submit.textContent = remaining ? `Thử lại sau ${remaining} giây` : 'Xác nhận thực hiện';
      if (remaining) cooldownTimer = setTimeout(tick, 250);
      else {
        cooldownTimer = null;
        errorBox.textContent = '';
      }
    }
    tick();
  }
  function summary(serialized) {
    const body = JSON.parse(serialized || '{}');
    const decisions = {
      SellerWins: 'Seller thắng',
      FullRefund: 'Hoàn toàn bộ',
      PartialRefund: 'Hoàn một phần',
      Refund: 'Hoàn tiền',
      ReturnRefund: 'Trả hàng để hoàn tiền',
      KeepOrder: 'Giữ nguyên đơn',
      Seller: 'Seller chịu trách nhiệm',
      Buyer: 'Buyer chịu trách nhiệm',
    };
    const result = [];
    if (typeof body.approve === 'boolean') result.push(body.approve ? 'Chấp thuận' : 'Từ chối');
    if (typeof body.paused === 'boolean')
      result.push(body.paused ? 'Tắt chương trình' : 'Bật chương trình');
    if (typeof body.suspended === 'boolean')
      result.push(body.suspended ? 'Đình chỉ chương trình' : 'Gỡ đình chỉ chương trình');
    for (const key of ['outcome', 'responsibility', 'proposal'])
      if (body[key]) result.push(decisions[body[key]] || String(body[key]));
    const fields = [
      ['amount', 'Số tiền', true],
      ['refundAmount', 'Hoàn tiền', true],
      ['feePercent', 'Phí phần trăm', false],
      ['fixedFee', 'Phí cố định', true],
      ['reason', 'Lý do', false],
      ['description', 'Nội dung', false],
      ['name', 'Chương trình', false],
      ['budget', 'Ngân sách', true],
    ];
    result.push(
      ...fields
        .filter(([key]) => body[key] !== undefined && body[key] !== null && body[key] !== '')
        .map(([key, label, money]) =>
          `${label}: ${money ? G4.money(body[key]) : String(body[key]).slice(0, 500)}`,
        ),
    );
    if (body.value !== undefined && body.type)
      result.push(`Mức giảm: ${body.isPercent ? body.value + '%' : G4.money(body.value)}`);
    if (body.freeShipping) result.push('Miễn phí giao hàng');
    return result.join(' · ');
  }
  function run(action, path, serialized, send) {
    if (pending) return Promise.reject(new Error('Hãy hoàn tất xác nhận đang mở trước.'));
    return new Promise((resolve, reject) => {
      const reference = path.match(/(?:orders|returns|disputes|address-changes|promotions)\/(\d+)/);
      $('action').textContent = action + (reference ? ` · Hồ sơ/đơn #${reference[1]}` : '');
      $('summary').textContent = summary(serialized);
      $('summary').hidden = !$('summary').textContent;
      input.value = '';
      input.removeAttribute('aria-invalid');
      errorBox.textContent = '';
      submit.disabled = false;
      cancel.disabled = false;
      submit.textContent = 'Xác nhận thực hiện';
      pending = { resolve, reject, send, focus: document.activeElement };
      dialog.showModal();
      input.focus();
    });
  }
  $('form').addEventListener('submit', async (event) => {
    event.preventDefault();
    if (!pending || busy || submit.disabled || !input.reportValidity()) return;
    busy = true;
    submit.disabled = true;
    cancel.disabled = true;
    submit.textContent = 'Đang xử lý…';
    errorBox.textContent = '';
    input.removeAttribute('aria-invalid');
    let password = input.value;
    input.value = '';
    try {
      const result = await pending.send(password);
      finish(null, result);
    } catch (error) {
      if (error.code === 'PasswordConfirmationInvalid') {
        errorBox.textContent = error.message;
        input.setAttribute('aria-invalid', 'true');
        input.focus();
      } else if (error.status === 429) {
        errorBox.textContent = error.message;
        cooldown(error.retryAfterSeconds || 60);
        input.focus();
      } else finish(error);
    } finally {
      password = '';
      busy = false;
      cancel.disabled = false;
      if (!cooldownTimer) {
        submit.disabled = false;
        submit.textContent = 'Xác nhận thực hiện';
      }
    }
  });
  cancel.addEventListener('click', abort);
  dialog.addEventListener('cancel', (event) => {
    event.preventDefault();
    abort();
  });
  window.G4.PasswordConfirmation = { run };
})();
