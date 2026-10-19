import { callBridge, serveMcp } from '../betterwintab-notes/server/index.mjs';
if (!process.argv[2]?.startsWith('BetterWinTab.Notes.Test.')) throw new Error('Expected isolated test pipe.');
await serveMcp(process.stdin, process.stdout, (action, args) => callBridge(action, args, process.argv[2]));
