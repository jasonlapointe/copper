# Deploying the Telegram bot to the cloud

Target: **GCP Cloud Run** (AWS App Runner is an almost identical alternative — same shape, swap the
deploy step). A GitHub Action builds a container on merge to `master` and ships it to Cloud Run,
which runs the bot and receives Telegram updates by **webhook**.

## Why the laptop build must change first

What runs locally today is laptop-shaped and can't deploy as-is:
- **Two processes** — a C# app that spawns the Node `wa.js`/`tg.js` bridge.
- **The Claude CLI** using your personal subscription login (a token on your machine).
- **Local files** for state (people, store, logs).

The cloud version collapses this into **one C# service**:
1. **Drop the Node bridge.** Telegram's Bot API is plain HTTPS — the C# service calls it directly
   (`getMe`, `sendMessage`, `setWebhook`). The `tg.js` skeleton was useful for local testing and to
   prove the protocol; the cloud service reimplements those few calls in C#.
2. **Webhook, not long-poll.** The service is already ASP.NET, so add a `POST /telegram/webhook`
   endpoint. On startup it calls Telegram `setWebhook` to its own public Cloud Run URL. Cloud Run
   scales to zero and wakes on each update — cheap for a personal bot.
3. **Brain via the Anthropic API, not the CLI.** Replace `ClaudeCli` (which shells to `claude`)
   with a small HTTP client using `ANTHROPIC_API_KEY`. Same two edges (`TranslateAsync`,
   `ProcessAsync`) — only how the model is called changes.
4. **State persistence.** Cloud Run's disk is ephemeral. Start simple: keep the people network and
   caches in a **GCS bucket** (or Firestore) instead of local files. Acceptable interim: run with
   ephemeral state — the auto-seed rebuilds the people network as people speak, and the translation
   cache just re-fills. Decide per how much history matters. (This is the "DB later" item arriving.)

None of this touches the *engine* — the prompts, the people-network logic, the two edges, the
dignity/grounding rules all carry over. It's the transport and the plumbing that change.

## The pipeline (`.github/workflows/deploy.yml`)

On merge to `master` (and manual `workflow_dispatch`):
1. Build and publish the C# service into a container image.
2. Push the image to Artifact Registry.
3. `gcloud run deploy` the image to Cloud Run.
4. The service calls `setWebhook` on boot, so Telegram starts delivering updates.

The workflow file is scaffolded but **inert until the refactor above is done and the secrets below
exist** — it's `workflow_dispatch` only for now so it doesn't fail on every push.

## What you provide (secrets live in GitHub, never in the repo)

Add these under the repo's **Settings → Secrets and variables → Actions**:
- `ANTHROPIC_API_KEY` — a Claude API key from console.anthropic.com (pay-per-use; this replaces the
  personal CLI login that can't run on a server).
- `TELEGRAM_BOT_TOKEN` — the BotFather token (the one currently in `telegram/.env`; regenerate it
  first since it passed through chat).
- `GCP_PROJECT` — your GCP project id.
- `GCP_SA_KEY` (or Workload Identity Federation) — a service account with permission to deploy to
  Cloud Run and push to Artifact Registry.

I never handle these — you paste them into GitHub's secret store; the Action reads them at run time.

## Sequence

1. **Refactor to the single cloud service** (the real work — steps 1–4 above). Biggest chunk.
2. **You set up GCP** (a project, enable Cloud Run + Artifact Registry, make the service account)
   and add the four GitHub secrets.
3. **Flip the deploy workflow on** (add the `push: master` trigger) and merge — the bot deploys.
