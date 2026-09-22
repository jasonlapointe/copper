# copper-bot

The deployable, cloud-ready Telegram version of Copper: a single C# service that translates a
cross-language group so everyone reads their own language. It talks to Telegram and to **Gemini on
GCP Vertex AI** directly over HTTPS — no Node bridge, no CLI, no local session, and **no API key**
(it authenticates to Vertex with its GCP service account). This is the go-forward product; the
`../legacy/` trees are the proof-of-concept forms it grew from.

## What it does

Add the bot to a group (or DM it). When someone writes, the bot detects the language, translates
the message into the group's *other* language, and posts the translation as a reply — using the
people network for context and dignity. It **auto-seeds** each person on first contact with their
detected language (no setup), and personas persist in **Firestore** so they survive restarts. One
model call per message, on a cheap model (Gemini 2.5 Flash by default).

## Run it locally (test before deploying)

Pure .NET — no Node. You need:
- `TELEGRAM_BOT_TOKEN` — from @BotFather.
- `GCP_PROJECT` — a GCP project with Vertex AI + Firestore enabled, and `gcloud auth application-default login` done (the app uses those credentials locally; on Cloud Run it uses the service account).

```bash
cd bot
export TELEGRAM_BOT_TOKEN=...    # (Windows: set "TELEGRAM_BOT_TOKEN=...")
export GCP_PROJECT=copper-babel-bot
dotnet run
```

With no `PUBLIC_URL` set it runs in **long-poll** mode — no public URL needed. Add the bot to a
group (turn Group Privacy OFF in @BotFather first) or DM it, and watch it translate.

## Configuration (all via environment)

| Env var | Default | Meaning |
|---|---|---|
| `TELEGRAM_BOT_TOKEN` | (required) | BotFather token |
| `GCP_PROJECT` | (required) | GCP project for Vertex AI (Gemini) + Firestore (personas) |
| `COPPER_MODEL` | `gemini-2.5-flash` | Vertex model id — e.g. `claude-haiku-4-5` to use Claude instead |
| `COPPER_REGION` | `global` | Vertex location |
| `COPPER_LANGS` | `en,ru` | The two languages to bridge |
| `PUBLIC_URL` | (unset) | Set → webhook mode (cloud). Unset → long-poll (local) |
| `WEBHOOK_SECRET` | (required in webhook mode) | Shared token Telegram echoes back so the webhook can verify requests are genuinely from Telegram |
| `COPPER_ALLOWED_CHATS` | (empty = all) | Comma-separated chat ids to restrict the bot to (bounds cost/abuse) |
| `PORT` | `8080` | HTTP port (Cloud Run sets this) |

## Deploy to the cloud

See [`DEPLOY.md`](DEPLOY.md). In short: the GitHub Action builds this folder's `Dockerfile` and
deploys to GCP Cloud Run, which sets `PUBLIC_URL` so the service runs in webhook mode. Secrets
(`TELEGRAM_BOT_TOKEN`, `WEBHOOK_SECRET`) come from GCP Secret Manager; GCP deploy creds are GitHub
secrets. No model API key — Vertex auth is the service account's identity.

## Security & persistence notes

- The webhook validates Telegram's `X-Telegram-Bot-Api-Secret-Token` header against `WEBHOOK_SECRET`
  and rejects anything else before doing any work.
- Personas live in Firestore, scoped per chat — one chat's people are never sent as context to
  another chat's translation.
- Other bots' messages are ignored (no bot↔bot loops); an optional chat allowlist bounds spend.

## Not yet (MVP scope)

Text only (media is a TODO), translates between two languages (N-language per-reader fan-out is a
follow-up), and per-message rate limiting / a translation cache are future hardening.
