# Releasing copper-bot

How a change to `bot/` (the copper-bot Telegram service) gets to production on GCP Cloud Run.
For the architecture and one-time GCP provisioning, see [`bot/DEPLOY.md`](bot/DEPLOY.md) — this
file is only about the release flow, rollback, and manual deploys.

## Release flow

```
branch  →  PR (base master)  →  CI green  →  merge to master  →  auto-deploy  →  health check
```

1. **Branch** off `master` and make your change under `bot/`.
2. **Open a PR** into `master`. `.github/workflows/ci.yml` runs on the PR and builds `bot/`
   (and the legacy `copper/` web app). CI is the merge gate — keep it green.
3. **Merge to master.** `.github/workflows/deploy.yml` fires on the push to `master`.
   It only runs while the repo variable `DEPLOY_ENABLED=true`.
4. **Auto-deploy.** The workflow:
   - re-builds `bot/` (`build` job) as a compile gate, then in the `deploy` job
   - builds and pushes a container image tagged with the exact commit SHA
     (`…/copper/copper-bot:<github.sha>`), so every deploy is traceable,
   - `gcloud run deploy`s that image,
   - **health-checks** the live service: `GET /` must return `copper-bot ok`, or the job fails.

Images are tagged by git SHA (never a moving `:v1`/`:latest`), which is what makes the
rollback below possible.

## Roll back

Every past deploy is still an image (`:<sha>`) in Artifact Registry and a Cloud Run revision.
Two ways to go back, fastest first:

**A. Shift traffic to the previous revision (no rebuild):**
```bash
PID=copper-babel-bot; REGION=us-central1
gcloud run revisions list --service copper-bot --region "$REGION" --project "$PID"
# roll all traffic to a known-good revision:
gcloud run services update-traffic copper-bot --region "$REGION" --project "$PID" \
  --to-revisions <REVISION_NAME>=100
```

**B. Redeploy a previous image by SHA:**
```bash
PID=copper-babel-bot; REGION=us-central1; SHA=<good-commit-sha>
gcloud run deploy copper-bot --project "$PID" --region "$REGION" \
  --image "us-central1-docker.pkg.dev/$PID/copper/copper-bot:$SHA" --allow-unauthenticated
```
(Use `--to-revisions` for the quickest recovery; redeploy-by-SHA when you also want a fresh
revision from a specific commit. The service env/secrets carry over from the running revision.)

## Manual deploy

- **From CI:** Actions → *Deploy (Cloud Run)* → **Run workflow** (`workflow_dispatch`). Deploys
  the SHA at the tip of the chosen ref, same as an auto-deploy.
- **From a laptop:** the exact `gcloud builds submit` + `gcloud run deploy` commands (with the
  correct env-var/secret flags) live in [`bot/DEPLOY.md`](bot/DEPLOY.md) → *Deploy → Manual*.
  Note the build uses `--default-buckets-behavior=regional-user-owned-bucket` so Cloud Build
  logs land in a project-owned regional bucket the deploy identity can access.

## Staging — recommended, not yet required

copper-bot is a single-environment personal product with one Telegram bot token and a family
allowlist. A separate staging env would mean a second bot token, service, and Firestore scope
for little benefit today. The compile gate + SHA-tagged images + automatic post-deploy health
check already catch the common failure (a bad revision never keeps traffic on a green check).
Revisit staging if the bot gains non-family users or the blast radius of a bad deploy grows —
at that point add a `copper-bot-staging` Cloud Run service fed by a `staging` branch before
promoting to `master`.
