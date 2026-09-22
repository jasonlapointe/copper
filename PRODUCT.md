# Copper — what it is

> **A Telegram bot that, when added to a conversation, translates any language into each
> person's own language in real time — and not a flat, literal translation. A context-rich
> translation that carries each person's tone, attitude, and character across the language
> barrier. The translation becomes their persona. The system prompt and the per-person context
> are the product.**

That paragraph is the whole thing. Everything below serves it.

## The insight

Machine translation has existed for years and it still makes people sound stiff, foreign, and
smaller than they are — because it translates *words*. A human interpreter who knows both people
translates *them*: their warmth, their humor, their bluntness, the name they call you, the thing
they left unsaid. Copper is that interpreter, at the speed of chat, for anyone.

The barrier between people who don't share a language has never really been vocabulary. It's the
loss of self in the crossing. Copper's bet is that if you give a capable model the right system
prompt and a living, growing model of *who each person is*, the barrier disappears — two people
just talk, each in their own language, each sounding exactly like themselves.

## How it delivers that

- **Add it to a chat.** In a Telegram group (or a direct relay), Copper reads each message and
  renders it into the other person's language, posted right in the conversation.
- **It knows the people.** A "people network" — one profile per person — holds who they are, how
  they speak, their register and quirks, and what's going on in their lives. That context is fed
  to the model on every translation, so the output matches the *speaker*, not a dictionary.
- **It learns automatically.** The first time someone speaks, Copper detects their language and
  creates their profile with zero setup; over time it refines each person's "grounding" — a dated
  snapshot of who they are right now. Turn 100 is more them than turn 1.
- **Dignity is the bar.** If a reader could tell the message was translated, Copper failed.
  Faithful to meaning, never inventing, never flattening — the person comes across whole.

## Where it came from

Copper began as a way for one person to talk with family in Kazakhstan across
English/Russian/Kazakh. It grew — WhatsApp web bridge → a browser app → and finally the thing it
was always meant to be: a cloud-deployed Telegram bot anyone can add to a conversation. The family
use case proved the heart of it; the product generalizes to any people, any languages.

## The non-negotiables (see VALUES)

Harmony (bring people closer, not move text) · Dignity (no one sounds "translated") · Fidelity
(carry meaning whole, invent nothing) · Knowing over rendering (the people model is the product) ·
Consent & trust (people's words and data are theirs).

## The architecture, in one line

A single service (`bot/`) on GCP Cloud Run: Telegram webhook in, a people-network + a carefully
built system prompt as context, one Gemini-on-Vertex call per message out. Deterministic plumbing,
intelligence only at the translation edge. See `bot/README.md` and `copper/DECISIONS.md`.
