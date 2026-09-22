# Telegram bridge — build plan

Goal: run Copper's interpreter engine on Telegram, where bots are an official, free, sanctioned
feature (no unofficial client, no patches, no ban risk). This is the commercial path — the engine
we built carries over; only the transport changes.

## Why this reuses almost everything

The valuable part of Copper — the people network, the "interpreter not translator" prompts, the
two LLM edges (`TranslateAsync` / `ProcessAsync`), caching, grounding — is transport-agnostic and
lives in the C# backend. The only WhatsApp-specific piece is `wa.js`. `telegram/tg.js` is a
drop-in replacement that speaks the **same JSON-lines `serve` protocol**, so `WaService` can drive
it unchanged. First milestone is literally: point the backend at this bridge and watch it work.

## Product shape — one bot serves BOTH

A single bot handles both 1:1 DMs and groups at once. Turning **Group Privacy OFF** in BotFather
lets it read all group messages and does **not** affect DMs (those are always visible). So the
design target is: treat **each chat (by its Telegram chat id) as its own conversation**, and route
by the chat's type — the bot doesn't have to be "a DM bot" or "a group bot," it's both.

- **1:1 relay (`chatType: "private"`).** Two people, two languages — translate X↔Y. Simplest to
  ship first; good for proving the loop end to end.
- **Translated group (`chatType: "group"`/`"supergroup"`).** One message, several readers who may
  each speak a different language, so translation is **per reader**. The bot knows each sender from
  the message (`sender` field). Naturally viral — one person adds the bot, the whole group is a user.

### Automatic language seeding (required — no manual setup)

When a person the engine has never seen speaks for the first time, the engine **automatically**:
1. detects the language of their message (the translate edge already reads the text — it returns a
   detected-language code alongside the translation, no extra call),
2. creates `people/<slug>.md` for them, seeded with a grounding stub that records their detected
   language, their Telegram identity (`sender` + `chatId`), and the date of first contact,
3. uses that language for everything it renders to or from them thereafter.

This is not optional and not a manual step — it's core to the "invisible bridge" promise: you add
the bot to a group or start a DM and it just works, learning each person as they arrive. It reuses
the existing auto-learn machinery (`AppConfig.AppendPersonNote` / grounding refresh); the only new
piece is "unknown sender → create file + record detected language" on the first message. The person
is still curated over time (Copper refines the grounding), but the first touch is automatic.

Every emitted message now carries `chatId`, `chatType`, and `sender` so the engine can route both.
The MVP skeleton still *binds a single chat* (first chat to message it) to keep the first wiring
trivial; the evolution below removes that.

### Evolution from single-chat MVP → multi-chat
1. Drop `boundChatId`; keep a conversation per `chatId` (its own recent buffer + people context).
2. Make `send`/`sendMedia` take a target `chatId` (which chat to reply into) instead of the single
   bound chat. (This extends the serve protocol; update `WaService` to pass the chat id.)
3. For groups, fan a message out to each reader's language; for DMs, it's a straight X↔Y.

## Steps

1. **Get a token.** Message [@BotFather](https://t.me/BotFather) → `/newbot` → copy the token.
   Run: `TELEGRAM_BOT_TOKEN=<token> node tg.js serve`. (Node 18+; zero dependencies.)
2. **Prove the loop.** Point `copper/copper.json` `bridgeDir` at `../telegram`, and make
   `WaService` spawn `tg.js` instead of `wa.js` (or add a `bridgeCmd` config field — small change).
   DM the bot; confirm the engine translates it in the UI. This validates full reuse.
3. **Per-reader translation (for the group shape).** A group message needs rendering into each
   member's language. Extend the engine so a message fans out to N target languages, and have the
   bot post each reader their version — or post one message with all languages, MVP-simplest.
   Track each member's language in the people network (seed it when they first speak).
4. **Media.** Implement `sendMedia` (multipart `sendPhoto`) and inbound photo download
   (`getFile` → download → hand the local path to the engine, same as the WhatsApp media flow).
5. **History caveat.** Bots can't read messages from before they joined; `read` returns what the
   bridge has buffered this session. The engine's local cache still persists translations across
   restarts, so this only affects a cold first run.
6. **Webhook (production).** Swap long-polling for a webhook when you deploy — lower latency, no
   polling loop.

## Monetization (when it's working)

Telegram has **native payments / Telegram Stars** for bots — you can charge a subscription inside
the platform with no Stripe/merchant setup to start (add Stripe later for cards). Gate premium
behavior (more contacts, higher volume, richer context) behind it.

## Architecture note for later

For a clean multi-transport codebase, extract an `IChatBridge` interface (ReadAsync, SendAsync,
SendMediaAsync, OnLiveMessage, StatusAsync) that both a `WhatsAppBridge` and a `TelegramBridge`
implement, and let `copper.json` pick one. Not needed for the MVP — the shared `serve` protocol
already lets one `WaService` drive either bridge — but it's the right shape once Telegram is real.

## What's done vs. what's yours to build

- **Done:** `tg.js` skeleton — connects, long-polls, receives text, sends text, buffers history,
  lists chats, speaks the serve protocol.
- **Yours:** pick the product shape; per-reader language handling for groups; media up/download;
  wire `WaService` to spawn this bridge; webhook + payments when you productionize.
