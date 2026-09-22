# Copper

**A Telegram bot that translates any language into each person's own — in real time, and in
their own voice.** Add it to a conversation and two people who don't share a language just talk;
each reads in their language, and each comes across as *themselves*, not "translated." The people
model and the system prompt are the product. See **[PRODUCT.md](PRODUCT.md)** for the full vision
and **[MONETIZATION.md](MONETIZATION.md)** for the business.

---

## 👉 The product lives in [`bot/`](bot/)

`bot/` is **copper-bot**: a single C# service, deployed to **GCP Cloud Run**, that runs the live
Telegram bot. It's the only thing you need to build, run, or deploy. Start at
**[`bot/README.md`](bot/README.md)**.

- **Transport:** Telegram (webhook)
- **Model:** Gemini 2.5 Flash on GCP Vertex AI (no API key — service-account auth); model is one
  env var, so Claude is a drop-in alternative
- **Memory:** the people-network (personas) persists in **Firestore**, scoped per chat
- **Deploy:** `.github/workflows/deploy.yml` auto-deploys on merge to `master`
- **Design record:** [`copper/DECISIONS.md`](copper/DECISIONS.md) (append-only, newest at the
  bottom — read #38+ for the current architecture) and [`copper/VALUES.md`](copper/VALUES.md)

## History — earlier forms (kept for reference, not the product)

Copper reached the Telegram bot through two proof-of-concept forms. They're kept for their history
and reasoning trail, but **new work targets `bot/`**; don't build on these:

- **`copper/`** — the second form: a C# **web-UI** app that bridged one WhatsApp contact for a
  single user in the browser, using a local `claude` CLI and local file storage. Still runnable on
  the original machine; superseded by `bot/`. (Also holds the canonical `DECISIONS.md`, `VALUES.md`,
  and the original `SPEC.md`.)
- **`wa.js`** + **`patches/`** — the first form: a Node WhatsApp-Web bridge (`whatsapp-web.js`).
  The patches fix real library breakages and must not be deleted if that bridge is ever run.
- **`telegram/`** — an intermediate Node Telegram-bridge skeleton and its setup notes; the C#
  `bot/` took the "reimplement directly" path instead. `telegram/SETUP.md` still has a useful
  beginner's guide to creating a bot with @BotFather.

Decisions #2–#37 in the log describe these historical forms; #38+ describe the current product
(see decision #47 for the map). The **values** and the **people-network / persona ideas** carry
forward; the old transport and plumbing do not.

## License

MIT — see [LICENSE](LICENSE). Contributions: [CONTRIBUTING.md](CONTRIBUTING.md).
