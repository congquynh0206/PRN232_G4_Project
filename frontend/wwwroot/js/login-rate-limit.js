(() => {
  const notice = document.getElementById('login-rate-limit');
  const seconds = Number(notice?.dataset.retryAfter || 0);
  const button = document.querySelector('.login-form button[type="submit"]');
  if (!notice || !button || !(seconds > 0)) return;
  const original = button.textContent;
  const deadline = Date.now() + seconds * 1000;
  notice.hidden = false;
  const update = () => {
    const remaining = Math.max(0, Math.ceil((deadline - Date.now()) / 1000));
    button.disabled = remaining > 0;
    button.textContent = remaining ? `Thử lại sau ${remaining} giây` : original;
    notice.textContent = remaining
      ? 'Bạn đã thử đăng nhập quá nhiều lần. Vui lòng chờ trước khi gửi lại.'
      : 'Bạn có thể thử đăng nhập lại.';
    if (!remaining) clearInterval(timer);
  };
  const timer = setInterval(update, 1000);
  update();
})();
