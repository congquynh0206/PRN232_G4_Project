(() => {
    const { $, esc, api, message, modal } = G4;
    const FIELDS = [
        ['fullName', 'Tên người nhận', 100],
        ['street', 'Đường / số nhà', 100],
        ['city', 'Thành phố', 50],
        ['state', 'Tỉnh / vùng', 50],
        ['country', 'Quốc gia', 50],
    ];
    const MAX_ADDRESSES = 10;
    const clean = (value) => String(value ?? '').trim().replace(/\s+/g, ' ');
    const lineOf = (a) => [a.street, a.city, a.state, a.country].filter(Boolean).join(', ');
    function validate(values) {
        const errors = {};
        for (const [field, label, max] of FIELDS) {
            const text = label.toLowerCase();
            if (!values[field]) errors[field] = `Vui lòng nhập ${text}.`;
            else if (values[field].length > max) errors[field] = `Tối đa ${max} ký tự cho ${text}.`;
        }
        return errors;
    }
    G4.BuyerAddresses = { create };

    /**
     * Address book for checkout. `onChange({ selectedId, address })` fires only when the address that
     * will receive the order changes (another address chosen, selected one edited/deleted), so the
     * caller can request a fresh quote from the server. `lock(true|false)` brackets every write.
     */
    function create({ onChange, lock = () => { } }) {
        let addresses = [],
            selectedId = null,
            disabled = false,
            editingId = null,
            saving = false,
            opener = null;
        const key = (a) => (a ? [a.id, a.fullName, a.street, a.city, a.state, a.country].join('|') : '');
        const current = () => addresses.find((a) => a.id === selectedId) || null;

        function adopt(list, { select = null, silent = false } = {}) {
            const beforeId = selectedId,
                beforeKey = key(current());
            addresses = list;
            if (select != null && list.some((a) => a.id === select)) selectedId = select;
            else if (!list.some((a) => a.id === selectedId))
                selectedId = (list.find((a) => a.isDefault) || list[0])?.id ?? null;
            render();
            if (!silent && (selectedId !== beforeId || key(current()) !== beforeKey))
                onChange({ selectedId, address: current() });
        }
        function badges(a) {
            return `${a.isDefault ? '<span class="address-badge">Mặc định</span>' : ''}${a.id === selectedId ? '<span class="address-badge chosen">Đang giao tới đây</span>' : ''}`;
        }
        function body(a) {
            return `<span class="address-title"><strong>${esc(a.fullName)}</strong>${badges(a)}</span><span>${esc(a.street)}</span><span>${esc(a.city)}, ${esc(a.state)}, ${esc(a.country)}</span>`;
        }
        function render() {
            const list = $('address-list'),
                empty = !addresses.length;
            list.setAttribute('aria-busy', 'false');
            $('address-empty').hidden = !empty;
            list.hidden = empty;
            list.innerHTML = addresses
                .map(
                    (a) =>
                        `<label class="address-option ${a.id === selectedId ? 'selected' : ''}"><input type="radio" name="delivery-address" value="${a.id}" ${a.id === selectedId ? 'checked' : ''} ${disabled || saving ? 'disabled' : ''}/><span class="address-text">${body(a)}</span></label>`,
                )
                .join('');
            list.querySelectorAll('input[name="delivery-address"]').forEach(
                (radio) =>
                (radio.onchange = () => {
                    if (disabled || !radio.checked) return;
                    choose(Number(radio.value));
                }),
            );
            renderManage();
        }
        function renderManage() {
            const box = $('address-manage-list');
            if (!box) return;
            $('address-form-save').disabled = disabled || saving;
            $('address-form-cancel').disabled = saving;
            $('address-dialog-close').disabled = saving;
            for (const [field] of FIELDS) $(`address-form-${field}`).disabled = disabled || saving;
            $('address-form-default').disabled = disabled || saving || !addresses.length || !!addresses.find(a => a.id === editingId)?.isDefault;
            $('address-add').disabled = disabled || saving || addresses.length >= MAX_ADDRESSES;
            $('address-limit').hidden = addresses.length < MAX_ADDRESSES;
            box.innerHTML = addresses.length
                ? addresses
                    .map((a) => {
                        const off = disabled || saving ? 'disabled' : '';
                        return `<article class="address-card ${a.id === selectedId ? 'selected' : ''}" data-address-card="${a.id}"><div class="address-card-main">${body(a)}</div><div class="address-card-actions"><button type="button" data-act="use" data-id="${a.id}" ${a.id === selectedId || disabled || saving ? 'disabled' : ''}>Giao tới đây</button><button type="button" data-act="edit" data-id="${a.id}" ${off}>Sửa</button><button type="button" data-act="default" data-id="${a.id}" ${a.isDefault || disabled || saving ? 'disabled' : ''}>Đặt mặc định</button><button type="button" class="danger" data-act="delete" data-id="${a.id}" ${off}>Xóa</button></div></article>`;
                    })
                    .join('')
                : '<p class="address-empty-note">Chưa có địa chỉ nào. Hãy thêm địa chỉ giao hàng đầu tiên.</p>';
        }
        function choose(id) {
            if (id === selectedId) return;
            selectedId = id;
            render();
            onChange({ selectedId, address: current() });
        }
        async function write(work) {
            if (saving) return;
            saving = true;
            lock(true);
            renderManage();
            try {
                await work();
            } finally {
                saving = false;
                lock(false);
                renderManage();
            }
        }
        // ---- form ----
        const input = (field) => $(`address-form-${field}`);
        function showErrors(errors) {
            let first = null;
            for (const [field] of FIELDS) {
                const text = errors[field] || '';
                $(`address-error-${field}`).textContent = text;
                input(field).setAttribute('aria-invalid', text ? 'true' : 'false');
                if (text && !first) first = field;
            }
            if (first) input(first).focus();
        }
        function openForm(a) {
            editingId = a?.id ?? null;
            $('address-form-title').textContent = a ? 'Sửa địa chỉ' : 'Thêm địa chỉ mới';
            for (const [field] of FIELDS) input(field).value = a?.[field] ?? '';
            $('address-form-default').checked = !!a?.isDefault || !addresses.length;
            $('address-form-default').disabled = !!a?.isDefault || !addresses.length;
            $('address-form-hint').hidden = !!addresses.length && !a?.isDefault;
            $('address-form-hint').textContent =
                !addresses.length ? 'Địa chỉ đầu tiên tự động là địa chỉ mặc định.' : 'Đây là địa chỉ mặc định. Hãy đặt địa chỉ khác làm mặc định để thay đổi.';
            showErrors({});
            $('address-form').hidden = false;
            $('address-form-placeholder').hidden = true;
            $('address-add').hidden = true;
            renderManage();
            input('fullName').focus();
        }
        function closeForm() {
            editingId = null;
            $('address-form').hidden = true;
            $('address-form-placeholder').hidden = false;
            $('address-add').hidden = false;
        }
        async function submit(event) {
            event.preventDefault();
            if (saving || disabled) return;
            const values = Object.fromEntries(FIELDS.map(([f]) => [f, clean(input(f).value)]));
            const errors = validate(values);
            showErrors(errors);
            if (Object.keys(errors).length) return;
            const payload = { ...values, isDefault: $('address-form-default').checked };
            await write(async () => {
                try {
                    const book = editingId
                        ? await api(`addresses/${editingId}/edit`, 'POST', payload)
                        : await api('addresses/create', 'POST', payload);
                    const wasEditing = !!editingId;
                    closeForm();
                    adopt(book.addresses);
                    message(wasEditing ? 'Đã lưu địa chỉ.' : 'Đã thêm địa chỉ.', true);
                } catch (error) {
                    if (error.fields) showErrors(error.fields);
                    message(error.message);
                }
            });
        }
        async function act(button) {
            const id = Number(button.dataset.id),
                a = addresses.find((x) => x.id === id);
            if (!a) return;
            if (button.dataset.act === 'use') return choose(id);
            if (button.dataset.act === 'edit') return openForm(a);
            if (button.dataset.act === 'default')
                return write(async () => {
                    try {
                        adopt((await api(`addresses/${id}/default`, 'POST', {})).addresses);
                        message('Đã đặt địa chỉ mặc định.', true);
                    } catch (error) {
                        message(error.message);
                    }
                });
            if (button.dataset.act === 'delete') {
                const notes = [
                    'Đơn đã tạo vẫn giữ nguyên địa chỉ và giá ban đầu.',
                    a.isDefault && addresses.length > 1
                        ? 'Đây là địa chỉ mặc định; hệ thống sẽ chọn một địa chỉ còn lại làm mặc định.'
                        : '',
                    a.id === selectedId
                        ? addresses.length > 1
                            ? 'Đây là địa chỉ đang chọn giao tới; báo giá sẽ được tính lại với địa chỉ khác.'
                            : 'Đây là địa chỉ duy nhất; bạn cần thêm địa chỉ mới trước khi thanh toán.'
                        : '',
                ].filter(Boolean);
                const ok = await modal({
                    title: 'Xóa địa chỉ này?',
                    description: `${a.fullName} — ${lineOf(a)}. ${notes.join(' ')}`,
                    confirm: 'Xóa địa chỉ',
                });
                if (!ok) return;
                await write(async () => {
                    try {
                        if (editingId === id) closeForm();
                        adopt((await api(`addresses/${id}/delete`, 'POST', {})).addresses);
                        message('Đã xóa địa chỉ.', true);
                    } catch (error) {
                        message(error.message);
                        adopt(await api('addresses'));
                    }
                });
            }
        }
        function openManage(addNew = false) {
            const dialog = $('address-dialog');
            opener = document.activeElement;
            closeForm();
            renderManage();
            if (!dialog.open) dialog.showModal();
            if (addNew || !addresses.length) openForm(null);
        }
        $('manage-addresses').onclick = () => openManage(false);
        $('address-empty-add').onclick = () => openManage(true);
        $('address-add').onclick = () => openForm(null);
        $('address-form').onsubmit = (e) => G4.safe(() => submit(e));
        $('address-form-cancel').onclick = () => { if (!saving) closeForm(); };
        $('address-dialog-close').onclick = () => { if (!saving) $('address-dialog').close(); };
        $('address-dialog').addEventListener('cancel', event => { if (saving) event.preventDefault(); });
        $('address-dialog').addEventListener('close', () => {
            closeForm();
            if (opener?.isConnected) opener.focus();
        });
        $('address-manage-list').onclick = (e) => {
            const button = e.target.closest('button[data-act]');
            if (button && !button.disabled) G4.safe(() => act(button));
        };
        for (const [field] of FIELDS)
            input(field).addEventListener('input', () => {
                $(`address-error-${field}`).textContent = '';
                input(field).setAttribute('aria-invalid', 'false');
            });
        return {
            async load() {
                adopt(await api('addresses'), { silent: true });
            },
            selectedId: () => selectedId,
            selectedKey: () => key(current()),
            selected: current,
            count: () => addresses.length,
            openManage,
            setDisabled(value) {
                disabled = value;
                render();
            },
        };
    }
})();
