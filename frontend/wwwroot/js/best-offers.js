(() => {
  const { $, api, esc, money, date, message, productThumbnail } = G4;
  const state = { addresses: [], catalog: [], offers: [], filter: 'all' };
  // One idempotency key per click/submit, kept until it succeeds so a retry reuses it.
  const pendingKeys = new WeakMap();
  const keyFor = (element) => {
    if (!pendingKeys.has(element)) pendingKeys.set(element, crypto.randomUUID());
    return pendingKeys.get(element);
  };
  const active = (offer) => ['PendingSeller', 'PendingBuyer'].includes(offer.status);
  const payLater = (offer) => offer.paymentMethod === 'PayLater';
  const round2 = (value) => Math.round(Number(value) * 100) / 100;
  const cents = (value) => round2(value).toFixed(2);
  const myTurn = (offer) =>
    (offer.status === 'PendingSeller' && offer.viewerRole === 'seller') ||
    (offer.status === 'PendingBuyer' && offer.viewerRole === 'buyer');
  // "action" = the viewer must answer, "waiting" = the other side must answer.
  const group = (offer) => (!active(offer) ? 'closed' : myTurn(offer) ? 'action' : 'waiting');
  const otherRole = (offer) => (offer.viewerRole === 'buyer' ? 'seller' : 'buyer');
  const partyName = (role, viewerRole) =>
    role === viewerRole ? 'Bạn' : role === 'seller' ? 'Người bán' : 'Người mua';
  const percentOff = (amount, listPrice) =>
    listPrice > 0 ? Math.round((1 - amount / listPrice) * 1000) / 10 : 0;
  const savingText = (amount, listPrice) => {
    const saved = round2(listPrice - amount);
    return saved > 0 ? `thấp hơn ${money(saved)} (${percentOff(amount, listPrice)}%)` : '';
  };

  const statusBadge = (offer) => {
    const kind = group(offer);
    if (kind === 'action') return { label: 'Đến lượt bạn', tone: 'action' };
    if (kind === 'waiting')
      return {
        label: offer.viewerRole === 'buyer' ? 'Chờ người bán' : 'Chờ người mua',
        tone: 'waiting',
      };
    if (offer.status === 'Accepted')
      return {
        label: payLater(offer) ? 'Đã chốt · chờ thanh toán' : 'Đã chốt · đã thanh toán',
        tone: 'success',
      };
    return {
      label: { Declined: 'Đã từ chối', Expired: 'Đã hết hạn' }[offer.status] || offer.status,
      tone: 'muted',
    };
  };
  const cardFields = (prefix) => `
    <label>${prefix}
      <select name="cardProfile">
        <option value="Success">4111…1111 — thành công</option>
        <option value="Declined">4000…0002 — từ chối</option>
      </select>
    </label>
    <label>Hạn thẻ<input name="cardExpiry" value="12/30" maxlength="5" /></label>`;

  function remaining(expiresAt) {
    const minutes = Math.floor((new Date(expiresAt) - Date.now()) / 60000);
    if (minutes < 1) return null;
    const hours = Math.floor(minutes / 60);
    if (!hours) return `${minutes} phút`;
    return minutes % 60 ? `${hours} giờ ${minutes % 60} phút` : `${hours} giờ`;
  }

  function deadlineText(element) {
    const left = remaining(element.dataset.offerExpires);
    if (!left) return 'Đã quá hạn phản hồi, hệ thống sẽ đóng phiên.';
    const who = element.dataset.offerWho;
    return who === 'Bạn'
      ? `Bạn cần phản hồi trong ${left}.`
      : `${who} có ${left} để phản hồi.`;
  }

  // Each list loads on its own: one failed request (rate limit, API restart, network) shows
  // its reason in that list instead of leaving every list stuck on "Đang tải…".
  function renderList(element, request, render) {
    if (request.status === 'fulfilled') {
      try {
        element.innerHTML = render(request.value);
        return null;
      } catch (error) {
        request = { status: 'rejected', reason: error };
      }
    }
    const reason = request.reason?.message || 'Lỗi không xác định';
    element.innerHTML =
      `<p class="empty-state">Chưa tải được danh sách: ${esc(reason)} ` +
      'Bấm “Làm mới” để thử lại.</p>';
    return request.reason;
  }

  async function loadLists(lists) {
    lists.forEach(([element]) => element.setAttribute('aria-busy', 'true'));
    const results = await Promise.allSettled(lists.map(([, request]) => request()));
    const errors = lists
      .map(([element, , render], index) => renderList(element, results[index], render))
      .filter(Boolean);
    lists.forEach(([element]) => element.setAttribute('aria-busy', 'false'));
    if (errors.length) message(errors[0].message || 'Chưa tải được dữ liệu trả giá.');
  }

  // Realtime refreshes re-render the lists; keep open forms and typed amounts.
  function keepDrafts(root) {
    const drafts = [...root.querySelectorAll('[data-keep]')].map((element) => [
      element.id,
      element.tagName === 'DETAILS' ? element.open : element.value,
    ]);
    return () =>
      drafts.forEach(([id, value]) => {
        const element = document.getElementById(id);
        if (!element) return;
        if (element.tagName === 'DETAILS') element.open = value;
        else if (value !== '') {
          element.value = value;
          element.dispatchEvent(new Event('input', { bubbles: true }));
        }
      });
  }

  function sortOffers(offers) {
    const rank = { action: 0, waiting: 1, closed: 2 };
    const lastAt = (offer) => new Date(offer.turns?.at(-1)?.createdAt || 0).getTime();
    return [...offers].sort(
      (a, b) =>
        rank[group(a)] - rank[group(b)] ||
        (active(a)
          ? new Date(a.expiresAt) - new Date(b.expiresAt)
          : lastAt(b) - lastAt(a)) ||
        b.id - a.id,
    );
  }

  async function buyerLoad() {
    const catalog = $('offer-catalog');
    const offers = $('buyer-best-offer-list');
    if (!catalog || !offers) return;
    try {
      const addressBook = await api('addresses');
      state.addresses = Array.isArray(addressBook)
        ? addressBook
        : addressBook.addresses || [];
    } catch {
      state.addresses = [];
    }
    const restore = keepDrafts($('best-offers'));
    // Both requests settle before either list renders, so each list can use the other's data.
    await loadLists([
      [
        offers,
        () => api('best-offers/buyer').then((result) => (state.offers = result)),
        (result) =>
          result.length
            ? sortOffers(result)
                .map((offer) => offerCard(offer))
                .join('') + '<p class="empty-state" data-offer-filter-empty hidden></p>'
            : '<p class="empty-state">Bạn chưa có phiên trả giá nào. Chọn một sản phẩm bên ' +
              'dưới và bấm “Trả giá” để bắt đầu.</p>',
      ],
      [
        catalog,
        () => api('best-offers/catalog').then((result) => (state.catalog = result)),
        (products) =>
          products.length
            ? products.map(productCard).join('')
            : '<p class="empty-state">Chưa có sản phẩm bật trả giá.</p>',
      ],
    ]);
    restore();
    renderFilters();
  }

  async function sellerLoad() {
    const products = $('seller-offer-products');
    const offers = $('seller-offer-list');
    if (!products || !offers) return;
    const restore = keepDrafts($('best-offers'));
    await loadLists([
      [
        products,
        () => api('best-offers/seller/products'),
        (items) =>
          items.length
            ? items.map(settingsCard).join('')
            : '<p class="empty-state">Chưa có sản phẩm giá cố định trong cửa hàng.</p>',
      ],
      [
        offers,
        () => api('best-offers/seller'),
        (result) =>
          result.length
            ? sortOffers(result)
                .map((offer) => offerCard(offer))
                .join('')
            : '<p class="empty-state">Chưa có đề nghị trả giá.</p>',
      ],
    ]);
    restore();
  }

  const filters = [
    ['all', 'Tất cả'],
    ['action', 'Cần bạn phản hồi'],
    ['waiting', 'Chờ người bán'],
    ['closed', 'Đã kết thúc'],
  ];

  function renderFilters() {
    const bar = $('buyer-offer-filters');
    if (!bar) return;
    const counts = { all: state.offers.length, action: 0, waiting: 0, closed: 0 };
    state.offers.forEach((offer) => (counts[group(offer)] += 1));
    bar.hidden = !state.offers.length;
    bar.innerHTML = filters
      .map(
        ([key, label]) =>
          `<button type="button" data-offer-filter="${key}"
            aria-pressed="${state.filter === key}"
            class="${key === 'action' && counts.action ? 'has-attention' : ''}">
            ${label}<span class="offer-filter-count">${counts[key]}</span>
          </button>`,
      )
      .join('');
    applyFilter();
  }

  function applyFilter() {
    const list = $('buyer-best-offer-list');
    if (!list) return;
    let shown = 0;
    list.querySelectorAll('[data-offer-group]').forEach((card) => {
      card.hidden = state.filter !== 'all' && card.dataset.offerGroup !== state.filter;
      if (!card.hidden) shown += 1;
    });
    const empty = list.querySelector('[data-offer-filter-empty]');
    if (empty) {
      empty.hidden = shown > 0;
      empty.textContent = 'Không có phiên nào trong mục này.';
    }
  }

  function productCard(product) {
    const listPrice = Number(product.listPrice);
    const openOffer = state.offers.find(
      (offer) => offer.productId === product.productId && active(offer),
    );
    const exhausted = product.buyerOffersUsed >= product.buyerOfferLimit;
    const left = product.buyerOfferLimit - product.buyerOffersUsed;
    let body;
    if (openOffer) {
      body = `<div class="offer-note">
        <span>Bạn đang thương lượng sản phẩm này
          (${esc(statusBadge(openOffer).label.toLowerCase())}).</span>
        <button type="button" data-offer-jump="${openOffer.id}">Xem phiên</button>
      </div>`;
    } else if (exhausted) {
      body = '<div class="offer-note">Bạn đã dùng hết lượt trả giá cho sản phẩm này.</div>';
    } else if (!state.addresses.length) {
      body = `<div class="offer-note">
        <span>Thêm địa chỉ giao hàng trước khi trả giá.</span>
        <button type="button" data-offer-addresses>Thêm địa chỉ</button>
      </div>`;
    } else {
      body = composeForm(product, listPrice);
    }
    return `<article class="offer-card offer-product">
      <div class="offer-product-main">
        ${productThumbnail(product.imageUrl, product.title, 'offer-thumb')}
        <div class="offer-product-info">
          <strong class="offer-title">${esc(product.title)}</strong>
          <span class="offer-product-price">${money(listPrice)}</span>
          <small>Còn ${product.available} sản phẩm ·
            Còn ${Math.max(left, 0)}/${product.buyerOfferLimit} lượt trả giá</small>
        </div>
      </div>
      ${body}
    </article>`;
  }

  function composeForm(product, listPrice) {
    const id = product.productId;
    const options = state.addresses
      .map(
        (address) =>
          `<option value="${address.id}" ${address.isDefault ? 'selected' : ''}>` +
          `${esc(address.fullName)} — ${esc(address.city)}</option>`,
      )
      .join('');
    const quick = [5, 10, 15, 20]
      .map((percent) => cents(listPrice * (1 - percent / 100)))
      .filter((amount) => Number(amount) > 0)
      .map(
        (amount, index) =>
          `<button type="button" data-offer-fill="${amount}">
            -${[5, 10, 15, 20][index]}% · ${money(amount)}</button>`,
      )
      .join('');
    return `<details class="offer-compose" id="offer-compose-${id}" data-keep>
      <summary>
        <span class="when-closed">Trả giá</span><span class="when-open">Thu gọn</span>
      </summary>
      <form data-offer-create="${id}" class="offer-form">
        <label>Giá bạn đề nghị (USD)
          <input id="offer-amount-${id}" data-keep name="amount" type="number" min="0.01"
            max="${cents(listPrice - 0.01)}" step="0.01" inputmode="decimal" required
            data-list-price="${listPrice}" placeholder="Thấp hơn ${cents(listPrice)}" />
        </label>
        <p class="offer-preview" data-offer-preview>
          Nhập giá thấp hơn giá niêm yết ${money(listPrice)} hoặc chọn nhanh:</p>
        <div class="offer-quick" aria-label="Chọn nhanh mức giảm">${quick}</div>
        <label>Địa chỉ giao hàng
          <select name="addressId" required>${options}</select>
        </label>
        <fieldset class="offer-payment">
          <legend>Khi người bán chấp nhận</legend>
          <label class="offer-check">
            <input type="radio" name="paymentMethod" value="PayLater" checked />
            <span><strong>Thanh toán sau</strong>
              <small>Tạo đơn chờ thanh toán, bạn trả trong tab Đơn hàng trước hạn.</small></span>
          </label>
          <label class="offer-check">
            <input type="radio" name="paymentMethod" value="Card" />
            <span><strong>Auto-Pay</strong>
              <small>Thu tiền credit card ngay khi đề nghị được chấp nhận.</small></span>
          </label>
        </fieldset>
        <div data-card-fields hidden class="offer-card-fields">
          ${cardFields('Credit card đã lưu')}
        </div>
        <button class="primary">Gửi đề nghị</button>
      </form>
    </details>`;
  }

  function settingsCard(product) {
    return `<article class="offer-card">
      <form data-offer-settings="${product.productId}" class="offer-form">
        <div class="offer-card-head">
          <strong>${esc(product.title)}</strong><span>Giá ${money(product.price)}</span>
        </div>
        <label class="offer-check">
          <input type="checkbox" name="allowOffers" ${product.allowOffers ? 'checked' : ''} />
          Cho phép người mua trả giá
        </label>
        <label>Tự động chấp nhận từ mức giá
          <input name="autoAcceptPrice" type="number" min="0.01" step="0.01"
            max="${product.price}" value="${product.autoAcceptPrice ?? ''}" />
        </label>
        <label>Tự động từ chối dưới mức giá
          <input name="autoDeclineBelow" type="number" min="0" step="0.01"
            max="${product.price}" value="${product.autoDeclineBelow ?? ''}" />
        </label>
        <button>Lưu cấu hình</button>
      </form>
    </article>`;
  }

  const lastProposal = (offer, role) =>
    (offer.turns || [])
      .filter((turn) => turn.actorRole === role && ['Offer', 'Counter'].includes(turn.action))
      .at(-1)?.amount;

  // Valid counter range: seller between the buyer's offer and the list price; buyer between
  // their previous offer and the seller's counter. The server enforces the same rule.
  function counterRange(offer) {
    if (offer.status === 'PendingSeller')
      return { min: offer.currentAmount + 0.01, max: offer.listPrice - 0.01 };
    const previous = lastProposal(offer, 'buyer') ?? 0;
    return { min: previous + 0.01, max: offer.currentAmount - 0.01 };
  }

  function counterForm(offer) {
    const role = offer.viewerRole;
    const range = counterRange(offer);
    if (range.min > range.max)
      return '<small class="offer-hint">Không còn khoảng giá để trả giá lại; hãy chấp nhận ' +
        'hoặc từ chối.</small>';
    const middle = cents(Math.min(range.max, Math.max(range.min, (range.min + range.max) / 2)));
    return `<form class="offer-counter" data-offer-counter="${role}" data-id="${offer.id}">
      <label for="offer-counter-${offer.id}">Hoặc trả giá lại</label>
      <div class="offer-counter-row">
        <input id="offer-counter-${offer.id}" data-keep name="amount" type="number" step="0.01"
          min="${cents(range.min)}" max="${cents(range.max)}" inputmode="decimal"
          placeholder="${money(cents(range.min))} – ${money(cents(range.max))}" required />
        <button>Gửi giá</button>
      </div>
      <div class="offer-quick">
        <button type="button" data-offer-fill="${middle}">Chia đôi · ${money(middle)}</button>
      </div>
      <small class="offer-hint">Trong khoảng ${money(cents(range.min))} –
        ${money(cents(range.max))}.</small>
    </form>`;
  }

  // Bar scaled from 0 to the list price with a marker for each side's latest price.
  function priceMeter(offer, buyerPrice, sellerPrice) {
    const listPrice = Number(offer.listPrice);
    if (!(listPrice > 0) || buyerPrice == null) return '';
    const at = (value) => Math.min(100, Math.max(0, (value / listPrice) * 100)).toFixed(1);
    const gap =
      sellerPrice != null && sellerPrice > buyerPrice
        ? `<span class="offer-meter-gap"
            style="left:${at(buyerPrice)}%;width:${(at(sellerPrice) - at(buyerPrice)).toFixed(1)}%">
          </span>`
        : '';
    const seller =
      sellerPrice != null
        ? `<span class="offer-meter-mark is-seller" style="left:${at(sellerPrice)}%"></span>`
        : '';
    return `<div class="offer-meter" aria-hidden="true">
      <span class="offer-meter-fill" style="width:${at(buyerPrice)}%"></span>${gap}
      <span class="offer-meter-mark is-buyer" style="left:${at(buyerPrice)}%"></span>${seller}
    </div>`;
  }

  function priceCompare(offer) {
    const viewer = offer.viewerRole;
    const listPrice = Number(offer.listPrice);
    const buyerPrice = lastProposal(offer, 'buyer');
    const sellerPrice = lastProposal(offer, 'seller');
    const cell = (tone, label, value, note) => `<div class="offer-price ${tone}">
        <span>${label}</span>
        <strong>${value == null ? '—' : money(value)}</strong>
        <small>${note}</small>
      </div>`;
    const off = (value) => (value == null ? '' : `Giảm ${percentOff(value, listPrice)}%`);
    let summary = '';
    if (offer.status === 'Accepted') {
      const saved = savingText(offer.currentAmount, listPrice);
      summary =
        `Giá chốt <strong>${money(offer.currentAmount)}</strong>` +
        `${saved ? `, ${saved} so với giá niêm yết` : ''}.`;
    } else if (active(offer) && buyerPrice != null && sellerPrice != null) {
      summary = `Hai bên còn chênh <strong>${money(round2(sellerPrice - buyerPrice))}</strong>.`;
    } else if (group(offer) === 'waiting' && viewer === 'buyer' && buyerPrice != null) {
      summary = `Đang chờ người bán phản hồi mức ${money(buyerPrice)}.`;
    }
    return `<div class="offer-compare">
        ${cell('is-list', 'Giá niêm yết', listPrice, 'Giá gốc')}
        ${cell(
          'is-buyer',
          viewer === 'buyer' ? 'Bạn đề nghị' : 'Người mua đề nghị',
          buyerPrice,
          off(buyerPrice),
        )}
        ${
          offer.status === 'Accepted'
            ? cell('is-final', 'Giá chốt', offer.currentAmount, off(offer.currentAmount))
            : cell(
                'is-seller',
                viewer === 'seller' ? 'Bạn đưa ra' : 'Người bán đưa ra',
                sellerPrice,
                sellerPrice == null ? 'Chưa trả giá' : off(sellerPrice),
              )
        }
      </div>
      ${priceMeter(offer, buyerPrice, sellerPrice)}
      ${summary ? `<p class="offer-summary">${summary}</p>` : ''}`;
  }

  const verbs = {
    Offer: 'gửi đề nghị',
    Counter: 'trả giá lại',
    Accept: 'chấp nhận',
    Decline: 'từ chối',
  };
  const results = {
    AutoDeclined: ['Tự động từ chối (dưới ngưỡng của người bán)', 'muted'],
    PaymentFailed: ['Thanh toán credit card thất bại', 'danger'],
    AcceptedAndPaid: ['Đã chốt và thanh toán', 'success'],
    AcceptedUnpaid: ['Đã chốt, đơn chờ thanh toán', 'success'],
  };

  function timeline(offer) {
    const items = (offer.turns || []).map((turn) => {
      const autoAccepted =
        ['Offer', 'Counter'].includes(turn.action) && turn.result?.startsWith('Accepted');
      const [resultText, tone] = results[turn.result] || [turn.result, 'muted'];
      const tag = turn.result
        ? `<span class="offer-tag is-${tone}">${esc(
            autoAccepted ? `Người bán tự động chấp nhận · ${resultText}` : resultText,
          )}</span>`
        : '';
      return `<li class="is-${esc(turn.actorRole)}">
        <div><strong>${partyName(turn.actorRole, offer.viewerRole)}</strong>
          ${verbs[turn.action] || esc(turn.action)} <b>${money(turn.amount)}</b>${tag}</div>
        <time datetime="${esc(turn.createdAt)}">${date(turn.createdAt)}</time>
      </li>`;
    });
    if (offer.status === 'Expired')
      items.push(`<li class="is-system"><div>Phiên hết hạn vì không có phản hồi.</div>
        <time datetime="${esc(offer.expiresAt)}">${date(offer.expiresAt)}</time></li>`);
    return items.length
      ? `<details class="offer-history" ${items.length <= 4 ? 'open' : ''}>
          <summary>Lịch sử thương lượng (${items.length})</summary>
          <ol class="offer-timeline">${items.join('')}</ol>
        </details>`
      : '';
  }

  function decideBlock(offer) {
    const role = offer.viewerRole;
    const other = partyName(otherRole(offer), role);
    const saved = savingText(offer.currentAmount, offer.listPrice);
    const savedText = saved ? `, ${saved} so với giá niêm yết` : '';
    let hint;
    if (payLater(offer))
      hint =
        role === 'buyer'
          ? 'Chấp nhận sẽ tạo đơn chờ thanh toán; bạn trả trong tab Đơn hàng trước hạn.'
          : 'Chấp nhận sẽ tạo đơn chờ người mua thanh toán.';
    else
      hint =
        role === 'buyer'
          ? 'Chấp nhận sẽ thu tiền credit card ngay và tạo đơn đã thanh toán.'
          : 'Chấp nhận sẽ thu tiền credit card của người mua ngay.';
    let counter = counterForm(offer);
    if (role === 'buyer' && offer.buyerOfferCount >= offer.buyerOfferLimit)
      counter = '<small class="offer-hint">Bạn đã dùng hết lượt trả giá cho sản phẩm này.</small>';
    return `<div class="offer-decide">
      <p class="offer-decide-lead">${other} đang đề nghị
        <strong>${money(offer.currentAmount)}</strong>${savedText}.
      </p>
      <div class="offer-decide-buttons">
        <button class="primary" data-offer-action="${role}/accept" data-id="${offer.id}">
          Chấp nhận ${money(offer.currentAmount)}</button>
        <button class="offer-decline" data-offer-action="${role}/decline" data-id="${offer.id}">
          Từ chối</button>
      </div>
      <small class="offer-hint">${hint} Cần nhập lại mật khẩu khi chấp nhận.</small>
      ${counter}
    </div>`;
  }

  function offerCard(offer) {
    const badge = statusBadge(offer);
    const kind = group(offer);
    const image = state.catalog.find((item) => item.productId === offer.productId)?.imageUrl;
    const meta = [
      `Phiên #${offer.id}`,
      `Khi chốt: ${payLater(offer) ? 'thanh toán sau' : 'Auto-Pay'}`,
    ];
    if (offer.viewerRole === 'buyer')
      meta.push(
        `Còn ${Math.max(offer.buyerOfferLimit - offer.buyerOfferCount, 0)}/` +
          `${offer.buyerOfferLimit} lượt trả giá`,
      );
    let deadline = '';
    if (active(offer)) {
      const turnRole = offer.status === 'PendingSeller' ? 'seller' : 'buyer';
      const who = partyName(turnRole, offer.viewerRole);
      deadline = `<p class="offer-deadline" data-offer-expires="${esc(offer.expiresAt)}"
          data-offer-who="${who}">
        <span data-offer-countdown></span>
        <small>Hạn: ${date(offer.expiresAt)}</small></p>`;
    }
    let payment = '';
    if (active(offer) && offer.viewerRole === 'buyer' && !payLater(offer)) {
      payment = `<details class="offer-more">
        <summary>Đổi credit card dùng khi chốt đơn</summary>
        <form data-offer-payment="${offer.id}" class="offer-form">
          ${cardFields('Credit card dùng khi chốt đơn')}
          <button>Cập nhật credit card</button>
        </form>
      </details>`;
    }
    let orderNote = '';
    if (offer.orderId)
      orderNote = `<div class="offer-note is-success">
        <span>${
          payLater(offer)
            ? offer.viewerRole === 'buyer'
              ? `Đơn hàng #${offer.orderId} đang chờ thanh toán. Hãy thanh toán trước hạn;
                quá hạn người bán có thể hủy đơn và bạn bị cộng điểm phạt.`
              : `Đơn hàng #${offer.orderId} đang chờ người mua thanh toán. Quá hạn bạn có
                thể hủy đơn và người mua bị cộng điểm phạt.`
            : `Đơn hàng #${offer.orderId} đã được tạo và thanh toán.`
        }</span>
        <button type="button" data-nav-target="orders">Mở Đơn hàng</button>
      </div>`;
    return `<article class="offer-card offer-session is-${kind}" data-offer-id="${offer.id}"
        data-offer-group="${kind}">
      <header class="offer-session-head">
        ${productThumbnail(image, offer.productTitle, 'offer-thumb')}
        <div class="offer-session-title">
          <strong class="offer-title">${esc(offer.productTitle)}</strong>
          <small>${meta.map(esc).join(' · ')}</small>
        </div>
        <span class="offer-status is-${badge.tone}">${esc(badge.label)}</span>
      </header>
      ${priceCompare(offer)}
      ${deadline}
      ${kind === 'action' ? decideBlock(offer) : ''}
      ${orderNote}
      ${payment}
      ${timeline(offer)}
    </article>`;
  }

  function updateCountdowns() {
    document.querySelectorAll('[data-offer-expires]').forEach((element) => {
      const text = element.querySelector('[data-offer-countdown]');
      if (text) text.textContent = deadlineText(element);
    });
  }

  function updatePreview(input) {
    const preview = input.closest('form')?.querySelector('[data-offer-preview]');
    if (!preview) return;
    const listPrice = Number(input.dataset.listPrice);
    const amount = Number(input.value);
    preview.classList.remove('is-error', 'is-good');
    if (!input.value) {
      preview.textContent =
        `Nhập giá thấp hơn giá niêm yết ${money(listPrice)} hoặc chọn nhanh:`;
    } else if (!(amount > 0) || amount >= listPrice) {
      preview.textContent = `Giá đề nghị phải lớn hơn 0 và thấp hơn ${money(listPrice)}.`;
      preview.classList.add('is-error');
    } else {
      preview.textContent =
        `Bạn đề nghị ${money(amount)}, ${savingText(amount, listPrice)} so với giá niêm yết.`;
      preview.classList.add('is-good');
    }
  }

  document.addEventListener('input', (event) => {
    if (event.target.matches?.('[data-offer-create] input[name="amount"]'))
      updatePreview(event.target);
  });

  document.addEventListener('change', (event) => {
    const radio = event.target.closest('[data-offer-create] input[name="paymentMethod"]');
    if (!radio) return;
    const form = radio.closest('form');
    form.querySelector('[data-card-fields]').hidden = radio.value !== 'Card' || !radio.checked;
  });

  document.addEventListener('submit', async (event) => {
    const create = event.target.closest('[data-offer-create]');
    const settings = event.target.closest('[data-offer-settings]');
    const counter = event.target.closest('[data-offer-counter]');
    const payment = event.target.closest('[data-offer-payment]');
    if (!create && !settings && !counter && !payment) return;
    event.preventDefault();
    const form = event.target;
    const values = new FormData(form);
    const submit = form.querySelector('button:not([type="button"])');
    if (submit) submit.disabled = true;
    try {
      if (create) {
        const autoPay = values.get('paymentMethod') === 'Card';
        await api('best-offers', 'POST', {
          productId: Number(create.dataset.offerCreate),
          amount: Number(values.get('amount')),
          addressId: Number(values.get('addressId')),
          paymentMethod: autoPay ? 'Card' : 'PayLater',
          cardProfile: autoPay ? values.get('cardProfile') : null,
          cardExpiry: autoPay ? values.get('cardExpiry') : null,
          key: keyFor(form),
        });
        pendingKeys.delete(form);
        form.reset();
        form.closest('details').open = false;
        message('Đã gửi đề nghị trả giá.', true);
        await buyerLoad();
      } else if (settings) {
        await api('best-offers/seller/settings', 'POST', {
          productId: Number(settings.dataset.offerSettings),
          allowOffers: values.has('allowOffers'),
          autoAcceptPrice: values.get('autoAcceptPrice')
            ? Number(values.get('autoAcceptPrice'))
            : null,
          autoDeclineBelow: values.get('autoDeclineBelow')
            ? Number(values.get('autoDeclineBelow'))
            : null,
        });
        message('Đã lưu cấu hình Best Offer.', true);
        await sellerLoad();
      } else if (payment) {
        await api(`best-offers/${payment.dataset.offerPayment}/buyer/payment-profile`, 'POST', {
          cardProfile: values.get('cardProfile'),
          cardExpiry: values.get('cardExpiry'),
        });
        message('Đã cập nhật thẻ mô phỏng cho phiên.', true);
        await buyerLoad();
      } else {
        const path = `best-offers/${counter.dataset.id}/${counter.dataset.offerCounter}/counter`;
        await api(path, 'POST', {
          amount: Number(values.get('amount')),
          key: keyFor(form),
        });
        pendingKeys.delete(form);
        form.reset();
        message('Đã gửi giá phản hồi.', true);
        await (counter.dataset.offerCounter === 'buyer' ? buyerLoad() : sellerLoad());
      }
    } catch (error) {
      message(error.message || 'Không thể hoàn tất thao tác.');
    } finally {
      if (submit?.isConnected) submit.disabled = false;
    }
  });

  function jumpTo(offerId) {
    let card = document.querySelector(`[data-offer-id="${CSS.escape(offerId)}"]`);
    if (card?.hidden) {
      state.filter = 'all';
      renderFilters();
      card = document.querySelector(`[data-offer-id="${CSS.escape(offerId)}"]`);
    }
    if (!card) return;
    card.scrollIntoView({ behavior: 'smooth', block: 'center' });
    card.classList.remove('is-highlight');
    void card.offsetWidth;
    card.classList.add('is-highlight');
  }

  document.addEventListener('click', async (event) => {
    const fill = event.target.closest('[data-offer-fill]');
    if (fill) {
      const input = fill.closest('form')?.querySelector('input[name="amount"]');
      if (input) {
        input.value = fill.dataset.offerFill;
        input.dispatchEvent(new Event('input', { bubbles: true }));
        input.focus();
      }
      return;
    }
    const filter = event.target.closest('[data-offer-filter]');
    if (filter) {
      state.filter = filter.dataset.offerFilter;
      renderFilters();
      return;
    }
    const jump = event.target.closest('[data-offer-jump]');
    if (jump) {
      jumpTo(jump.dataset.offerJump);
      return;
    }
    if (event.target.closest('[data-offer-addresses]')) {
      G4.BuyerCart.addressBook?.openManage(state.addresses.length === 0);
      return;
    }
    const button = event.target.closest('[data-offer-action]');
    if (!button) return;
    button.disabled = true;
    try {
      await api(`best-offers/${button.dataset.id}/${button.dataset.offerAction}`, 'POST', {
        key: keyFor(button),
      });
      pendingKeys.delete(button);
      message('Đã cập nhật phiên trả giá.', true);
      await (button.dataset.offerAction.startsWith('buyer/') ? buyerLoad() : sellerLoad());
    } catch (error) {
      message(error.message || 'Không thể hoàn tất thao tác.');
      button.disabled = false;
    }
  });

  // Keep "còn x giờ y phút" current between list reloads.
  const observer = new MutationObserver(updateCountdowns);
  ['buyer-best-offer-list', 'seller-offer-list'].forEach((id) => {
    const list = $(id);
    if (list) observer.observe(list, { childList: true });
  });
  setInterval(updateCountdowns, 30000);

  $('buyer-offers-refresh')?.addEventListener('click', () => G4.safe(buyerLoad));
  $('seller-offers-refresh')?.addEventListener('click', () => G4.safe(sellerLoad));
  $('buyer-offers-addresses')?.addEventListener('click', () => {
    G4.BuyerCart.addressBook?.openManage(state.addresses.length === 0);
  });
  $('address-dialog')?.addEventListener('close', () => {
    if ($('best-offers')?.classList.contains('active')) G4.safe(buyerLoad);
  });
  G4.BestOffers = { buyerLoad, sellerLoad };
})();
