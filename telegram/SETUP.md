# Telegram setup (from zero)

Never used Telegram? This gets you from nothing to a working bot in ~10 minutes.

## A. Get a Telegram account
1. Install **Telegram** on your phone (App Store / Play Store, free).
2. Open it → **Start Messaging** → enter your phone number → type the code it texts you → set a name.
3. Optional: install **Telegram Desktop** on your computer and log in by scanning the QR from the
   phone app — convenient for testing next to Copper.

Vocabulary: people and bots have `@usernames`; you open a bot's `@username` and tap **Start** to
begin. **@BotFather** is Telegram's official bot for creating bots.

## B. Create your bot + get the token
1. In Telegram search, open **@BotFather** (blue checkmark = official). Tap **Start**.
2. Send **`/newbot`**.
3. Give it a **name** (display name, e.g. `Copper`).
4. Give it a **username** ending in `bot` (e.g. `copper_yourname_bot`).
5. BotFather returns a **token** like `8123456789:AAH...`.
   **Treat the token like a password.** Never commit it — it only ever goes in an environment
   variable. To retrieve it later: BotFather → **`/mybots`** → your bot → **API Token**.

## C. Run the bridge and see a message arrive
```bash
# Windows (cmd/PowerShell via the shell here):
cd /c/dev/marat && set "TELEGRAM_BOT_TOKEN=PASTE_TOKEN" && node telegram/tg.js serve
# macOS/Linux:
TELEGRAM_BOT_TOKEN=PASTE_TOKEN node telegram/tg.js serve
```
Open your bot by its `@username` in Telegram, tap **Start**, send "hello". The bridge should print
a `ready` line then a `message` line — your first message flowing into Copper.

## The common gotcha: group privacy
For the **translated group** shape, a bot by default only sees messages addressed to it. To let it
read (and translate) all group messages: **BotFather → `/mybots` → your bot → Bot Settings →
Group Privacy → Turn OFF**, then add the bot to the group. For a **1:1 relay** bot, skip this —
direct messages to the bot are always visible.

## Next
Once messages arrive in the bridge, wire it into Copper's engine — see [`PLAN.md`](PLAN.md) step 2.
