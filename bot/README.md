# copper-bot

The deployable, cloud-ready Telegram version of Copper: a single C# service that translates a
cross-language group so everyone reads their own language. It talks to Telegram and Anthropic
directly over HTTPS — no Node bridge, no CLI, no local session. This is the go-forward product;
the `../copper` web app is the proof-of-concept it grew from.

## What it does

Add the bot to a group (or DM it). When someone writes, the bot detects the language, translates
the message into the group's *other* language, and posts the translation as a reply — using the
people network for context and dignity. It **auto-seeds** each person on first contact with their
detected language (no setup). One LLM call per message, on a cheap model (Haiku by default).

## Run it locally (test with family before deploying)

Needs Node? No — this one is pure .NET. You need two secrets:
- `TELEGRAM_BOT_TOKEN` — from @BotFather.
- `ANTHROPIC_API_KEY` — from https://console.anthropic.com (pay-per-use; a few dollars on Haiku).

```bash
cd bot
export TELEGRAM_BOT_TOKEN=...   # (Windows: set "TELEGRAM_BOT_TOKEN=...")
export ANTHROPIC_API_KEY=...
dotnet run
```

With no `PUBLIC_URL` set it runs in **long-poll** mode — no public URL needed. Add the bot to a
group (turn Group Privacy OFF in @BotFather first) or DM it, and watch it translate. State (the
people network) lands in `bot/data/` locally.

## Configuration (all via environment)

| Env var | Default | Meaning |
|---|---|---|
| `TELEGRAM_BOT_TOKEN` | (required) | BotFather token |
| `ANTHROPIC_API_KEY` | (required) | Claude API key |
| `COPPER_MODEL` | `claude-haiku-4-5` | Model id — bump to `claude-sonnet-5` for more polish |
| `COPPER_LANGS` | `en,ru` | The two languages to bridge |
| `PUBLIC_URL` | (unset) | Set → webhook mode (cloud). Unset → long-poll (local) |
| `DATA_DIR` | `data` | Where the people network is stored |
| `PORT` | `8080` | HTTP port (Cloud Run sets this) |

## Deploy to the cloud

See [`../telegram/DEPLOY.md`](../telegram/DEPLOY.md). In short: the GitHub Action builds this
folder's `Dockerfile` and deploys to GCP Cloud Run, which sets `PUBLIC_URL` so the service runs in
webhook mode. Secrets (`ANTHROPIC_API_KEY`, `TELEGRAM_BOT_TOKEN`, GCP creds) live in GitHub, never
in the repo.

## Not yet (MVP scope)

Text only (media is a TODO), translates between two languages (N-language per-reader fan-out is a
follow-up), and stores state on local disk (move to a bucket/DB for durable cloud state).
