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

## Product shape — decide this first

Telegram (like WhatsApp) won't let a bot silently sit inside someone's existing private chats.
The two sanctioned shapes are both *better* products than a silent overlay:

1. **Translated group bot (recommended first).** Add the bot to a group; every member writes in
   their own language and reads in their own. Perfect for a cross-language family/friend group,
   and naturally viral — one person adds it, the whole group is a user. Challenge: one message has
   *many* recipients with different languages, so translation is per-reader (see below).
2. **1:1 relay bot.** Each person DMs the bot in their language; it relays the translated message
   to the other. Simpler language model (two known sides), needs both to start the bot.

Start with #1 for reach, or #2 if you want the simplest first cut. This choice drives the `TODO`s
in `tg.js`.

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
