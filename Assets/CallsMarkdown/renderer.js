'use strict';
window.CallsMarkdown = {
    render(markdown) {
        // The shared parser reserves these control characters for inline tokens.
        const text = String(markdown ?? '').replace(/[\u0000\u0001]/g, '\uFFFD');
        return window.NE.markdownToHtml(text)
            .replace(/<li><p>\[([ xX])\]\s/g, (_, state) => {
                const complete = state.toLowerCase() === 'x';
                return `<li class="task-item"><p><span class="task-marker" role="img" aria-label="${complete ? 'Completed' : 'Pending'}">${complete ? '☑' : '☐'}</span> `;
            })
            .replace(/<table>/g, '<div class="table-scroll" tabindex="0" role="region" aria-label="Table"><table>')
            .replace(/<\/table>/g, '</table></div>');
    }
};
