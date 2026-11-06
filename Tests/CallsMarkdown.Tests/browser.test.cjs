// Run with Playwright available on NODE_PATH; no packages are added to the app.
const assert = require('node:assert/strict');
const fs = require('node:fs/promises');
const http = require('node:http');
const path = require('node:path');
const { chromium } = require('playwright');

(async () => {
    const assets = path.resolve(__dirname, '../../Assets');
    const server = http.createServer(async (request, response) => {
        try {
            const pathname = new URL(request.url, 'http://localhost').pathname;
            const file = path.resolve(assets, '.' + pathname);
            if (!file.startsWith(assets + path.sep)) throw new Error('Outside assets');
            response.setHeader('Content-Type', file.endsWith('.js') ? 'text/javascript' : file.endsWith('.css') ? 'text/css' : 'text/html');
            response.end(await fs.readFile(file));
        } catch { response.writeHead(404).end(); }
    });
    await new Promise(resolve => server.listen(0, '127.0.0.1', resolve));
    let browser;
    try {
        browser = await chromium.launch({ channel: 'msedge', headless: true });
        const page = await browser.newPage({ viewport: { width: 1000, height: 650 } });
        const errors = [];
        const requests = [];
        page.on('pageerror', error => errors.push(error.message));
        page.on('request', request => requests.push(request.url()));
        await page.addInitScript(() => {
            window.chrome ??= {};
            window.previewMessages = [];
            window.chrome.webview = {
                addEventListener: (_, handler) => window.deliverPreview = data => handler({ data }),
                postMessage: message => window.previewMessages.push(message)
            };
        });
        await page.goto(`http://127.0.0.1:${server.address().port}/CallsMarkdown/index.html`);
        await page.waitForFunction(() => window.previewMessages.some(message => message.type === 'ready'));
        async function send(markdown) {
            await page.evaluate(markdown => window.deliverPreview({ type: 'render', markdown }), markdown);
            await page.evaluate(() => new Promise(resolve => requestAnimationFrame(() => requestAnimationFrame(resolve))));
        }
        const initial = '# Call notes\n\n' + Array.from({ length: 100 }, (_, index) => `Paragraph ${index}: readable content.`).join('\n\n');
        await send(initial);
        assert.equal(await page.locator('h1').innerText(), 'Call notes');
        assert.equal(await page.evaluate(() => scrollY), 0, 'A selected report opens at its start');
        await page.evaluate(() => scrollTo(0, 500));
        await send(initial + '\n\nAdded text.');
        assert.equal(await page.evaluate(() => scrollY), 500, 'Streaming preserves an older reading position');
        await page.evaluate(() => scrollTo(0, document.body.scrollHeight));
        await send(initial + '\n\nAdded text.\n\nFurther text.');
        assert.ok(await page.evaluate(() => document.documentElement.scrollHeight - innerHeight - scrollY < 2), 'Streaming follows the bottom');
        await page.evaluate(() => getSelection().selectAllChildren(document.querySelector('h1')));
        await send(initial + '\n\nAdded text.\n\nFurther text.\n\nQueued while selecting.');
        assert.equal(await page.getByText('Queued while selecting.').count(), 0, 'Streaming leaves selected text available to copy');
        await page.evaluate(() => getSelection().removeAllRanges());
        await page.getByText('Queued while selecting.').waitFor();
        await page.evaluate(() => getSelection().selectAllChildren(document.querySelector('h1')));
        await send('# Replacement');
        assert.equal(await page.locator('h1').innerText(), 'Replacement', 'Switching reports replaces selected old content immediately');
        await send('# Safe\n\n<img src="https://example.com/pixel" onerror="alert(1)">\n\n[Open](https://example.com/)\n\n[Bad](javascript:alert)');
        assert.equal(await page.locator('img').count(), 0);
        assert.equal(await page.locator('a').count(), 1);
        await page.getByRole('link', { name: 'Open' }).click();
        assert.equal(await page.evaluate(() => window.previewMessages.at(-1).url), 'https://example.com/');
        assert.ok(requests.every(url => url.startsWith('http://127.0.0.1:')), 'The viewer loads only its local assets');
        assert.deepEqual(errors, [], 'Viewer scripts run without errors under the actual CSP');
        console.log('Calls Markdown browser: scroll, selection, bridge and resource safety assertions passed.');
    } finally {
        await browser?.close();
        await new Promise(resolve => server.close(resolve));
    }
})().catch(error => { console.error(error); process.exitCode = 1; });
