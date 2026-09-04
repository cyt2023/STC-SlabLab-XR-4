STC SlabLab — macOS
=====================

FIRST USE
1. Double-click "Setup Backend.command" and wait for setup to finish.
2. Double-click "Start STC SlabLab.command" whenever you want to use the app.
3. If macOS blocks the app, right-click it once and choose Open.

DATA
The included For_VR/UnityRaw folder is detected automatically. To use another
dataset, choose its root folder on the first page. A variable folder must contain
matching .raw and .raw.ini files.

OPTIONAL AI PROVIDER
Full Matrix has a deterministic local fallback and does not require an API key.
To enable model-generated charts or summaries, copy .env.example to .env and set
OPENAI_API_KEY, or configure the Qwen/DashScope variables documented there.

STOPPING
Quit the app normally. Double-click "Stop Backend.command" when you also want to
stop the two local analysis services.

SUPPORT
Runtime logs: .runtime/backend/logs
Unity logs: ~/Library/Logs/STC SlabLab/STC SlabLab Flat/Player.log
