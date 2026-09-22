# Deploying copper-bot

The bot runs on **GCP Cloud Run** as a Telegram **webhook** service. A GitHub Action builds this
folder's `Dockerfile` and deploys on every merge to `master`. All of this is already provisioned in
project **`copper-babel-bot`** — this doc is the reference for how it fits together and how to do it
again from scratch.

## Architecture (what runs where)

- **Cloud Run** hosts the container; it scales to zero and wakes on each Telegram webhook POST.
- **Gemini 2.5 Flash on Vertex AI** does the translation, authenticated by the Cloud Run **service
  account** (no API key). Model/region are env vars (`COPPER_MODEL`, `COPPER_REGION=global`).
- **Firestore** stores the people-network (personas), so it survives scale-to-zero.
- **Secret Manager** holds `TELEGRAM_BOT_TOKEN` and `WEBHOOK_SECRET`; Cloud Run injects them as env.
- The service verifies Telegram's `X-Telegram-Bot-Api-Secret-Token` header against `WEBHOOK_SECRET`
  and registers its own webhook (with that secret) on startup.

## One-time GCP setup (already done for copper-babel-bot)

```bash
PID=copper-babel-bot
gcloud services enable aiplatform.googleapis.com run.googleapis.com \
  artifactregistry.googleapis.com cloudbuild.googleapis.com \
  secretmanager.googleapis.com firestore.googleapis.com --project=$PID
gcloud firestore databases create --location=nam5 --project=$PID
gcloud artifacts repositories create copper --repository-format=docker --location=us-central1 --project=$PID
# Enable Gemini in Vertex (Google model → immediate quota). Grant the runtime (compute) SA
# roles/aiplatform.user, roles/datastore.user, roles/secretmanager.secretAccessor.
# Store secrets: TELEGRAM_BOT_TOKEN and a high-entropy WEBHOOK_SECRET in Secret Manager.
```

## GitHub secrets (used by the deploy Action)

`GCP_SA_KEY` (deploy service-account key), `GCP_PROJECT`, `GCP_PROJECT_NUMBER`, and the repo
variable `DEPLOY_ENABLED=true`. No model API key — Vertex auth is the runtime identity.

## Deploy

- **Automatic:** merge to `master` → `.github/workflows/deploy.yml` builds and deploys.
- **Manual (first time / debugging):**
  ```bash
  PID=copper-babel-bot; REGION=us-central1; NUM=48720416559
  IMG="$REGION-docker.pkg.dev/$PID/copper/copper-bot:$(git rev-parse --short HEAD)"
  gcloud builds submit bot --tag "$IMG" --project=$PID
  gcloud run deploy copper-bot --image "$IMG" --region "$REGION" --project=$PID --allow-unauthenticated \
    --set-env-vars "^##^PUBLIC_URL=https://copper-bot-$NUM.$REGION.run.app##GCP_PROJECT=$PID##COPPER_REGION=global##COPPER_MODEL=gemini-2.5-flash##COPPER_LANGS=en,ru" \
    --set-secrets "TELEGRAM_BOT_TOKEN=TELEGRAM_BOT_TOKEN:latest,WEBHOOK_SECRET=WEBHOOK_SECRET:latest"
  ```
  (The `^##^` sets `##` as the env-var delimiter so the comma in `COPPER_LANGS` stays literal.)

## Verify a deploy

```bash
curl -s https://copper-bot-<NUM>.us-central1.run.app/        # -> "copper-bot ok"
# webhook rejects unsigned requests (401) and accepts signed ones (200):
curl -s -o /dev/null -w "%{http_code}\n" -X POST .../telegram/webhook -d '{}'   # 401
```

## Budget

~$5–10/month at family volume, usually less: Cloud Run scales to zero, Gemini Flash is pennies per
message, Firestore/Registry/Build sit in free tiers.
