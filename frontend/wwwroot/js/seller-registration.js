(() => {
  const $ = (id) => document.getElementById(id);
  let pending, busy = false, finished = false, sampleLoading = false;
  function kindChanged() {
    const bank = $('registration-kind').value === 'BANK';
    $('registration-bank-label').hidden = !bank;
    $('registration-bank').required = bank;
    $('registration-identity-help').textContent = bank ? 'Nhập mã ngân hàng và số tài khoản 6–30 chữ số. Chỉ dùng dữ liệu giả lập.'
      : $('registration-kind').value === 'PASSPORT' ? 'Passport: 1–3 chữ cái và 6–9 chữ số. Chỉ dùng dữ liệu giả lập.'
        : 'CCCD: 12 chữ số, giữ nguyên số 0 đầu. Chỉ dùng dữ liệu giả lập.';
  }
  function payload() {
    const password = $('registration-password').value;
    if (password !== $('registration-confirm').value) throw new Error('Xác nhận mật khẩu chưa khớp.');
    if (password.length < 8 || password.length > 128 || !/\p{L}/u.test(password) || !/\p{Nd}/u.test(password))
      throw new Error('Mật khẩu cần 8–128 ký tự, gồm chữ và số.');
    return { username: $('registration-username').value.trim(), email: $('registration-email').value.trim(), password,
      confirmPassword: $('registration-confirm').value, storeName: $('registration-shop').value.trim(),
      identity: { kind: $('registration-kind').value, value: $('registration-identity').value,
        bankCode: $('registration-kind').value === 'BANK' ? $('registration-bank').value : null }, key: crypto.randomUUID() };
  }
  function lock() {
    $('registration-fields').disabled = busy || !!pending || finished || sampleLoading;
    $('registration-submit').disabled = busy || finished || sampleLoading;
    $('registration-submit').textContent = finished ? 'Đã tạo tài khoản' : busy ? 'Đang kiểm tra và tạo tài khoản…' : pending ? 'Thử gửi lại cùng yêu cầu' : 'Đăng ký người bán';
    $('seller-registration-form').setAttribute('aria-busy', String(busy));
    document.querySelectorAll('[data-registration-sample]').forEach((b) => { b.disabled = busy || !!pending || finished || sampleLoading; });
  }
  async function submit(e) {
    e.preventDefault(); if (busy || finished || sampleLoading) return;
    const error = $('registration-error'); error.textContent = '';
    try {
      pending ||= payload(); busy = true; lock();
      const response = await fetch($('seller-registration-form').action, { method: 'POST',
        headers: { 'Content-Type': 'application/json', RequestVerificationToken: document.querySelector('input[name="__RequestVerificationToken"]').value },
        body: JSON.stringify(pending) });
      const result = await response.json().catch(() => ({}));
      if (!response.ok) {
        if (response.status >= 400 && response.status < 500) pending = null;
        throw new Error(result.error || result.title || 'Chưa xác định được kết quả đăng ký.');
      }
      finished = true; pending = null;
      $('registration-password').value = ''; $('registration-confirm').value = '';
      $('registration-success').hidden = false;
      $('registration-success').textContent = `Đăng ký thành công. Shop ${result.storeName}, tài khoản ${result.email} đã hoạt động ở cấp 1 với số dư 0. Bạn có thể đăng nhập bằng mật khẩu vừa tạo.`;
    } catch (ex) {
      error.textContent = (ex.message || 'Đăng ký chưa thành công.') + (pending ? ' Kết quả chưa rõ; hãy thử gửi lại cùng yêu cầu, không đổi thông tin.' : '');
      error.focus();
    } finally { busy = false; lock(); }
  }
  async function sample(name) {
    if (busy || pending || finished || sampleLoading) return;
    sampleLoading = true; lock();
    try {
      const reply = await fetch('/Account/RegistrationSamples', { cache: 'no-store' });
      const data = await reply.json();
      if (!reply.ok) throw new Error(data.error || 'Chưa tải được dữ liệu mẫu.');
      const item = data.find((x) => x.name === name);
      if (!item) throw new Error('Không tìm thấy tình huống mẫu.');
      const unique = crypto.randomUUID().slice(0, 8);
      $('registration-username').value = `Seller demo ${unique}`;
      $('registration-shop').value = `Shop demo ${unique}`;
      $('registration-email').value = `seller-${unique}@example.test`;
      $('registration-password').value = 'DemoSeller123!'; $('registration-confirm').value = 'DemoSeller123!';
      $('registration-password-help').textContent = 'Mật khẩu mẫu: DemoSeller123! Bạn có thể đổi trước khi đăng ký.';
      $('registration-kind').value = item.identity.kind; $('registration-identity').value = item.identity.value;
      $('registration-bank').value = item.identity.bankCode || ''; kindChanged();
      $('registration-error').textContent = '';
    } catch (ex) { $('registration-error').textContent = ex.message; $('registration-error').focus(); }
    finally { sampleLoading = false; lock(); }
  }
  $('seller-registration-form').onsubmit = submit;
  $('registration-kind').onchange = kindChanged;
  $('registration-password').oninput = () => {
    $('registration-password-help').textContent = 'Từ 8 đến 128 ký tự, gồm chữ và số.';
  };
  document.querySelectorAll('[data-registration-sample]').forEach((b) => { b.onclick = () => sample(b.dataset.registrationSample); });
  kindChanged();
  window.G4Registration = { payload, submit, sample };
})();
