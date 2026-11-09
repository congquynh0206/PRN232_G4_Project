(() => {
  const keys = new Map();
  const failures = new Map();
  const storageKey = (orderId) => `g4-klarna-attempt:${orderId}`;
  function clear(orderId) {
    keys.delete(orderId);
    try { sessionStorage.removeItem(storageKey(orderId)); } catch { /* Storage may be disabled. */ }
  }
  function attemptKey(orderId) {
    let key = keys.get(orderId);
    if (!key) {
      try { key = sessionStorage.getItem(storageKey(orderId)); } catch { /* Storage may be disabled. */ }
    }
    if (!key) key = crypto.randomUUID();
    keys.set(orderId, key);
    try { sessionStorage.setItem(storageKey(orderId), key); } catch { /* Storage may be disabled. */ }
    return key;
  }
  function recordFailure(orderId, paymentId) {
    if (paymentId == null) return;
    const marker = String(paymentId);
    let previous = failures.get(orderId);
    if (!previous) {
      try { previous = sessionStorage.getItem(`g4-klarna-failure:${orderId}`); } catch { /* Storage may be disabled. */ }
    }
    if (previous === marker) return;
    clear(orderId);
    failures.set(orderId, marker);
    try { sessionStorage.setItem(`g4-klarna-failure:${orderId}`, marker); } catch { /* Storage may be disabled. */ }
  }
  async function begin(orderId, failedPaymentId = null) {
    recordFailure(orderId, failedPaymentId);
    const result = await G4.api(`orders/${orderId}/pay/klarna`, 'POST', {
      key: attemptKey(orderId),
    });
    if (!result.redirectUrl) {
      throw new Error('Chưa có đường dẫn Klarna. Vui lòng kiểm tra trạng thái đơn hàng.');
    }
    const url = new URL(result.redirectUrl);
    if (url.protocol !== 'https:') {
      throw new Error('Đường dẫn Klarna không hợp lệ. Vui lòng kiểm tra trạng thái đơn hàng.');
    }
    location.assign(url.href);
  }
  async function reconcile(orderId) {
    const result = await G4.api(`orders/${orderId}/pay/klarna/reconcile`, 'POST');
    if (['Authorized', 'Succeeded', 'Failed'].includes(result.status)) clear(orderId);
    return result;
  }
  G4.Klarna = { begin, reconcile };
})();
