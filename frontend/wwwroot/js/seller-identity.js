(() => {
  const { $, api, esc, date, safe } = G4;
  let profile, pending, busy = false;
  const stamp = (v) => date(typeof v === 'string' && !/(Z|[+-]\d{2}:\d{2})$/i.test(v) ? v + 'Z' : v);
  function historyHtml(items) {
    return items.length ? items.map((x) => `<article class="finance-row"><div><strong>${esc(x.maskedValue)}</strong><small>${x.current ? 'Định danh hiện tại' : 'Lịch sử đã xác minh'} · ${stamp(x.verifiedAt)}</small></div></article>`).join('') : '<p class="empty-note">Chưa có định danh đã xác minh. Bạn có thể bổ sung bên dưới.</p>';
  }
  function lock() {
    $('seller-identity-fields').disabled = busy || !!pending || !!profile?.permanentlyBlocked;
    $('seller-identity-submit').disabled = busy || !!profile?.permanentlyBlocked;
    $('seller-identity-submit').textContent = busy ? 'Đang xác minh…' : pending ? 'Thử lại cùng yêu cầu' : 'Xác minh và cập nhật';
    $('seller-identity-form').setAttribute('aria-busy', String(busy));
  }
  async function load() {
    profile = await api('seller/identity');
    $('seller-identity-summary').textContent = profile.permanentlyBlocked ? 'Tài khoản bị khóa bán vĩnh viễn; không được thay đổi định danh.' : 'Xác minh giả lập. Hệ thống chỉ hiển thị thông tin đã che.';
    $('seller-identity-history').innerHTML = historyHtml(profile.items); lock();
  }
  async function submit(e) {
    e.preventDefault(); if (busy) return;
    $('seller-identity-error').textContent = '';
    try {
      if (!profile) throw new Error('Hãy làm mới hồ sơ trước khi cập nhật.');
      pending ||= { expectedVersion: profile.version, key: crypto.randomUUID(), identity: {
        kind: $('seller-identity-kind').value, value: $('seller-identity-value').value,
        bankCode: $('seller-identity-kind').value === 'BANK' ? $('seller-identity-bank').value : null } };
      busy = true; lock(); await api('seller/identity', 'POST', pending); pending = null;
      $('seller-identity-value').value = ''; $('seller-identity-bank').value = '';
      $('seller-identity-result').textContent = 'Đã xác minh giả lập và cập nhật định danh.'; await load();
    } catch (error) {
      if (error.status >= 400 && error.status < 500) { pending = null; try { await load(); } catch {} }
      $('seller-identity-error').textContent = error.message + (pending ? ' Kết quả chưa rõ; hãy thử lại cùng yêu cầu.' : '');
      $('seller-identity-error').focus();
    } finally { busy = false; lock(); }
  }
  function init() {
    $('seller-identity-fields').disabled = true;
    $('seller-identity-form').onsubmit = submit;
    $('refresh-seller-identity').onclick = () => safe(load);
    $('seller-identity-kind').onchange = () => {
      const bank = $('seller-identity-kind').value === 'BANK';
      $('seller-identity-bank-label').hidden = !bank; $('seller-identity-bank').required = bank;
    };
  }
  G4.SellerIdentity = { init, load, historyHtml };
})();
