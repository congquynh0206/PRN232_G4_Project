(() => {
  const { $, api, esc, money, safe } = G4;
  const filter = $('buyer-offer-filter');
  const dialog = $('buyer-coupon-picker');
  let page = 1;
  let request = 0;

  function terms(p) {
    const discount = p.freeShipping
      ? 'Miễn phí vận chuyển'
      : p.type === 'Volume'
        ? p.tiers.map((t) => 'Từ ' + t.minQuantity + ' món giảm ' + t.percent + '%').join(' · ')
        : 'Giảm ' + (p.isPercent ? p.value + '%' : money(p.value))
          + (p.type === 'Shipping' ? ' phí vận chuyển' : '');
    return [
      discount,
      p.minSubtotal > 0 && 'Từ ' + money(p.minSubtotal) + ' tiền hàng đủ điều kiện',
      p.minQuantity > 1 && 'Từ ' + p.minQuantity + ' sản phẩm',
      p.cap != null && 'Giảm tối đa ' + money(p.cap),
      p.maxUsagePerBuyer != null && p.maxUsagePerBuyer + ' lượt mỗi người mua',
    ].filter(Boolean).join(' · ');
  }

  function card(p, action, showProducts = false) {
    const seller = p.sellerId == null ? 'Toàn sàn' : p.sellerName || 'Shop #' + p.sellerId;
    const endAt = p.endAt.endsWith('Z') ? p.endAt : p.endAt + 'Z';
    const expires = new Intl.DateTimeFormat('vi-VN', {
      dateStyle: 'short', timeStyle: 'short',
    }).format(new Date(endAt));
    const products = p.products.map((item) =>
      '<li>' + esc(item.title || 'Sản phẩm #' + item.id)
        + (item.price == null ? '' : ' · ' + money(item.price)) + '</li>',
    ).join('');
    return '<article class="buyer-offer-card"><h3>' + esc(p.name) + '</h3>'
      + '<p><strong>' + esc(terms(p)) + '</strong></p>'
      + '<p>' + esc(seller) + ' · Đến ' + esc(expires) + '</p>'
      + (p.code ? '<p><code>' + esc(p.code) + '</code></p>' : '')
      + (showProducts ? '<details><summary>Sản phẩm áp dụng (' + p.productCount + ')</summary>'
        + (products ? '<ul>' + products + '</ul>' : '<p>Chưa có sản phẩm.</p>')
        + (p.productCount > p.products.length ? '<p>Hiển thị 12 sản phẩm đầu tiên.</p>' : '')
        + '</details>' : '')
      + action + '</article>';
  }

  async function load(nextPage = 1) {
    const token = ++request;
    const status = $('buyer-offer-status');
    const list = $('buyer-offer-list');
    const prev = $('buyer-offer-prev');
    const next = $('buyer-offer-next');
    prev.disabled = next.disabled = true;
    list.replaceChildren();
    status.textContent = 'Đang tải ưu đãi…';
    try {
      const result = await api('buyer/promotions/offers?type=' + filter.value + '&page=' + nextPage);
      if (token !== request) return;
      page = nextPage;
      status.textContent = result.items.length ? '' : 'Chưa có ưu đãi phù hợp.';
      list.innerHTML = result.items.map((p, i) => {
        if (!p.code) return card(p, '', true);
        const sellers = G4.BuyerCart.offerSellers(p);
        const actions = sellers.map((seller) =>
          '<button type="button" data-checkout-offer="' + seller.sellerId + '" data-offer="' + i
          + '">Dùng tại Checkout · ' + esc(seller.sellerName) + '</button>',
        ).join('');
        return card(p, '<div class="toolbar"><button type="button" data-copy-code="' + i
          + '">Sao chép mã</button>' + actions + '</div>', true);
      }).join('');
      list.querySelectorAll('[data-checkout-offer]').forEach((button) => {
        button.onclick = () => safe(() => G4.BuyerCart.openOffer(
          Number(button.dataset.checkoutOffer), result.items[Number(button.dataset.offer)],
        ));
      });
      list.querySelectorAll('[data-copy-code]').forEach((button) => {
        button.onclick = () => safe(async () => {
          await navigator.clipboard.writeText(result.items[Number(button.dataset.copyCode)].code);
          status.textContent = 'Đã sao chép mã giảm giá.';
        });
      });
      prev.disabled = page <= 1;
      next.disabled = !result.hasMore;
    } catch (error) {
      if (token === request) {
        status.textContent = error.message;
        prev.disabled = page <= 1;
        next.disabled = false;
      }
    }
  }

  async function pick(sellerId, preview, apply, selectedOffer) {
    const status = $('buyer-coupon-status');
    const list = $('buyer-coupon-list');
    const more = $('buyer-coupon-more');
    let couponPage = 0;
    async function loadMore() {
      more.hidden = true;
      status.textContent = 'Đang tải mã giảm giá…';
      try {
        const result = selectedOffer
          ? { items: [selectedOffer], hasMore: false }
          : await api('buyer/promotions/offers?type=Coupon&sellerId='
            + sellerId + '&page=' + (couponPage + 1));
        if (!dialog.open || more.onclick !== loadMore) return;
        couponPage++;
        status.textContent = list.children.length || result.items.length
          ? 'Kiểm tra mã để xem điều kiện và tổng tiền.'
          : 'Chưa có mã công khai cho shop này.';
        for (const offer of result.items) {
          const template = document.createElement('template');
          template.innerHTML = card(offer,
            '<p role="status"></p><button type="button">Kiểm tra mã</button>');
          const button = template.content.querySelector('button');
          const feedback = template.content.querySelector('[role="status"]');
          button.onclick = async () => {
            button.disabled = true;
            feedback.textContent = 'Đang tính báo giá…';
            try {
              const { before, after } = await preview(offer.code);
              feedback.textContent = 'Có thể dùng · Hiện tại ' + money(before)
                + ' · Dùng mã ' + money(after)
                + (after > before ? ' · Tổng tiền tăng khi đổi ưu đãi.' : '');
              button.textContent = 'Dùng mã';
              button.onclick = () => {
                dialog.close();
                safe(() => apply(offer.code));
              };
            } catch (error) {
              feedback.textContent = 'Chưa thể áp dụng · ' + error.message;
            } finally {
              button.disabled = false;
            }
          };
          list.append(template.content);
        }
        more.hidden = !result.hasMore;
      } catch (error) {
        if (dialog.open && more.onclick === loadMore) {
          status.textContent = error.message;
          more.hidden = false;
        }
      }
    }
    list.replaceChildren();
    more.onclick = loadMore;
    dialog.showModal();
    await loadMore();
  }

  filter.onchange = () => safe(() => load());
  $('buyer-offer-prev').onclick = () => safe(() => load(page - 1));
  $('buyer-offer-next').onclick = () => safe(() => load(page + 1));
  G4.BuyerOffers = { load, pick };
})();
