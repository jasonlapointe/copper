# Copper — Spec

> Core values are locked in [VALUES.md](VALUES.md) — harmony, dignity, fidelity,
> knowing over rendering, consent & trust. They outrank everything below.

**One-liner:** A chat agent that sits between Jason (English) and one WhatsApp contact
(Russian/Kazakh), bridging meaning and context — not just words — and adapting to how the
two people actually talk over time.

## Mission

**Language is just friction. Copper is the Rosetta Stone** — not a dictionary between two
languages but a live, growing model of *people and their interactions* that makes the language
barrier disappear. The difference between a translation engine and a real interaction is
**knowing the people on both ends**.

**Dignity is non-negotiable.** Word-for-word translation and foreign-sounding phrasing make
smart people sound less than they are — polyglots get dismissed over an accent or an odd word
choice every day. Copper is the social glue: each person always comes across in the other's
language as they truly are — fluent, intelligent, fully themselves. If the reader can tell
there was a language barrier, Copper failed. A translation engine renders words; Copper renders *Marat* — who he is,
what he does, what he cares about, how he phrases things, what's happening in his family's
life. Human to human, across the cultural divide. Every feature is judged against this:
does it deepen Copper's model of the two people it bridges?

**The people network is Copper's core asset** — `people/*.md`, one file per person, both
sides modeled: Jason (voice, context), the contact, and everyone who lives in the
conversations (Sabira, Gulmira, ...). Relationships link the files; the agent reads the
network every turn and writes back what it learns about *anyone*. It grows from three
sources, in order of value:
1. **The conversation itself** — every exchange teaches register, vocabulary, interests,
   life events. Copper mines each turn and records what lasts.
2. **Jason** — Copper actively asks. When a gap in its understanding matters ("what does
   Marat do for work?", "what's the dombra about?"), it asks Jason one short question
   rather than translating blind.
3. **Materials Jason brings** — a LinkedIn profile, photos, backstory. Jason supplies or
   approves these; Copper folds them into the profile. (Copper doesn't scrape people on
   its own — Jason drives what enters the model.)

## MVP scope (what we're validating)

Prove, over a few real turns with Marat, that:

1. Jason can talk to Copper in plain English ("what did he say?", "tell him we'll call Sunday").
2. Copper reads the real chat, and presents Marat's messages as consumable English texts —
   with context (who's Sabira, what's the dombra thread) — not raw translation output.
3. Copper renders Jason's replies in natural local language, in Jason's voice, shows a
   back-translation, and sends only on Jason's console confirm.
4. Copper records durable learnings to the contact profile so turn 10 is better than turn 1.
5. Copper demonstrates *knowing the person*: it references context a translation engine
   couldn't (the trip, the dombra, who Sabira is), and it asks Jason when a gap matters.

Anything not needed to validate those five things is out of scope for MVP (see DECISIONS.md).

## Architecture (MVP)

```
Jason ⇄ copper (C# console chat)
            ├── claude CLI headless (-p, --resume) — the brain, on this machine's Claude
            │     session (subscription auth, NO API key); strict JSON contract:
            │     { reply, action(read/status), draft(text+back_translation), learned[] }
            ├── node wa.js serve — ONE persistent WhatsApp bridge process (JSON-lines stdio):
            │     reads, sends, status, and the LIVE FEED of incoming messages
            └── people/*.md — the people network (both sides + everyone mentioned)
```

- **Program.cs** — console chat REPL + live feed: incoming messages print (📩) instantly and
  fold into the next turn. `you>` in, `copper>` out. `quit` exits.
- **Agent.cs** — runs the contract loop: brain answers JSON; Copper executes `action` (read/
  status via the bridge), gates `draft` behind the console y/N, writes `learned` notes to the
  people network. Nothing sends without Jason's yes — enforced in code.
- **ClaudeCli.cs** — spawns `claude -p --output-format json` (session continuity via
  `--resume`); clears `CLAUDECODE` so Copper can be launched from a Claude Code terminal.
- **WaService.cs** — owns the single `wa.js serve` process; request/response over stdin/stdout,
  live-message events. (Only one wa.js process can run — Chromium locks the profile.)
- **AppConfig.cs / copper.json** — bridge dir, contact (name, chat name, profile file), self file.
- **people/*.md** — the network; `learned` entries append dated notes, new people get new files.
- **TurnLogger.cs → logs/turns.jsonl** — one JSON line per turn: ts, user text, reply, actions,
  sends/declines, learned, cli calls, tokens, cost, duration. Usage monitoring.

## Invariants (do not break)

- **Nothing is sent without Jason's explicit y at the console.** The gate lives inside the
  send tool in our code, not in the prompt.
- Copper reads/writes **only the configured contact's chat**.
- Reading produces **no read receipts** (bridge has sendSeen removed).
- Outgoing messages are **Jason's meaning in Jason's voice** — natural local phrasing, zero embellishment.
- No credentials anywhere: WhatsApp links via QR (user-scanned); Claude API key comes from the
  environment (`ANTHROPIC_API_KEY` or `ant auth login` profile).

## Run

```
cd C:\dev\marat\copper
dotnet run          # serves the chat UI at http://localhost:5077
```

Prereqs: Node bridge linked (`node wa.js login` once in C:\dev\marat), `claude login` done once
(the brain rides the machine's Claude Code session — no API key). The UI is a local web chat:
bubbles, live 📩 incoming messages, and a draft card whose **Send** button is the only path to
transmission (typing instead of pressing Send/Cancel = decline with feedback).

## Validation plan

One real exchange with Marat: check → Jason replies → confirm → send → Marat answers → check.
Success = Jason never touches Russian/Kazakh, never re-explains context Copper already knew,
and nothing went out unconfirmed. Then iterate.

**Monitoring (logs/turns.jsonl):** per turn — tools run, sends/declines, tokens, duration,
and `learned` (profile notes saved). The learning metrics to watch across sessions:
- `learned` per session should be > 0 early on (the model is growing) and taper as it matures.
- Re-explanations by Jason should trend to zero — if he keeps repeating context, the profile
  isn't being used or written well.

## Roadmap (planned phases)

- **Onboarding = authenticate, then choose conversations to import.** Each user links their own
  WhatsApp (the QR scan) and is then shown their conversation list; they pick which chats to
  bridge. Every chosen conversation becomes a bridged contact with its own `people/` file, seeded
  from that chat's history and participants. No manual `copper.json` editing — the whole
  contact/people setup comes from selecting conversations. The user curates what's kept; the
  bridge only proposes. (Implies multi-contact support, which today's one-contact-per-config
  model defers.)
- **Angular + Bearing front end.** Port the vanilla-HTML UI to the org's Angular/Bearing stack
  once the concept is stable (the C# backend already exposes clean JSON endpoints).
- **Persistence to a database.** Move `store/`, `logs/`, and the people network from local
  files to a database when the volume or multi-device need justifies it.

## Later (explicitly deferred)

Multi-contact switching, media download, inbound polling/notifications, GUI, packaging,
streaming responses, session persistence of conversation history (profile notes persist; the
chat transcript in WhatsApp itself is the durable history).
