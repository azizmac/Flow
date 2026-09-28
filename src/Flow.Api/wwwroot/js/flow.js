// Минимальный JS-мост для интерфейса Flow: глобальные хоткеи, буфер обмена, фокус,
// геометрия якорей для поповеров, приём файлов и выход. Всё остальное — в Razor/CSS.
// Содержимого вложений здесь больше нет: картинки и скачивание — прямые ссылки /files/{id}.
window.flow = (function () {
    let hotkeyRef = null;
    // Промис загрузки бандла редактора: он один на страницу, грузим по требованию.
    let editorLoading = null;
    // Куда вернуть фокус после закрытия слоя (дровер, модалка). Стек — слои могут вкладываться.
    const focusStack = [];

    // Зоны приёма файлов: id зоны → { off: снять обработчики, clear: погасить подсветку } (см. attachZone).
    const dropZones = new Map();

    // Перетаскивания роадмапа и сетки дашборда: id корня → функция, снимающая обработчики (см. roadmapAttach, gridAttach).
    const pointerDrags = new Map();

    // После перетаскивания браузер всё равно пришлёт click по элементу — он открыл бы слайдер или диалог. Гасим ровно один
    // такой click в фазе перехвата на window: раньше любых обработчиков Blazor.
    let swallowClick = false;
    window.addEventListener('click', function (e) {
        if (!swallowClick) return;
        swallowClick = false;
        e.stopPropagation();
        e.preventDefault();
    }, true);

    // Общий каркас: pointerdown на корне выбирает, что тянуть (pick → состояние или null), движение и отпускание — на
    // window, чтобы курсор мог уйти за край. Порог 4px отличает перетаскивание от клика. Во время движения элемент
    // меняет только свой inline-стиль; при отпускании стиль возвращается как был, а итог уходит в .NET одним вызовом —
    // событие на каждый пиксель по SignalR при серверном рендере не нужно. Blazor потом перерисует элемент с новыми
    // значениями (или оставит прежние, если сервер отказал).
    function attachPointerDrag(rootId, pick, move, drop) {
        const root = document.getElementById(rootId);
        if (!root) return false;
        detachPointerDrag(rootId);

        let state = null;
        const onDown = function (e) {
            if (e.button !== 0) return;
            state = pick(e, root);
            if (!state) return;
            state.x0 = e.clientX;
            state.y0 = e.clientY;
            state.moved = false;
            state.style = state.el.getAttribute('style');
        };
        const onMove = function (e) {
            if (!state) return;
            const dx = e.clientX - state.x0, dy = e.clientY - state.y0;
            if (!state.moved) {
                if (Math.abs(dx) < 4 && Math.abs(dy) < 4) return;
                state.moved = true;
                state.el.classList.add('dragging');
                document.body.classList.add('pointer-dragging');
            }
            e.preventDefault();
            move(state, dx, dy, e, root);
        };
        const onUp = function (e) {
            if (!state) return;
            const s = state;
            state = null;
            if (!s.moved) return;
            swallowClick = true;
            setTimeout(function () { swallowClick = false; }, 0);
            s.el.classList.remove('dragging');
            document.body.classList.remove('pointer-dragging');
            if (s.style === null) s.el.removeAttribute('style'); else s.el.setAttribute('style', s.style);
            if (s.ghost) s.ghost.remove();
            drop(s, e.clientX - s.x0, e.clientY - s.y0, e, root);
        };
        const onKey = function (e) {
            if (e.key !== 'Escape' || !state || !state.moved) return;
            const s = state;
            state = null;
            s.el.classList.remove('dragging');
            document.body.classList.remove('pointer-dragging');
            if (s.style === null) s.el.removeAttribute('style'); else s.el.setAttribute('style', s.style);
            if (s.ghost) s.ghost.remove();
        };

        root.addEventListener('pointerdown', onDown);
        window.addEventListener('pointermove', onMove);
        window.addEventListener('pointerup', onUp);
        window.addEventListener('keydown', onKey);
        pointerDrags.set(rootId, function () {
            root.removeEventListener('pointerdown', onDown);
            window.removeEventListener('pointermove', onMove);
            window.removeEventListener('pointerup', onUp);
            window.removeEventListener('keydown', onKey);
        });
        return true;
    }

    // Дуга перехода графа workflow — та же формула, что WorkflowPage.EdgePath: от края узла к краю, изгиб по нормали.
    function graphEdgePath(ax, ay, bx, by, nw, nh) {
        const dx = bx - ax, dy = by - ay;
        const len = Math.max(1, Math.sqrt(dx * dx + dy * dy));
        const ux = dx / len, uy = dy / len;
        const trim = function (extra) {
            return Math.min(ux === 0 ? Infinity : nw / 2 / Math.abs(ux), uy === 0 ? Infinity : nh / 2 / Math.abs(uy)) + extra;
        };
        const lift = 26 + len * 0.22;
        const r = function (v) { return Math.round(v * 10) / 10; };
        return 'M ' + r(ax + ux * trim(0)) + ' ' + r(ay + uy * trim(0))
            + ' Q ' + r((ax + bx) / 2 + uy * lift) + ' ' + r((ay + by) / 2 - ux * lift)
            + ' ' + r(bx - ux * trim(6)) + ' ' + r(by - uy * trim(6));
    }

    // Точка экрана → координаты холста SVG (viewBox может быть сжат под ширину панели).
    function svgPoint(svg, clientX, clientY) {
        const m = svg.getScreenCTM();
        if (!m) return { x: clientX, y: clientY };
        const p = new DOMPoint(clientX, clientY).matrixTransform(m.inverse());
        return { x: p.x, y: p.y };
    }

    function detachPointerDrag(rootId) {
        const off = pointerDrags.get(rootId);
        if (!off) return;
        pointerDrags.delete(rootId);
        try { off(); } catch (_) { }
    }

    // Перетаскивание кончилось — гасим подсветку у всех зон сразу. По одной нельзя: вложенная зона
    // (редактор внутри карточки) забирает drop себе и останавливает всплытие, и внешняя иначе
    // так и осталась бы в рамке «Отпустите файлы».
    function clearZones() {
        dropZones.forEach(function (zone) {
            try { zone.clear(); } catch (_) { }
        });
    }

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

    // Перехват, а не всплытие: обработчик зоны вызывает stopPropagation, и до документа событие
    // не дошло бы. Сюда же попадает бросок мимо зон и уход перетаскивания за пределы окна.
    document.addEventListener('drop', clearZones, true);
    document.addEventListener('dragend', clearZones, true);
    document.addEventListener('dragleave', function (e) {
        // relatedTarget пуст только когда курсор ушёл из окна целиком, а не на соседний элемент.
        if (!e.relatedTarget) clearZones();
    }, true);

    function isEditable(el) {
        if (!el) return false;
        const tag = (el.tagName || '').toLowerCase();
        return tag === 'input' || tag === 'textarea' || tag === 'select' || el.isContentEditable === true;
    }

    // Esc в поле переименования на месте (DsInput EscapeReverts) отменяет правку, а не закрывает диалог. Перехватчик
    // MudDialog висит на контейнере диалога нативно, а Blazor разбирает события делегированием у корня документа, так
    // что @onkeydown:stopPropagation до контейнера не дотягивается. Поэтому клавиша гасится на захвате, полю
    // возвращается исходное значение событием input, и фокус снимается: переименование сохраняет по blur, а
    // неизменённое имя — no-op.
    document.addEventListener('keydown', function (e) {
        if (e.key !== 'Escape' || !e.target || typeof e.target.closest !== 'function') return;
        const field = e.target.closest('[data-esc-revert]');
        if (!field || !isEditable(e.target)) return;
        e.stopPropagation();
        e.preventDefault();
        e.target.value = field.getAttribute('data-esc-revert');
        e.target.dispatchEvent(new Event('input', { bubbles: true }));
        e.target.blur();
    }, true);

    document.addEventListener('keydown', function (e) {
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
        // «/» — фокус в строку поиска; символ гасим здесь же, иначе он окажется в поле, которое только что получило фокус.
        const slash = e.key === '/' && !e.ctrlKey && !e.metaKey && !e.altKey;
        // preventDefault нужен синхронно: .NET-обработчик асинхронный и не успеет отменить ввод символа
        // в поле, которое откроется по хоткею (N → дровер с автофокусом на названии).
        if (!editable && (plainLetter || slash || e.key === 'Escape')) e.preventDefault();
        hotkeyRef.invokeMethodAsync('OnKeyDown', e.key, e.ctrlKey || e.metaKey, e.shiftKey, editable)
            .catch(function () { });
    });

    // Картинка вложения не загрузилась — помечаем её, чтобы вместо битой иконки сработало правило
    // .md-att-img.missing. Раньше класс вешал цикл оживления разметки, теперь разметка приходит
    // готовой, и остаётся один слушатель на документ. Обязательно в фазе захвата: событие error
    // у изображений не всплывает, на document его иначе не поймать.
    document.addEventListener('error', function (e) {
        const el = e.target;
        if (el && el.tagName === 'IMG' && el.hasAttribute('data-attachment')) el.classList.add('missing');
    }, true);

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

        // Позиция курсора в поле ввода — подсказкам FQL нужна она, а не конец строки.
        caret: function (id) {
            var el = document.getElementById(id);
            return el && typeof el.selectionStart === 'number' ? el.selectionStart : -1;
        },

        setCaret: function (id, pos) {
            var el = document.getElementById(id);
            if (!el) return;
            el.focus();
            if (typeof el.setSelectionRange === 'function') el.setSelectionRange(pos, pos);
        },

        focus: function (id, select) {
            const el = document.getElementById(id);
            if (!el) return;
            el.focus();
            if (select && typeof el.select === 'function') el.select();
        },

        scrollIntoView: function (id) {
            const el = document.getElementById(id);
            if (el) el.scrollIntoView({ block: 'nearest' });
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

            const off = function () {
                zone.removeEventListener('dragenter', onEnter);
                zone.removeEventListener('dragover', onOver);
                zone.removeEventListener('dragleave', onLeave);
                zone.removeEventListener('drop', onDrop);
                if (acceptPaste) zone.removeEventListener('paste', onPaste);
                clear();
            };

            dropZones.set(zoneId, { off: off, clear: clear });
            return true;
        },

        detachZone: function (zoneId) {
            const zone = dropZones.get(zoneId);
            if (!zone) return;
            dropZones.delete(zoneId);
            try { zone.off(); } catch (_) { }
        },

        // Роадмап (docs/TZ_task_views.md §4, этап 2H): тело полосы двигает обе даты, края (.rm-edge-l / .rm-edge-r) —
        // начало или срок, ромб — единственную дату. Шаг — день (data-px на корне). Задача из «Без дат» тянется
        // призраком и кладётся на день под курсором. .NET получает только итог: OnBarDragged(id, mode, days) или
        // OnUndatedDropped(id, dayIndex).
        roadmapAttach: function (rootId, ref) {
            const px = function (root) { return Number(root.dataset.px) || 8; };
            return attachPointerDrag(rootId,
                function (e, root) {
                    const bar = e.target.closest('[data-rm-drag]');
                    if (bar && root.contains(bar)) {
                        const mode = e.target.closest('.rm-edge-l') ? 'start' : e.target.closest('.rm-edge-r') ? 'end' : 'move';
                        return { el: bar, id: bar.dataset.task, mode: mode, left: bar.offsetLeft, width: bar.offsetWidth };
                    }
                    const item = e.target.closest('[data-rm-undated]');
                    return item && root.contains(item) ? { el: item, id: item.dataset.task, mode: 'place' } : null;
                },
                function (s, dx, dy, e, root) {
                    const step = px(root);
                    const days = Math.round(dx / step) * step;
                    if (s.mode === 'move') {
                        s.el.style.transform = (s.el.classList.contains('rm-diamond') ? 'rotate(45deg) ' : '') + 'translateX(' + days + 'px)';
                    } else if (s.mode === 'start') {
                        const shift = Math.min(days, s.width - step);
                        s.el.style.left = (s.left + shift) + 'px';
                        s.el.style.width = (s.width - shift) + 'px';
                    } else if (s.mode === 'end') {
                        s.el.style.width = Math.max(step, s.width + days) + 'px';
                    } else {
                        if (!s.ghost) {
                            s.ghost = document.createElement('div');
                            s.ghost.className = 'rm-ghost';
                            s.ghost.textContent = s.el.textContent.trim();
                            document.body.appendChild(s.ghost);
                        }
                        s.ghost.style.left = (e.clientX + 12) + 'px';
                        s.ghost.style.top = (e.clientY + 8) + 'px';
                    }
                },
                function (s, dx, dy, e, root) {
                    const step = px(root);
                    if (s.mode === 'place') {
                        const lanes = root.querySelector('.rm-lanes');
                        if (!lanes) return;
                        const r = lanes.getBoundingClientRect();
                        if (e.clientX < r.left || e.clientX > r.right || e.clientY < r.top || e.clientY > r.bottom) return;
                        ref.invokeMethodAsync('OnUndatedDropped', s.id, Math.floor((e.clientX - r.left) / step));
                        return;
                    }
                    const days = Math.round(dx / step);
                    if (days !== 0) ref.invokeMethodAsync('OnBarDragged', s.id, s.mode, days);
                });
        },

        // Сетка дашборда (docs/TZ_task_views.md §8, этап 2H): шапка виджета ([data-dw-move]) переносит его, уголок
        // ([data-dw-resize]) меняет размер; шаг — колонка (12 на ширину сетки) и строка 90px с зазором сетки. .NET получает
        // только сдвиг в клетках: OnWidgetDragged(id, mode, dCols, dRows).
        gridAttach: function (rootId, ref) {
            const cell = function (root) {
                const cs = getComputedStyle(root);
                const gap = parseFloat(cs.columnGap) || 12;
                const rowGap = parseFloat(cs.rowGap) || gap;
                return { w: (root.clientWidth - gap * 11) / 12 + gap, h: 90 + rowGap };
            };
            return attachPointerDrag(rootId,
                function (e, root) {
                    const handle = e.target.closest('[data-dw-move], [data-dw-resize]');
                    if (!handle || !root.contains(handle) || e.target.closest('button, a')) return null;
                    const widget = handle.closest('[data-widget]');
                    if (!widget) return null;
                    return { el: widget, id: widget.dataset.widget, mode: handle.hasAttribute('data-dw-resize') ? 'resize' : 'move',
                        width: widget.offsetWidth, height: widget.offsetHeight };
                },
                function (s, dx, dy, e, root) {
                    const c = cell(root);
                    const cols = Math.round(dx / c.w), rows = Math.round(dy / c.h);
                    if (s.mode === 'move') {
                        s.el.style.transform = 'translate(' + cols * c.w + 'px, ' + rows * c.h + 'px)';
                    } else {
                        s.el.style.width = Math.max(c.w / 2, s.width + cols * c.w) + 'px';
                        s.el.style.height = Math.max(90, s.height + rows * c.h) + 'px';
                    }
                },
                function (s, dx, dy, e, root) {
                    const c = cell(root);
                    const cols = Math.round(dx / c.w), rows = Math.round(dy / c.h);
                    if (cols !== 0 || rows !== 0) ref.invokeMethodAsync('OnWidgetDragged', s.id, s.mode, cols, rows);
                });
        },

        // Граф workflow (docs/TZ_workflow_config.md §2, этап 3D): узел ([data-node]) тянется целиком, стрелки его
        // переходов перерисовываются следом; от кружка [data-connect] тянется линия к другому узлу. .NET получает
        // только итог: OnNodeMoved(id, x, y) в координатах холста или OnConnect(from, to).
        graphAttach: function (rootId, ref) {
            const size = function (svg) { return { w: Number(svg.dataset.nw) || 128, h: Number(svg.dataset.nh) || 34 }; };
            return attachPointerDrag(rootId,
                function (e, root) {
                    const svg = root.querySelector('svg');
                    if (!svg) return null;
                    const connect = e.target.closest('[data-connect]');
                    if (connect && root.contains(connect)) {
                        const node = connect.closest('[data-node]');
                        return { el: node, svg: svg, mode: 'connect', id: connect.dataset.connect,
                            x: Number(node.dataset.x), y: Number(node.dataset.y), start: svgPoint(svg, e.clientX, e.clientY) };
                    }
                    const node = e.target.closest('[data-node]');
                    if (!node || !root.contains(node)) return null;
                    const edges = Array.from(svg.querySelectorAll('path[data-from="' + node.dataset.node + '"], path[data-to="' + node.dataset.node + '"]'))
                        .map(function (p) { return { el: p, d: p.getAttribute('d') }; });
                    return { el: node, svg: svg, mode: 'move', id: node.dataset.node, x: Number(node.dataset.x), y: Number(node.dataset.y),
                        start: svgPoint(svg, e.clientX, e.clientY), edges: edges };
                },
                function (s, dx, dy, e) {
                    const p = svgPoint(s.svg, e.clientX, e.clientY);
                    const mx = p.x - s.start.x, my = p.y - s.start.y;
                    if (s.mode === 'connect') {
                        if (!s.line) {
                            s.line = document.createElementNS('http://www.w3.org/2000/svg', 'line');
                            s.line.setAttribute('class', 'wf-temp');
                            s.svg.appendChild(s.line);
                        }
                        s.line.setAttribute('x1', s.x);
                        s.line.setAttribute('y1', s.y);
                        s.line.setAttribute('x2', p.x);
                        s.line.setAttribute('y2', p.y);
                        return;
                    }
                    s.el.style.transform = 'translate(' + mx + 'px, ' + my + 'px)';
                    const nx = s.x + mx, ny = s.y + my, sz = size(s.svg);
                    s.edges.forEach(function (edge) {
                        const other = edge.el.dataset.from === s.id ? edge.el.dataset.to : edge.el.dataset.from;
                        const o = s.svg.querySelector('[data-node="' + other + '"]');
                        if (!o) return;
                        const ox = Number(o.dataset.x), oy = Number(o.dataset.y);
                        edge.el.setAttribute('d', edge.el.dataset.from === s.id
                            ? graphEdgePath(nx, ny, ox, oy, sz.w, sz.h)
                            : graphEdgePath(ox, oy, nx, ny, sz.w, sz.h));
                    });
                },
                function (s, dx, dy, e) {
                    if (s.line) s.line.remove();
                    if (s.edges) s.edges.forEach(function (edge) { edge.el.setAttribute('d', edge.d); });
                    const p = svgPoint(s.svg, e.clientX, e.clientY);
                    if (s.mode === 'connect') {
                        const hit = document.elementFromPoint(e.clientX, e.clientY);
                        const target = hit && hit.closest('[data-node]');
                        if (target && target.dataset.node !== s.id) ref.invokeMethodAsync('OnConnect', s.id, target.dataset.node);
                        return;
                    }
                    ref.invokeMethodAsync('OnNodeMoved', s.id, s.x + p.x - s.start.x, s.y + p.y - s.start.y);
                });
        },

        dragDetach: function (rootId) { detachPointerDrag(rootId); },

        // Выход должен быть POST: cookie flow.auth объявлена SameSite=Lax, поэтому кросс-сайтовый
        // POST её не донесёт и принудительно разлогинить человека чужой страницей нельзя. Кнопка выхода
        // живёт внутри circuit'а и формы вокруг себя не имеет, поэтому форму создаём здесь.
        submitPost: function (url) {
            const form = document.createElement('form');
            form.method = 'post';
            form.action = url;
            document.body.appendChild(form);
            form.submit();
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
