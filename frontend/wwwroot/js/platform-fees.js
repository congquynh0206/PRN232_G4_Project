(() => {
  const { $, api, esc, money, date, pager, safe, message } = G4;
  let latestId = 0,
    page = 1,
    initialized = false,
    request = null,
    saving = false;
  const localDate = (value) =>
    date(value && !/(Z|[+-]\d{2}:\d{2})$/i.test(value) ? value + 'Z' : value);
  function historyHtml(items, currentId, now = Date.now()) {
    return items.length
      ? items
          .map((x) => {
            const effective = new Date(
              x.effectiveAt + (/(Z|[+-]\d{2}:\d{2})$/i.test(x.effectiveAt) ? '' : 'Z'),
            ).getTime();
            const state =
              x.id === currentId
                ? 'Đang áp dụng'
                : effective > now
                  ? 'Chờ hiệu lực'
                  : 'Đã thay thế';
            return `<div class="finance-entry"><p><strong>${esc(state)} · ${esc(x.feePercent)}% + ${money(x.fixedFee)}</strong></p><p>Giá trị trước: ${esc(x.previousFeePercent)}% + ${money(x.previousFixedFee)}</p><p>Hiệu lực: ${esc(localDate(x.effectiveAt))}</p><p>Lưu bởi ${esc(x.changedBy)} · ${esc(localDate(x.createdAt))}</p><p>Lý do: ${esc(x.reason)}</p></div>`;
          })
          .join('')
      : '<p class="empty-note">Chưa có thay đổi cấu hình.</p>';
  }
  function payload() {
    const scheduled = $('platform-fee-timing').value === 'scheduled';
    const effective = $('platform-fee-effective').value;
    if (
      scheduled &&
      (!effective ||
        !Number.isFinite(new Date(effective).getTime()) ||
        new Date(effective).getTime() <= Date.now())
    )
      throw new Error('Chọn thời điểm hiệu lực trong tương lai.');
    return {
      feePercent: Number($('platform-fee-percent').value),
      fixedFee: Number($('platform-fee-fixed').value),
      effectiveAt: scheduled ? new Date(effective).toISOString() : null,
      reason: $('platform-fee-reason').value.trim(),
      expectedLatestPolicyId: latestId,
    };
  }
  function error(text) {
    const box = $('platform-fee-error');
    box.textContent = text;
    box.hidden = !text;
    if (text) box.focus();
  }
  async function load(nextPage = page, reset = false) {
    const result = await api(`admin/platform-fees?page=${nextPage}&pageSize=10`);
    page = result.page;
    $('platform-fee-current').innerHTML =
      `<h2>Mức phí hiện tại</h2><p><strong>${esc(result.current.feePercent)}% doanh thu người bán + ${money(result.current.fixedFee)}/đơn</strong></p><p class="small">Hiệu lực từ ${esc(localDate(result.current.effectiveAt))}</p>`;
    $('platform-fee-history').innerHTML = historyHtml(result.items, result.current.id);
    pager('platform-fee-pagination', result, (p) => safe(() => load(p)));
    if (!initialized || reset) {
      latestId = result.latestPolicyId;
      $('platform-fee-percent').value = result.current.feePercent;
      $('platform-fee-fixed').value = Number(result.current.fixedFee).toFixed(2);
      $('platform-fee-reason').value = '';
      $('platform-fee-timing').value = 'now';
      $('platform-fee-effective').value = '';
      updateTiming();
      initialized = true;
      request = null;
      error('');
    }
  }
  function updateTiming() {
    const scheduled = $('platform-fee-timing').value === 'scheduled';
    $('platform-fee-schedule').hidden = !scheduled;
    $('platform-fee-effective').required = scheduled;
  }
  function init() {
    if (!$('platform-fee-form')) return;
    $('platform-fee-timing').onchange = updateTiming;
    $('refresh-platform-fees').onclick = () => {
      if (!saving) safe(() => load(1, true));
    };
    $('platform-fee-form').onsubmit = async (event) => {
      event.preventDefault();
      if (saving) return;
      error('');
      try {
        const data = payload(),
          fingerprint = JSON.stringify(data);
        if (!request || request.fingerprint !== fingerprint)
          request = { fingerprint, key: crypto.randomUUID() };
        saving = true;
        $('platform-fee-fields').disabled = true;
        $('refresh-platform-fees').disabled = true;
        $('save-platform-fee').textContent = 'Đang lưu…';
        await api('admin/platform-fees', 'POST', { ...data, requestKey: request.key });
        request = null;
        message('Đã lưu mức phí và lịch sử thay đổi.', true);
        await load(1, true);
      } catch (e) {
        error(e.message);
      } finally {
        saving = false;
        $('platform-fee-fields').disabled = false;
        $('refresh-platform-fees').disabled = false;
        $('save-platform-fee').textContent = 'Lưu mức phí';
      }
    };
  }
  G4.PlatformFees = { init, load, historyHtml, payload };
})();
