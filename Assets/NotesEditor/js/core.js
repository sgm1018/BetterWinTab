// Shared state and DOM/selection helpers for the notes editor.
'use strict';

window.NE = (() => {
    const $ = id => document.getElementById(id);
    const webview = window.chrome && window.chrome.webview;

    const els = {
        title: $('title'),
        editor: $('editor'),
        scroller: $('scroller'),
        slashMenu: $('slash-menu'),
        bubble: $('bubble'),
        turnMenu: $('turn-menu'),
        colorMenu: $('color-menu'),
        fontMenu: $('font-menu'),
        linkBox: $('link-box'),
        linkInput: $('link-input'),
        saveState: $('save-state'),
        fontButton: $('font-button'),
        turnLabel: $('turn-label'),
        colorSwatch: $('color-swatch'),
    };

    const state = {
        noteId: null,
        font: 'handwritten',
        dirty: false,
        saveTimer: 0,
        loading: false,
    };

    // Elements that behave as a single "block" (one line/paragraph in Notion terms).
    const BLOCK_TAGS = new Set(['P', 'H1', 'H2', 'H3', 'LI', 'BLOCKQUOTE', 'PRE', 'DIV']);
    const TOP_LEVEL_BLOCKS = new Set([...BLOCK_TAGS, 'UL', 'OL', 'HR', 'TABLE']);

    function post(msg) {
        if (webview) webview.postMessage(msg);
        else console.log('[host]', JSON.stringify(msg));
    }

    const sel = () => window.getSelection();

    function exec(command, value = null) {
        return document.execCommand(command, false, value);
    }

    function anchorElement() {
        const s = sel();
        if (!s.rangeCount) return null;
        let n = s.anchorNode;
        if (n && n.nodeType === Node.TEXT_NODE) n = n.parentNode;
        return n;
    }

    function selectionInEditor() {
        const s = sel();
        return s.rangeCount > 0 && els.editor.contains(s.anchorNode) && els.editor.contains(s.focusNode);
    }

    function closestIn(node, predicate) {
        while (node && node !== els.editor && node !== document.body) {
            if (node.nodeType === Node.ELEMENT_NODE && predicate(node)) return node;
            node = node.parentNode;
        }
        return null;
    }

    const closestTag = (node, tag) => closestIn(node, el => el.tagName === tag);
    const currentBlock = () => closestIn(anchorElement(), el => BLOCK_TAGS.has(el.tagName));

    function topLevelBlock() {
        let n = sel().rangeCount ? sel().anchorNode : null;
        while (n && n.parentNode !== els.editor) {
            if (n === document.body || !n.parentNode) return null;
            n = n.parentNode;
        }
        return n && n.nodeType === Node.ELEMENT_NODE ? n : null;
    }

    function isEmptyBlock(el) {
        return el.textContent.replace(/\u200b/g, '') === '' && !el.querySelector('hr, img, li');
    }

    function setCaret(node, offset) {
        const r = document.createRange();
        r.setStart(node, offset);
        r.collapse(true);
        const s = sel();
        s.removeAllRanges();
        s.addRange(r);
    }

    function placeCaretAt(el, atEnd) {
        const r = document.createRange();
        r.selectNodeContents(el);
        r.collapse(!atEnd);
        const s = sel();
        s.removeAllRanges();
        s.addRange(r);
    }

    function selectRange(range) {
        const s = sel();
        s.removeAllRanges();
        s.addRange(range);
    }

    function textBeforeCaret(block) {
        const s = sel();
        if (!s.rangeCount) return '';
        const r = document.createRange();
        r.setStart(block, 0);
        r.setEnd(s.anchorNode, s.anchorOffset);
        return r.toString();
    }

    function caretAtBlockStart(block) {
        return sel().isCollapsed && textBeforeCaret(block) === '' ;
    }

    function unwrap(el) {
        const parent = el.parentNode;
        if (!parent) return;
        while (el.firstChild) parent.insertBefore(el.firstChild, el);
        parent.removeChild(el);
    }

    function escapeHtml(text) {
        return text.replace(/[&<>"']/g, c => ({ '&': '&amp;', '<': '&lt;', '>': '&gt;', '"': '&quot;', "'": '&#39;' }[c]));
    }

    function caretRect() {
        const s = sel();
        if (!s.rangeCount) return null;
        const r = s.getRangeAt(0).cloneRange();
        r.collapse(true);
        const rect = r.getClientRects()[0];
        if (rect) return rect;
        const block = currentBlock() || els.editor;
        return block.getBoundingClientRect();
    }

    /** Shows a fixed-position popover next to an anchor rect, flipping above when there is no room. */
    function placePopover(el, rect, align = 'left') {
        el.classList.remove('hidden');
        const w = el.offsetWidth;
        const h = el.offsetHeight;
        let top = rect.bottom + 6;
        if (top + h > window.innerHeight - 8) top = Math.max(8, rect.top - h - 6);
        let left = align === 'right' ? rect.right - w : rect.left;
        left = Math.min(Math.max(8, left), window.innerWidth - w - 8);
        el.style.top = `${top}px`;
        el.style.left = `${left}px`;
    }

    /** Marks the note as modified and schedules a debounced save to the host. */
    function changed() {
        if (state.loading || !state.noteId) return;
        state.dirty = true;
        els.saveState.textContent = 'Saving…';
        clearTimeout(state.saveTimer);
        state.saveTimer = setTimeout(flush, 450);
    }

    function serialize() {
        const editor = els.editor;
        const isEmpty = editor.textContent.replace(/[\u200b\s]/g, '') === '' &&
            !editor.querySelector('hr, li, pre, h1, h2, h3, blockquote');
        if (isEmpty) return '';
        const clone = editor.cloneNode(true);
        clone.querySelectorAll('[data-placeholder]').forEach(el => el.removeAttribute('data-placeholder'));
        return clone.innerHTML;
    }

    function titleText() {
        return els.title.innerText.replace(/\s+/g, ' ').trim();
    }

    /** Sends pending changes to the host immediately. */
    function flush() {
        clearTimeout(state.saveTimer);
        state.saveTimer = 0;
        if (!state.dirty || !state.noteId) return;
        state.dirty = false;
        post({
            type: 'change',
            id: state.noteId,
            title: titleText(),
            html: serialize(),
            font: state.font,
            preview: els.editor.innerText.replace(/\s+/g, ' ').trim().slice(0, 200),
        });
        els.saveState.textContent = 'Saved';
    }

    return {
        els, state, BLOCK_TAGS, TOP_LEVEL_BLOCKS,
        post, sel, exec, anchorElement, selectionInEditor, closestIn, closestTag, currentBlock, topLevelBlock,
        isEmptyBlock, setCaret, placeCaretAt, selectRange, textBeforeCaret, caretAtBlockStart, unwrap,
        escapeHtml, caretRect, placePopover, changed, flush,
    };
})();
