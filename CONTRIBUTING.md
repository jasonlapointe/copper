# Contributing to Copper

This is a small, shared project. The rules below keep `master` always-working and keep everyone's
private data private. **`master` is protected**: nobody pushes to it directly — every change lands
through a pull request that the maintainer (Jason) reviews and approves.

## The golden rule

**Never commit private data.** No real message history, contact details, phone numbers, photos, or
your WhatsApp session. The `.gitignore` already blocks `copper/people/`, `copper/store/`,
`copper/logs/`, `media/`, `outgoing/`, `copper/copper.json`, and `.wwebjs_auth/`. If you add a new
kind of private data, add it to `.gitignore` in the same change. When in doubt, run
`git status` before committing and make sure nothing personal is staged.

## Workflow: branch → test → pull request

1. **Get the latest `master`:**
   ```bash
   git checkout master
   git pull origin master
   ```
2. **Create your own branch** — never work on `master`:
   ```bash
   git checkout -b ilyas/short-description      # e.g. ilyas/kazakh-keyboard
   ```
3. **Make your change to the product (`bot/`), then run and test it yourself:**
   ```bash
   cd bot && dotnet build             # must build with 0 errors
   # run it in local long-poll mode against your OWN bot + a test group:
   #   set TELEGRAM_BOT_TOKEN and GCP_PROJECT, then: dotnet run
   ```
   Confirm the bot starts and translates in a test Telegram group. Test on your *own* bot and your
   *own* test chats — never against someone else's conversations or data. (`bot/` is the product;
   `copper/`, `wa.js`, `telegram/` are legacy — see the README.)
4. **Commit and push your branch:**
   ```bash
   git add -p                         # review each change as you stage it
   git commit -m "Clear description of what and why"
   git push -u origin ilyas/short-description
   ```
5. **Open a pull request** against `master` (the push output prints a link, or use
   `gh pr create`). Describe what you changed, why, and how you tested it.
6. **Wait for review.** Jason reviews the PR, may ask for changes, and approves it. Only an
   approved PR can merge — that's enforced by branch protection, not just politeness.

## What makes a PR easy to approve

- **It builds and you ran it.** Say so in the PR description: what you tested and what you saw.
- **It's one focused change.** Small PRs get reviewed fast; large mixed ones stall.
- **You wrote down any real decision** in [`copper/DECISIONS.md`](copper/DECISIONS.md) — a new row
  with the date and the *why*. This is how we avoid re-arguing settled choices.
- **It respects the design.** Keep logic in the deterministic C# pipeline where possible; only use
  the language model for the two translation "edges." See [`copper/SPEC.md`](copper/SPEC.md).
- **No private data.** (Yes, it's worth saying twice.)

Questions are always welcome — open a draft PR or an issue and ask. Breaking things on your own
branch is how you learn; that's exactly what branches are for.
