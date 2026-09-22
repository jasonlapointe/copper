#!/usr/bin/env node
/*
 * tg.js — Telegram bridge for Copper (SKELETON — a starting point to build on).
 *
 * Mirrors the same JSON-lines "serve" protocol that wa.js speaks, so Copper's existing
 * C# engine (WaService → Agent → the LLM edges, cache, people network) can drive it with
 * little or no change: point copper.json's bridgeDir at this folder and run.
 *
 * Zero dependencies: Telegram's Bot API is plain HTTPS and Node 18+ has global fetch.
 * No library, no patches, no ban risk — this is an official, sanctioned API.
 *
 * Protocol (stdout, one JSON object per line):
 *   {event:"ready", me, chat}            once connected
 *   {event:"message", id, ts, body, media}   for each incoming message in the bridged chat
 *   {id, ok:true, data}                  reply to a request
 *   {id, ok:false, error}                failed request
 * Requests (stdin, one JSON object per line):
 *   {id, cmd:"read", limit}     {id, cmd:"send", text}     {id, cmd:"status"}
 *   {id, cmd:"chats", count}    {id, cmd:"sendMedia", path, caption}
 *
 * Run:  TELEGRAM_BOT_TOKEN=<from @BotFather> node tg.js serve [<chatId>]
 */

const readline = require('readline');

const TOKEN = process.env.TELEGRAM_BOT_TOKEN || process.argv[3];
const API = `https://api.telegram.org/bot${TOKEN}`;
const [, , cmd, chatArg] = process.argv;

// The chat this bridge relays. For MVP you can pass a chat id, or leave it empty and let the
// first chat that messages the bot claim it. TODO(product): decide the shape — a translated
// GROUP (bot is a member, everyone reads their own language) or a 1:1 RELAY bot. See PLAN.md.
let boundChatId = chatArg || null;

const emit = (obj) => console.log(JSON.stringify(obj));
const buffer = [];           // recent messages, since bots can't fetch history before they joined
const knownChats = new Map(); // chatId -> title/name, for the "chats" command

async function tg(method, params) {
  const res = await fetch(`${API}/${method}`, {
    method: 'POST',
    headers: { 'Content-Type': 'application/json' },
    body: JSON.stringify(params || {}),
  });
  const json = await res.json();
  if (!json.ok) throw new Error(json.description || `${method} failed`);
  return json.result;
}

async function serve() {
  if (!TOKEN) { emit({ event: 'fatal', error: 'Set TELEGRAM_BOT_TOKEN (get one from @BotFather).' }); process.exit(2); }
  const me = await tg('getMe');
  emit({ event: 'ready', me: me.username, chat: boundChatId || '(awaiting first message)' });

  // Long-poll for updates. TODO: switch to a webhook for production (lower latency, no polling).
  let offset = 0;
  for (;;) {
    let updates = [];
    try {
      updates = await tg('getUpdates', { offset, timeout: 50 });
    } catch (e) {
      emit({ event: 'error', error: e.message });
      await new Promise((r) => setTimeout(r, 2000));
      continue;
    }
    for (const u of updates) {
      offset = u.update_id + 1;
      const m = u.message;
      if (!m) continue;
      const id = String(m.chat.id);
      knownChats.set(id, m.chat.title || [m.chat.first_name, m.chat.last_name].filter(Boolean).join(' ') || id);
      if (!boundChatId) boundChatId = id; // first chat claims the bridge (MVP)
      if (id !== String(boundChatId)) continue;

      // TODO(product): in a GROUP, tag who sent it (m.from.first_name) so the engine can
      // translate per-recipient; in a RELAY, map senders to the two sides.
      const media = m.photo ? '[photo]' : null; // TODO: download via getFile + files.download
      const msg = { id: String(m.message_id), t: m.date, who: 'THEM', body: m.text || m.caption || `[${mediaType(m)}]`, media };
      buffer.push(msg);
      if (buffer.length > 500) buffer.shift();
      emit({ event: 'message', ...msg });
    }
  }
}

function mediaType(m) {
  if (m.photo) return 'photo';
  if (m.sticker) return 'sticker';
  if (m.voice) return 'voice';
  if (m.document) return 'document';
  return 'message';
}

async function handle(req) {
  switch (req.cmd) {
    case 'read':
      return buffer.slice(-(req.limit || 20));
    case 'send': {
      const sent = await tg('sendMessage', { chat_id: boundChatId, text: req.text });
      return { id: String(sent.message_id) };
    }
    case 'status':
      return `bot @${(await tg('getMe')).username}, chat ${boundChatId}`;
    case 'chats':
      return [...knownChats.values()].slice(0, req.count || 15);
    case 'sendMedia': {
      // TODO: upload the local file with multipart/form-data to sendPhoto. fetch supports
      // FormData + Blob in Node 18+. Caption goes in the `caption` field.
      throw new Error('sendMedia not implemented yet — see PLAN.md');
    }
    default:
      throw new Error(`unknown cmd: ${req.cmd}`);
  }
}

if (cmd === 'serve') {
  serve().catch((e) => { emit({ event: 'fatal', error: e.message }); process.exit(1); });
  const rl = readline.createInterface({ input: process.stdin });
  rl.on('line', async (line) => {
    let req; try { req = JSON.parse(line); } catch { return; }
    try { emit({ id: req.id, ok: true, data: await handle(req) }); }
    catch (e) { emit({ id: req.id, ok: false, error: e.message }); }
  });
} else {
  console.error('Usage: TELEGRAM_BOT_TOKEN=<token> node tg.js serve [<chatId>]');
  process.exit(1);
}
