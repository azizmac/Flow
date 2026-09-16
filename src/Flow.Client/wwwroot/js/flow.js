// Минимальный JS-мост для Flow.Client: глобальные хоткеи, буфер обмена,
// фокус, геометрия якорей для поповеров и localStorage. Всё остальное — в Razor/CSS.
window.flow = (function () {
    let hotkeyRef = null;
    // Промис загрузки бандла редактора: он один на страницу, грузим по требованию.
    let editorLoading = null;
    // Куда вернуть фокус после закрытия слоя (дровер, модалка). Стек — слои могут вкладываться.
    const focusStack = [];

    const FOCUSABLE = 'a[href], button:not([disabled]), input:not([disabled]), textarea:not([disabled]),' +
        ' select:not([disabled]), [tabindex]:not([tabindex="-1"])';

    function visibleFocusable(root) {
        return Array.prototype.slice.call(root.querySelectorAll(FOCUSABLE))
            .filter(function (el) { return el.offsetWidth > 0 || el.offsetHeight > 0 || el === document.activeElement; });
    }

    // Зоны приёма файлов: id зоны → функция снятия обработчиков (см. attachZone).
    const dropZones = new Map();

    // Файлы из перетаскивания или буфера кладём в скрытый <input type="file"> и будим change:
    // до DataTransfer.files из Blazor WASM не дотянуться, InputFile умеет читать только свой input.
    function pushFiles(input, files, fromClipboard) {
        const data = new DataTransfer();
        for (const file of files) data.items.add(fromClipboard ? namedScreenshot(file) : file);
        if (!data.files.length) return;

        input.files = data.files;
        input.dispatchEvent(new Event('change', { bubbles: true }));
    }

    // Скриншот из буфера приезжает безымянным или как image.png — в списке вложений это бесполезно.
    function namedScreenshot(file) {
        if (file.name && file.name !== 'image.png') return file;

        const now = new Date();
        const pad = n => String(n).padStart(2, '0');
        const name = 'Снимок экрана ' + now.getFullYear() + '-' + pad(now.getMonth() + 1) + '-' + pad(now.getDate())
            + ' ' + pad(now.getHours()) + '-' + pad(now.getMinutes()) + '.png';
        try {
            return new File([file], name, { type: file.type || 'image/png' });
        } catch (_) {
            return file;
        }
    }

    function draggingFiles(e) {
        const types = e.dataTransfer && e.dataTransfer.types;
        return !!types && Array.prototype.indexOf.call(types, 'Files') >= 0;
    }

    // Файл, брошенный мимо зоны приёма, браузер открывает вместо страницы — а вместе с ней теряется
    // и недописанный комментарий. Отменяем такое перетаскивание: зоны свои события уже разобрали.
    // Перетаскивание текста (выделение внутри редактора) не трогаем — там нет Files.
    document.addEventListener('dragover', function (e) { if (draggingFiles(e)) e.preventDefault(); });
    document.addEventListener('drop', function (e) { if (draggingFiles(e)) e.preventDefault(); });

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

        // Под полем открыт список, которому принадлежат стрелки/Enter/Tab/Esc (строка поиска в
        // сайдбаре). preventDefault обязан быть синхронным — поэтому здесь, а .NET-обработчик дальше
        // сам решает, что с клавишей делать: декларативный @onkeydown:preventDefault вычисляется
        // на рендере, то есть на клавишу позже. В Markdown-редакторе свой путь — он отдаёт клавиши
        // в .NET сам (HandleEditorKey), см. MarkdownEditor.
        if (e.target && e.target.getAttribute && e.target.getAttribute('data-listnav') === '1'
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
        // Esc при открытом списке упоминаний принадлежит списку: MarkdownEditor закроет его сам,
        // а дровер под редактором закрывать рано — это отняло бы недописанный комментарий.
        // Ввод идёт во вложенный элемент редактора, поэтому флаг ищем на предках, а не на самой цели.
        if (e.key === 'Escape' && e.target && typeof e.target.closest === 'function'
            && e.target.closest('[data-mention="1"]')) return;
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

        // ---- Вложения ----
        // Файлы приезжают сюда байтами из .NET (byte[] → Uint8Array), а не по ссылке: публичных
        // ссылок у вложений нет, а тег img и <a href> не носят Bearer-токен. Blob живёт до
        // revokeBlobUrl — за превью следит компонент списка, иначе вкладка течёт на длинной ленте.
        blobUrl: function (contentType, bytes) {
            try {
                return URL.createObjectURL(new Blob([bytes], { type: contentType || 'application/octet-stream' }));
            } catch (_) {
                return null;
            }
        },

        revokeBlobUrl: function (url) {
            try { if (url) URL.revokeObjectURL(url); } catch (_) { }
        },

        // После разбора выбора input нужно очистить: браузер не шлёт change, если выбрали тот же файл,
        // и повторить загрузку после ошибки было бы нельзя.
        resetFileInput: function (id) {
            const el = document.getElementById(id);
            if (el) el.value = '';
        },

        // Зона приёма файлов: карточка задачи, слайдер или редактор. Подсветка обязательна — иначе
        // непонятно, куда именно бросать. Вложенные зоны (редактор внутри карточки) забирают файл себе:
        // обработчик на внутренней зоне срабатывает первым и останавливает всплытие.
        attachZone: function (zoneId, inputId, acceptPaste) {
            const zone = document.getElementById(zoneId);
            const input = document.getElementById(inputId);
            if (!zone || !input) return false;

            window.flow.detachZone(zoneId);
            let depth = 0;

            const clear = function () {
                depth = 0;
                zone.classList.remove('drop-on');
            };

            const onEnter = function (e) {
                if (!draggingFiles(e)) return;
                e.preventDefault();
                // Счётчик, а не флаг: dragleave приходит и при переходе на дочерний элемент.
                depth++;
                zone.classList.add('drop-on');
            };
            const onOver = function (e) {
                if (!draggingFiles(e)) return;
                e.preventDefault();
                e.dataTransfer.dropEffect = 'copy';
            };
            const onLeave = function (e) {
                if (!draggingFiles(e)) return;
                depth = Math.max(0, depth - 1);
                if (!depth) zone.classList.remove('drop-on');
            };
            const onDrop = function (e) {
                if (!draggingFiles(e)) return;
                e.preventDefault();
                e.stopPropagation();
                clear();
                pushFiles(input, e.dataTransfer.files, false);
            };
            const onPaste = function (e) {
                const files = e.clipboardData && e.clipboardData.files;
                if (!files || !files.length) return;
                e.preventDefault();
                e.stopPropagation();
                pushFiles(input, files, true);
            };

            zone.addEventListener('dragenter', onEnter);
            zone.addEventListener('dragover', onOver);
            zone.addEventListener('dragleave', onLeave);
            zone.addEventListener('drop', onDrop);
            if (acceptPaste) zone.addEventListener('paste', onPaste);

            dropZones.set(zoneId, function () {
                zone.removeEventListener('dragenter', onEnter);
                zone.removeEventListener('dragover', onOver);
                zone.removeEventListener('dragleave', onLeave);
                zone.removeEventListener('drop', onDrop);
                if (acceptPaste) zone.removeEventListener('paste', onPaste);
                clear();
            });

            return true;
        },

        detachZone: function (zoneId) {
            const off = dropZones.get(zoneId);
            if (!off) return;
            dropZones.delete(zoneId);
            try { off(); } catch (_) { }
        },

        // Оживление ссылок на вложения в отрендеренном Markdown: картинке подставляется blob, ссылка
        // на файл получает обработчик клика. Содержимое приносит .NET — оно доступно только по токену.
        // Метка data-att-done защищает от повторной работы, когда компонент перерисовался сам по себе.
        hydrateAttachments: async function (containerId, ref) {
            const box = document.getElementById(containerId);
            if (!box) return;

            const images = box.querySelectorAll('img[data-attachment]:not([data-att-done])');
            for (const image of images) {
                image.setAttribute('data-att-done', '1');
                try {
                    const url = await ref.invokeMethodAsync('ResolveAttachment', image.getAttribute('data-attachment'));
                    if (url) image.src = url;
                    else image.classList.add('missing');
                } catch (_) {
                    image.classList.add('missing');
                }
            }

            box.querySelectorAll('a[data-attachment]:not([data-att-done])').forEach(function (link) {
                link.setAttribute('data-att-done', '1');
                link.addEventListener('click', function (e) {
                    e.preventDefault();
                    ref.invokeMethodAsync('DownloadAttachment', link.getAttribute('data-attachment')).catch(function () { });
                });
            });
        },

        saveFile: function (name, contentType, bytes) {
            let url = null;
            try {
                url = URL.createObjectURL(new Blob([bytes], { type: contentType || 'application/octet-stream' }));
                const a = document.createElement('a');
                a.href = url;
                a.download = name || 'file';
                document.body.appendChild(a);
                a.click();
                a.remove();
            } catch (_) {
                return false;
            }
            // Освобождаем с задержкой: браузер начинает запись файла уже после возврата из обработчика,
            // и отозванный сразу blob обрывает скачивание.
            setTimeout(function () { URL.revokeObjectURL(url); }, 20000);
            return true;
        },

        // Бандл редактора (CodeMirror, ~500 КБ) грузим только когда на странице понадобился ввод Markdown:
        // списки задач и профили открываются без него. Повторные вызовы ждут ту же загрузку.
        loadEditor: function () {
            if (window.flowEditor) return Promise.resolve(true);
            if (editorLoading) return editorLoading;

            editorLoading = new Promise(function (resolve) {
                const script = document.createElement('script');
                script.src = 'js/flow-editor.js';
                script.onload = function () { resolve(!!window.flowEditor); };
                script.onerror = function () { editorLoading = null; resolve(false); };
                document.head.appendChild(script);
            });
            return editorLoading;
        }
    };
})();
