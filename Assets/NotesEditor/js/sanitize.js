// Whitelist-based HTML sanitizer used for pasted content and for notes loaded from disk.
'use strict';

(() => {
    const ALLOWED = {
        B: [], STRONG: [], I: [], EM: [], U: [], S: [], STRIKE: [], DEL: [], CODE: [], MARK: [],
        A: ['href'], H1: [], H2: [], H3: [], P: [], BR: [], UL: ['class'], OL: ['start'], LI: ['data-checked'],
        IMG: ['src', 'alt'],
        BLOCKQUOTE: [], PRE: [], HR: [], SPAN: [], FONT: ['color'],
        TABLE: [], THEAD: [], TBODY: [], TR: [], TH: [], TD: [], DIV: [],
    };
    const IMAGE_DATA_URL = /^data:image\/(?:png|jpeg|gif|webp|bmp);base64,[a-z0-9+/]+={0,2}$/i;
    const RENAME = { H4: 'H3', H5: 'H3', H6: 'H3', INS: 'U', TT: 'CODE', KBD: 'CODE', SAMP: 'CODE' };
    const DROP = new Set(['SCRIPT', 'STYLE', 'IFRAME', 'OBJECT', 'EMBED', 'META', 'LINK', 'TITLE', 'NOSCRIPT',
        'TEMPLATE', 'SVG', 'MATH', 'CANVAS', 'VIDEO', 'AUDIO', 'INPUT', 'BUTTON', 'SELECT', 'TEXTAREA', 'FORM', 'HEAD']);
    const BLOCK_CHILDREN = 'p, div, h1, h2, h3, h4, h5, h6, ul, ol, li, blockquote, pre, table, hr';

    function cleanStyle(style, keepColors) {
        if (!style) return '';
        const probe = document.createElement('span');
        probe.setAttribute('style', style);
        const props = keepColors
            ? ['color', 'background-color', 'font-family', 'font-size', 'font-weight', 'font-style',
                'text-decoration-line', 'text-decoration-color', 'text-decoration-style',
                'text-decoration-thickness', 'letter-spacing', 'text-transform']
            : ['font-weight', 'font-style', 'text-decoration-line'];
        const out = [];
        for (const prop of props) {
            const value = probe.style.getPropertyValue(prop);
            if (value && !/url\(|expression|var\(/i.test(value)) out.push(`${prop}: ${value}`);
        }
        return out.join('; ');
    }

    function cleanTree(src, dst, keepColors) {
        for (const child of Array.from(src.childNodes)) {
            if (child.nodeType === Node.TEXT_NODE) {
                dst.appendChild(document.createTextNode(child.data));
                continue;
            }
            if (child.nodeType !== Node.ELEMENT_NODE) continue;

            let tag = child.tagName.toUpperCase();
            if (DROP.has(tag)) continue;
            tag = RENAME[tag] || tag;

            // Pasted <div>s become paragraphs unless they only wrap other blocks.
            if (tag === 'DIV' && !keepColors) {
                if (child.querySelector(BLOCK_CHILDREN)) { cleanTree(child, dst, keepColors); continue; }
                tag = 'P';
            }

            // Google Docs wraps everything in <b style="font-weight:normal">.
            if ((tag === 'B' || tag === 'STRONG') && /font-weight:\s*(normal|[1-4]00)/i.test(child.getAttribute('style') || '')) {
                cleanTree(child, dst, keepColors);
                continue;
            }

            if (!ALLOWED[tag] || (tag === 'FONT' && !keepColors)) {
                cleanTree(child, dst, keepColors);
                if (/^(TR|TABLE|TD|TH|SECTION|ARTICLE)$/.test(tag)) dst.appendChild(document.createElement('br'));
                continue;
            }

            const el = document.createElement(tag);
            for (const attr of ALLOWED[tag]) {
                const raw = child.getAttribute(attr);
                if (raw == null) continue;
                const value = raw.trim();
                if (attr === 'href') {
                    if (/^(?:https?:|mailto:|\.{1,2}\/|#)/i.test(value)) el.setAttribute('href', value);
                } else if (attr === 'src' && tag === 'IMG') {
                    if (IMAGE_DATA_URL.test(value)) el.setAttribute('src', value);
                } else if (attr === 'alt' && tag === 'IMG') {
                    el.setAttribute('alt', value);
                } else if (attr === 'class') {
                    if (child.classList.contains('todo')) el.className = 'todo';
                } else if (attr === 'data-checked') {
                    el.setAttribute('data-checked', value === 'true' ? 'true' : 'false');
                } else if (attr === 'start' && tag === 'OL' && /^[1-9]\d*$/.test(value)) {
                    el.setAttribute('start', value);
                } else if (attr === 'color') {
                    if (/^#?[0-9a-z]{3,8}$/i.test(value)) el.setAttribute('color', value);
                }
            }
            const style = cleanStyle(child.getAttribute('style'), keepColors);
            if (style) el.setAttribute('style', style);

            if (tag === 'A' && !el.hasAttribute('href')) {
                cleanTree(child, dst, keepColors);
                continue;
            }
            if (tag === 'IMG' && !el.hasAttribute('src')) continue;

            cleanTree(child, el, keepColors);
            if (tag === 'SPAN' && !el.getAttribute('style')) {
                while (el.firstChild) dst.appendChild(el.firstChild);
                continue;
            }
            dst.appendChild(el);
        }
    }

    /**
     * Returns a sanitized DocumentFragment. keepColors=true preserves inline color styles
     * (used for the editor's own saved HTML); pasted content drops them so foreign
     * dark-on-light colors don't become unreadable on the dark page.
     */
    function sanitize(html, keepColors) {
        const doc = new DOMParser().parseFromString(`<body>${html}</body>`, 'text/html');
        const fragment = document.createDocumentFragment();
        cleanTree(doc.body, fragment, keepColors);
        return fragment;
    }

    function sanitizeToHtml(html, keepColors) {
        const holder = document.createElement('div');
        holder.appendChild(sanitize(html, keepColors));
        return holder.innerHTML;
    }

    NE.sanitize = sanitize;
    NE.sanitizeToHtml = sanitizeToHtml;
})();
