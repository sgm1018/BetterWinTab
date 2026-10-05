// Block-level and inline formatting operations (turn into, lists, to-dos, colors, links...).
'use strict';

(() => {
    const { els, sel, exec, anchorElement, closestTag, currentBlock, topLevelBlock, isEmptyBlock,
        placeCaretAt, setCaret, changed, unwrap, escapeHtml, TOP_LEVEL_BLOCKS } = NE;
    const editor = els.editor;

    const TEXT_COLORS = [
        { id: 'default', label: 'Default', value: null },
        { id: 'gray', label: 'Gray', value: '#9b9b9b' },
        { id: 'brown', label: 'Brown', value: '#ba856f' },
        { id: 'orange', label: 'Orange', value: '#e8894f' },
        { id: 'yellow', label: 'Yellow', value: '#e8bd52' },
        { id: 'green', label: 'Green', value: '#5fc485' },
        { id: 'blue', label: 'Blue', value: '#52aee8' },
        { id: 'purple', label: 'Purple', value: '#ae84e8' },
        { id: 'pink', label: 'Pink', value: '#e86fae' },
        { id: 'red', label: 'Red', value: '#f0605a' },
    ];
    const BG_COLORS = [
        { id: 'default', label: 'Default', value: null },
        { id: 'gray', label: 'Gray', value: '#323232' },
        { id: 'brown', label: 'Brown', value: '#4a3228' },
        { id: 'orange', label: 'Orange', value: '#5c3b20' },
        { id: 'yellow', label: 'Yellow', value: '#5a4a18' },
        { id: 'green', label: 'Green', value: '#1d4630' },
        { id: 'blue', label: 'Blue', value: '#163f5a' },
        { id: 'purple', label: 'Purple', value: '#3d2c5a' },
        { id: 'pink', label: 'Pink', value: '#582a44' },
        { id: 'red', label: 'Red', value: '#5e2a27' },
    ];

    // ── Structure normalization ────────────────────────────────────────────

    /** Wraps stray top-level inline content in <p> and guarantees at least one block. */
    function ensureStructure() {
        const s = sel();
        const saved = s.rangeCount && editor.contains(s.anchorNode) ? { node: s.anchorNode, offset: s.anchorOffset } : null;
        let paragraph = null;
        let moved = false;

        for (const node of Array.from(editor.childNodes)) {
            if (node.nodeType === Node.ELEMENT_NODE && TOP_LEVEL_BLOCKS.has(node.tagName)) { paragraph = null; continue; }
            if (node.nodeType === Node.TEXT_NODE && node.data === '') { node.remove(); continue; }
            if (node.nodeType !== Node.ELEMENT_NODE && node.nodeType !== Node.TEXT_NODE) { node.remove(); continue; }
            if (!paragraph) {
                paragraph = document.createElement('p');
                editor.insertBefore(paragraph, node);
            }
            paragraph.appendChild(node);
            moved = true;
        }

        // <hr> must live at the top level.
        editor.querySelectorAll(':scope :is(p, h1, h2, h3, div) > hr').forEach(hr => {
            const parent = hr.parentNode;
            parent.after(hr);
            if (isEmptyBlock(parent)) parent.remove();
            moved = true;
        });

        // Chromium sometimes nests blocks inside a paragraph; lift them out.
        editor.querySelectorAll('p, h1, h2, h3').forEach(el => {
            if (el.isConnected && el.querySelector(':scope > :is(ul, ol, p, h1, h2, h3, pre, blockquote, div, table)')) {
                liftNestedBlocks(el);
                moved = true;
            }
        });

        if (!editor.firstElementChild) {
            editor.innerHTML = '<p><br></p>';
            if (document.activeElement === editor) placeCaretAt(editor.firstElementChild, false);
            return;
        }

        if (moved && saved && saved.node.isConnected) {
            const max = saved.node.nodeType === Node.TEXT_NODE ? saved.node.length : saved.node.childNodes.length;
            setCaret(saved.node, Math.min(saved.offset, max));
        }
    }

    function liftNestedBlocks(el) {
        const parent = el.parentNode;
        let run = null;
        for (const child of Array.from(el.childNodes)) {
            const isBlock = child.nodeType === Node.ELEMENT_NODE && /^(UL|OL|P|H1|H2|H3|PRE|BLOCKQUOTE|DIV|HR|TABLE)$/.test(child.tagName);
            if (isBlock) {
                parent.insertBefore(child, el);
                run = null;
            } else {
                if (!run && child.nodeType === Node.ELEMENT_NODE && child.tagName === 'BR') continue;
                if (!run) {
                    run = document.createElement(el.tagName);
                    parent.insertBefore(run, el);
                }
                run.appendChild(child);
            }
        }
        el.remove();
    }

    /** Keeps an editable paragraph after blocks that are hard to escape (code, quote, divider). */
    function ensureTrailingParagraph() {
        const last = editor.lastElementChild;
        if (last && /^(PRE|BLOCKQUOTE|HR|TABLE)$/.test(last.tagName)) {
            const p = document.createElement('p');
            p.appendChild(document.createElement('br'));
            editor.appendChild(p);
        }
        editor.querySelectorAll(':scope > hr').forEach(hr => {
            if (!hr.nextElementSibling) {
                const p = document.createElement('p');
                p.appendChild(document.createElement('br'));
                hr.after(p);
            }
        });
    }

    // ── Block types ────────────────────────────────────────────────────────

    function listKindOf(list) {
        if (!list) return null;
        if (list.tagName === 'OL') return 'numbered';
        return list.classList.contains('todo') ? 'todo' : 'bullet';
    }

    function blockType() {
        const anchor = anchorElement();
        const li = closestTag(anchor, 'LI');
        if (li) return listKindOf(li.parentElement);
        if (closestTag(anchor, 'PRE')) return 'code';
        if (closestTag(anchor, 'BLOCKQUOTE')) return 'quote';
        const block = currentBlock();
        if (block && /^H[1-3]$/.test(block.tagName)) return block.tagName.toLowerCase();
        return 'text';
    }

    function toggleListOff(list) {
        exec(list.tagName === 'OL' ? 'insertOrderedList' : 'insertUnorderedList');
    }

    function unwrapQuote() {
        const quote = closestTag(anchorElement(), 'BLOCKQUOTE');
        if (!quote) return;
        // Wrap loose inline content so it keeps paragraph semantics after unwrapping.
        if (!quote.querySelector('p, h1, h2, h3, ul, ol, pre, div')) {
            const p = document.createElement('p');
            while (quote.firstChild) p.appendChild(quote.firstChild);
            quote.appendChild(p);
        }
        const s = sel();
        const saved = s.rangeCount ? { node: s.anchorNode, offset: s.anchorOffset } : null;
        unwrap(quote);
        if (saved && saved.node.isConnected) setCaret(saved.node, saved.offset);
    }

    /** Converts the current block(s) to the given type; toggles back to text when already that type. */
    function turnInto(type) {
        if (!NE.selectionInEditor()) editor.focus();
        const anchor = anchorElement();
        const li = closestTag(anchor, 'LI');
        const list = li ? li.parentElement : null;
        const kind = listKindOf(list);

        if (type === 'bullet' || type === 'numbered' || type === 'todo') {
            if (kind === type) {
                toggleListOff(list);
            } else {
                if (kind) {
                    // Switching between OL and UL needs the opposite command; UL<->todo is just a class.
                    if ((type === 'numbered') !== (list.tagName === 'OL'))
                        exec(type === 'numbered' ? 'insertOrderedList' : 'insertUnorderedList');
                } else {
                    if (closestTag(anchor, 'BLOCKQUOTE')) unwrapQuote();
                    const block = currentBlock();
                    if (block && /^(H1|H2|H3|PRE)$/.test(block.tagName)) exec('formatBlock', 'p');
                    exec(type === 'numbered' ? 'insertOrderedList' : 'insertUnorderedList');
                }
                const newList = closestTag(anchorElement(), type === 'numbered' ? 'OL' : 'UL');
                if (newList) {
                    if (type === 'todo') {
                        newList.classList.add('todo');
                        newList.querySelectorAll(':scope > li').forEach(item => {
                            if (!item.hasAttribute('data-checked')) item.setAttribute('data-checked', 'false');
                        });
                    } else {
                        newList.classList.remove('todo');
                        if (!newList.classList.length) newList.removeAttribute('class');
                        newList.querySelectorAll(':scope > li[data-checked]').forEach(item => item.removeAttribute('data-checked'));
                    }
                }
            }
        } else {
            if (list) toggleListOff(list);
            const current = blockType();
            if (current === 'quote' && type !== 'quote') unwrapQuote();

            const tag = { text: 'p', h1: 'h1', h2: 'h2', h3: 'h3', quote: 'blockquote', code: 'pre' }[type] || 'p';
            if (current === type && type !== 'text') {
                if (type === 'quote') unwrapQuote(); else exec('formatBlock', 'p');
            } else if (type !== 'quote' || current !== 'quote') {
                exec('formatBlock', tag);
            }
        }

        ensureStructure();
        ensureTrailingParagraph();
        changed();
    }

    function insertDivider() {
        const block = topLevelBlock();
        const hr = document.createElement('hr');
        const p = document.createElement('p');
        p.appendChild(document.createElement('br'));
        if (block && block.tagName === 'P' && isEmptyBlock(block)) block.replaceWith(hr);
        else if (block) block.after(hr);
        else editor.appendChild(hr);
        hr.after(p);
        placeCaretAt(p, false);
        changed();
    }

    function insertText(text) {
        exec('insertText', text);
        changed();
    }

    // ── Inline formatting ──────────────────────────────────────────────────

    function inlineFormat(command) {
        exec(command);
        changed();
    }

    function toggleInlineCode() {
        const s = sel();
        if (!s.rangeCount) return;
        const code = closestTag(anchorElement(), 'CODE');
        if (code && !closestTag(code, 'PRE')) {
            unwrap(code);
            changed();
            return;
        }
        if (s.isCollapsed || closestTag(anchorElement(), 'PRE')) return;
        exec('insertHTML', `<code>${escapeHtml(s.toString())}</code>`);
        changed();
    }

    const SENTINEL = { color: '#010203', background: '#030201' };
    const SENTINEL_RGB = { color: 'rgb(1, 2, 3)', background: 'rgb(3, 2, 1)' };

    function stripSentinel(prop, rgb) {
        editor.querySelectorAll('[style]').forEach(el => {
            if (el.style.getPropertyValue(prop) === rgb) {
                el.style.removeProperty(prop);
                if (!el.getAttribute('style')) {
                    el.removeAttribute('style');
                    if (el.tagName === 'SPAN') unwrap(el);
                }
            }
        });
        editor.querySelectorAll('font[color]').forEach(f => {
            if (f.getAttribute('color').toLowerCase() === SENTINEL.color) unwrap(f);
        });
    }

    /** Applies a text color (value=null resets to the default color). */
    function applyTextColor(value) {
        const collapsed = sel().isCollapsed;
        exec('styleWithCSS', true);
        if (value) exec('foreColor', value);
        else if (collapsed) exec('foreColor', getComputedStyle(editor).color);
        else { exec('foreColor', SENTINEL.color); stripSentinel('color', SENTINEL_RGB.color); }
        exec('styleWithCSS', false);
        changed();
    }

    /** Applies a background/highlight color (value=null removes it). */
    function applyBackgroundColor(value) {
        const collapsed = sel().isCollapsed;
        exec('styleWithCSS', true);
        if (value) exec('hiliteColor', value);
        else if (collapsed) exec('hiliteColor', 'transparent');
        else { exec('hiliteColor', SENTINEL.background); stripSentinel('background-color', SENTINEL_RGB.background); }
        exec('styleWithCSS', false);
        changed();
    }

    function clearFormatting() {
        exec('removeFormat');
        exec('unlink');
        const s = sel();
        if (s.rangeCount) {
            const range = s.getRangeAt(0);
            editor.querySelectorAll('code, mark, span[style], font').forEach(el => {
                if (!closestTag(el, 'PRE') && range.intersectsNode(el)) unwrap(el);
            });
        }
        changed();
    }

    function normalizeUrl(raw) {
        const value = raw.trim();
        if (!value) return '';
        if (/^(https?:|mailto:)/i.test(value)) return value;
        if (/^[^\s@]+@[^\s@]+\.[^\s@]+$/.test(value)) return `mailto:${value}`;
        if (/^[a-z][a-z0-9+.-]*:/i.test(value)) return '';
        return `https://${value}`;
    }

    function applyLink(range, rawUrl) {
        editor.focus();
        NE.selectRange(range);
        const url = normalizeUrl(rawUrl);
        if (url) exec('createLink', url);
        else exec('unlink');
        changed();
    }

    function insertDate(withTime) {
        const now = new Date();
        const text = withTime
            ? now.toLocaleTimeString(undefined, { hour: '2-digit', minute: '2-digit' })
            : now.toLocaleDateString(undefined, { day: 'numeric', month: 'long', year: 'numeric' });
        insertText(text);
    }

    // ── Markdown-style shortcuts ───────────────────────────────────────────

    const MARKDOWN_RULES = [
        [/^#$/, 'h1'], [/^##$/, 'h2'], [/^###$/, 'h3'],
        [/^[-*+]$/, 'bullet'], [/^1[.)]$/, 'numbered'],
        [/^\[ ?\]$/, 'todo'], [/^\[x\]$/i, 'todo-done'],
        [/^>$/, 'quote'], [/^```$/, 'code'],
    ];

    /** Called after a space or "-" is typed; converts "# ", "- ", "[] ", "> ", "```", "---" etc. */
    function tryMarkdownShortcut(typed) {
        const s = sel();
        if (!s.isCollapsed) return false;
        const block = currentBlock();
        if (!block || block.tagName === 'PRE' || closestTag(block, 'PRE')) return false;
        const before = NE.textBeforeCaret(block).replace(/\u00a0/g, ' ');

        if (typed === '-') {
            if (before !== '---' || block.tagName !== 'P' || block.textContent.replace(/\u00a0/g, ' ') !== '---') return false;
            block.textContent = '';
            insertDivider();
            return true;
        }

        if (!before.endsWith(' ')) return false;
        const marker = before.slice(0, -1);
        const rule = MARKDOWN_RULES.find(([re]) => re.test(marker));
        if (!rule) return false;
        if (block.tagName === 'LI' || closestTag(block, 'LI')) return false;

        const range = document.createRange();
        range.setStart(block, 0);
        range.setEnd(s.anchorNode, s.anchorOffset);
        NE.selectRange(range);
        exec('delete');

        if (rule[1] === 'todo-done') {
            turnInto('todo');
            const li = closestTag(anchorElement(), 'LI');
            if (li) li.setAttribute('data-checked', 'true');
        } else {
            turnInto(rule[1]);
        }
        return true;
    }

    // ── Keyboard behaviors inside special blocks ───────────────────────────

    function newParagraphAfter(block) {
        const p = document.createElement('p');
        p.appendChild(document.createElement('br'));
        block.after(p);
        placeCaretAt(p, false);
        changed();
    }

    /** Enter handling; returns true when the default behavior was replaced. */
    function handleEnter(e) {
        const anchor = anchorElement();
        const pre = closestTag(anchor, 'PRE');
        if (pre) {
            const s = sel();
            const atEnd = NE.textBeforeCaret(pre).length >= pre.textContent.replace(/\n+$/, '').length;
            // Shift/Ctrl+Enter, or Enter on an empty last line, leaves the code block.
            if (e.shiftKey || e.ctrlKey || (atEnd && s.isCollapsed && caretAfterLineBreak(pre))) {
                trimTrailingNewlines(pre);
                newParagraphAfter(pre);
            } else {
                exec('insertLineBreak');
                changed();
            }
            return true;
        }

        const quote = closestTag(anchor, 'BLOCKQUOTE');
        if (quote && !e.shiftKey) {
            const block = currentBlock();
            const line = block && block !== quote && quote.contains(block) ? block : quote;
            if (isEmptyBlock(line)) {
                if (line === quote) {
                    const p = document.createElement('p');
                    p.appendChild(document.createElement('br'));
                    quote.replaceWith(p);
                    placeCaretAt(p, false);
                    changed();
                } else {
                    line.remove();
                    newParagraphAfter(quote);
                }
                return true;
            }
            if (NE.textBeforeCaret(quote).length === quote.textContent.length) {
                newParagraphAfter(quote);
                return true;
            }
        }
        return false;
    }

    /** True when the caret sits on an empty line right after a line break inside the block. */
    function caretAfterLineBreak(block) {
        const s = sel();
        let node = s.anchorNode;
        let offset = s.anchorOffset;
        if (node.nodeType === Node.TEXT_NODE) {
            if (node.data.slice(0, offset).endsWith('\n')) return true;
            if (offset > 0 && node.data.slice(0, offset).trim() !== '') return false;
            while (node !== block && !node.previousSibling) node = node.parentNode;
            if (node === block) return false;
            node = node.previousSibling;
        } else {
            node = node.childNodes[offset - 1];
        }
        while (node && node.nodeType === Node.TEXT_NODE && node.data === '') node = node.previousSibling;
        if (!node) return false;
        if (node.nodeType === Node.TEXT_NODE) return node.data.endsWith('\n');
        return node.tagName === 'BR' || (node.lastChild != null && node.lastChild.nodeName === 'BR');
    }

    function trimTrailingNewlines(pre) {
        let last = pre.lastChild;
        while (last && last.nodeType === Node.ELEMENT_NODE && last.tagName === 'BR') {
            const prev = last.previousSibling;
            last.remove();
            last = prev;
        }
        if (last && last.nodeType === Node.TEXT_NODE) last.data = last.data.replace(/\n+$/, '');
        if (!pre.firstChild) pre.appendChild(document.createElement('br'));
    }

    /** Backspace at the start of a heading/quote/code/to-do turns it back into plain text. */
    function handleBackspace() {
        const s = sel();
        if (!s.isCollapsed) return false;
        const anchor = anchorElement();
        const special = closestTag(anchor, 'PRE') || closestTag(anchor, 'BLOCKQUOTE');
        const block = special || currentBlock();
        if (!block || !NE.caretAtBlockStart(block)) return false;
        if (/^(H1|H2|H3|PRE)$/.test(block.tagName)) { exec('formatBlock', 'p'); changed(); return true; }
        if (block.tagName === 'BLOCKQUOTE') { unwrapQuote(); changed(); return true; }
        return false;
    }

    function handleTab(e) {
        if (closestTag(anchorElement(), 'LI')) exec(e.shiftKey ? 'outdent' : 'indent');
        else if (!e.shiftKey) exec('insertText', '\t');
        changed();
    }

    function toggleTodo(li) {
        li.setAttribute('data-checked', li.getAttribute('data-checked') === 'true' ? 'false' : 'true');
        changed();
    }

    Object.assign(NE, {
        TEXT_COLORS, BG_COLORS,
        ensureStructure, ensureTrailingParagraph, blockType, turnInto, insertDivider, insertText, insertDate,
        inlineFormat, toggleInlineCode, applyTextColor, applyBackgroundColor, clearFormatting, applyLink,
        tryMarkdownShortcut, handleEnter, handleBackspace, handleTab, toggleTodo,
    });
})();
