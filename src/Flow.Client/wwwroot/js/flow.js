// Минимальный JS-мост для Flow.Client: глобальные хоткеи, буфер обмена,
// фокус, геометрия якорей для поповеров и localStorage. Всё остальное — в Razor/CSS.
window.flow = (function () {
    let hotkeyRef = null;
    // Куда вернуть фокус после закрытия слоя (дровер, модалка). Стек — слои могут вкладываться.
    const focusStack = [];

    const FOCUSABLE = 'a[href], button:not([disabled]), input:not([disabled]), textarea:not([disabled]),' +
        ' select:not([disabled]), [tabindex]:not([tabindex="-1"])';

    function visibleFocusable(root) {
        return Array.prototype.slice.call(root.querySelectorAll(FOCUSABLE))
            .filter(function (el) { return el.offsetWidth > 0 || el.offsetHeight > 0 || el === document.activeElement; });
    }

    // Открытый модальный слой: поповер обрабатывает Tab сам, поэтому здесь только дровер и модалка.
    function openLayer() {
        return document.querySelector('.modal') || document.querySelector('.drawer.in');
    }

    function isEditable(el) {
        if (!el) return false;
        const tag = (el.tagName || '').toLowerCase();
        return tag === 'input' || tag === 'textarea' || tag === 'select' || el.isContentEditable === true;
    }

    // Открытый поповер сам обрабатывает стрелки/Home/End/Tab: фокус ходит по его пунктам
    // и не убегает наружу. preventDefault обязан быть синхронным, поэтому это здесь, а не в .NET.
    const MENU_KEYS = { ArrowDown: 'next', ArrowUp: 'prev', Home: 'first', End: 'last' };

    document.addEventListener('keydown', function (e) {
        // Tab не должен выводить фокус за пределы открытого дровера/модалки.
        if (e.key === 'Tab') {
            const layer = openLayer();
            const inPop = e.target && e.target.closest && e.target.closest('.pop');
            if (layer && !inPop) {
                const items = visibleFocusable(layer);
                if (!items.length) {
                    e.preventDefault();
                } else if (!layer.contains(e.target)) {
                    e.preventDefault();
                    items[e.shiftKey ? items.length - 1 : 0].focus();
                } else {
                    const at = items.indexOf(e.target);
                    if (!e.shiftKey && at === items.length - 1) { e.preventDefault(); items[0].focus(); }
                    else if (e.shiftKey && at === 0) { e.preventDefault(); items[items.length - 1].focus(); }
                }
            }
        }

        // Открыт список @упоминаний в Markdown-редакторе: стрелки/Enter/Tab/Esc принадлежат списку,
        // а не textarea. preventDefault обязан быть синхронным — поэтому здесь, .NET-обработчик дальше сам решает.
        if (e.target && e.target.getAttribute && e.target.getAttribute('data-mention') === '1'
            && (e.key === 'ArrowDown' || e.key === 'ArrowUp' || e.key === 'Enter' || e.key === 'Tab' || e.key === 'Escape')) {
            e.preventDefault();
            return;
        }

        const pop = e.target && e.target.closest ? e.target.closest('.pop') : null;
        if (pop) {
            const mode = e.key === 'Tab' ? (e.shiftKey ? 'prev' : 'next') : MENU_KEYS[e.key];
            if (mode) {
                e.preventDefault();
                window.flow.menuFocus(pop.id, mode);
                return;
            }
        }
        if (!hotkeyRef) return;
        const editable = isEditable(e.target);
        // В полях ввода пропускаем только Esc и Ctrl/Cmd+Enter — остальное принадлежит полю.
        if (editable && e.key !== 'Escape' && !((e.ctrlKey || e.metaKey) && e.key === 'Enter')) return;
        const plainLetter = (e.key === 'n' || e.key === 'N' || e.key === 'т' || e.key === 'Т') && !e.ctrlKey && !e.metaKey && !e.altKey;
        // preventDefault нужен синхронно: .NET-обработчик асинхронный и не успеет отменить ввод символа
        // в поле, которое откроется по хоткею (N → дровер с автофокусом на названии).
        if (!editable && (plainLetter || e.key === 'Escape')) e.preventDefault();
        hotkeyRef.invokeMethodAsync('OnKeyDown', e.key, e.ctrlKey || e.metaKey, e.shiftKey, editable)
            .catch(function () { });
    });

    return {
        registerHotkeys: function (ref) { hotkeyRef = ref; },
        unregisterHotkeys: function () { hotkeyRef = null; },

        copy: async function (text) {
            try {
                if (navigator.clipboard && window.isSecureContext) {
                    await navigator.clipboard.writeText(text);
                    return true;
                }
            } catch (_) { }
            try {
                const ta = document.createElement('textarea');
                ta.value = text;
                ta.setAttribute('readonly', '');
                ta.style.position = 'fixed';
                ta.style.opacity = '0';
                document.body.appendChild(ta);
                ta.select();
                const ok = document.execCommand('copy');
                document.body.removeChild(ta);
                return ok;
            } catch (_) {
                return false;
            }
        },

        // Запомнить текущий фокус перед открытием слоя.
        pushFocus: function () {
            focusStack.push(document.activeElement);
        },

        // Вернуть фокус туда, откуда слой открыли (если элемент ещё в документе).
        popFocus: function () {
            const el = focusStack.pop();
            if (el && el.isConnected && typeof el.focus === 'function') el.focus();
        },

        // Первый фокусируемый элемент внутри контейнера — автофокус при открытии слоя.
        focusFirstIn: function (selector) {
            const box = document.querySelector(selector);
            if (!box) return;
            const items = visibleFocusable(box);
            if (items.length) items[0].focus();
        },

        focus: function (id, select) {
            const el = document.getElementById(id);
            if (!el) return;
            el.focus();
            if (select && typeof el.select === 'function') el.select();
        },

        // Геометрия якоря + размеры viewport — для позиционирования поповеров в fixed-слое.
        // layerId (необязателен) — скрим поповера (position:fixed; inset:0): если поповер лежит
        // внутри предка с transform/filter, тот перехватывает роль containing block и left/top
        // считаются от него, а не от viewport. originX/originY — начало этого отсчёта; их нужно
        // вычесть из желаемой позиции. Меряем именно по скриму: у самого поповера есть собственный
        // transform анимации появления, и getBoundingClientRect дал бы смещённый прямоугольник.
        rect: function (id, layerId) {
            const el = document.getElementById(id);
            if (!el) return null;
            const r = el.getBoundingClientRect();
            let originX = 0, originY = 0;
            const layer = layerId ? document.getElementById(layerId) : null;
            if (layer) {
                const l = layer.getBoundingClientRect();
                originX = l.left;
                originY = l.top;
            }
            return { left: r.left, top: r.top, right: r.right, bottom: r.bottom, width: r.width, height: r.height, vw: window.innerWidth, vh: window.innerHeight, originX: originX, originY: originY };
        },

        // Перемещение фокуса по пунктам меню/списка внутри контейнера.
        // mode: 'first' (текущий выбранный, иначе первый) | 'last' | 'next' | 'prev'.
        menuFocus: function (containerId, mode) {
            const box = document.getElementById(containerId);
            if (!box) return;
            const items = Array.prototype.slice.call(box.querySelectorAll('.pop-item:not([disabled])'));
            if (!items.length) return;
            let idx;
            if (mode === 'first') {
                const cur = items.findIndex(function (i) { return i.classList.contains('cur'); });
                idx = cur >= 0 ? cur : 0;
            } else if (mode === 'last') {
                idx = items.length - 1;
            } else {
                const at = items.indexOf(document.activeElement);
                const step = mode === 'prev' ? -1 : 1;
                idx = at < 0 ? (step > 0 ? 0 : items.length - 1) : (at + step + items.length) % items.length;
            }
            items[idx].focus();
        },

        // localStorage может быть недоступен (приватный режим, запрет site data) — тогда null / no-op.
        storageGet: function (key) {
            try { return window.localStorage.getItem(key); } catch (_) { return null; }
        },
        storageSet: function (key, value) {
            try {
                if (value === null || value === undefined) window.localStorage.removeItem(key);
                else window.localStorage.setItem(key, value);
            } catch (_) { }
        },

        scrollIntoView: function (id) {
            const el = document.getElementById(id);
            if (el) el.scrollIntoView({ block: 'nearest' });
        },

        // ---- Markdown-редактор (MarkdownEditor.razor): правки текста в textarea с сохранением каретки. ----
        // Возвращают новое значение — .NET держит его в состоянии, чтобы после ре-рендера textarea не откатилась.
        editor: {
            // Текущее значение и границы выделения.
            state: function (id) {
                const el = document.getElementById(id);
                if (!el) return null;
                return { value: el.value, start: el.selectionStart, end: el.selectionEnd };
            },

            // Обернуть выделение (или вставить placeholder): **текст**, `код`, [ссылка](url).
            wrap: function (id, before, after, placeholder) {
                const el = document.getElementById(id);
                if (!el) return null;
                const s = el.selectionStart, e = el.selectionEnd;
                const selected = el.value.substring(s, e);
                const inner = selected.length ? selected : placeholder;
                el.setRangeText(before + inner + after, s, e, 'end');
                // Без выделения — ставим каретку внутрь обёртки, чтобы можно было сразу печатать.
                if (!selected.length) el.setSelectionRange(s + before.length, s + before.length + inner.length);
                el.focus();
                return el.value;
            },

            // Префикс строк выделения: заголовок, цитата, список (ordered — с нумерацией).
            prefixLines: function (id, prefix, ordered) {
                const el = document.getElementById(id);
                if (!el) return null;
                const v = el.value;
                const lineStart = v.lastIndexOf('\n', el.selectionStart - 1) + 1;
                let lineEnd = v.indexOf('\n', el.selectionEnd);
                if (lineEnd < 0) lineEnd = v.length;
                const lines = v.substring(lineStart, lineEnd).split('\n');
                const out = lines.map(function (l, i) { return (ordered ? (i + 1) + '. ' : prefix) + l; }).join('\n');
                el.setRangeText(out, lineStart, lineEnd, 'select');
                el.focus();
                return el.value;
            },

            // Заменить «@частичное» перед кареткой на «@username ».
            insertMention: function (id, atPos, username) {
                const el = document.getElementById(id);
                if (!el) return null;
                const caret = el.selectionStart;
                el.setRangeText('@' + username + ' ', atPos, caret, 'end');
                el.focus();
                return el.value;
            }
        }
    };
})();
