(() => {
  const { $, api, esc } = G4;
  const toggle = $('notification-bell-toggle');
  if (!toggle) return;
  const panel = $('notification-bell-panel'),
    list = $('notification-bell-list'),
    readAll = $('notification-bell-read-all'),
    error = $('notification-bell-error');
  let page = 1,
    snapshot,
    loading = false,
    again = false,
    mutating = false,
    timer,
    count = null,
    rendered;
  function showError(message) {
    error.textContent = message;
    error.hidden = !message;
  }
  function close(restoreFocus = true) {
    panel.hidden = true;
    toggle.setAttribute('aria-expanded', 'false');
    if (restoreFocus) toggle.focus();
  }
  const utc = (value) => (/(Z|[+-]\d{2}:\d{2})$/i.test(value) ? value : value + 'Z');
  function render(data) {
    snapshot = data;
    page = data.page;
    const unread = data.unreadCount;
    $('notification-bell-badge').textContent = unread > 9 ? '9+' : String(unread);
    $('notification-bell-badge').hidden = unread === 0;
    const phrase = unread ? `Bạn có ${unread} thông báo chưa đọc` : 'Không có thông báo chưa đọc';
    toggle.setAttribute('aria-label', `Thông báo. ${phrase}`);
    $('notification-bell-count').textContent = phrase;
    if (count !== unread) $('notification-bell-announcement').textContent = phrase;
    count = unread;
    readAll.disabled = mutating || unread === 0;
    const pages = Math.max(1, Math.ceil(data.totalCount / data.pageSize));
    $('notification-bell-page').textContent = `Trang ${page} / ${pages}`;
    $('notification-bell-prev').disabled = page <= 1;
    $('notification-bell-next').disabled = page >= pages;
    $('notification-bell-empty').hidden = data.totalCount !== 0;
    const serialized = JSON.stringify(data.items);
    if (rendered === serialized) return;
    rendered = serialized;
    const focused = list.contains(document.activeElement)
      ? document.activeElement.dataset.notificationId
      : null;
    list.innerHTML = data.items
      .map((n) => {
        const stamp = utc(n.createdAt);
        const date = new Date(stamp).toLocaleString('vi-VN', {
          dateStyle: 'short',
          timeStyle: 'short',
        });
        return `<li><button type="button" data-notification-id="${n.id}"
          class="${n.readAt ? '' : 'is-unread'}">
          <strong>${esc(n.title)}</strong><span>${esc(n.message)}</span>
          <time datetime="${esc(stamp)}">${esc(date)}</time>
          <span class="bell-read-state">${n.readAt ? 'Đã đọc' : 'Chưa đọc'} · Xem chi tiết</span>
        </button></li>`;
      })
      .join('');
    if (focused) list.querySelector(`[data-notification-id="${CSS.escape(focused)}"]`)?.focus();
  }
  async function refresh() {
    if (loading) {
      again = true;
      return;
    }
    loading = true;
    list.setAttribute('aria-busy', 'true');
    try {
      render(await api(`me/notifications?page=${page}&pageSize=10`));
      showError('');
    } catch (e) {
      showError(e.message || 'Chưa tải được thông báo. Vui lòng bấm Làm mới.');
    } finally {
      list.setAttribute('aria-busy', 'false');
      loading = false;
      if (again) {
        again = false;
        schedule();
      }
    }
  }
  function schedule() {
    clearTimeout(timer);
    timer = setTimeout(refresh, 150);
  }
  toggle.addEventListener('click', () => {
    if (!panel.hidden) return close();
    panel.hidden = false;
    toggle.setAttribute('aria-expanded', 'true');
    $('notification-bell-close').focus();
    refresh();
  });
  $('notification-bell-close').addEventListener('click', () => close());
  $('notification-bell-refresh').addEventListener('click', refresh);
  $('notification-bell-prev').addEventListener('click', () => {
    page = Math.max(1, page - 1);
    refresh();
  });
  $('notification-bell-next').addEventListener('click', () => {
    page++;
    refresh();
  });
  document.addEventListener('click', (event) => {
    if (!panel.hidden && !event.target.closest('.notification-bell')) close(false);
  });
  document.addEventListener('keydown', (event) => {
    if (event.key === 'Escape' && !panel.hidden) {
      event.preventDefault();
      close();
    }
  });
  readAll.addEventListener('click', async () => {
    if (mutating || !snapshot) return;
    const throughId = snapshot.latestId;
    mutating = true;
    readAll.disabled = true;
    try {
      await api('me/notifications/read-all', 'POST', { throughId });
      await refresh();
    } catch (e) {
      showError(e.message);
    } finally {
      mutating = false;
      readAll.disabled = !snapshot?.unreadCount;
    }
  });
  list.addEventListener('click', async (event) => {
    const button = event.target.closest('[data-notification-id]');
    if (!button || mutating) return;
    const notification = snapshot.items.find((n) => String(n.id) === button.dataset.notificationId);
    if (!notification) return;
    mutating = true;
    button.disabled = true;
    try {
      await api(`me/notifications/${notification.id}/read`, 'POST');
      // The server stores only role-local paths. Also reject unsafe/cross-role stored links here.
      const target = new URL(notification.targetUrl, location.origin);
      const rolePath = '/' + location.pathname.split('/')[1].toLowerCase();
      if (target.origin !== location.origin || target.pathname.toLowerCase() !== rolePath)
        throw new Error('Liên kết thông báo không hợp lệ. Vui lòng làm mới.');
      location.assign(target.href);
    } catch (e) {
      showError(e.message);
      button.disabled = false;
    } finally {
      mutating = false;
    }
  });
  // Bell updates must continue while an order form/dialog is open; they never replace that form.
  G4.Realtime?.subscribe(['notification-inbox'], schedule, { immediate: true });
  refresh();
})();
