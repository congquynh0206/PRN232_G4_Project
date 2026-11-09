(() => {
  const navigation = document.querySelector('[data-role-navigation]');
  if (!navigation) return;
  const role = navigation.dataset.roleNavigation;
  const destination = role === 'seller' ? '/Seller' : '/Buyer';
  function bind(container) {
    container.querySelectorAll('[data-tab]').forEach((button) => {
      button.removeAttribute('aria-controls');
      button.removeAttribute('aria-current');
      button.classList.remove('active');
      button.addEventListener('click', () => {
        location.assign(`${destination}?tab=${encodeURIComponent(button.dataset.tab)}`);
      });
    });
  }
  bind(navigation);
  const toggle = document.getElementById('navigation-toggle');
  const drawer = document.getElementById('navigation-drawer');
  if (!toggle || !drawer) return;
  const clone = navigation.cloneNode(true);
  clone.querySelectorAll('[id]').forEach((element) => element.removeAttribute('id'));
  bind(clone);
  document.getElementById('navigation-drawer-content').append(clone);
  toggle.addEventListener('click', () => {
    drawer.showModal();
    toggle.setAttribute('aria-expanded', 'true');
  });
  document.getElementById('navigation-close').addEventListener('click', () => drawer.close());
  drawer.addEventListener('close', () => {
    toggle.setAttribute('aria-expanded', 'false');
    toggle.focus();
  });
})();
