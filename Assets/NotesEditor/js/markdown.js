// Converts pasted Markdown text into the editor's supported rich-text blocks.
'use strict';

(() => {
    const { escapeHtml } = NE;
    const LIST_MARKER = /^(\s*)([-+*]|\d+[.)])\s+(.*)$/;
    const FENCE = /^\s{0,3}(`{3,}|~{3,})(.*)$/;

    function isMarkdown(text) {
        return /^\s{0,3}(?:#{1,6}\s|`{3,}|~{3,}|>\s?|[-+*]\s+|\d+[.)]\s+|(?:\*\s*){3,}$)/m.test(text) ||
            /^\s*\|?.+\|.+\n\s*\|?\s*:?-{3,}:?\s*(?:\|\s*:?-{3,}:?\s*)+\|?\s*$/m.test(text) ||
            /(?:`[^`\n]+`|\*\*|__|~~|\[[^\]]+\]\([^)]+\)|\*[^*\n]+\*|_[^_\n]+_)/.test(text);
    }

    function safeLink(url) {
        return /^(?:https?:|mailto:|\.{1,2}\/|#)/i.test(url);
    }

    function inline(text) {
        const placeholders = [];
        const reserve = html => {
            const token = `\u0000${placeholders.length}\u0000`;
            placeholders.push(html);
            return token;
        };
        let result = escapeHtml(text);
        result = result.replace(/(`+)(.+?)\1/g, (_, marker, code) =>
            reserve(`<code>${code}</code>`));
        result = result.replace(/\[([^\]]+)\]\(([^)\s]+)(?:\s+["'][^)]*["'])?\)/g, (match, label, url) => {
            const decodedUrl = url.replace(/&amp;/g, '&');
            if (!safeLink(decodedUrl)) return label;
            return reserve(`<a href="${escapeHtml(decodedUrl)}">${inline(label)}</a>`);
        });
        result = result
            .replace(/\*\*(.+?)\*\*/g, '<strong>$1</strong>')
            .replace(/__(.+?)__/g, '<strong>$1</strong>')
            .replace(/~~(.+?)~~/g, '<s>$1</s>')
            .replace(/(^|[^\w*])\*([^*\n]+)\*(?!\*)/g, '$1<em>$2</em>')
            .replace(/(^|[^\w_])_([^_\n]+)_(?!\w)/g, '$1<em>$2</em>');
        return result.replace(/\u0000(\d+)\u0000/g, (_, index) => placeholders[Number(index)]);
    }

    function indentWidth(value) {
        return value.replace(/\t/g, '    ').length;
    }

    function isRule(line) {
        return /^\s{0,3}(?:(?:\*\s*){3,}|(?:-\s*){3,}|(?:_\s*){3,})$/.test(line);
    }

    function isHeading(line) {
        return /^\s{0,3}#{1,6}\s+/.test(line);
    }

    function isFence(line) {
        return FENCE.test(line);
    }

    function tableCells(line) {
        const marker = '\u0001';
        return line.trim().replace(/\\\|/g, marker).replace(/^\||\|$/g, '')
            .split('|').map(cell => cell.replaceAll(marker, '|').trim());
    }

    function isTableStart(lines, index) {
        if (index + 1 >= lines.length || !lines[index].includes('|')) return false;
        const cells = tableCells(lines[index + 1]);
        return cells.length > 1 && cells.every(cell => /^:?-{3,}:?$/.test(cell));
    }

    function isBlockStart(lines, index) {
        const line = lines[index];
        return isFence(line) || isHeading(line) || isRule(line) ||
            /^\s{0,3}>/.test(line) || LIST_MARKER.test(line) || isTableStart(lines, index);
    }

    function parseList(lines, start) {
        const first = LIST_MARKER.exec(lines[start]);
        const baseIndent = indentWidth(first[1]);
        const ordered = /^\d/.test(first[2]);
        const tag = ordered ? 'ol' : 'ul';
        const startNumber = ordered ? Number.parseInt(first[2], 10) : 1;
        const items = [];
        let index = start;

        while (index < lines.length) {
            const marker = LIST_MARKER.exec(lines[index]);
            if (!marker || indentWidth(marker[1]) !== baseIndent ||
                /^\d/.test(marker[2]) !== ordered) break;

            const itemLines = [marker[3]];
            index++;
            while (index < lines.length) {
                const line = lines[index];
                const nextMarker = LIST_MARKER.exec(line);
                if (nextMarker && indentWidth(nextMarker[1]) <= baseIndent) break;
                if (!line.trim()) {
                    itemLines.push('');
                    index++;
                    continue;
                }
                const width = indentWidth(line.match(/^\s*/)[0]);
                if (width <= baseIndent) break;
                itemLines.push(line.replace(/^\s+/, leading => {
                    const expanded = leading.replace(/\t/g, '    ');
                    return ' '.repeat(Math.max(0, expanded.length - baseIndent - 2));
                }));
                index++;
            }

            while (itemLines.length && !itemLines[itemLines.length - 1].trim()) itemLines.pop();
            items.push(`<li>${parseBlocks(itemLines)}</li>`);
        }

        const startAttribute = ordered && startNumber > 1 ? ` start="${startNumber}"` : '';
        return { html: `<${tag}${startAttribute}>${items.join('')}</${tag}>`, next: index };
    }

    function parseBlocks(lines) {
        const output = [];
        let index = 0;
        while (index < lines.length) {
            const line = lines[index];
            if (!line.trim()) {
                index++;
                continue;
            }

            const fence = FENCE.exec(line);
            if (fence) {
                const code = [];
                index++;
                while (index < lines.length && !new RegExp(`^\\s{0,3}${fence[1][0]}{${fence[1].length},}\\s*$`).test(lines[index]))
                    code.push(lines[index++]);
                if (index < lines.length) index++;
                output.push(`<pre><code>${escapeHtml(code.join('\n'))}</code></pre>`);
                continue;
            }

            const heading = /^\s{0,3}(#{1,6})\s+(.*?)\s*#*\s*$/.exec(line);
            if (heading) {
                const level = Math.min(Number(heading[1].length), 3);
                output.push(`<h${level}>${inline(heading[2])}</h${level}>`);
                index++;
                continue;
            }

            if (isRule(line)) {
                output.push('<hr>');
                index++;
                continue;
            }

            if (/^\s{0,3}>/.test(line)) {
                const quote = [];
                while (index < lines.length && /^\s{0,3}>/.test(lines[index]))
                    quote.push(lines[index++].replace(/^\s{0,3}>\s?/, ''));
                output.push(`<blockquote>${parseBlocks(quote)}</blockquote>`);
                continue;
            }

            if (isTableStart(lines, index)) {
                const header = tableCells(lines[index]);
                index += 2;
                const rows = [];
                while (index < lines.length && lines[index].includes('|') && lines[index].trim())
                    rows.push(tableCells(lines[index++]));
                const headerHtml = header.map(cell => `<th>${inline(cell)}</th>`).join('');
                const rowsHtml = rows.map(row =>
                    `<tr>${header.map((_, cellIndex) => `<td>${inline(row[cellIndex] || '')}</td>`).join('')}</tr>`).join('');
                output.push(`<table><thead><tr>${headerHtml}</tr></thead><tbody>${rowsHtml}</tbody></table>`);
                continue;
            }

            if (LIST_MARKER.test(line)) {
                const list = parseList(lines, index);
                output.push(list.html);
                index = list.next;
                continue;
            }

            const paragraph = [line.trim()];
            index++;
            while (index < lines.length && lines[index].trim() && !isBlockStart(lines, index))
                paragraph.push(lines[index++].trim());
            output.push(`<p>${inline(paragraph.join(' '))}</p>`);
        }
        return output.join('');
    }

    function markdownToHtml(text) {
        return parseBlocks(text.replace(/\r\n?/g, '\n').split('\n'));
    }

    Object.assign(NE, { isMarkdown, markdownToHtml });
})();
