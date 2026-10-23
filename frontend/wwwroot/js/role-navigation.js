(() => {
  let nav,
    onNavigate,
    current,
    defaultId,
    historyMode = 'push',
    moveFocus = true;
  const $ = (id) => document.getElementById(id);
  const drawer = () => $('navigation-drawer');
  const hashId = () => location.hash.slice(1);
  function valid(id) {
    return (
      !!id &&
      !!$(id)?.classList.contains('tab-panel') &&
      (!!nav?.querySelector(`[data-tab="${CSS.escape(id)}"]`) ||
        (nav?.dataset.roleNavigation === 'buyer' && id === 'checkout'))
    );
  }
  function closeDrawer() {
    if (drawer()?.open) drawer().close();
    $('navigation-toggle')?.setAttribute('aria-expanded', 'false');
  }
  function select(id) {
    if (!valid(id)) return false;
    current = id;
    document.querySelectorAll('.tab-panel').forEach((panel) => {
      panel.classList.toggle('active', panel.id === id);
    });
    const selectedId = id === 'checkout' ? 'summary' : id;
    nav.querySelectorAll('[data-tab]').forEach((button) => {
      const active = button.dataset.tab === selectedId;
      button.classList.toggle('active', active);
      if (active) button.setAttribute('aria-current', 'page');
      else button.removeAttribute('aria-current');
    });
    const label = id === 'checkout' ? 'Thanh toán' : nav.querySelector('.active').dataset.navLabel;
    if ($('navigation-current')) $('navigation-current').textContent = label;
    document.title = `${label} - Sàn G4`;
    if (historyMode && hashId() !== id) {
      const url = new URL(location.href);
      url.hash = id;
      history[historyMode === 'replace' ? 'replaceState' : 'pushState'](null, '', url);
    }
    closeDrawer();
    if (moveFocus) {
      const heading = $(id).querySelector('h1, h2');
      heading?.setAttribute('tabindex', '-1');
      heading?.focus({ preventScroll: true });
      window.scrollTo({ top: 0, behavior: 'instant' });
    }
    return true;
  }
  function visit(id, mode = 'push', focus = true) {
    historyMode = mode;
    moveFocus = focus;
    try {
      onNavigate(valid(id) ? id : defaultId);
    } finally {
      historyMode = 'push';
      moveFocus = true;
    }
  }
  function init(navigate) {
    nav = document.querySelector('[data-role-navigation]');
    if (!nav) return;
    onNavigate = navigate;
    defaultId = nav.querySelector('[data-tab]').dataset.tab;
    nav.addEventListener('click', (event) => {
      const button = event.target.closest('[data-tab]');
      if (button) visit(button.dataset.tab);
    });
    document.addEventListener('click', (event) => {
      const button = event.target.closest('[data-nav-target]');
      if (button) visit(button.dataset.navTarget);
    });
    if (drawer()) {
      const mobile = matchMedia('(max-width: 1000px)');
      const placeNavigation = () => {
        closeDrawer();
        (mobile.matches ? $('navigation-drawer-content') : $('workspace-sidebar')).append(nav);
      };
      mobile.addEventListener('change', placeNavigation);
      placeNavigation();
      $('navigation-toggle').addEventListener('click', () => {
        drawer().showModal();
        $('navigation-toggle').setAttribute('aria-expanded', 'true');
        nav.querySelector('[aria-current="page"]')?.focus();
      });
      $('navigation-close').addEventListener('click', closeDrawer);
      drawer().addEventListener('close', () => {
        $('navigation-toggle').setAttribute('aria-expanded', 'false');
      });
      drawer().addEventListener('click', (event) => {
        if (event.target !== drawer()) return;
        const rect = drawer().getBoundingClientRect();
        if (
          event.clientX < rect.left ||
          event.clientX > rect.right ||
          event.clientY < rect.top ||
          event.clientY > rect.bottom
        )
          closeDrawer();
      });
    }
    window.addEventListener('popstate', () => visit(hashId(), 'replace'));
    window.addEventListener('hashchange', () => {
      if (hashId() !== current) visit(hashId(), 'replace');
    });
    const params = new URLSearchParams(location.search);
    const linkedOrders = params.has('orderId') || params.has('paypalCheckoutId');
    const initial = valid(hashId())
      ? hashId()
      : linkedOrders && valid('orders')
        ? 'orders'
        : params.get('tab');
    visit(initial || defaultId, 'replace', false);
  }
  G4.Navigation = {
    init,
    select,
    get current() {
      return current;
    },
  };
})();
