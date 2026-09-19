// Исходник редактора Markdown для Flow.Client: CodeMirror 6 + разметка Markdown, видимая прямо при наборе.
// Собранный бандл лежит в wwwroot/js/flow-editor.js; как пересобрать — см. README.md рядом.
//
// Наружу торчит window.flowEditor с тем же набором операций, что раньше делал flow.editor.* над textarea
// (state / wrap / prefixLines / insertMention), поэтому MarkdownEditor.razor не переписывает свою логику
// упоминаний и тулбара, а только вызывает другие функции.
//
// Клавиши, которые нужны меню упоминаний (↑ ↓ Enter Tab Esc) и тулбару (Ctrl B/I/K, Ctrl Enter), уходят
// в .NET синхронно через invokeMethod: ответ нужен здесь и сейчас, чтобы решить судьбу события. В ответ
// приходит команда — что именно сделать. Правку текста выполняем здесь же: вызвать JS из .NET, пока тот
// отвечает на синхронный вызов, нельзя — вложенный вызов не проходит и правка молча теряется. Новое
// значение уходит наверх обычным асинхронным каналом (HandleEditorInput), действия — через RunEditorAction.

import { EditorState, Compartment, Prec } from '@codemirror/state';
import { EditorView, keymap, placeholder as placeholderExt, drawSelection } from '@codemirror/view';
import { history, historyKeymap, defaultKeymap } from '@codemirror/commands';
import { syntaxHighlighting, HighlightStyle } from '@codemirror/language';
import { markdown } from '@codemirror/lang-markdown';
import { tags } from '@lezer/highlight';

// Разметка видна как разметка: заголовок крупнее, жирный жирным, ссылка цветом акцента.
// Сами символы (**, ##, [ ]) остаются в тексте — это Markdown, а не WYSIWYG, — но приглушены,
// чтобы не мешали читать написанное.
const flowHighlight = HighlightStyle.define([
    { tag: tags.heading1, fontSize: '1.45em', fontWeight: '800', lineHeight: '1.5' },
    { tag: tags.heading2, fontSize: '1.28em', fontWeight: '800', lineHeight: '1.5' },
    { tag: tags.heading3, fontSize: '1.14em', fontWeight: '700', lineHeight: '1.5' },
    { tag: tags.heading4, fontWeight: '700' },
    { tag: tags.heading5, fontWeight: '700' },
    { tag: tags.heading6, fontWeight: '700' },
    { tag: tags.strong, fontWeight: '800', color: '#fff' },
    { tag: tags.emphasis, fontStyle: 'italic' },
    { tag: tags.strikethrough, textDecoration: 'line-through', opacity: '0.7' },
    { tag: tags.link, color: 'var(--accent)' },
    { tag: tags.url, color: 'var(--accent)', textDecoration: 'underline' },
    { tag: tags.monospace, fontFamily: 'var(--font-mono)', color: 'var(--tan)' },
    { tag: tags.quote, color: 'var(--text-secondary)', fontStyle: 'italic' },
    { tag: tags.list, color: 'var(--accent)' },
    { tag: tags.processingInstruction, color: 'rgba(255,255,255,0.35)', fontWeight: '500' },
    { tag: tags.contentSeparator, color: 'rgba(255,255,255,0.35)' }
]);

const flowTheme = EditorView.theme({
    '&': {
        color: '#fff',
        backgroundColor: 'transparent',
        font: 'var(--fw-medium) 13.5px var(--font-text)'
    },
    '.cm-content': { padding: '12px 14px', caretColor: 'var(--accent)' },
    '.cm-line': { padding: '0' },
    '&.cm-focused': { outline: 'none' },
    '.cm-cursor, .cm-dropCursor': { borderLeftColor: 'var(--accent)' },
    '&.cm-focused .cm-selectionBackground, .cm-selectionBackground, .cm-content ::selection': {
        backgroundColor: 'rgba(255, 186, 0, 0.25)'
    },
    '.cm-placeholder': { color: 'var(--text-tertiary)' },
    '.cm-scroller': { fontFamily: 'inherit', lineHeight: '1.55', overflow: 'auto' }
}, { dark: true });

/** @type {Map<string, {view: EditorView, ref: any, editable: Compartment}>} */
const editors = new Map();

function get(id) {
    return editors.get(id);
}

// Забирает ли компонент эту клавишу себе. Решать обязан JS, причём синхронно: CodeMirror ждёт
// true/false, чтобы решить судьбу события, а спросить .NET синхронно нельзя — при серверном рендере
// компонент живёт на другом конце SignalR, и invokeMethod там физически невозможен (раньше он тихо
// бросал, catch возвращал null, и меню упоминаний просто переставало работать без единой ошибки).
// Поэтому .NET заранее сообщает сюда своё состояние: setMentionOpen и hasCancel при mount.
const MENU_KEYS = ['ArrowDown', 'ArrowUp', 'Enter', 'Tab', 'Escape'];
const EDIT_KEYS = ['Mod-Enter', 'Mod-b', 'Mod-i', 'Mod-k'];

function takesKey(entry, key) {
    if (!entry || !entry.ref) return false;
    if (entry.mentionOpen && MENU_KEYS.indexOf(key) >= 0) return true;
    if (EDIT_KEYS.indexOf(key) >= 0) return true;
    // Escape вне меню — только если наверху есть кому отменять (иначе это обычная клавиша редактора).
    return key === 'Escape' && entry.hasCancel;
}

// Выполнить команду от .NET: текст правим здесь, наверх отдаём новое значение.
function runCommand(entry, id, cmd) {
    let value;
    switch (cmd.kind) {
        case 'wrap':
            value = window.flowEditor.wrap(id, cmd.before, cmd.after, cmd.placeholder);
            break;
        case 'prefix':
            value = window.flowEditor.prefixLines(id, cmd.prefix, cmd.ordered);
            break;
        case 'mention':
            value = window.flowEditor.insertMention(id, cmd.at, cmd.username);
            break;
        case 'action':
            entry.ref.invokeMethodAsync('RunEditorAction', cmd.action).catch(function () { });
            return;
        default:
            // handled: состояние поменяли в .NET, здесь делать нечего.
            return;
    }

    if (value !== null && value !== undefined) {
        entry.ref.invokeMethodAsync('HandleEditorInput', value).catch(function () { });
    }
}

window.flowEditor = {
    /** Состояние меню упоминаний из .NET: по нему takesKey решает, чьи стрелки и Enter. */
    setMentionOpen: function (id, open) {
        const entry = get(id);
        if (entry) entry.mentionOpen = open === true;
    },

    /** Выполнить команду, присланную .NET после асинхронного разбора клавиши. */
    run: function (id, cmd) {
        const entry = get(id);
        if (entry && cmd) runCommand(entry, id, cmd);
    },

    /**
     * Создать редактор внутри контейнера. options: { value, placeholder, readOnly }.
     * dotNetRef должен иметь [JSInvokable] HandleEditorInput(string) и HandleEditorKeyAsync(string).
     * options.hasCancel — есть ли наверху обработчик отмены: от него зависит, забирать ли Escape.
     */
    mount: function (id, dotNetRef, options) {
        const host = document.getElementById(id);
        if (!host) return false;

        this.destroy(id);
        const opts = options || {};
        const editable = new Compartment();
        // mentionOpen и hasCancel — копия состояния .NET: по ним takesKey решает синхронно (см. выше).
        const entry = { view: null, ref: dotNetRef, editable: editable, mentionOpen: false, hasCancel: opts.hasCancel === true };

        // Клавиши забираем через keymap наивысшего приоритета, а не через domEventHandlers: последние
        // в связке с keymap срабатывают не на всякое событие, и Enter уходил в редактор мимо списка
        // упоминаний. Возврат true = «команда выполнена»: CodeMirror сам гасит событие и дальше его
        // не несёт. Раскладку keymap разбирает сам — на русской Ctrl+B приходит как key «и».
        const ask = (key) => {
            if (!takesKey(entry, key)) return false;

            // Клавишу гасим здесь и сейчас, а .NET разбирается с ней асинхронно и сам присылает
            // команду обратно через flowEditor.run — см. HandleEditorKeyAsync в MarkdownEditor.
            entry.ref.invokeMethodAsync('HandleEditorKeyAsync', key).catch(function () { });
            return true;
        };

        const menuKeys = Prec.highest(keymap.of([
            { key: 'ArrowDown', run: () => ask('ArrowDown') },
            { key: 'ArrowUp', run: () => ask('ArrowUp') },
            { key: 'Enter', run: () => ask('Enter') },
            { key: 'Tab', run: () => ask('Tab') },
            { key: 'Escape', run: () => ask('Escape') },
            { key: 'Mod-Enter', run: () => ask('Mod-Enter') },
            { key: 'Mod-b', run: () => ask('Mod-b') },
            { key: 'Mod-i', run: () => ask('Mod-i') },
            { key: 'Mod-k', run: () => ask('Mod-k') },
        ]));

        const view = new EditorView({
            parent: host,
            state: EditorState.create({
                doc: opts.value || '',
                extensions: [
                    history(),
                    drawSelection(),
                    EditorView.lineWrapping,
                    markdown(),
                    syntaxHighlighting(flowHighlight),
                    flowTheme,
                    placeholderExt(opts.placeholder || ''),
                    menuKeys,
                    keymap.of([...defaultKeymap, ...historyKeymap]),
                    editable.of(EditorView.editable.of(!opts.readOnly)),
                    EditorView.updateListener.of(update => {
                        if (!update.docChanged && !update.selectionSet) return;
                        if (!entry.ref) return;
                        try {
                            entry.ref.invokeMethodAsync('HandleEditorInput', update.state.doc.toString());
                        } catch (_) {
                            // Компонент уничтожен между обновлением и вызовом — обновлять нечего.
                        }
                    })
                ]
            })
        });

        entry.view = view;
        editors.set(id, entry);
        return true;
    },

    destroy: function (id) {
        const entry = editors.get(id);
        if (!entry) return;
        entry.ref = null;
        entry.view.destroy();
        editors.delete(id);
    },

    focus: function (id) {
        const entry = get(id);
        if (!entry) return;
        entry.view.focus();
    },

    setReadOnly: function (id, readOnly) {
        const entry = get(id);
        if (!entry) return;
        entry.view.dispatch({ effects: entry.editable.reconfigure(EditorView.editable.of(!readOnly)) });
    },

    /** Значение и границы выделения — как у textarea: .NET определяет по ним токен упоминания. */
    state: function (id) {
        const entry = get(id);
        if (!entry) return null;
        const sel = entry.view.state.selection.main;
        return { value: entry.view.state.doc.toString(), start: sel.from, end: sel.to };
    },

    /** Значение пришло со стороны .NET (очистка после отправки, отмена правки). */
    setValue: function (id, value) {
        const entry = get(id);
        if (!entry) return null;
        const current = entry.view.state.doc.toString();
        if (current === (value || '')) return current;

        entry.view.dispatch({
            changes: { from: 0, to: current.length, insert: value || '' },
            selection: { anchor: (value || '').length }
        });
        return entry.view.state.doc.toString();
    },

    /** Обернуть выделение (или вставить подсказку): **текст**, `код`, [ссылка](url). */
    wrap: function (id, before, after, placeholderText) {
        const entry = get(id);
        if (!entry) return null;
        const view = entry.view;
        const sel = view.state.selection.main;
        const selected = view.state.sliceDoc(sel.from, sel.to);
        const inner = selected.length ? selected : placeholderText;

        view.dispatch({
            changes: { from: sel.from, to: sel.to, insert: before + inner + after },
            // Без выделения ставим каретку внутрь обёртки, чтобы можно было сразу печатать.
            selection: selected.length
                ? { anchor: sel.from + before.length + inner.length + after.length }
                : { anchor: sel.from + before.length, head: sel.from + before.length + inner.length }
        });
        view.focus();
        return view.state.doc.toString();
    },

    /** Префикс строк выделения: заголовок, цитата, список (ordered — с нумерацией). */
    prefixLines: function (id, prefix, ordered) {
        const entry = get(id);
        if (!entry) return null;
        const view = entry.view;
        const sel = view.state.selection.main;
        const from = view.state.doc.lineAt(sel.from).number;
        const to = view.state.doc.lineAt(sel.to).number;
        const changes = [];

        for (let n = from, i = 0; n <= to; n++, i++) {
            const line = view.state.doc.line(n);
            changes.push({ from: line.from, insert: ordered ? (i + 1) + '. ' : prefix });
        }

        view.dispatch({ changes });
        view.focus();
        return view.state.doc.toString();
    },

    /** Заменить «@частичное» перед кареткой на «@username ». */
    insertMention: function (id, atPos, username) {
        const entry = get(id);
        if (!entry) return null;
        const view = entry.view;
        const caret = view.state.selection.main.head;

        view.dispatch({
            changes: { from: atPos, to: caret, insert: '@' + username + ' ' },
            selection: { anchor: atPos + username.length + 2 }
        });
        view.focus();
        return view.state.doc.toString();
    }
};
