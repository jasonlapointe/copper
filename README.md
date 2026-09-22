# Copper

**A WhatsApp interpreter that makes the language barrier disappear.**

Copper sits between you and someone on WhatsApp who speaks a different language. You read and
write in your language; they read and write in theirs. Neither of you sees the other's language
unless you go looking for it. The point is simple: **two people talking, with the language
invisible** — so families and friends who don't share a language can just... talk.

> **A note on this project — Jason & Ilyas.** This is ours to build on together. Jason started it
> to talk with family in Kazakhstan (English ↔ Russian/Kazakh); we're growing it into something
> both our families can use. It's also a good project to *learn* with — the code is small,
> readable, and every design choice is written down in [`copper/DECISIONS.md`](copper/DECISIONS.md).
> Ilyas — read this whole file top to bottom once; it explains not just *how* to run it but *why*
> it's built the way it is. Then poke at the code. Break things. Ask questions. Nothing here is
> sacred except the [values](copper/VALUES.md).

---

## The idea: an interpreter, not a translation box

A translation box takes words in one language and swaps them for words in another. It produces
things like *"How is your health situation currently?"* — technically correct, unmistakably
foreign, and it quietly makes the speaker sound less warm and less intelligent than they are.

A good human **interpreter** does something different. They know both people. They know that
*"молодцы что прилетели"* isn't "well done for flying" — it's a warm *"so glad you made the trip."*
They carry the *meaning and the tone*, not the words. Copper aims to be that interpreter.

The way it does this is by keeping a small, growing model of **the people in the conversation** —
who they are, how they talk, what's happening in their lives — and using that context every time
it translates. That model lives in plain markdown files you can read and edit
([`copper/people/`](copper/people.example)), one per person, and Copper updates it as it learns.

Three rules it never breaks (the full list is in [`VALUES.md`](copper/VALUES.md)):
1. **Dignity** — no one should ever sound "translated."
2. **Fidelity** — carry the meaning whole; never invent, never embellish.
3. **Consent** — you send every message yourself; your data stays on your machine.

---

## How it's built (and how a message flows)

Copper has three parts:

```
   ┌─────────────────────────────────────────────────────────────────┐
   │  Your browser — the chat UI (copper/wwwroot/index.html)          │
   │  Looks like WhatsApp. You type in your language.                 │
   └───────────────────────────┬─────────────────────────────────────┘
                               │  HTTP + Server-Sent Events
   ┌───────────────────────────┴─────────────────────────────────────┐
   │  The backend — C# / ASP.NET (copper/*.cs)                        │
   │  The "deterministic pipeline": history, live feed, sending,      │
   │  caching, and the people network. Plain code, no AI here.        │
   │                                                                   │
   │   ...calls the LLM only at two "edges":                          │
   │      • an incoming message  → translated into your language      │
   │      • your outgoing message → rendered in their language        │
   └───────────────────────────┬─────────────────────────────────────┘
                               │  spawns & talks to
   ┌───────────────────────────┴─────────────────────────────────────┐
   │  The bridge — Node.js (wa.js, using whatsapp-web.js)             │
   │  The only piece that actually touches WhatsApp Web.              │
   └───────────────────────────────────────────────────────────────────┘
```

**A key design idea worth understanding:** *deterministic where we can be, AI only at the edges.*
Reading messages, showing history, sending, caching, storing — none of that needs a language
model, so it's ordinary code that behaves the same way every time. The model is expensive and
non-deterministic, so we call it **only** for the two things that genuinely need judgment:
turning a foreign message into natural English, and turning your English into a natural foreign
message. Each of those is a single, stateless call. This makes Copper fast, cheap, and
predictable, and keeps the "magic" in two small, well-defined places.

**Following one incoming message, end to end:**
1. Marat sends a message on WhatsApp. The **bridge** (`wa.js`) is watching his chat and pushes it
   to the backend the instant it arrives (a "live feed").
2. The **backend** checks its local cache (`copper/store/`). Seen this message before? Show the
   saved translation instantly — no model call.
3. New message? The backend assembles context — the **people network** plus the recent chat — and
   makes one call to the model: *"translate this, given who these people are."*
4. The English comes back, gets cached, and appears in your browser as if Marat had written it in
   English. His original Russian is one click away if you ever want it.

**Following one outgoing message:**
1. You type in English and press Enter. That Enter *is* your approval — Copper never sends anything
   on its own.
2. The backend makes one model call: *"say this the way this person would, in their language, in
   the sender's voice."*
3. The bridge sends it to WhatsApp. Your bubble shows your English; the translated version that
   was actually delivered is one click away.

---

## The code, file by file (a map for reading it)

**Bridge (Node.js)**
- [`wa.js`](wa.js) — the whole WhatsApp bridge. A long-running `serve` mode speaks simple JSON lines
  over stdin/stdout: it streams incoming messages and answers read/send/chats/status/sendMedia
  requests. Start here if you want to see how we talk to WhatsApp.

**Backend (C#)** — in [`copper/`](copper/)
- `Program.cs` — the web server and all the HTTP endpoints (`/api/message`, `/api/history`,
  `/api/translate`, `/api/photo`, `/api/events`…). The wiring that connects everything.
- `Agent.cs` — the brain's two edges: `ProcessAsync` (your message → sent) and `TranslateAsync`
  (incoming → your language), plus caption rendering and the "grounding" refresh. The prompts that
  make the interpreter behave live here — this is the most interesting file.
- `WaService.cs` — owns the one long-running bridge process and turns its JSON lines into tidy C#
  method calls. Also carries the live-message event.
- `ClaudeCli.cs` — how we call the model: it shells out to the `claude` CLI in headless mode, so it
  uses your Claude subscription and needs no API key.
- `AppConfig.cs` — loads `copper.json` and the `people/` network; knows how to append a note to a
  person's file or rewrite their "grounding" paragraph.
- `TranslationStore.cs` — the local cache so nothing is ever translated twice (incoming keyed by
  WhatsApp message id; your sent messages keyed so your English survives across reloads).
- `TurnLogger.cs` — one JSON line per action, for seeing usage and cost.
- `SseHub.cs` — pushes live events (new messages, status) to the browser.
- `wwwroot/index.html` — the entire UI in one file: plain HTML, CSS, and JavaScript, no framework.
  Deliberately simple so it's easy to change while we're still figuring out what we want.

**Docs** — [`copper/SPEC.md`](copper/SPEC.md) (the spec), [`copper/DECISIONS.md`](copper/DECISIONS.md)
(every decision and *why*, newest at the bottom — read this to understand the reasoning),
[`copper/VALUES.md`](copper/VALUES.md) (what we won't compromise).

---

## Setup

**You need:** [Node.js](https://nodejs.org/) 18+, the [.NET SDK](https://dotnet.microsoft.com/) 9+,
and the [Claude CLI](https://docs.claude.com/en/docs/claude-code) signed in (`claude auth login`) —
Copper's brain uses your Claude subscription through the CLI, so **no API key is needed**.

```bash
# 1. Install the bridge's dependencies
npm install

# 2. Apply the two whatsapp-web.js patches (explained in the next section)
cd node_modules/whatsapp-web.js \
  && git apply ../../patches/wwebjs-fix.diff \
  && git apply ../../patches/wwebjs-media-fix.diff \
  && cd ../..

# 3. Link YOUR WhatsApp — scan the QR with your phone (Settings → Linked Devices → Link a Device)
node wa.js login

# 4. Make your own config and people files (these stay private — they're gitignored)
cp copper/copper.example.json copper/copper.json      # then edit it
cp -r copper/people.example copper/people             # then edit the .md files inside

# 5. Run it — opens the chat at http://localhost:5077
cd copper && dotnet run
```

Then open **http://localhost:5077** and start talking.

---

## Configuration

Edit `copper/copper.json` (your copy of `copper.example.json`; it's gitignored so your contact's
details never leave your machine):

| Field | Meaning |
|-------|---------|
| `bridgeDir` | Path to the folder with `wa.js` (default `..`). |
| `model` | Claude model id (default `claude-opus-5`). |
| `selfFile` | Your own people file, e.g. `people/you.md`. |
| `contact.name` | The contact's display name. |
| `contact.chatName` | The exact WhatsApp chat name or phone number to bridge. |
| `contact.profileFile` | The contact's people file, e.g. `people/contact.md`. |

One contact per config, and Copper only ever touches that one chat.

---

## The WhatsApp patches (why they exist)

Copper talks to WhatsApp through an open-source library, `whatsapp-web.js`. WhatsApp changes its
internal web code often, and when it does, the library breaks until someone fixes it. Two such
breakages affect us, so we carry two small, already-reviewed fixes in [`patches/`](patches/):

- **`wwebjs-fix.diff`** — without it, listing chats throws a cryptic `r: r` error (WhatsApp renamed
  an internal field from `_serialized` to `$1`).
- **`wwebjs-media-fix.diff`** — without it, sending a photo throws *"Data passed to getter must
  include an id property"* (a stray internal field collides with the message id).

You re-apply them after every `npm install` (step 2 above). If `git apply` fails, a newer library
version may already include the fix — try running without the patches first.

> This is the cost of using an unofficial WhatsApp client: expect the occasional patch when
> WhatsApp changes something. Each one is usually a line or two, and the library community fixes
> them fast — we just copy their fix into `patches/`.

---

## Working on it together

- **Understand a change before making it.** Read the relevant `DECISIONS.md` rows first — a lot of
  what looks arbitrary was a deliberate choice with a reason.
- **When you decide something, write it down.** Add a row to `DECISIONS.md` with the date and the
  *why*. Future-us will thank you.
- **Keep the two-edges shape.** New features should stay in the deterministic pipeline where
  possible; only reach for the model when a task genuinely needs language judgment.
- **Never commit private data.** The `.gitignore` already blocks your WhatsApp session, messages,
  logs, people files, photos, and config. Keep it that way — if you add a new kind of private data,
  add it to `.gitignore` in the same change.

---

## Roadmap (where we're taking it)

- **Onboarding by importing conversations** — instead of hand-editing config, you link WhatsApp,
  see your chat list, and pick which conversations to bridge; each becomes a contact with a people
  file seeded from that chat's history. This is the next big piece.
- **A real front end (Angular)** — the current UI is one plain HTML file, good for moving fast.
  Once the idea is settled, rebuild it properly.
- **Database persistence** — move the local files to a database when we outgrow them.

---

## Privacy & a word of caution

- **Your data stays on your machine.** Message history, the translation cache, logs, the people
  network, downloaded photos, and your WhatsApp session are all gitignored and never leave your
  computer. Never commit `copper/people/`, `copper/store/`, `copper/logs/`, `media/`, `outgoing/`,
  `copper/copper.json`, or `.wwebjs_auth/`.
- **`whatsapp-web.js` is an unofficial WhatsApp client.** Automating WhatsApp is against its Terms
  of Service. Use this for personal, human-scale conversation at your own risk; never bulk-send.
- **Nothing is sent without you.** You press Enter on every message. Copper never messages anyone
  on its own.

## License

MIT — see [`LICENSE`](LICENSE). Use it, change it, share it.
