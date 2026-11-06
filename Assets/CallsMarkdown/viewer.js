'use strict';
(() => {
    const documentBody = document.getElementById('document');
    const bridge = window.chrome?.webview;
    let queued;
    let scheduled = false;
    let previous = '';
    let hasRendered = false;

    function renderLatest() {
        scheduled = false;
        const payload = queued;
        if (!payload) return;
        if (payload.theme) {
            for (const key of ['background', 'surface', 'text', 'secondary', 'border', 'accent']) {
                const color = payload.theme[key];
                if (/^#[0-9a-f]{6}$/i.test(color)) document.documentElement.style.setProperty(`--${key}`, color);
            }
        }
        const markdown = typeof payload.markdown === 'string' ? payload.markdown : '';
        if (hasRendered && markdown === previous) return;
        const extending = hasRendered && markdown.startsWith(previous);
        const selection = window.getSelection();
        if (extending && selection && !selection.isCollapsed && documentBody.contains(selection.anchorNode)) return;
        const scroll = document.scrollingElement;
        const atBottom = scroll.scrollHeight - scroll.clientHeight - scroll.scrollTop < 64;
        const position = scroll.scrollTop;
        documentBody.innerHTML = window.CallsMarkdown.render(markdown);
        // Streaming follows the bottom only while the reader is already there.
        scroll.scrollTop = !extending ? 0 : atBottom ? scroll.scrollHeight : position;
        previous = markdown;
        hasRendered = true;
    }

    function scheduleRender() {
        if (!scheduled) {
            scheduled = true;
            requestAnimationFrame(renderLatest);
        }
    }
    bridge?.addEventListener('message', event => {
        if (event.data?.type !== 'render') return;
        queued = event.data;
        scheduleRender();
    });
    document.addEventListener('selectionchange', scheduleRender);
    document.addEventListener('click', event => {
        const link = event.target.closest?.('a');
        if (!link) return;
        event.preventDefault();
        if (!event.isTrusted) return;
        try {
            const url = new URL(link.getAttribute('href'));
            if (url.protocol === 'https:' || url.protocol === 'http:')
                bridge?.postMessage({ type: 'open', url: url.href });
        } catch { /* Relative and non-web destinations stay inside the document. */ }
    });
    document.addEventListener('auxclick', event => {
        if (event.target.closest?.('a')) event.preventDefault();
    });
    bridge?.postMessage({ type: 'ready' });
})();
