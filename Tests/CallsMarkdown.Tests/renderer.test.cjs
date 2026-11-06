const assert = require('node:assert/strict');
const fs = require('node:fs');
const path = require('node:path');
const vm = require('node:vm');
const root = path.resolve(__dirname, '../..');
const context = vm.createContext({ window: {} });
for (const asset of ['CallsMarkdown/bootstrap.js', 'NotesEditor/js/markdown.js', 'CallsMarkdown/renderer.js']) {
    const filename = path.join(root, 'Assets', asset);
    assert.ok(fs.existsSync(filename), `Required viewer asset is missing: ${asset}`);
    vm.runInContext(fs.readFileSync(filename, 'utf8'), context, { filename });
    context.NE = context.window.NE;
}
const render = context.window.CallsMarkdown.render;
assert.equal(render('# Notes\n\n## Decisions\n\n### Next steps'), '<h1>Notes</h1><h2>Decisions</h2><h3>Next steps</h3>');
assert.match(render('- First\n- Second\n\n1. Ordered\n2. Again'), /<ul><li><p>First<\/p><\/li><li><p>Second<\/p><\/li><\/ul><ol>/);
assert.match(render('- [x] Sent\n- [ ] Pending'), /class="task-item".*aria-label="Completed".*Sent.*aria-label="Pending".*Pending/s);
assert.match(render('| Name | State |\n| --- | --- |\n| Call | Ready |'), /class="table-scroll".*<table><thead>.*<th>Name<\/th>.*<td>Ready<\/td>.*<\/table><\/div>/);
assert.equal(render('```html\n<script>alert(1)</script>\n```'), '<pre><code>&lt;script&gt;alert(1)&lt;/script&gt;</code></pre>');
assert.equal(render('<img src=x onerror=alert(1)>'), '<p>&lt;img src=x onerror=alert(1)&gt;</p>');
for (const url of ['javascript:alert', 'data:text/html,evil', 'file:///secret', 'vbscript:evil']) {
    assert.doesNotMatch(render(`[bad](${url})`), /href=/, url);
}
assert.match(render('[Open](https://example.com?q=one&next=two)'), /href="https:\/\/example.com\?q=one&amp;next=two"/);
assert.doesNotMatch(render('![Remote](https://example.com/image.png)'), /<img/);
assert.doesNotMatch(render('\u00000\u0000 <script>bad</script>'), /<script>|undefined/);
assert.equal(render(null), '');
console.log('Calls Markdown renderer: all assertions passed.');
