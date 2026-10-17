(() => {
  const listeners = [], pending = new Set();
  let connection, connected = false, timer, flushing = false, stopped = false;
  const status = document.getElementById('realtime-status');
  function state(text, online) { connected = online; if (status) { status.textContent = text; status.dataset.connected = String(online); } }
  function blocked() {
    return document.hidden || !!document.querySelector('dialog[open]:not(#detail-dialog)') ||
      !!document.activeElement?.closest('input,textarea,select,[contenteditable=true],.pagination');
  }
  function invalidate(topics = ['all']) { topics.forEach((t) => pending.add(t)); schedule(); }
  function schedule() { clearTimeout(timer); timer = setTimeout(flush, 350); }
  async function flush() {
    if (stopped) { pending.clear(); return; }
    if (!pending.size || flushing) return;
    if (blocked()) { timer = setTimeout(flush, 1000); return; }
    const topics = new Set(pending); pending.clear(); flushing = true;
    try {
      for (const listener of listeners) {
        if (!topics.has('all') && !listener.topics.some((t) => topics.has(t))) continue;
        try { if (await listener.handler(topics) === false) pending.add('all'); } catch { pending.add('all'); }
      }
    } finally { flushing = false; if (pending.size) timer = setTimeout(flush, 3000); }
  }
  async function session() {
    const result = await fetch('/Realtime/Session', { cache: 'no-store', credentials: 'same-origin' });
    if (result.status === 401 || result.redirected) { stopped = true; throw new Error('Signed out'); }
    if (!result.ok) throw new Error('Realtime unavailable');
    return result.json();
  }
  async function start() {
    if (stopped || typeof navigator !== 'undefined' && navigator.onLine === false) return;
    if (connection && connection.state !== signalR.HubConnectionState.Disconnected) return;
    try {
      if (!window.signalR) throw new Error('Missing SignalR');
      if (!connection) {
        const s = await session();
        connection = new signalR.HubConnectionBuilder().withUrl(s.url, {
          accessTokenFactory: async () => (await session()).token,
          withCredentials: false,
        }).configureLogging(signalR.LogLevel.Error).withAutomaticReconnect([0, 2000, 5000, 10000, 30000]).build();
        connection.on('CommerceChanged', (event) => invalidate(event.topics));
        connection.onreconnecting(() => state('Đang kết nối lại…', false));
        connection.onreconnected(() => { state('Đang cập nhật trực tiếp', true); invalidate(); });
        connection.onclose(() => { state('Mất kết nối trực tiếp; đang thử lại', false); if (!stopped) setTimeout(start, 5000); });
      }
      await connection.start(); state('Đang cập nhật trực tiếp', true); invalidate();
    } catch { state('Chưa kết nối trực tiếp; vẫn có thể làm mới', false); if (!stopped) setTimeout(start, 5000); }
  }
  window.G4.Realtime = { subscribe(topics, handler) { listeners.push({ topics, handler }); }, invalidate,
    get connected() { return connected; }, flush, get connection() { return connection; } };
  document.addEventListener('visibilitychange', () => { if (!document.hidden) invalidate(); });
  document.addEventListener('focusout', () => { if (pending.size) schedule(); });
  document.addEventListener('close', () => { if (pending.size) schedule(); }, true);
  window.addEventListener('online', () => { invalidate(); if (!connected && connection?.state === signalR.HubConnectionState.Disconnected) start(); });
  window.addEventListener('offline', () => { state('Đang ngoại tuyến; sẽ cập nhật khi có mạng', false); connection?.stop().catch(() => {}); });
  // Slow fallback also covers changes during a prolonged disconnected session.
  setInterval(() => { if (!connected) invalidate(); }, 30000);
  start();
})();
