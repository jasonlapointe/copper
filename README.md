# Copper

**A WhatsApp interpreter that makes the language barrier disappear.**

Copper sits between you and one WhatsApp contact who speaks a different language. You read and
write in your own language; they read and write in theirs. It is not a word-for-word translation
box — it keeps a growing model of *the people talking* (who they are, how they speak, what's
happening in their lives) so that each message lands the way a fluent native speaker would say
it. The goal is simple: two people talking, with the language invisible.

Copper was built by one person to talk with family in Kazakhstan (English ↔ Russian/Kazakh),
but it works for any language pair the model handles.

> **Values it was built on:** *harmony* (bring people closer, don't just move text), *dignity*
> (no one should ever sound "translated"), *fidelity* (carry meaning whole, invent nothing),
> and *consent* (your data stays yours). See [`copper/VALUES.md`](copper/VALUES.md).

---

## How it works

```
You  ⇄  Copper UI (chat in your browser)
              │
              ├── ASP.NET backend (C#) — the deterministic pipeline:
              │     reads history, streams the live feed, sends, caches, stores
              │
              ├── LLM edges (Claude, two stateless calls):
              │     • incoming message  → contextual translation into your language
              │     • your message      → natural phrasing in the contact's language
              │
              └── node bridge (wa.js, whatsapp-web.js) — talks to WhatsApp Web
```

- **Deterministic where it can be, AI only at the edges.** Polling, history, the live feed,
  sending, and caching are plain code. The model is called only to translate an incoming
  message or render an outgoing one — each a single stateless call with context assembled from
  the people network and recent chat.
- **The people network** (`copper/people/*.md`) is the heart. One markdown file per person,
  both sides of the conversation, each opening with a dated *grounding statement* — a short
  "who they are right now" that Copper refreshes over time. It learns from the conversation and
  from facts you tell it in passing ("btw, she just started university").
- **Nothing re-translates.** Every translated message is cached locally by its WhatsApp id, so
  reopening the chat is instant and free.
- **Seamless send.** Type in your language, press Enter — it goes out in theirs. Your own
  bubbles show your words; the translated version Marat received is one labeled click away.

---

## Setup

**Prerequisites:** [Node.js](https://nodejs.org/) 18+, the [.NET SDK](https://dotnet.microsoft.com/) 9+,
and either an `ANTHROPIC_API_KEY` **or** the [Claude CLI](https://docs.claude.com/en/docs/claude-code)
signed in (`claude auth login`) — Copper's brain uses the CLI by default, so no API key is required.

```bash
# 1. Install the WhatsApp bridge dependencies
npm install

# 2. Apply the two whatsapp-web.js compatibility patches (see "Patches" below)
cd node_modules/whatsapp-web.js && git apply ../../patches/wwebjs-fix.diff && git apply ../../patches/wwebjs-media-fix.diff && cd ../..

# 3. Link your WhatsApp — scan the QR with your phone (Settings → Linked Devices)
node wa.js login

# 4. Configure your contact
cp copper/copper.example.json copper/copper.json          # then edit it
cp -r copper/people.example copper/people                 # then edit the .md files

# 5. Run — opens the chat UI at http://localhost:5077
cd copper && dotnet run
```

Open **http://localhost:5077** and start talking.

---

## Configuration

`copper/copper.json` (copied from `copper.example.json`, and gitignored so your contact's
details never leave your machine):

| Field | Meaning |
|-------|---------|
| `bridgeDir` | Path to the folder containing `wa.js` (default `..`). |
| `model` | Claude model id (default `claude-opus-5`). |
| `selfFile` | Your own people-network file (e.g. `people/you.md`). |
| `contact.name` | The contact's display name. |
| `contact.chatName` | The exact WhatsApp chat name or phone number to bridge. |
| `contact.profileFile` | The contact's people-network file (e.g. `people/contact.md`). |

One contact per configuration. Copper only ever reads and writes that one chat.

---

## Patches

`whatsapp-web.js` 1.34.7 breaks on current WhatsApp Web builds (2.3000.104x+) because WhatsApp
renamed internal fields. Two small, reviewed patches (from the library's own upstream fixes) live
in [`patches/`](patches/) and must be re-applied after every `npm install`:

- **`wwebjs-fix.diff`** — fixes `getChats` throwing a minified `r: r` (the `_serialized` → `$1` rename).
- **`wwebjs-media-fix.diff`** — fixes photo/media sends throwing *"Data passed to getter must
  include an id property"* (a stray `__x_id` collision).

If `git apply` fails, a newer `whatsapp-web.js` release may already include the fix — test first.

---

## Roadmap

- **Onboarding by importing conversations** — after linking WhatsApp, pick which chats to bridge;
  each becomes a contact with a people-network file seeded from that conversation. No hand-editing config.
- **Angular + Bearing front end** — replace the vanilla-HTML UI once the concept is stable.
- **Database persistence** — move local files to a database when volume warrants it.

See [`copper/SPEC.md`](copper/SPEC.md) for the full spec and [`copper/DECISIONS.md`](copper/DECISIONS.md)
for the design-decision log.

---

## Privacy & a word of caution

- **Your data stays local.** Message history, the translation cache, usage logs, the people
  network, downloaded media, and your WhatsApp session are all gitignored and never leave your
  machine. Keep it that way — don't commit `copper/people/`, `copper/store/`, `copper/logs/`,
  `media/`, `outgoing/`, `copper/copper.json`, or `.wwebjs_auth/`.
- **`whatsapp-web.js` is an unofficial WhatsApp client.** Automating WhatsApp is against its
  Terms of Service; use this for personal, human-scale conversation at your own risk. Never
  bulk-send.
- **Nothing is sent without you.** You type and press Enter on every outgoing message — Copper
  never messages anyone on its own.

## License

MIT — see [`LICENSE`](LICENSE).
