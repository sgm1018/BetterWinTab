// Run with Playwright available on NODE_PATH, as for the Calls Markdown browser test.
const assert = require('node:assert/strict');
const path = require('node:path');
const { test } = require('node:test');
const { pathToFileURL } = require('node:url');
const { chromium } = require('playwright');

test('Notes pastes plain text at the selection with only the clipboard line breaks', async t => {
    const browser = await chromium.launch({ channel: 'msedge', headless: true });
    t.after(() => browser.close());
    const page = await browser.newPage();
    const editorUrl = pathToFileURL(path.join(__dirname, '../../Assets/NotesEditor/editor.html'));
    await page.goto(editorUrl.href);

    const cases = [
        ['single-line selection', 7, 15, 'pasted', ['before pasted! after']],
        ['trailing clipboard newline', 7, 15, 'pasted\r\n', ['before pasted', '! after']],
        ['leading clipboard newline', 7, 7, '\n<pasted>', ['before ', '<pasted>!selected after']],
        ['intentional blank line', 7, 15, 'pasted\n\nsecond', ['before pasted', '', 'second! after']],
    ];

    for (const [name, start, end, text, expectedParagraphs] of cases) {
        await t.test(name, async () => {
            await page.evaluate(({ start, end, text }) => {
                window.notesEditor.load({ id: 'test-note', html: '<p>before selected after</p>' });
                const editor = document.getElementById('editor');
                editor.focus();
                const node = editor.firstElementChild.firstChild;
                const selection = window.getSelection();
                selection.setBaseAndExtent(node, start, node, end);

                const clipboardData = new DataTransfer();
                clipboardData.setData('text/plain', text);
                editor.dispatchEvent(new ClipboardEvent('paste', {
                    clipboardData,
                    bubbles: true,
                    cancelable: true,
                }));
            }, { start, end, text });

            await page.keyboard.insertText('!');
            assert.deepEqual(await page.locator('#editor > p').allTextContents(), expectedParagraphs);
        });
    }
});
