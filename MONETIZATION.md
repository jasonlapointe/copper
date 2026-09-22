# Copper — monetization plan

Working plan for turning Copper into a business. Draft to iterate on together; nothing here is
locked. See `PRODUCT.md` for what we're selling and `VALUES.md` for the lines we won't cross.

## What we're selling

Not "translation" — that's a commodity racing to zero. We sell **presence across a language
barrier**: your family, your friend, your counterpart comes through as *themselves*. The moat is
the **people-network + system-prompt craft** (the persona-aware context), not the raw model, which
anyone can call. That craft, the product polish, and the trust are what a competitor can't copy by
wrapping an API.

## Who pays, and why (in order of how warm the lead is)

1. **Cross-language families & couples** (our origin, and the most emotionally sticky). People pay
   for staying close to family abroad — this is a "never cancel" category once it's part of daily life.
2. **Expat / immigrant communities** — talking with relatives in the old country; community groups.
3. **Small international teams & solo operators** — a freelancer and an overseas client, a founder
   and an overseas supplier; a shared Telegram group that just works in both languages.
4. **Partner channels** (you said you have partners lined up) — resellers/communities who bring
   whole groups of users at once. Highest-leverage acquisition; design a referral/revenue-share.

## Pricing model

Telegram has **native payments (Telegram Stars)** for bots — charge a subscription in-platform with
no Stripe/merchant setup to launch (add Stripe/cards later for larger plans). Proposed tiers:

| Tier | Who | What | Rough price |
|---|---|---|---|
| **Free** | Try-before-buy | 1 chat, capped messages/day, basic context | $0 |
| **Family** | Households | A few chats, full persona memory, unlimited human-scale volume | ~$5–8/mo |
| **Pro** | Power users / small teams | More chats, priority latency, richer context, media | ~$15–25/mo |
| **Partner / white-label** | Communities & resellers | Bulk seats, co-branding, revenue share | Custom |

Anchor on value (staying close to people you love), not on "cost per translation."

## Unit economics (why the margin works)

At Gemini 2.5 Flash on Vertex, a translated message costs a fraction of a cent; a heavy family user
runs a few hundred messages/month → **single-digit cents to low dollars of model cost per user**.
Cloud Run scales to zero, so idle users cost ~nothing. A ~$5–8/mo plan carries a healthy gross
margin even before optimization (caching, batching, model tiering by plan). The cheap-model choice
we already made is the thing that makes consumer pricing viable.

## Go-to-market (leveraging your testers & partners)

- **Phase 0 — Private beta (now).** Your lined-up testers + the family use case. Goal: prove
  retention and gather the phrases/edge cases that sharpen the persona prompts. No pricing yet;
  learn what "it sounds like me" really requires.
- **Phase 1 — Paid beta.** Turn on Stars, Free + Family tiers, to the same warm circle + first
  partner cohort. Goal: first revenue, validate willingness to pay, watch churn.
- **Phase 2 — Partner channel.** Formalize revenue-share with the partners; they bring groups.
  This is the scalable acquisition engine — one partner = many users.
- **Phase 3 — Broaden.** Open signup, add languages/media/Pro features, consider white-label.

## What to instrument from day one

Retention (do they keep talking through it weekly?), messages/user, cost/user, chats/user, and the
qualitative signal that matters most: **"does it sound like me?"** — collect thumbs/edits on
translations to keep improving the persona craft. Keep the per-turn usage log we already have and
extend it.

## Risks & how we hold the line

- **Platform dependence (Telegram).** Mitigate by keeping the engine transport-agnostic (it already
  is) so WhatsApp Business API, Discord, etc. are add-ons, not rewrites.
- **Model commoditization.** Our defensibility is the persona/context craft + trust + UX, not the
  model. Keep investing there; keep the model swappable (it already is, via one env var).
- **Privacy & trust (non-negotiable).** We store people's messages and personas — treat that as
  sacred: encryption, clear data ownership, easy deletion, never sell data. Trust is the business.
- **Quality bar.** A bad translation in an intimate conversation is worse than none. Guard the
  dignity bar; measure it.

## The ask (what "monetize with me" needs next)

1. Lock Phase 0 scope and get the testers onto the live bot.
2. Decide the first two tiers and turn on Telegram Stars.
3. Introduce the partners; design the revenue-share.
4. Stand up the trust essentials (data ownership, deletion, a simple privacy page) before real users.

We build; you bring the testers, partners, and market read. Let's go.
