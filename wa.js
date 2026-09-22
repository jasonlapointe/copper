#!/usr/bin/env node
/*
 * wa.js — minimal WhatsApp CLI for chatting with Marat (translation workflow).
 *
 * Commands:
 *   node wa.js login                     Link this machine (scan QR with your phone)
 *   node wa.js status                    Check whether the saved session is still valid
 *   node wa.js chats [n]                 List the n most recent chats (default 15)
 *   node wa.js read "<chat>" [n]         Show last n messages from a chat (default 20)
 *   node wa.js send "<chat>" "<text>"    Send a message to a chat
 *   node wa.js serve "<chat>"            Long-running JSON-lines server for Copper:
 *                                        emits {event:"message",...} live, answers
 *                                        {id,cmd:"read"|"send"|"chats"|"status",...} on stdin.
 *
 * Session is stored in ./.wwebjs_auth — no passwords involved, ever.
 * Only ONE wa.js process can run at a time (Chromium locks the profile).
 */

const { Client, LocalAuth, MessageMedia } = require('whatsapp-web.js');
const qrcode = require('qrcode-terminal');
const readline = require('readline');
const fs = require('fs');
const path = require('path');

const MEDIA_DIR = path.join(__dirname, 'media');

// Download an image message to ./media (cached by message id); returns the file name or null.
async function mediaFor(m) {
  if (!m.hasMedia || m.type !== 'image') return null;
  try {
    if (!fs.existsSync(MEDIA_DIR)) fs.mkdirSync(MEDIA_DIR);
    const safeId = String(m.id.id).replace(/[^A-Za-z0-9._-]/g, '');
    const existing = fs.readdirSync(MEDIA_DIR).find((f) => f.startsWith(safeId + '.'));
    if (existing) return existing;
    const media = await m.downloadMedia();
    if (!media || !media.data) return null;
    const ext = ((media.mimetype || 'image/jpeg').split('/')[1] || 'jpg').split(';')[0];
    const file = `${safeId}.${ext}`;
    fs.writeFileSync(path.join(MEDIA_DIR, file), Buffer.from(media.data, 'base64'));
    return file;
  } catch {
    return null;
  }
}

const [, , cmd, ...args] = process.argv;

if (!cmd) {
  console.log('Usage: node wa.js <login|status|chats|read|send> [...]');
  process.exit(1);
}

const client = new Client({
  authStrategy: new LocalAuth({ dataPath: './.wwebjs_auth' }),
  puppeteer: { headless: true },
});

let sawQr = false;
let shuttingDown = false;

client.on('qr', (qr) => {
  sawQr = true;
  if (cmd === 'login') {
    console.log('Scan this QR code with WhatsApp on your phone:');
    console.log('(WhatsApp > Settings > Linked Devices > Link a Device)\n');
    qrcode.generate(qr, { small: true });
  } else {
    console.error('Not logged in. Run:  node wa.js login');
    shuttingDown = true;
    client.destroy().catch(() => {}).finally(() => process.exit(2));
  }
});

client.on('auth_failure', (msg) => {
  console.error('Authentication failed:', msg);
  client.destroy().finally(() => process.exit(2));
});

async function findChat(nameQuery) {
  const chats = await client.getChats();
  const q = nameQuery.toLowerCase();
  const exact = chats.find((c) => (c.name || '').toLowerCase() === q);
  if (exact) return exact;
  const partial = chats.filter((c) => (c.name || '').toLowerCase().includes(q));
  if (partial.length === 1) return partial[0];
  if (partial.length > 1) {
    console.error(`Ambiguous chat name "${nameQuery}". Matches:`);
    partial.slice(0, 10).forEach((c) => console.error(`  - ${c.name}`));
    return null;
  }
  console.error(`No chat found matching "${nameQuery}". Try: node wa.js chats`);
  return null;
}

function fmtTime(ts) {
  return new Date(ts * 1000).toISOString().replace('T', ' ').slice(0, 16) + ' UTC';
}

client.on('ready', async () => {
  if (cmd === 'serve') {
    await serve();
    return; // long-running: no destroy, no timeout
  }
  try {
    switch (cmd) {
      case 'login':
      case 'status': {
        const me = client.info;
        console.log(`Logged in as: ${me.pushname} (${me.wid.user})`);
        break;
      }

      case 'chats': {
        const n = parseInt(args[0] || '15', 10);
        const chats = await client.getChats();
        for (const c of chats.slice(0, n)) {
          const unread = c.unreadCount ? `  [${c.unreadCount} unread]` : '';
          const kind = c.isGroup ? ' (group)' : '';
          console.log(`${c.name || c.id.user}${kind}${unread}`);
        }
        break;
      }

      case 'read': {
        const [name, countArg] = args;
        if (!name) { console.error('Usage: node wa.js read "<chat>" [n]'); break; }
        const chat = await findChat(name);
        if (!chat) break;
        const limit = parseInt(countArg || '20', 10);
        const messages = await chat.fetchMessages({ limit });
        console.log(`=== ${chat.name} — last ${messages.length} messages ===`);
        for (const m of messages) {
          const who = m.fromMe ? 'ME' : (chat.name || 'THEM');
          const body = m.body || (m.hasMedia ? `[${m.type}]` : `[${m.type}]`);
          console.log(`[${fmtTime(m.timestamp)}] ${who}: ${body}`);
        }
        // Deliberately no sendSeen(): reading through the bridge must not
        // produce blue ticks — Jason hasn't actually read anything yet.
        break;
      }

      case 'send': {
        const [name, ...textParts] = args;
        const text = textParts.join(' ');
        if (!name || !text) { console.error('Usage: node wa.js send "<chat>" "<text>"'); break; }
        const chat = await findChat(name);
        if (!chat) break;
        await chat.sendMessage(text);
        console.log(`Sent to ${chat.name}: ${text}`);
        // Give the send a moment to flush before teardown.
        await new Promise((r) => setTimeout(r, 3000));
        break;
      }

      default:
        console.error(`Unknown command: ${cmd}`);
    }
  } catch (err) {
    console.error('Error:', err.stack || err.message || err);
    process.exitCode = 1;
  } finally {
    await client.destroy();
    process.exit(process.exitCode || 0);
  }
});

async function serve() {
  const emit = (obj) => console.log(JSON.stringify(obj));
  const chat = await findChat(args[0] || '');
  if (!chat) {
    emit({ event: 'fatal', error: `chat not found: ${args[0]}` });
    process.exit(2);
  }
  emit({ event: 'ready', me: client.info.pushname, chat: chat.name || chat.id.user });

  client.on('message', async (m) => {
    if (m.from === chat.id._serialized) {
      emit({ event: 'message', id: String(m.id.id), ts: m.timestamp, body: m.body || `[${m.type}]`, media: await mediaFor(m) });
    }
  });
  client.on('disconnected', (reason) => {
    emit({ event: 'fatal', error: `disconnected: ${reason}` });
    process.exit(3);
  });

  const rl = readline.createInterface({ input: process.stdin });
  rl.on('line', async (line) => {
    let req;
    try { req = JSON.parse(line); } catch { return; }
    try {
      let data;
      switch (req.cmd) {
        case 'read': {
          const messages = await chat.fetchMessages({ limit: req.limit || 20 });
          data = [];
          for (const m of messages) {
            data.push({
              id: String(m.id.id),
              t: m.timestamp,
              who: m.fromMe ? 'ME' : 'THEM',
              body: m.body || `[${m.type}]`,
              media: await mediaFor(m),
            });
          }
          break;
        }
        case 'send': {
          await chat.sendMessage(req.text);
          data = 'sent';
          break;
        }
        case 'sendMedia': {
          const media = MessageMedia.fromFilePath(req.path);
          const sent = await chat.sendMessage(media, req.caption ? { caption: req.caption } : {});
          data = { id: String(sent.id.id) };
          break;
        }
        case 'chats': {
          const chats = await client.getChats();
          data = chats.slice(0, req.count || 15).map((c) => c.name || c.id.user);
          break;
        }
        case 'status':
          data = `logged in as ${client.info.pushname}`;
          break;
        default:
          throw new Error(`unknown cmd: ${req.cmd}`);
      }
      emit({ id: req.id, ok: true, data });
    } catch (err) {
      emit({ id: req.id, ok: false, error: err.message });
    }
  });
  rl.on('close', () => {
    client.destroy().catch(() => {}).finally(() => process.exit(0));
  });
}

client.initialize().catch((err) => {
  if (shuttingDown) return; // teardown race after a deliberate exit — not a real failure
  console.error('Failed to start WhatsApp client:', err.message);
  process.exit(1);
});

// Safety net: don't hang forever (login gets longer to allow QR scanning; serve runs forever).
const timeoutMs = cmd === 'login' ? 180000 : 240000;
if (cmd !== 'serve') setTimeout(() => {
  console.error(sawQr && cmd === 'login'
    ? 'Timed out waiting for QR scan. Run "node wa.js login" again.'
    : 'Timed out. Check your internet connection and that your phone is online.');
  process.exit(3);
}, timeoutMs).unref();
