(() => {
  // Shared address-form behaviour: country/state selects from the server list, dial code that
  // follows the country, and VietMap house-number suggestions for Vietnam. The server still
  // validates and canonicalizes every field; this module only helps the buyer fill the form.
  const { $, esc, api } = G4;
  const VIETNAM = 'Vietnam';
  const UNITED_STATES = 'United States';
  const MIN_LOOKUP = 3;
  let cached = null;

  function regions() {
    cached ??= api('address-suggestions/regions').catch((error) => {
      cached = null;
      throw error;
    });
    return cached;
  }

  // Same idea as the server key: no diacritics (đ → d), letters and digits only.
  const key = (value) =>
    String(value || '')
      .replace(/[đĐ]/g, 'd')
      .normalize('NFD')
      .replace(/[̀-ͯ]/g, '')
      .toLowerCase()
      .replace(/[^a-z0-9]/g, '');
  const COUNTRY_ALIASES = { vn: VIETNAM, vnm: VIETNAM, us: UNITED_STATES, usa: UNITED_STATES };

  function country(data, value) {
    const k = COUNTRY_ALIASES[key(value)] || value;
    return (
      data.countries.find(
        (c) => c.value === k || key(c.value) === key(k) || key(c.label) === key(k),
      ) ||
      data.countries[0]
    );
  }

  /** Stored value for a typed or legacy state ("Hanoi" → "Hà Nội", "California" → "CA"). */
  function stateValue(c, value) {
    const k = key(value).replace(/^(thanhpho|tinh|tp)(?=.)/, '');
    if (!k) return '';
    const found = c.states.find(
      (s) =>
        s.value === value ||
        key(s.value) === k ||
        key(s.label.replace(/\s*\([A-Z]{2}\)$/, '')) === k,
    );
    return found ? found.value : '';
  }

  /** "+84912345678" → "0912345678", "+12125550123" → "2125550123". Other text is kept. */
  function national(phone, countryValue) {
    const text = String(phone || '').trim();
    if (countryValue === VIETNAM && /^\+84\d+$/.test(text)) return '0' + text.slice(3);
    if (countryValue === UNITED_STATES && /^\+1\d{10}$/.test(text)) return text.slice(2);
    return text;
  }

  /** Display form for read-only views, e.g. "+84 912 345 678". */
  function formatPhone(phone) {
    const text = String(phone || '').trim();
    const vn = /^\+84(\d{3})(\d{3})(\d{3,4})$/.exec(text);
    if (vn) return `+84 ${vn[1]} ${vn[2]} ${vn[3]}`;
    const us = /^\+1(\d{3})(\d{3})(\d{4})$/.exec(text);
    if (us) return `+1 (${us[1]}) ${us[2]}-${us[3]}`;
    return text;
  }

  function options(list, selected) {
    return list
      .map(
        (o) =>
          `<option value="${esc(o.value)}" ${o.value === selected ? 'selected' : ''}>` +
          `${esc(o.label)}</option>`,
      )
      .join('');
  }

  /**
   * Enhances the inputs `${prefix}country|state|city|phone|postalCode|street` and the optional
   * `${prefix}lookup` box. Returns { fill(values), values() } once the region list is loaded.
   */
  async function attach(prefix, { lookup = false } = {}) {
    const data = await regions();
    const el = (name) => $(prefix + name);
    const countrySelect = el('country'),
      stateSelect = el('state'),
      phone = el('phone');
    const label = (name) => document.querySelector(`label[for="${prefix}${name}"]`);
    let dial = null;
    if (phone && !phone.parentElement?.classList.contains('phone-row')) {
      const row = document.createElement('div');
      row.className = 'phone-row';
      phone.parentElement.insertBefore(row, phone);
      dial = document.createElement('span');
      dial.className = 'dial-code';
      dial.id = prefix + 'dial';
      row.append(dial, phone);
      const described = [dial.id, phone.getAttribute('aria-describedby')].filter(Boolean);
      phone.setAttribute('aria-describedby', described.join(' '));
    } else if (phone) dial = el('dial');

    function applyCountry(value, state = stateSelect.value) {
      const c = country(data, value);
      countrySelect.innerHTML = options(
        data.countries.map((x) => ({ value: x.value, label: x.label })),
        c.value,
      );
      const chosen = stateValue(c, state);
      const prompt = c.value === VIETNAM ? 'Chọn tỉnh/thành phố' : 'Chọn bang';
      stateSelect.innerHTML =
        `<option value="" ${chosen ? '' : 'selected'}>${prompt}</option>` +
        options(c.states, chosen);
      if (dial) dial.textContent = c.dialCode;
      if (phone)
        phone.placeholder = c.value === VIETNAM ? '0912 345 678' : '(212) 555-0123';
      const city = label('city');
      if (city) city.textContent = c.value === VIETNAM ? 'Phường / xã' : 'Thành phố (City)';
      const zip = label('postalCode');
      if (zip)
        zip.textContent =
          c.value === VIETNAM ? 'Mã bưu chính (tùy chọn)' : 'ZIP Code (bắt buộc)';
      if (search) search.wrap.hidden = !(lookup && data.lookupEnabled && c.value === VIETNAM);
    }

    const search =
      lookup && data.lookupEnabled && el('lookup') ? suggestions(el, applyCountry) : null;
    // No key or not a buyer: hide the search box (and its label) instead of leaving it inert.
    if (!search && el('lookup')) {
      const box = el('lookup');
      (box.closest('.address-lookup') || box).hidden = true;
      const caption = label('lookup');
      if (caption) caption.hidden = true;
    }
    countrySelect.addEventListener('change', () => applyCountry(countrySelect.value, ''));

    function fill(values = {}) {
      const c = country(data, values.country || VIETNAM).value;
      applyCountry(c, values.state || '');
      for (const name of ['fullName', 'street', 'city', 'postalCode'])
        if (el(name) && values[name] !== undefined) el(name).value = values[name] || '';
      if (phone && values.phone !== undefined) phone.value = national(values.phone, c);
      if (search) search.reset();
    }
    applyCountry(countrySelect.value || VIETNAM);
    return {
      fill,
      lookupEnabled: !!search,
      values: () => ({
        fullName: el('fullName')?.value.trim() ?? '',
        phone: phone?.value.trim() ?? '',
        street: el('street')?.value.trim() ?? '',
        city: el('city')?.value.trim() ?? '',
        state: stateSelect.value,
        country: countrySelect.value,
        postalCode: el('postalCode')?.value.trim() ?? '',
      }),
    };
  }

  // VietMap autocomplete under the `${prefix}lookup` input. Responses that arrive after a newer
  // keystroke are ignored; any provider error hides the box and leaves manual entry working.
  function suggestions(el, applyCountry) {
    const input = el('lookup');
    let wrap = input.closest('.address-lookup');
    if (!wrap) {
      // Generated dialogs place label and input side by side; group them so both can hide.
      wrap = document.createElement('div');
      wrap.className = 'address-lookup';
      const caption = document.querySelector(`label[for="${input.id}"]`);
      input.parentElement.insertBefore(wrap, caption || input);
      if (caption) wrap.append(caption);
      wrap.append(input);
    }
    const list = document.createElement('ul');
    list.className = 'address-suggest';
    list.id = input.id + '-list';
    list.setAttribute('role', 'listbox');
    list.hidden = true;
    const note = document.createElement('small');
    note.className = 'address-suggest-note';
    note.setAttribute('aria-live', 'polite');
    input.after(list, note);
    input.setAttribute('autocomplete', 'off');
    input.setAttribute('aria-controls', list.id);
    input.setAttribute('aria-autocomplete', 'list');
    let timer = null,
      sequence = 0,
      rows = [];

    function close() {
      list.hidden = true;
      list.innerHTML = '';
      input.setAttribute('aria-expanded', 'false');
    }
    function disable(text) {
      close();
      input.disabled = true;
      note.textContent = text;
    }
    async function search(text) {
      const id = ++sequence;
      try {
        const found = await api(`address-suggestions?q=${encodeURIComponent(text)}`);
        if (id !== sequence) return;
        rows = found;
        list.innerHTML = found.length
          ? found
              .map(
                (r, i) =>
                  `<li role="option"><button type="button" data-suggest="${i}">` +
                  `${esc(r.display)}</button></li>`,
              )
              .join('')
          : '<li class="address-suggest-empty">Không thấy địa chỉ phù hợp; hãy nhập tay.</li>';
        list.hidden = false;
        input.setAttribute('aria-expanded', 'true');
        note.textContent = '';
      } catch (error) {
        if (id !== sequence) return;
        if (error.status === 503) disable('Gợi ý địa chỉ tạm thời không khả dụng; hãy nhập tay.');
        else note.textContent = error.message;
      }
    }
    async function pick(index) {
      const row = rows[index];
      if (!row) return;
      close();
      note.textContent = 'Đang lấy chi tiết địa chỉ…';
      try {
        const d = await api(`address-suggestions/detail?ref=${encodeURIComponent(row.ref)}`);
        applyCountry(d.country, d.state);
        if (d.street) el('street').value = d.street;
        if (d.city) el('city').value = d.city;
        input.value = row.display;
        for (const name of ['street', 'city', 'state'])
          el(name)?.dispatchEvent(new Event('input', { bubbles: true }));
        note.textContent = d.state
          ? 'Đã điền địa chỉ. Kiểm tra lại số nhà trước khi lưu.'
          : 'Đã điền đường và phường; hãy chọn tỉnh/thành phố.';
      } catch (error) {
        if (error.status === 503) disable('Gợi ý địa chỉ tạm thời không khả dụng; hãy nhập tay.');
        else note.textContent = error.message;
      }
    }
    input.addEventListener('input', () => {
      clearTimeout(timer);
      sequence++;
      const text = input.value.trim().replace(/\s+/g, ' ');
      if (text.length < MIN_LOOKUP) return close();
      timer = setTimeout(() => search(text), 400);
    });
    input.addEventListener('keydown', (event) => {
      if (event.key === 'ArrowDown' && !list.hidden) {
        event.preventDefault();
        list.querySelector('button')?.focus();
      }
      if (event.key === 'Enter') event.preventDefault();
    });
    list.addEventListener('keydown', (event) => {
      const buttons = [...list.querySelectorAll('button')];
      const at = buttons.indexOf(document.activeElement);
      if (event.key === 'ArrowDown') buttons[Math.min(buttons.length - 1, at + 1)]?.focus();
      else if (event.key === 'ArrowUp') (at <= 0 ? input : buttons[at - 1]).focus();
      else return;
      event.preventDefault();
    });
    list.addEventListener('click', (event) => {
      const button = event.target.closest('[data-suggest]');
      if (button) pick(Number(button.dataset.suggest));
    });
    return {
      wrap,
      reset() {
        clearTimeout(timer);
        sequence++;
        input.value = '';
        note.textContent = '';
        close();
      },
    };
  }

  G4.AddressFields = { regions, attach, national, formatPhone, VIETNAM, UNITED_STATES };
})();
