(() => {
  const email = document.getElementById('email');
  const password = document.getElementById('password');
  const list = document.querySelector('.demo-accounts');
  if (!email || !list) return;
  list.addEventListener('click', (event) => {
    const button = event.target.closest('.demo-account');
    if (!button) return;
    email.value = button.dataset.email || '';
    list.querySelectorAll('.demo-account[aria-pressed="true"]').forEach((item) => {
      item.setAttribute('aria-pressed', 'false');
    });
    button.setAttribute('aria-pressed', 'true');
    (password || email).focus();
  });
})();
