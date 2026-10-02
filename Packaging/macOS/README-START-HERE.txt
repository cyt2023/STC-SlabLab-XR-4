STC SlabLab — macOS
=====================

FIRST USE
1. Double-click "Setup Backend.command" and wait for setup to finish.
2. Double-click "Start STC SlabLab.command" whenever you want to use the app.
3. If macOS blocks the app, right-click it once and choose Open.

DATA SOURCE (LIVE OR BUNDLED)
The app reads the Wave data through a local service that prefers the live Wave
gateway, so the machine should be connected to the Tailscale network that hosts
it (and a .env file with WAVE_API_KEY should sit next to this file).
This package already includes that .env, so nothing has to be configured — but
it also means the Wave API key travels with the package. Keep it to the people
who are supposed to use it. The file is written with owner-only permissions
(chmod 600), so other accounts on the machine cannot read it; the app only ever
sends the key to the Wave gateway, and it is stripped from any error text the
app reports.
If the gateway cannot be reached, the app falls back to the dataset bundled in
For_VR/WaveCache, which needs no network, no Tailscale and no API key.
The opening screen says which one answered ("DATA SOURCE · LIVE" or
"DATA SOURCE · LOCAL CACHE (NO NETWORK)"), so the operator never has to guess
where the values came from.
On a cold start the bundled backend can need a few seconds to come up; the app
keeps trying by itself ("Reconnecting to the Wave server (retry 1 of 3)...")
and reaches the data without anyone pressing Retry.
Open http://127.0.0.1:8020/health and look at "waveMode" to see which source
answered: "live" (gateway), "cache" (bundled dataset) or "offline" (neither,
with the reason in the log).
If a window is neither live nor bundled, the app's message names the window, the
field and the folder it looked in — so a failure says what to fix (join the
tailnet, or run "Prepare Offline Data.command" while you still have network).
To refresh the bundled window while you still have network, double-click
"Prepare Offline Data.command" (it hydrates the window and copies it into
For_VR/WaveCache; each 24h window is only a few MB).

NOTE FOR THE FIRST RUN
"Setup Backend.command" installs the Python dependencies, so that one step needs
internet access (PyPI). After it finishes, Live mode needs the Tailscale network
and the bundled cache needs nothing at all.

DATA
The included For_VR/UnityRaw folder is detected automatically. To use another
dataset, choose its root folder on the first page. A variable folder must contain
matching .raw and .raw.ini files.

SNAPSHOTS
"Snapshot" in the action bar saves the current screen as a PNG. The file lands in
~/Library/Application Support/STC SlabLab/STC SlabLab Flat/Captures, and the
notice line names the file right after it is written.

KEYBOARD AND HINTS
Hovering a button shows a one-line explanation of what it does; "Help" (or H /
F1) lists every key. The ones worth remembering:
  Page Up / Page Down   previous / next step
  X                     reset view (camera and both Fields)
  P                     snapshot
  T                     type the Time range instead of dragging it
  Y                     toggle the slab frame
  Esc                   close the overlay

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
