import { test } from 'node:test';
import assert from 'node:assert/strict';
import { spawn } from 'node:child_process';
import { randomUUID } from 'node:crypto';
import { once } from 'node:events';
import net from 'node:net';
import readline from 'node:readline';
import { fileURLToPath } from 'node:url';
import { callBridge, handle, validate, tools } from '../betterwintab-notes/server/index.mjs';

test('retries a pipe that was not listening before the request was sent', { skip: process.platform !== 'win32' }, async () => {
  const pipeName = 'BetterWinTab.Notes.Retry.' + randomUUID();
  const bridge = net.createServer(socket => {
    socket.once('data', () => socket.end('{"ok":true,"result":{"connected":true}}\n'));
  });
  const start = setTimeout(() => bridge.listen('\\\\.\\pipe\\' + pipeName), 65);
  try {
    assert.deepEqual(await callBridge('status', {}, pipeName), { connected: true });
  } finally {
    clearTimeout(start);
    if (bridge.listening) await new Promise(resolve => bridge.close(resolve));
  }
});

test('MCP discovery, notifications and invalid arguments', async () => {
  const init = await handle({ jsonrpc: '2.0', id: 1, method: 'initialize', params: { protocolVersion: '2025-06-18' } });
  assert.equal(init.result.protocolVersion, '2025-06-18');
  assert.equal((await handle({ jsonrpc: '2.0', id: 2, method: 'tools/list' })).result.tools.length, 7);
  assert.equal(await handle({ jsonrpc: '2.0', method: 'notifications/initialized' }), null);
  assert.throws(() => validate('create_note', { title: 'missing content' }));
  assert.throws(() => validate('list_notes', { limit: 500 }));
  assert.throws(() => validate('get_note', { id: 'x', arbitraryPath: 'x' }));
  assert.throws(() => validate('update_note', { id: 'x', expectedUpdatedAt: 'x', content: '', appendText: 'x' }));
  assert.equal(tools.find(t => t.name === 'update_note').annotations.destructiveHint, true);
});

test('SSH machine creation is a local write with validated credentials and sanitized output', async () => {
  const tool = tools.find(t => t.name === 'create_ssh_machine');
  assert.deepEqual(tool.inputSchema.required, ['host', 'username', 'password']);
  assert.equal(tool.inputSchema.properties.name.maxLength, 100);
  assert.equal(tool.inputSchema.properties.port.default, 22);
  assert.equal(tool.inputSchema.properties.username.maxLength, 255);
  assert.equal(tool.annotations.readOnlyHint, false);
  assert.equal(tool.annotations.openWorldHint, false);
  const args = { name: 'Lab', host: '192.0.2.23', port: 2222, username: 'tester', password: 'test only' };
  assert.doesNotThrow(() => validate(tool.name, args));
  assert.throws(() => validate(tool.name, { ...args, host: 'example.test' }));
  assert.throws(() => validate(tool.name, { ...args, port: 0 }));
  assert.throws(() => validate(tool.name, { ...args, password: '' }));
  assert.doesNotThrow(() => validate(tool.name, { ...args, password: '   ' }));
  assert.doesNotThrow(() => validate(tool.name, { ...args, password: 'é'.repeat(2048) }));
  assert.throws(() => validate(tool.name, { ...args, password: 'é'.repeat(2049) }), /4096 bytes/);

  const request = { jsonrpc: '2.0', id: 3, method: 'tools/call', params: { name: tool.name, arguments: args } };
  const result = await handle(request, async () => ({ id: 'ssh-test', ...args }));
  assert.equal(result.result.isError, false);
  assert.deepEqual(JSON.parse(result.result.content[0].text), {
    id: 'ssh-test', name: 'Lab', host: '192.0.2.23', port: 2222, username: 'tester',
  });
});

test('stdio MCP to actual C# named pipe and persistence', { timeout: 30000 }, async () => {
  const fixture = spawn('dotnet', [fileURLToPath(new URL('./BridgeHarness/bin/Debug/net8.0/BridgeHarness.dll', import.meta.url)), '--serve'], { stdio: ['pipe', 'pipe', 'pipe'] });
  let fixtureError = '';
  fixture.stderr.on('data', chunk => { fixtureError += chunk; });
  const lines = readline.createInterface({ input: fixture.stdout });
  const ready = await new Promise((resolve, reject) => {
    lines.on('line', line => { if (line.startsWith('READY ')) resolve(line.slice(6)); });
    fixture.once('exit', code => reject(new Error(`Harness exited ${code}: ${fixtureError}`)));
  });
  assert.ok(ready);
  const server = spawn(process.execPath, [fileURLToPath(new URL('./stdio-runner.mjs', import.meta.url)), ready], { stdio: ['pipe', 'pipe', 'pipe'] });
  const replies = readline.createInterface({ input: server.stdout });
  let id = 0;
  const rpc = async (method, params) => {
    const response = once(replies, 'line');
    server.stdin.write(JSON.stringify({ jsonrpc: '2.0', id: ++id, method, params }) + '\n');
    const result = JSON.parse((await response)[0]);
    assert.equal(result.id, id);
    return result.result;
  };
  const call = async (name, args = {}) => {
    const response = await rpc('tools/call', { name, arguments: args });
    assert.equal(response.isError, false, response.content[0].text);
    return JSON.parse(response.content[0].text);
  };
  try {
    await rpc('initialize', { protocolVersion: '2025-11-25', capabilities: {}, clientInfo: { name: 'test', version: '1' } });
    assert.equal((await rpc('tools/list')).tools.length, 7);
    assert.equal((await call('status')).connected, true);
    const created = await call('create_note', { title: 'Integración áé', content: 'Línea 1\nLínea 2' });
    const note = await call('get_note', { id: created.id });
    assert.equal(note.content, 'Línea 1\nLínea 2');
    const updated = await call('update_note', { id: note.id, expectedUpdatedAt: note.updatedAt, appendText: 'Añadido' });
    assert.ok(updated.content.endsWith('Añadido'));
    const conflict = await rpc('tools/call', { name: 'update_note', arguments: { id: note.id, expectedUpdatedAt: note.updatedAt, title: 'stale' } });
    assert.equal(conflict.isError, true);
    assert.match(conflict.content[0].text, /ha cambiado/);
    assert.equal((await call('list_notes', { query: 'Integración' })).total, 1);
    assert.equal((await call('list_folders')).folders.length, 1);
    if (process.platform === 'win32') {
      const machine = await call('create_ssh_machine', {
        name: 'SSH integration', host: '2001:db8::23', username: 'tester', password: 'solo de prueba ñ!&',
      });
      assert.ok(machine.id);
      assert.deepEqual(machine, { id: machine.id, name: 'SSH integration', host: '2001:db8::23', port: 22, username: 'tester' });
    }
    server.stdin.write('{invalid json}\n');
    assert.equal(JSON.parse((await once(replies, 'line'))[0]).error.code, -32700);
  } finally {
    server.stdin.end();
    fixture.stdin.end();
    await Promise.all([once(server, 'exit'), once(fixture, 'exit')]);
    replies.close();
    lines.close();
  }
});
