// "/" command menu, floating selection toolbar and its popovers.
'use strict';

(() => {
    const { els, sel, exec, anchorElement, closestTag, placePopover, escapeHtml } = NE;
    const editor = els.editor;

    // ── Command catalogue (also used by the "Turn into" menu) ─────────────

    const BLOCK_COMMANDS = [
        { id: 'text', label: 'Text', desc: 'Just start writing with plain text', icon: 'T', kw: 'paragraph plain normal texto parrafo' },
        { id: 'h1', label: 'Heading 1', desc: 'Big section heading', icon: 'H1', kw: 'title h1 # titulo encabezado' },
        { id: 'h2', label: 'Heading 2', desc: 'Medium section heading', icon: 'H2', kw: 'subtitle h2 ## titulo encabezado' },
        { id: 'h3', label: 'Heading 3', desc: 'Small section heading', icon: 'H3', kw: 'h3 ### titulo encabezado' },
        { id: 'bullet', label: 'Bulleted list', desc: 'Create a simple bulleted list', icon: '•', kw: 'ul unordered list lista vinetas' },
        { id: 'numbered', label: 'Numbered list', desc: 'Create a list with numbering', icon: '1.', kw: 'ol ordered list lista numerada' },
        { id: 'todo', label: 'To-do list', desc: 'Track tasks with a checklist', icon: '☑', kw: 'todo checkbox task checklist tareas lista' },
        { id: 'quote', label: 'Quote', desc: 'Capture a quote', icon: '❝', kw: 'blockquote citation cita' },
        { id: 'code', label: 'Code', desc: 'Capture a code snippet', icon: '</>', kw: 'pre codeblock snippet codigo' },
    ];

    function buildSlashCommands() {
        const list = [];
        BLOCK_COMMANDS.forEach(c => list.push({ ...c, section: 'Basic blocks', block: true, run: () => NE.turnInto(c.id) }));
        list.push({ id: 'divider', section: 'Basic blocks', label: 'Divider', desc: 'Visually divide blocks', icon: '—', kw: 'hr line separator rule divisor linea', run: () => NE.insertDivider() });

        list.push(
            { id: 'bold', section: 'Style', label: 'Bold', desc: 'Make text bold', icon: 'B', shortcut: 'Ctrl+B', kw: 'strong negrita', run: () => NE.inlineFormat('bold') },
            { id: 'italic', section: 'Style', label: 'Italic', desc: 'Make text italic', icon: 'i', shortcut: 'Ctrl+I', kw: 'em cursiva', run: () => NE.inlineFormat('italic') },
            { id: 'underline', section: 'Style', label: 'Underline', desc: 'Underline text', icon: 'U', shortcut: 'Ctrl+U', kw: 'subrayado', run: () => NE.inlineFormat('underline') },
            { id: 'strike', section: 'Style', label: 'Strikethrough', desc: 'Cross text out', icon: 'S', shortcut: 'Ctrl+Shift+S', kw: 'strike tachado', run: () => NE.inlineFormat('strikeThrough') },
        );

        NE.TEXT_COLORS.forEach(c => list.push({
            id: `color-${c.id}`, section: 'Text color', label: c.value ? `${c.label} text` : 'Default text',
            desc: '', icon: 'A', iconStyle: c.value ? `color:${c.value}` : '', kw: `color colour texto ${c.id}`,
            run: () => NE.applyTextColor(c.value),
        }));
        NE.BG_COLORS.forEach(c => list.push({
            id: `bg-${c.id}`, section: 'Background', label: c.value ? `${c.label} background` : 'Default background',
            desc: '', icon: 'A', iconStyle: c.value ? `background:${c.value}` : '', kw: `highlight background fondo resaltar marcador ${c.id}`,
            run: () => NE.applyBackgroundColor(c.value),
        }));

        list.push(
            { id: 'date', section: 'Insert', label: 'Date', desc: "Insert today's date", icon: '📅', kw: 'today fecha hoy', run: () => NE.insertDate(false) },
            { id: 'time', section: 'Insert', label: 'Time', desc: 'Insert the current time', icon: '🕒', kw: 'now hora', run: () => NE.insertDate(true) },
        );
        return list;
    }

    let SLASH_COMMANDS = null;
    const slash = { open: false, node: null, offset: 0, query: '', items: [], index: 0, lastHitLength: 0 };

    function normalize(text) {
        return text.toLowerCase().normalize('NFD').replace(/[\u0300-\u036f]/g, '');
    }

    function filterCommands(query) {
        const q = normalize(query.trim());
        if (!q) return SLASH_COMMANDS;
        const terms = q.split(/\s+/);
        return SLASH_COMMANDS.filter(c => {
            const hay = normalize(`${c.label} ${c.kw || ''} ${c.section}`);
            return terms.every(t => hay.includes(t));
        });
    }

    function menuItemHtml(c, index, active) {
        return `<div class="menu-item${active ? ' active' : ''}" data-index="${index}">
            <span class="menu-icon${c.desc ? '' : ' small'}" style="${c.iconStyle || ''}">${escapeHtml(c.icon)}</span>
            <span class="menu-text"><span class="menu-label">${escapeHtml(c.label)}</span>${c.desc ? `<span class="menu-desc">${escapeHtml(c.desc)}</span>` : ''}</span>
            ${c.shortcut ? `<span class="menu-shortcut">${c.shortcut}</span>` : ''}
        </div>`;
    }

    function renderSlash() {
        const menu = els.slashMenu;
        if (!slash.items.length) {
            menu.innerHTML = '<div class="menu-empty">No results</div>';
            return;
        }
        let html = '';
        let section = null;
        slash.items.forEach((c, i) => {
            if (c.section !== section) {
                section = c.section;
                html += `<div class="menu-section">${escapeHtml(section)}</div>`;
            }
            html += menuItemHtml(c, i, i === slash.index);
        });
        menu.innerHTML = html;
        const active = menu.querySelector('.menu-item.active');
        if (active) active.scrollIntoView({ block: 'nearest' });
    }

    function slashAnchorRect() {
        try {
            const r = document.createRange();
            r.setStart(slash.node, slash.offset);
            r.setEnd(slash.node, slash.offset + 1);
            return r.getBoundingClientRect();
        } catch {
            return NE.caretRect();
        }
    }

    function positionSlash() {
        if (!slash.open) return;
        placePopover(els.slashMenu, slashAnchorRect());
    }

    /** Opens the menu if a "/" was just typed at the start of a line or after a space. */
    function maybeOpenSlash() {
        const s = sel();
        if (!s.isCollapsed) return;
        const node = s.anchorNode;
        const offset = s.anchorOffset;
        if (!node || node.nodeType !== Node.TEXT_NODE || offset < 1 || node.data[offset - 1] !== '/') return;
        if (closestTag(node.parentNode, 'PRE') || closestTag(node.parentNode, 'CODE')) return;

        const block = NE.currentBlock() || editor;
        const before = NE.textBeforeCaret(block);
        const prev = before.length >= 2 ? before[before.length - 2] : '';
        if (prev && !/\s/.test(prev)) return;

        SLASH_COMMANDS = SLASH_COMMANDS || buildSlashCommands();
        Object.assign(slash, { open: true, node, offset: offset - 1, query: '', index: 0, lastHitLength: 0 });
        slash.items = filterCommands('');
        hideBubble();
        renderSlash();
        positionSlash();
    }

    function updateSlash() {
        if (!slash.open) return;
        const s = sel();
        if (!s.isCollapsed || s.anchorNode !== slash.node || s.anchorOffset <= slash.offset ||
            !slash.node.isConnected || slash.node.data[slash.offset] !== '/') {
            closeSlash();
            return;
        }
        const query = slash.node.data.slice(slash.offset + 1, s.anchorOffset);
        if (query === slash.query) return;
        if (query.length > 30 || /^\s/.test(query) || /\s\s/.test(query)) { closeSlash(); return; }

        slash.query = query;
        slash.items = filterCommands(query);
        if (slash.items.length) slash.lastHitLength = query.length;
        else if (query.length - slash.lastHitLength >= 4) { closeSlash(); return; }
        slash.index = 0;
        renderSlash();
        positionSlash();
    }

    function closeSlash() {
        slash.open = false;
        els.slashMenu.classList.add('hidden');
    }

    function runSlashCommand(command) {
        const s = sel();
        const end = s.anchorNode === slash.node ? s.anchorOffset : slash.offset + 1 + slash.query.length;
        const range = document.createRange();
        range.setStart(slash.node, slash.offset);
        range.setEnd(slash.node, Math.min(end, slash.node.length));
        closeSlash();
        editor.focus();
        NE.selectRange(range);
        exec('delete');
        // Like Notion: a block command typed on a line that already has text creates a new block.
        if (command.block) {
            const block = NE.currentBlock();
            if (block && !NE.isEmptyBlock(block)) {
                NE.placeCaretAt(block, true);
                exec('insertParagraph');
            }
        }
        command.run();
        NE.changed();
    }

    /** Keyboard navigation while the menu is open; returns true when the key was consumed. */
    function handleSlashKey(e) {
        if (!slash.open) return false;
        const count = slash.items.length;
        switch (e.key) {
            case 'ArrowDown':
                if (count) { slash.index = (slash.index + 1) % count; renderSlash(); }
                break;
            case 'ArrowUp':
                if (count) { slash.index = (slash.index - 1 + count) % count; renderSlash(); }
                break;
            case 'Enter':
            case 'Tab':
                if (!count) { closeSlash(); return false; }
                runSlashCommand(slash.items[slash.index]);
                break;
            default:
                return false;
        }
        e.preventDefault();
        return true;
    }

    els.slashMenu.addEventListener('mousedown', e => {
        e.preventDefault();
        const item = e.target.closest('.menu-item');
        if (item) runSlashCommand(slash.items[Number(item.dataset.index)]);
    });
    els.slashMenu.addEventListener('mousemove', e => {
        const item = e.target.closest('.menu-item');
        if (!item) return;
        const index = Number(item.dataset.index);
        if (index !== slash.index) {
            slash.index = index;
            els.slashMenu.querySelectorAll('.menu-item').forEach(el => el.classList.toggle('active', Number(el.dataset.index) === index));
        }
    });

    // ── Bubble toolbar ─────────────────────────────────────────────────────

    let savedRange = null;
    let copiedTextStyle = null;
    let stylePaintActive = false;
    const subMenus = () => [els.turnMenu, els.colorMenu, els.linkBox];
    const isOpen = el => !el.classList.contains('hidden');

    const PAINTED_PROPERTIES = [
        ['color', 'color'],
        ['background-color', 'backgroundColor'],
        ['font-family', 'fontFamily'],
        ['font-size', 'fontSize'],
        ['font-weight', 'fontWeight'],
        ['font-style', 'fontStyle'],
        ['letter-spacing', 'letterSpacing'],
        ['text-transform', 'textTransform'],
    ];

    function decorationSource(element) {
        for (let node = element; node && node !== editor.parentElement; node = node.parentElement) {
            const style = getComputedStyle(node);
            if (style.textDecorationLine !== 'none') return style;
            if (/^(P|H1|H2|H3|LI|BLOCKQUOTE|PRE)$/.test(node.tagName)) break;
        }
        return getComputedStyle(element);
    }

    function backgroundSource(element) {
        for (let node = element; node && node !== editor; node = node.parentElement) {
            const style = getComputedStyle(node);
            if (style.backgroundColor !== 'transparent' && !/^rgba\([^)]*,\s*0(?:\.0+)?\)$/.test(style.backgroundColor))
                return style;
        }
        return getComputedStyle(element);
    }

    function selectedTextNodes(range) {
        const walker = document.createTreeWalker(editor, NodeFilter.SHOW_TEXT);
        const result = [];
        let node;
        while ((node = walker.nextNode())) {
            if (!node.length || !range.intersectsNode(node)) continue;
            const start = node === range.startContainer ? range.startOffset : 0;
            const end = node === range.endContainer ? range.endOffset : node.length;
            if (end > start) result.push({ node, start, end });
        }
        return result;
    }

    function captureSelectedStyle() {
        const selection = sel();
        if (!selection.rangeCount || selection.isCollapsed || !NE.selectionInEditor()) return null;
        const range = selection.getRangeAt(0);
        const firstText = selectedTextNodes(range)[0]?.node;
        if (!firstText) return null;

        const element = firstText.parentElement;
        const computed = getComputedStyle(element);
        const background = backgroundSource(element);
        const decoration = decorationSource(element);
        const style = {};
        for (const [property, key] of PAINTED_PROPERTIES) {
            style[property] = property === 'background-color' ? background.backgroundColor : computed[key];
        }
        style['text-decoration-line'] = decoration.textDecorationLine;
        style['text-decoration-color'] = decoration.textDecorationColor;
        style['text-decoration-style'] = decoration.textDecorationStyle;
        style['text-decoration-thickness'] = decoration.textDecorationThickness;
        return style;
    }

    function setStylePaintActive(active) {
        stylePaintActive = active;
        const button = els.bubble.querySelector('[data-action="copy-style"]');
        button.classList.toggle('active', active);
        button.setAttribute('aria-pressed', String(active));
        button.title = active ? 'Desactivar copiar estilo (Esc)' : 'Copiar estilo';
    }

    function toggleStylePaint() {
        if (stylePaintActive) {
            setStylePaintActive(false);
            copiedTextStyle = null;
            return;
        }
        const style = captureSelectedStyle();
        if (!style) return;
        copiedTextStyle = style;
        setStylePaintActive(true);
    }

    function applyCopiedStyle() {
        if (!stylePaintActive || !copiedTextStyle || !NE.selectionInEditor()) return false;
        const selection = sel();
        if (!selection.rangeCount || selection.isCollapsed) return false;

        exec('removeFormat');
        const range = selection.getRangeAt(0).cloneRange();
        const segments = selectedTextNodes(range);
        if (!segments.length) return false;

        const spans = [];
        for (const segment of segments) {
            const { node, start, end } = segment;
            if (end < node.length) node.splitText(end);
            const selectedNode = start > 0 ? node.splitText(start) : node;
            const span = document.createElement('span');
            for (const [property, value] of Object.entries(copiedTextStyle))
                span.style.setProperty(property, value);
            selectedNode.parentNode.insertBefore(span, selectedNode);
            span.appendChild(selectedNode);
            spans.push(span);
        }

        const firstText = spans[0].firstChild;
        const lastText = spans[spans.length - 1].firstChild;
        const paintedRange = document.createRange();
        paintedRange.setStart(firstText, 0);
        paintedRange.setEnd(lastText, lastText.length);
        NE.selectRange(paintedRange);
        NE.changed();
        return true;
    }

    function closeSubMenus() {
        subMenus().forEach(el => el.classList.add('hidden'));
    }

    function hideBubble() {
        els.bubble.classList.add('hidden');
        closeSubMenus();
    }

    function positionBubble(range) {
        const bubble = els.bubble;
        const rects = range.getClientRects();
        const rect = range.getBoundingClientRect();
        const first = rects[0] || rect;
        bubble.classList.remove('hidden');
        const w = bubble.offsetWidth;
        const h = bubble.offsetHeight;
        let top = first.top - h - 8;
        if (top < 48) top = rect.bottom + 8;
        let left = (rects.length > 1 ? first.left : rect.left + rect.width / 2 - w / 2);
        left = Math.min(Math.max(8, left), window.innerWidth - w - 8);
        bubble.style.top = `${top}px`;
        bubble.style.left = `${left}px`;
    }

    function refreshBubbleState() {
        const anchor = anchorElement();
        const set = (action, on) => {
            const btn = els.bubble.querySelector(`[data-action="${action}"]`);
            if (btn) btn.classList.toggle('active', !!on);
        };
        set('bold', document.queryCommandState('bold'));
        set('italic', document.queryCommandState('italic'));
        set('underline', document.queryCommandState('underline'));
        set('strike', document.queryCommandState('strikeThrough'));
        set('code', closestTag(anchor, 'CODE'));
        set('link', closestTag(anchor, 'A'));
        const type = NE.blockType();
        const cmd = BLOCK_COMMANDS.find(c => c.id === type);
        els.turnLabel.textContent = cmd ? cmd.label : 'Text';
        const color = anchor ? getComputedStyle(anchor).color : '';
        els.colorSwatch.style.textDecorationColor = color;
    }

    function updateBubble() {
        if (slash.open || subMenus().some(isOpen)) return;
        const s = sel();
        if (!s.rangeCount || s.isCollapsed || !NE.selectionInEditor() || !s.toString().trim()) {
            hideBubble();
            return;
        }
        refreshBubbleState();
        positionBubble(s.getRangeAt(0));
    }

    function openTurnMenu(button) {
        const current = NE.blockType();
        els.turnMenu.innerHTML = '<div class="menu-section">Turn into</div>' + BLOCK_COMMANDS.map((c, i) =>
            `<div class="menu-item${c.id === current ? ' checked' : ''}" data-index="${i}">
                <span class="menu-icon small">${escapeHtml(c.icon)}</span>
                <span class="menu-label">${escapeHtml(c.label)}</span>
                ${c.id === current ? '<span class="menu-shortcut">✓</span>' : ''}
            </div>`).join('');
        placePopover(els.turnMenu, button.getBoundingClientRect());
    }

    function openColorMenu(button) {
        const swatch = (c, i, kind) => {
            const style = kind === 'bg' ? (c.value ? `background:${c.value}` : '') : (c.value ? `color:${c.value}` : '');
            const title = kind === 'bg' ? `${c.label} background` : `${c.label} text`;
            return `<div class="swatch${c.value ? '' : ' default'}" data-kind="${kind}" data-index="${i}" title="${title}" style="${style}">A</div>`;
        };
        els.colorMenu.innerHTML =
            '<div class="menu-section">Text color</div>' +
            `<div class="swatches">${NE.TEXT_COLORS.map((c, i) => swatch(c, i, 'fg')).join('')}</div>` +
            '<div class="menu-section">Background</div>' +
            `<div class="swatches">${NE.BG_COLORS.map((c, i) => swatch(c, i, 'bg')).join('')}</div>`;
        placePopover(els.colorMenu, button.getBoundingClientRect(), 'right');
    }

    function openLinkBox() {
        const s = sel();
        if (!s.rangeCount || s.isCollapsed || !NE.selectionInEditor()) return;
        savedRange = s.getRangeAt(0).cloneRange();
        const link = closestTag(anchorElement(), 'A');
        els.linkInput.value = link ? link.getAttribute('href') || '' : '';
        if (els.bubble.classList.contains('hidden')) positionBubble(savedRange);
        closeSubMenus();
        placePopover(els.linkBox, els.bubble.getBoundingClientRect());
        els.linkInput.focus();
        els.linkInput.select();
    }

    function closeLinkBox(restore) {
        els.linkBox.classList.add('hidden');
        if (restore && savedRange) {
            editor.focus();
            NE.selectRange(savedRange);
        }
    }

    els.linkInput.addEventListener('keydown', e => {
        if (e.key === 'Enter') {
            e.preventDefault();
            const range = savedRange;
            closeLinkBox(false);
            if (range) NE.applyLink(range, els.linkInput.value);
            hideBubble();
        } else if (e.key === 'Escape') {
            e.preventDefault();
            e.stopPropagation();
            closeLinkBox(true);
        }
    });

    els.bubble.addEventListener('mousedown', e => {
        e.preventDefault();
        const button = e.target.closest('button');
        if (!button) return;
        const action = button.dataset.action;
        switch (action) {
            case 'bold': NE.inlineFormat('bold'); break;
            case 'italic': NE.inlineFormat('italic'); break;
            case 'underline': NE.inlineFormat('underline'); break;
            case 'strike': NE.inlineFormat('strikeThrough'); break;
            case 'code': NE.toggleInlineCode(); break;
            case 'clear': NE.clearFormatting(); break;
            case 'copy-style': toggleStylePaint(); return;
            case 'link': openLinkBox(); return;
            case 'turn-into':
                if (isOpen(els.turnMenu)) closeSubMenus(); else { closeSubMenus(); openTurnMenu(button); }
                return;
            case 'color':
                if (isOpen(els.colorMenu)) closeSubMenus(); else { closeSubMenus(); openColorMenu(button); }
                return;
        }
        refreshBubbleState();
        const s = sel();
        if (s.rangeCount && !s.isCollapsed) positionBubble(s.getRangeAt(0));
    });

    els.turnMenu.addEventListener('mousedown', e => {
        e.preventDefault();
        const item = e.target.closest('.menu-item');
        if (!item) return;
        NE.turnInto(BLOCK_COMMANDS[Number(item.dataset.index)].id);
        closeSubMenus();
        updateBubble();
    });

    els.colorMenu.addEventListener('mousedown', e => {
        e.preventDefault();
        const item = e.target.closest('.swatch');
        if (!item) return;
        const index = Number(item.dataset.index);
        if (item.dataset.kind === 'fg') NE.applyTextColor(NE.TEXT_COLORS[index].value);
        else NE.applyBackgroundColor(NE.BG_COLORS[index].value);
        closeSubMenus();
        updateBubble();
    });

    // ── Font menu (top bar) ────────────────────────────────────────────────

    const FONTS = [
        { id: 'handwritten', label: 'Handwritten' },
        { id: 'sans', label: 'Sans' },
        { id: 'serif', label: 'Serif' },
        { id: 'mono', label: 'Mono' },
    ];

    function setFont(id, notify) {
        if (!FONTS.some(f => f.id === id)) id = 'handwritten';
        NE.state.font = id;
        document.body.className = `font-${id}`;
        if (notify) NE.changed();
    }

    function openFontMenu() {
        els.fontMenu.innerHTML = '<div class="menu-section">Font</div>' + FONTS.map(f =>
            `<div class="menu-item${f.id === NE.state.font ? ' checked' : ''}" data-font="${f.id}">
                <span class="menu-label font-sample-${f.id}">${f.label}</span>
                ${f.id === NE.state.font ? '<span class="menu-shortcut">✓</span>' : ''}
            </div>`).join('');
        placePopover(els.fontMenu, els.fontButton.getBoundingClientRect(), 'right');
    }

    els.fontButton.addEventListener('mousedown', e => {
        e.preventDefault();
        if (isOpen(els.fontMenu)) els.fontMenu.classList.add('hidden'); else openFontMenu();
    });
    els.fontMenu.addEventListener('mousedown', e => {
        e.preventDefault();
        const item = e.target.closest('.menu-item');
        if (!item) return;
        setFont(item.dataset.font, true);
        els.fontMenu.classList.add('hidden');
    });

    function anyPopupOpen() {
        return slash.open || [els.turnMenu, els.colorMenu, els.linkBox, els.fontMenu].some(isOpen) || isOpen(els.bubble);
    }

    function closeAll() {
        closeSlash();
        hideBubble();
        els.fontMenu.classList.add('hidden');
    }

    function isInsidePopup(target) {
        return !!(target && target.closest && target.closest('.popover, #bubble, #font-button'));
    }

    Object.assign(NE, {
        slash, maybeOpenSlash, updateSlash, closeSlash, positionSlash, handleSlashKey,
        updateBubble, hideBubble, openLinkBox, setFont, anyPopupOpen, closeAll, isInsidePopup,
        isLinkBoxOpen: () => isOpen(els.linkBox),
        toggleStylePaint, isStylePaintActive: () => stylePaintActive,
        applyCopiedStyle,
        cancelStylePaint: () => {
            copiedTextStyle = null;
            setStylePaintActive(false);
        },
    });
})();
