// Event wiring and the bridge with the WinUI host (chrome.webview messages).
'use strict';

(() => {
    const { els, state, sel, exec, anchorElement, closestTag, changed, flush } = NE;
    const editor = els.editor;
    const title = els.title;

    exec('defaultParagraphSeparator', 'p');
    exec('styleWithCSS', false);

    // ── Placeholders ("Type '/' for commands") ─────────────────────────────

    let placeholderEl = null;

    function placeholderFor(el) {
        switch (el.tagName) {
            case 'H1': return 'Heading 1';
            case 'H2': return 'Heading 2';
            case 'H3': return 'Heading 3';
            case 'LI': return 'List';
            case 'PRE': return 'Code';
            case 'BLOCKQUOTE': return 'Quote';
            default: return "Type '/' for commands";
        }
    }

    function updatePlaceholder() {
        let target = null;
        if (document.activeElement === editor && NE.selectionInEditor() && sel().isCollapsed) {
            const block = NE.currentBlock();
            if (block && NE.isEmptyBlock(block) && !(block.tagName === 'LI' && block.parentElement.classList.contains('todo')))
                target = block;
        } else if (editor.childElementCount === 1 && editor.firstElementChild.tagName === 'P' && NE.isEmptyBlock(editor.firstElementChild)) {
            target = editor.firstElementChild;
        }
        if (target === placeholderEl) {
            if (target) target.setAttribute('data-placeholder', placeholderFor(target));
            return;
        }
        if (placeholderEl) placeholderEl.removeAttribute('data-placeholder');
        placeholderEl = target;
        if (target) target.setAttribute('data-placeholder', placeholderFor(target));
    }

    // ── Loading notes ──────────────────────────────────────────────────────

    function load(msg) {
        flush();
        NE.closeAll();
        state.loading = true;
        state.noteId = msg.id || null;
        title.textContent = msg.title || '';
        editor.replaceChildren(NE.sanitize(msg.html || '', true));
        NE.ensureStructure();
        disableNativeImageDragging();
        NE.setFont(msg.font || 'handwritten', false);
        const editable = state.noteId ? 'true' : 'false';
        title.contentEditable = editable;
        editor.contentEditable = editable;
        els.scroller.scrollTop = 0;
        els.saveState.textContent = '';
        placeholderEl = null;
        state.loading = false;
        updatePlaceholder();
    }

    function focusTarget(target) {
        if (!state.noteId) return;
        if (target === 'title') {
            title.focus();
            NE.placeCaretAt(title, true);
            return;
        }
        editor.focus();
        if (!NE.selectionInEditor()) NE.placeCaretAt(editor.lastElementChild || editor, true);
        updatePlaceholder();
    }

    const HEX = /^#([0-9a-f]{6}|[0-9a-f]{8})$/i;
    function cssColor(hex) {
        if (typeof hex !== 'string' || !HEX.test(hex)) return null;
        // AppSettings uses #AARRGGBB; CSS expects #RRGGBBAA.
        return hex.length === 9 ? `#${hex.slice(3)}${hex.slice(1, 3)}` : hex;
    }

    function applyTheme(theme) {
        const map = {
            accent: '--accent', accentDim: '--accent-dim', accentSubtle: '--accent-subtle', background: '--bg',
            surface: '--surface', card: '--card', border: '--border', text: '--text', textSecondary: '--text2',
            muted: '--muted', danger: '--danger',
        };
        for (const [key, variable] of Object.entries(map)) {
            const value = cssColor(theme[key]);
            if (value) document.documentElement.style.setProperty(variable, value);
        }
    }

    function handleHostMessage(msg) {
        if (!msg || typeof msg !== 'object') return;
        switch (msg.type) {
            case 'load': load(msg); break;
            case 'theme': applyTheme(msg); break;
            case 'flush': flush(); break;
            case 'focus': focusTarget(msg.target); break;
        }
    }

    if (window.chrome && window.chrome.webview) {
        window.chrome.webview.addEventListener('message', e => handleHostMessage(e.data));
    }
    // Exposed for manual testing outside the host.
    window.notesEditor = { load, applyTheme, flush, handleHostMessage };

    // ── Title ──────────────────────────────────────────────────────────────

    title.addEventListener('keydown', e => {
        const mod = e.ctrlKey || e.metaKey;
        if (e.key === 'Enter' || (e.key === 'ArrowDown' && !e.shiftKey) || (e.key === 'Tab' && !e.shiftKey)) {
            e.preventDefault();
            editor.focus();
            NE.placeCaretAt(editor.firstElementChild || editor, false);
            updatePlaceholder();
        } else if (mod && ['b', 'i', 'u'].includes(e.key.toLowerCase())) {
            e.preventDefault();
        }
    });
    title.addEventListener('input', () => {
        if (title.innerHTML === '<br>') title.innerHTML = '';
        changed();
    });
    title.addEventListener('paste', e => {
        e.preventDefault();
        const text = (e.clipboardData && e.clipboardData.getData('text/plain')) || '';
        exec('insertText', text.replace(/\s+/g, ' ').trim());
    });
    title.addEventListener('drop', e => e.preventDefault());

    // ── Editor ─────────────────────────────────────────────────────────────

    editor.addEventListener('keydown', e => {
        if (e.isComposing) return;
        if (NE.handleSlashKey(e)) return;

        const mod = e.ctrlKey || e.metaKey;
        const key = e.key.toLowerCase();

        if (mod && !e.altKey) {
            let handled = true;
            if (!e.shiftKey && key === 'b') NE.inlineFormat('bold');
            else if (!e.shiftKey && key === 'i') NE.inlineFormat('italic');
            else if (!e.shiftKey && key === 'u') NE.inlineFormat('underline');
            else if (e.shiftKey && (key === 's' || key === 'x')) NE.inlineFormat('strikeThrough');
            else if (!e.shiftKey && key === 'e') NE.toggleInlineCode();
            else if (!e.shiftKey && key === 'k') NE.openLinkBox();
            else if (e.shiftKey && e.code === 'Digit7') NE.turnInto('numbered');
            else if (e.shiftKey && e.code === 'Digit8') NE.turnInto('bullet');
            else if (e.shiftKey && e.code === 'Digit9') NE.turnInto('todo');
            else if (e.key === 'Enter') { const li = closestTag(anchorElement(), 'LI'); if (li && li.parentElement.classList.contains('todo')) NE.toggleTodo(li); else handled = NE.handleEnter(e); }
            else handled = false;
            if (handled) { e.preventDefault(); NE.updateBubble(); return; }
        }

        if (mod && e.altKey && !e.shiftKey) {
            const map = { Digit0: 'text', Digit1: 'h1', Digit2: 'h2', Digit3: 'h3' };
            if (map[e.code]) { e.preventDefault(); NE.turnInto(map[e.code]); return; }
        }

        if (e.key === 'Tab') {
            e.preventDefault();
            NE.handleTab(e);
            return;
        }
        if (e.key === 'Enter' && !mod) {
            if (NE.handleEnter(e)) { e.preventDefault(); return; }
        }
        if (e.key === 'Backspace' && !mod) {
            if (NE.handleBackspace()) { e.preventDefault(); return; }
        }
        if (e.key === 'ArrowUp' && !e.shiftKey && !mod) {
            const first = editor.firstElementChild;
            const block = NE.currentBlock();
            if (block && first && (block === first || first.contains(block)) && NE.caretAtBlockStart(block)) {
                e.preventDefault();
                title.focus();
                NE.placeCaretAt(title, true);
            }
        }
    });

    editor.addEventListener('input', e => {
        if (e.inputType === 'insertText' && e.data === '/') NE.maybeOpenSlash();
        else if (NE.slash.open) NE.updateSlash();

        if (e.inputType === 'insertText' && (e.data === ' ' || e.data === '-')) NE.tryMarkdownShortcut(e.data);

        if (e.inputType === 'insertParagraph') {
            const li = closestTag(anchorElement(), 'LI');
            if (li && li.parentElement.classList.contains('todo') && NE.isEmptyBlock(li)) li.setAttribute('data-checked', 'false');
        }

        NE.ensureStructure();
        NE.ensureTrailingParagraph();
        updatePlaceholder();
        if (!NE.slash.open) NE.hideBubble();
        changed();
    });

    editor.addEventListener('mousedown', e => {
        const li = e.target.closest && e.target.closest('ul.todo > li');
        if (li && editor.contains(li)) {
            const rect = li.getBoundingClientRect();
            const size = parseFloat(getComputedStyle(li).fontSize) || 16;
            if (e.clientX < rect.left + size * 1.5 && e.clientY < rect.top + size * 1.8) {
                e.preventDefault();
                NE.toggleTodo(li);
                return;
            }
        }
        const link = e.target.closest && e.target.closest('a[href]');
        if (link && (e.ctrlKey || e.metaKey)) {
            e.preventDefault();
            NE.post({ type: 'open', url: link.getAttribute('href') });
        }
    });

    editor.addEventListener('mouseover', e => {
        const link = e.target.closest && e.target.closest('a[href]');
        if (link && !link.title) link.title = `${link.getAttribute('href')}\nCtrl+click to open`;
    });

    const IMAGE_MIME_TYPES = new Set(['image/png', 'image/jpeg', 'image/gif', 'image/webp', 'image/bmp']);

    function isSupportedImage(file) {
        return !!file && IMAGE_MIME_TYPES.has(file.type.toLowerCase());
    }

    function clipboardImageFile(data) {
        for (const item of Array.from(data.items || [])) {
            if (item.kind !== 'file' || !item.type.toLowerCase().startsWith('image/')) continue;
            const file = item.getAsFile();
            if (isSupportedImage(file)) return file;
        }
        return null;
    }

    function insertImageFile(file, range) {
        if (!isSupportedImage(file)) return false;
        const reader = new FileReader();
        reader.addEventListener('load', () => {
            const dataUrl = reader.result;
            if (typeof dataUrl !== 'string' || !/^data:image\/(?:png|jpeg|gif|webp|bmp);base64,[a-z0-9+/]+={0,2}$/i.test(dataUrl)) {
                console.error('The clipboard image could not be encoded as a supported image.');
                els.saveState.textContent = 'Image could not be inserted';
                return;
            }
            if (!editor.contains(range.commonAncestorContainer)) {
                console.error('The clipboard image insertion point is no longer in the editor.');
                els.saveState.textContent = 'Image could not be inserted';
                return;
            }
            editor.focus();
            NE.selectRange(range);
            exec('insertHTML', `<img src="${dataUrl}" alt="">`);
            afterPaste();
        });
        reader.addEventListener('error', () => {
            console.error('Failed to read the clipboard image.', reader.error);
            els.saveState.textContent = 'Image could not be inserted';
        });
        reader.addEventListener('abort', () => {
            console.error('Reading the clipboard image was aborted.');
            els.saveState.textContent = 'Image could not be inserted';
        });
        reader.readAsDataURL(file);
        return true;
    }

    editor.addEventListener('paste', e => {
        const data = e.clipboardData;
        if (!data) return;
        e.preventDefault();
        const inCode = closestTag(anchorElement(), 'PRE');
        const range = sel().rangeCount && NE.selectionInEditor() ? sel().getRangeAt(0).cloneRange() : null;
        const imageFile = clipboardImageFile(data);
        if (!inCode && imageFile && range) {
            insertImageFile(imageFile, range);
            return;
        }
        const html = data.getData('text/html');
        const text = data.getData('text/plain');
        if (!inCode && text && NE.isMarkdown(text)) {
            exec('insertHTML', NE.sanitizeToHtml(NE.markdownToHtml(text), false));
            afterPaste();
            return;
        }
        if (!inCode && html) {
            const clean = NE.sanitizeToHtml(html, false);
            if (clean.replace(/<[^>]*>/g, '').trim() || /<(?:hr|img)\b/i.test(clean)) {
                exec('insertHTML', clean);
                afterPaste();
                return;
            }
        }
        if (!text) return;
        if (inCode || !/\r?\n/.test(text)) exec('insertText', text);
        else exec('insertHTML', text.split(/\r?\n/).map(line => `<p>${NE.escapeHtml(line) || '<br>'}</p>`).join(''));
        afterPaste();
    });

    function afterPaste() {
        editor.querySelectorAll('p > table').forEach(table => {
            const paragraph = table.parentElement;
            if (paragraph.childNodes.length === 1) paragraph.replaceWith(table);
        });
        disableNativeImageDragging();
        NE.ensureStructure();
        NE.ensureTrailingParagraph();
        updatePlaceholder();
        changed();
    }

    function disableNativeImageDragging() {
        editor.querySelectorAll('img').forEach(image => { image.draggable = false; });
    }

    function caretRangeAtPoint(x, y) {
        const range = document.caretRangeFromPoint?.(x, y);
        if (range && editor.contains(range.commonAncestorContainer)) return range;

        const position = document.caretPositionFromPoint?.(x, y);
        if (position && editor.contains(position.offsetNode)) {
            const caret = document.createRange();
            caret.setStart(position.offsetNode, position.offset);
            caret.collapse(true);
            return caret;
        }

        const blocks = Array.from(editor.children);
        if (blocks.length === 0) return null;
        const target = blocks.find(block => y <= block.getBoundingClientRect().bottom) || blocks[blocks.length - 1];
        const caret = document.createRange();
        caret.selectNode(target);
        caret.collapse(y > target.getBoundingClientRect().top + target.getBoundingClientRect().height / 2);
        return caret;
    }

    function moveImageToPoint(image, x, y) {
        const range = caretRangeAtPoint(x, y);
        if (!range) return;

        if (range.intersectsNode(image)) {
            const rect = image.getBoundingClientRect();
            range.selectNode(image);
            range.collapse(y > rect.top + rect.height / 2);
        }

        const sourceBlock = image.parentElement;
        image.remove();
        range.insertNode(image);
        if (sourceBlock?.parentElement === editor &&
            NE.isEmptyBlock(sourceBlock) &&
            editor.childElementCount > 1)
            sourceBlock.remove();

        const caret = document.createRange();
        caret.setStartAfter(image);
        caret.collapse(true);
        editor.focus();
        NE.selectRange(caret);
        afterPaste();
    }

    let imagePointerDrag = null;

    function clearImagePointerDrag() {
        if (!imagePointerDrag) return;
        imagePointerDrag.image.classList.remove('pointer-dragging');
        document.body.classList.remove('image-dragging');
        imagePointerDrag = null;
    }

    editor.addEventListener('pointerdown', e => {
        if (e.button !== 0 || e.pointerType === 'touch') return;
        const image = e.target instanceof Element ? e.target.closest('img') : null;
        if (!image || !editor.contains(image)) return;

        e.preventDefault();
        imagePointerDrag = {
            image,
            pointerId: e.pointerId,
            startX: e.clientX,
            startY: e.clientY,
            dragging: false,
        };
    });

    document.addEventListener('pointermove', e => {
        if (!imagePointerDrag || e.pointerId !== imagePointerDrag.pointerId) return;
        const drag = imagePointerDrag;
        if (!drag.dragging && Math.hypot(e.clientX - drag.startX, e.clientY - drag.startY) < 5) return;

        drag.dragging = true;
        drag.image.classList.add('pointer-dragging');
        document.body.classList.add('image-dragging');
        e.preventDefault();
    }, true);

    document.addEventListener('pointerup', e => {
        if (!imagePointerDrag || e.pointerId !== imagePointerDrag.pointerId) return;
        const drag = imagePointerDrag;
        const target = document.elementFromPoint(e.clientX, e.clientY);
        if (drag.dragging && state.noteId && target && editor.contains(target))
            moveImageToPoint(drag.image, e.clientX, e.clientY);
        clearImagePointerDrag();
    }, true);

    document.addEventListener('pointercancel', clearImagePointerDrag, true);
    window.addEventListener('blur', clearImagePointerDrag);

    // External drops go through the sanitizer; native internal text drags stay unchanged.
    let internalDrag = false;
    editor.addEventListener('dragstart', () => { internalDrag = true; });
    document.addEventListener('dragend', () => { internalDrag = false; });
    document.addEventListener('dragover', e => { if (!internalDrag) e.preventDefault(); });
    document.addEventListener('drop', e => {
        if (internalDrag) return;
        e.preventDefault();
        if (!editor.contains(e.target) || !state.noteId) return;
        const range = document.caretRangeFromPoint(e.clientX, e.clientY);
        if (!range) return;
        editor.focus();
        NE.selectRange(range);
        const imageFile = Array.from(e.dataTransfer.files || []).find(isSupportedImage);
        if (imageFile) {
            insertImageFile(imageFile, range.cloneRange());
            return;
        }
        const html = e.dataTransfer.getData('text/html');
        const text = e.dataTransfer.getData('text/plain');
        if (html) exec('insertHTML', NE.sanitizeToHtml(html, false));
        else if (text) exec('insertText', text);
        afterPaste();
    }, true);

    editor.addEventListener('blur', () => {
        setTimeout(() => {
            if (!NE.isLinkBoxOpen() && document.activeElement !== editor) NE.hideBubble();
            updatePlaceholder();
        }, 0);
        flush();
    });
    title.addEventListener('blur', flush);
    window.addEventListener('blur', flush);
    document.addEventListener('visibilitychange', flush);

    // ── Selection-driven UI (bubble toolbar, slash menu, placeholders) ─────

    let mouseDown = false;
    let paintSelectionOnMouseUp = false;
    let bubbleTimer = 0;

    document.addEventListener('mousedown', e => {
        if (NE.isInsidePopup(e.target)) return;
        mouseDown = true;
        paintSelectionOnMouseUp = NE.isStylePaintActive() && editor.contains(e.target);
        NE.closeAll();
    });
    document.addEventListener('mouseup', () => {
        mouseDown = false;
        const shouldPaint = paintSelectionOnMouseUp;
        paintSelectionOnMouseUp = false;
        if (shouldPaint && NE.selectionInEditor() && !sel().isCollapsed)
            NE.applyCopiedStyle();
        setTimeout(NE.updateBubble, 0);
    });
    document.addEventListener('selectionchange', () => {
        if (NE.slash.open) NE.updateSlash();
        updatePlaceholder();
        if (mouseDown) return;
        clearTimeout(bubbleTimer);
        bubbleTimer = setTimeout(NE.updateBubble, 120);
    });
    els.scroller.addEventListener('scroll', () => {
        NE.positionSlash();
        if (!els.bubble.classList.contains('hidden')) NE.hideBubble();
    });
    window.addEventListener('resize', () => NE.closeAll());

    // Clicking the empty area below the content puts the caret at the end.
    els.scroller.addEventListener('mousedown', e => {
        if (e.target !== els.scroller && e.target !== document.getElementById('page')) return;
        if (!state.noteId) return;
        e.preventDefault();
        editor.focus();
        NE.placeCaretAt(editor.lastElementChild || editor, true);
        updatePlaceholder();
    });

    // ── Global shortcuts ───────────────────────────────────────────────────

    document.addEventListener('keydown', e => {
        const mod = e.ctrlKey || e.metaKey;
        const key = e.key.toLowerCase();
        if (e.key === 'Escape') {
            if (NE.isLinkBoxOpen()) return; // the link input restores the selection itself
            e.preventDefault();
            if (NE.isStylePaintActive()) {
                NE.cancelStylePaint();
                return;
            }
            if (NE.anyPopupOpen()) {
                const hadSelection = !sel().isCollapsed;
                NE.closeAll();
                if (hadSelection && NE.selectionInEditor()) sel().collapseToEnd();
                return;
            }
            flush();
            NE.post({ type: 'escape' });
            return;
        }
        if (mod && !e.shiftKey && !e.altKey && key === 's') { e.preventDefault(); flush(); return; }
        if (mod && !e.shiftKey && !e.altKey && key === 'n') { e.preventDefault(); flush(); NE.post({ type: 'newNote' }); return; }
        // Block browser shortcuts that make no sense inside the overlay.
        if (mod && !e.altKey && ['p', 'f', 'g', 'h', 'j', 'o', 'r', 'd', 'l'].includes(key)) e.preventDefault();
        if (e.key === 'F5' || e.key === 'F3' || e.key === 'F7') e.preventDefault();
    }, true);

    document.addEventListener('contextmenu', e => {
        if (!editor.contains(e.target) && !title.contains(e.target)) e.preventDefault();
    });

    load({ id: null, title: '', html: '' });
    NE.post({ type: 'ready' });
})();
