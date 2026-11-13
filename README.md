# JARVIS Council

A Windows-first, .NET 8 personal AI assistant with pluggable cloud/local models, persistent memory, specialist skills, safe workspace and Office tools, public web research, and an explicitly controlled autonomous software-development workflow. A native Android companion can submit commands to the laptop over a private Tailscale network.

## Quick start

Requirements:
- Windows 10/11
- .NET 8 SDK
- Git
- An AI provider: Gemini, OpenAI, local Ollama, or a configured OmniRoute gateway

Clone the repository, open PowerShell in the repository root, then:

```powershell
dotnet restore .\JARVIS.Council.sln
dotnet build .\JARVIS.Council.sln
dotnet test .\JARVIS.Council.sln
dotnet run --project .\src\Council.App\Council.App.csproj

# Optional: inspect local readiness without starting the assistant
dotnet run --project .\src\Council.App\Council.App.csproj -- --doctor
```

The normal startup is the interactive JARVIS assistant. `--doctor` checks the configured AI provider, Ollama models, local vision/OCR, Dhan credential presence, Abaqus launcher, voice runtimes and phone-bridge configuration. It never displays secrets or starts feeds, GUI actions or solver jobs. If no provider key is configured, it starts in mock mode so the local application and tools can still be exercised.

## Windows desktop app (first usable build)

Launch the WPF Command Center and chat window from PowerShell:

```powershell
.\scripts\Start-JarvisDesktop.ps1
```

The desktop chat uses the same provider setting as the main application. For a private, no-API-credit local setup, install Ollama and the small local model stack once:

```powershell
Set-ExecutionPolicy -Scope Process Bypass
.\scripts\Install-LocalModels.ps1
$env:JARVIS_AI_PROVIDER = "ollama"
$env:OLLAMA_MODEL = "qwen3.5:2b-q4_K_M"
dotnet run --project .\src\Council.Desktop\Council.Desktop.csproj
```

The desktop conversation history is stored locally under `%LOCALAPPDATA%\JARVIS\Memory\desktop-conversation.jsonl` and protected with Windows DPAPI. The desktop chat now calls the same bounded tool-calling runtime as the console assistant, including workspace file tools, public web research, Office document tools, Indian equity monitoring, Abaqus diagnostics, specialist agents, and explicitly requested visible-desktop computer use. Voice is on-demand: press VOICE to open the microphone, press STOP to cancel capture, and toggle SPEAK to read replies aloud. The dashboard provides system telemetry and launches the chat; some dashboard cards are informational and do not imply a background agent or market feed is running.

You can begin teaching the desktop assistant immediately: send `/remember I prefer concise answers with worked engineering examples` to save a personal fact. Use `/memories` to review facts and `/forget <id>` to remove one. To teach a repeatable project practice, use `/learn when I ask for a four-point bending setup => first confirm whether the support span is divided into three equal parts`; use `/instincts` to review learned practices and `/forget-instinct <id>` to remove one. `/skills` and `/agents` list JARVIS capabilities; `/clear` clears conversation history and explicit facts but retains learned project practices. Facts and practices are encrypted with Windows DPAPI for the current Windows account and are included as context when relevant. This is explicit memory-based personalization, not fine-tuning the underlying language model. Do not store passwords, API keys, OTPs or other secrets.

## Choose an AI provider

Set configuration in the current PowerShell session. Do not commit API keys or place them in the project workspace.

### Gemini

```powershell
$env:JARVIS_AI_PROVIDER = "gemini"
$env:GEMINI_API_KEY = "YOUR_GEMINI_API_KEY"
# Optional: $env:GEMINI_MODEL = "YOUR_MODEL_ID"
dotnet run --project .\src\Council.App\Council.App.csproj
```

### Local Ollama

The default local brain is Qwen 3.5 2B Q4_K_M (about 1.9 GB). Desktop screenshots use Qwen3-VL 2B, while embedded engineering-document images use the specialist GLM-OCR model. Vision models unload after each request by default to avoid keeping both resident on an 8 GB machine.

```powershell
$env:JARVIS_AI_PROVIDER = "ollama"
$env:OLLAMA_BASE_URL = "http://localhost:11434"
$env:OLLAMA_MODEL = "YOUR_INSTALLED_MODEL_TAG"
dotnet run --project .\src\Council.App\Council.App.csproj
```

### Local eyes, ears and mouth

Install the complete local model stack in PowerShell:

```powershell
Set-ExecutionPolicy -Scope Process Bypass
.\scripts\Install-LocalModels.ps1
```

The installer downloads:
- **Brain:** Qwen 3.5 2B Q4_K_M through Ollama (about 1.9 GB).
- **Screen/figure vision:** Qwen3-VL 2B through Ollama (about 1.9 GB) for desktop screenshots, GUI grounding, engineering diagrams, meshes, plots and figure interpretation.
- **Document OCR:** GLM-OCR through Ollama for rendered PDF pages and embedded engineering-document images, with separate text and table-recognition passes. Both models are separate from the chat brain and load on demand.
- **Ears:** Whisper small multilingual through Whisper.net (about 466 MB). It is loaded only when you use `/voice`, then disposed.
- **Mouth:** Piper neural TTS with medium-quality English, Hindi and Telugu voices. Only the selected voice is loaded to synthesize each reply.

The 8 GB profile uses one Ollama inference slot, serializes specialist teams and unloads vision models after each request by default (`JARVIS_OLLAMA_VISION_KEEP_ALIVE=0`). The main model uses a 4,096-token context, Flash Attention and an 8-bit KV cache. Whisper.net's CPU runtime requires Windows 11 (or Windows Server 2022) and the Microsoft Visual C++ 2022 x64 runtime; older Windows versions fall back to Windows speech recognition. Whisper and Piper are invoked only when needed; they are not kept resident. This trades some model reload latency for memory headroom. Do not run multiple local inference servers simultaneously.

After installation, close and reopen Ollama so its new user-level memory settings take effect. Set `JARVIS_AI_PROVIDER=ollama` to use the local Qwen brain. The cloud provider remains available and is not replaced. You can disable local Whisper with `JARVIS_LOCAL_WHISPER_ENABLED=false`; Piper falls back to Windows speech if unavailable.

Piper selects Telugu or Hindi voices automatically when the response contains those scripts; English is the default. The Piper runtime is GPL-3.0 and the downloaded voice models have their own model-card licensing; JARVIS downloads them locally and does not redistribute them.


### OpenAI

```powershell
$env:JARVIS_AI_PROVIDER = "openai"
$env:OPENAI_API_KEY = "YOUR_OPENAI_API_KEY"
# Optional: $env:OPENAI_MODEL = "YOUR_MODEL_ID"
dotnet run --project .\src\Council.App\Council.App.csproj
```

### OmniRoute

OmniRoute is a gateway, not a free API grant. Connect an eligible provider in the OmniRoute dashboard and explicitly allowlist only model IDs whose current cost you have checked.

```powershell
$env:JARVIS_AI_PROVIDER = "omniroute"
$env:OMNIROUTE_BASE_URL = "http://localhost:20128/v1"
$env:OMNIROUTE_API_KEY = "YOUR_LOCAL_OMNIROUTE_GATEWAY_KEY"
$env:OMNIROUTE_MODEL = "provider-prefix/exact-model-id"
$env:OMNIROUTE_FREE_MODELS = "provider-prefix/exact-model-id"
dotnet run --project .\src\Council.App\Council.App.csproj
```

The OmniRoute adapter rejects the `auto` alias and any model ID not present in the exact allowlist. Upstream billing and quota rules remain the user's responsibility.

## What JARVIS can do

- **Conversation and memory:** optional local conversation history, deterministic keyword retrieval, explicit long-term facts, and encrypted project-scoped learned practices with confidence/evidence tracking.
- **Specialist agents:** native delegated agents for planning, architecture, TDD, code review, security review, build-error diagnosis, research, documentation, and structural engineering. Each invocation has an isolated context and role-specific read-only tool allowlist; up to four independent agents can run in parallel.
- **Specialist skills:** discovers Markdown skills for development, engineering, research, Office, productivity, and other workflows.
- **Workspace tools:** list, search, read and create files inside the configured project workspace.
- **Office and research-paper tutor:** inspect Excel, Word, PowerPoint and selectable-text PDF files. `office_workspace` action `analyze_paper` extracts page-labelled PDF text in quick mode (up to 30 pages / 18,000 characters) or deep mode (up to 100 pages / 48,000 characters), with optional page ranges. The assistant uses this to produce structured, source-cited paper briefings and deep reviews. It can create new Excel workbooks, Word research notes and editable text-based PowerPoint presentations; existing files are never overwritten. PDF extraction uses layout-aware text ordering, heuristic cues for possible captions/equations/numeric rows, and per-page dimensions, word counts, and embedded raster-image counts. The local PDF reader renders up to four selected PDF pages per request (up to 1,400 px wide and 8 MB total) and also reuses extractable embedded PNG/JPEG assets. GLM-OCR performs separate text and table-recognition passes on these images; Qwen3-VL handles desktop screenshots and GUI grounding. Page rendering now captures vector-only plots and scanned page backgrounds, although recognition accuracy still depends on image quality and model capability. Unsupported or unreadable content is reported rather than guessed.
- **Web research:** `web_search` discovers public sources; `web_open` reads public HTML/plain-text pages. Search results and page contents are untrusted; verify important claims against the source. Search queries are sent to DuckDuckGo.
- **Autonomous development:** create, list and explicitly run persistent coding tasks. Each run requires a clean Git working tree, creates a checkpoint, uses the safe workspace executor, runs `dotnet build` and `dotnet test`, commits only after success, and rolls back failed attempts.
- **Voice:** `/voice` uses on-demand local Whisper small multilingual transcription; `/speak on` and `/speak off` use local Piper neural speech when installed, with Windows speech fallback. No always-on wake-word listener.
- **Desktop launcher:** opens only Notepad, Calculator, File Explorer and Task Manager.
- **Abaqus diagnostics:** reports whether a supported Abaqus launcher is detected.
- **Phone companion:** authenticated queue-only commands over Tailscale; the laptop user must type exact `YES` before a phone request executes.

## Optional persistent background developer

JARVIS can keep a low-CPU development-queue worker running in the Windows user session. This is separate from the desktop chat and is not started automatically by installing JARVIS.

Start it in the current PowerShell window:

```powershell
.\scripts\Start-JarvisAgent.ps1
```

Optionally register a user-level Windows logon task and start it immediately:

```powershell
.\scripts\Install-JarvisAgentTask.ps1 -StartNow
```

The scheduled task runs as the current user (no administrator privilege or stored Windows password), restarts after a failure, and logs to `%LOCALAPPDATA%\JARVIS\Agent\agent.log`. It defaults to the local Ollama model unless `JARVIS_AI_PROVIDER` is explicitly configured. It does not continuously run a language model: it polls the task queue every 30 seconds and stays idle when there is no approved work.

**Approval boundary:** asking JARVIS to create a development task only queues it. A user must explicitly approve a task in JARVIS before the background worker can claim it. Approval is one-shot, can be revoked before claim, and is consumed when claimed. An interrupted or failed task requires fresh approval. Execution requires a clean Git working tree, a checkpoint, safe workspace actions, successful `dotnet build` and `dotnet test`, and a commit only after validation passes. A failed attempt is rolled back. The worker and foreground JARVIS share cross-process task and execution locks so they cannot execute two development tasks at once.

Remove the logon task with:

```powershell
.\scripts\Install-JarvisAgentTask.ps1 -Uninstall
```

## Interactive commands

| Command | Action |
|---|---|
| `/help` | Show available commands and workflows |
| `/skills` | List discovered specialist skills |
| `/agents` | List available specialist agents and their read-only tool permissions |
| `/remember <fact>` | Save an explicit long-term fact |
| `/memories` | List saved facts |
| `/forget <id>` | Remove one saved fact |
| `/learn <trigger> => <action>` | Explicitly save a reusable practice for the current project |
| `/instincts` | List project-scoped learned practices with confidence and evidence counts |
| `/forget-instinct <id>` | Remove one learned practice |
| `/clear` | Clear conversation history and saved facts; learned practices remain |
| `/voice` | Capture one spoken request on demand |
| `/speak on` / `/speak off` | Enable or disable speech output |
| `/phone-queue` | List phone requests waiting for approval |
| `/phone-next` | Preview a phone request and require exact `YES` approval |
| `/exit` | Close JARVIS |

You can also ask naturally to delegate work to a specialist agent or team (for example, ask the code-reviewer and security-reviewer to review a supplied change). Agents can inspect only their explicitly permitted read-only tools; they cannot edit files or execute commands. All agents currently use the configured JARVIS provider and its default model, with isolated contexts but no per-agent model routing yet. The main assistant remains responsible for coordinating implementation through the guarded development-task workflow.

You can also ask naturally to create/list/run a development task. Creating a task does not execute it. Running a task requires an explicit request and a clean Git working tree. Ctrl+C cancels the active request; during an autonomous task, the task is marked paused and can be resumed by running it again.

## Optional local memory

Memory is disabled by default. Enable it in PowerShell:

```powershell
$env:JARVIS_MEMORY_ENABLED = "true"
dotnet run --project .\src\Council.App\Council.App.csproj
```

Conversation content, explicit facts, and user-approved learned practices are encrypted with Windows DPAPI for the current Windows user. Learned practices are scoped to the current workspace path, never auto-inferred, and only added through `/learn`; repeated reinforcement increases confidence. They remain separate from `/remember` facts. The default conversation file is under `%LOCALAPPDATA%\JARVIS-Council\Memory`; override it with `JARVIS_MEMORY_PATH`. The default development task store is `%LOCALAPPDATA%\JARVIS\development-tasks.json`. Encrypted memory files are tied to the Windows user profile; do not expect them to decrypt under another account or on a non-Windows system.

## Optional Android phone companion

The phone bridge is disabled unless both `JARVIS_BRIDGE_BIND` and `JARVIS_BRIDGE_TOKEN` are configured. Install and connect Tailscale on the laptop and phone, then set the laptop's Tailscale IPv4 address and a random token of at least 32 UTF-8 bytes:

```powershell
$env:JARVIS_BRIDGE_BIND = "100.x.y.z"
$env:JARVIS_BRIDGE_TOKEN = "YOUR_RANDOM_SECRET_AT_LEAST_32_BYTES"
$env:JARVIS_BRIDGE_PORT = "47821"
dotnet run --project .\src\Council.App\Council.App.csproj
```

Enter the same address, port and token in the Android companion. The app stores the token encrypted with Android Keystore. Do not port-forward the bridge or expose it to the public internet. Phone requests are queued only; the laptop user must approve each request locally.

## Indian share-market monitoring

JARVIS includes a local NSE/BSE equity watchlist, holdings ledger, EOD portfolio valuation and one-shot closing-price alerts. Ask naturally, for example:

- "Add RELIANCE and TCS to my watchlist."
- "Save 10 shares of RELIANCE at an average buy price of 1,250 rupees."
- "Alert me if RELIANCE's daily close goes above 1,500."
- "Show my portfolio and active alerts."

The background monitor checks saved holdings and active EOD alerts at **09:00 IST on weekdays while JARVIS is running**. It prints a daily summary in the JARVIS console. The time can be changed with JARVIS_MARKET_MONITOR_TIME. It records local holdings/alerts under %LOCALAPPDATA%\JARVIS\market; override the watchlist location with JARVIS_MARKET_WATCHLIST_PATH.

**Data limitation:** the current TejHQ integration uses daily end-of-day prices, usually the previous trading session's close. It is not a live quote stream, intraday scanner, option-chain feed or broker account sync. Alerts are evaluated against closing prices only. JARVIS cannot place, modify or cancel orders. A live monitor will require a supported broker/data-provider API and the user's explicit connection.

### Optional DhanHQ read-only connection

JARVIS also provides a separate read-only DhanHQ v2 connector for account verification, demat holdings, open positions and one-shot market snapshots. It does not place, modify, cancel, convert or exit orders/positions, and it does not silently copy broker holdings into the manually maintained local ledger.

Store credentials in Windows Credential Manager (recommended) before launching JARVIS:

```powershell
.\scripts\Set-DhanCredentials.ps1
# Then start JARVIS
dotnet run --project .\src\Council.App\Council.App.csproj
```

The script asks for the Dhan Client ID and prompts for the access token as a secure input. It stores both in the current Windows user credential vault; it does not write a `.env` file or project setting. Existing `DHAN_CLIENT_ID` and `DHAN_ACCESS_TOKEN` environment variables remain supported as a fallback.

Ask JARVIS to verify the Dhan connection, show broker holdings, show open positions, or fetch a quote using a Dhan exchange segment and numeric security ID. Never paste access tokens into chat or commit them. JARVIS reads the current-user Windows Credential Manager entries first, then falls back to process environment variables; it never prints credentials. Dhan's manually generated access token is typically valid for 24 hours, so refresh it with `scripts/Set-DhanCredentials.ps1` when it expires. Automatic token renewal is not implemented.

**Cost and data distinction:** Dhan's account/portfolio trading APIs are documented as free for individual users. Real-time market-data APIs, including market snapshots and WebSocket feeds, require Dhan's Data API subscription (currently ₹499 + applicable taxes/month according to Dhan support). JARVIS does not subscribe or place orders on your behalf. Without a valid Dhan data subscription, use the existing TejHQ EOD monitor or paper data workflows; the Dhan holdings and positions endpoints remain separate read-only account functions.

The Dhan quote action uses the REST LTP snapshot endpoint and requires a numeric Dhan security ID. Symbol-to-security-ID lookup is provided by `dhan_instrument_lookup`; the optional `dhan_live_feed` tool provides a read-only WebSocket tick/quote stream. Automatic token renewal is not implemented. Live subscriptions and tick snapshots are held in memory and must be restarted after JARVIS exits.

PowerShell settings:
~~~powershell
$env:JARVIS_MARKET_MONITOR_ENABLED = "true"
$env:JARVIS_MARKET_MONITOR_TIME = "09:00"
$env:JARVIS_MARKET_WATCHLIST_PATH = "$env:LOCALAPPDATA\JARVIS\market\watchlist.json"
~~~

## Useful environment variables

| Variable | Purpose |
|---|---|
| `JARVIS_AI_PROVIDER` | `gemini`, `openai`, `ollama`, `omniroute`, or `mock` |
| `JARVIS_MEMORY_ENABLED` | Set `true` to enable encrypted local memory |
| `JARVIS_MEMORY_PATH` | Override conversation-memory file path |
| `JARVIS_SKILLS_PATH` | Override specialist-skill directory |
| `JARVIS_OFFICE_WORKSPACE` | Dedicated Office document workspace |
| `JARVIS_HOTKEY_ENABLED` | Set `false` to disable Ctrl+Alt+J |
| `JARVIS_MARKET_MONITOR_ENABLED` | Enable/disable the daily EOD portfolio and alert check |
| `JARVIS_MARKET_MONITOR_TIME` | Daily check time in IST; default `09:00` |
| `JARVIS_MARKET_WATCHLIST_PATH` | Override local NSE/BSE holdings and watchlist file |
| `DHAN_CLIENT_ID`, `DHAN_ACCESS_TOKEN` | Optional environment-variable fallback for read-only DhanHQ access; Windows Credential Manager is preferred |
| — | Dhan symbol lookup downloads and caches the official public instrument master for six hours; no broker credentials required |
| `JARVIS_BRIDGE_BIND` | Laptop Tailscale IPv4 address |
| `JARVIS_BRIDGE_TOKEN` | Private phone-bridge bearer token |
| `JARVIS_BRIDGE_PORT` | Phone bridge port; defaults to 47821 |
| `GEMINI_API_KEY`, `OPENAI_API_KEY` | Provider credentials |
| `OLLAMA_BASE_URL`, `OLLAMA_MODEL` | Local Ollama endpoint and chat model |
| `JARVIS_VISION_MODEL`, `JARVIS_DOCUMENT_VISION_MODEL` | Separate local desktop-vision and engineering-document OCR models |
| `JARVIS_OLLAMA_VISION_KEEP_ALIVE` | Vision-model unload policy; defaults to `0` to conserve RAM |
| `OMNIROUTE_BASE_URL`, `OMNIROUTE_API_KEY`, `OMNIROUTE_MODEL`, `OMNIROUTE_FREE_MODELS` | OmniRoute gateway configuration |

## Dhan symbol resolution and broker data

Store Dhan credentials in Windows Credential Manager with `scripts/Set-DhanCredentials.ps1` (the access token is entered as a secure prompt and never saved in a project file). JARVIS reads those entries first and falls back to the two environment variables if no stored entry exists. Credentials are never printed in tool output. JARVIS resolves exact NSE/BSE cash-equity symbols through Dhan's official compact instrument-master CSV (`dhan_instrument_lookup`). The result supplies the Dhan security ID and API exchange segment needed by `dhan_readonly` market snapshots. The public master is cached in memory for six hours and refreshed on expiry; no broker credentials are needed for symbol lookup. Quote snapshots still require locally configured Dhan credentials and an active Data API entitlement. The Dhan connector remains read-only; it cannot place, modify or cancel orders.

## Dhan live market feed

The optional `dhan_live_feed` tool maintains one read-only DhanHQ v2 WebSocket connection while JARVIS is running. Start it with resolved `SEGMENT:SECURITY_ID` pairs (maximum 100 instruments), use `status` to inspect recent prices, and `stop` to disconnect. Ticker mode is the lightweight default; quote mode includes additional volume information. Persistent one-shot price alerts (`set_alert`, `alerts`, `remove_alert`) are evaluated on incoming subscribed ticks; starting without an instrument list subscribes to all active alert instruments. The monitor reconnects with bounded backoff and keeps the latest tick snapshots in memory only. Subscriptions are not restored after a JARVIS restart. It reads credentials from Windows Credential Manager (preferred) or the two environment-variable fallbacks, and requires an active Dhan Data API subscription. It never places or modifies orders.

## Safety model

- Direct workspace file tools are confined to the configured workspace and do not overwrite or delete existing files.
- Specialist agents receive fresh isolated model contexts and can invoke only their per-role read-only tool allowlists. They cannot write files, run shell commands, commit, or approve their own changes.
- Autonomous development can modify/delete workspace files only after an explicit task-run request and clean-Git preflight. Failed attempts are rolled back; successful validated changes are committed.
- Phone commands never execute at the HTTP endpoint and always require local laptop approval.
- Public page reading permits only HTTP/HTTPS on ports 80/443, validates DNS destinations and redirects, blocks private/reserved IP ranges, disables cookies/proxies, and caps response size.
- Memory is opt-in and encrypted at rest on Windows. Credential redaction is best-effort; never store passwords, API keys, recovery codes, or other secrets in memory.

## Current validation

GitHub Actions runs Windows .NET 8 restore/build/tests and an Android debug-APK build. Check the latest commit's workflow results before relying on a new feature. The integration work is kept on a draft pull request and is not merged into the default branch.


## Cloud engineering vision and visible desktop control

The default vision stack is local Ollama: Qwen3-VL for desktop interaction and GLM-OCR for embedded document images. To opt into Gemini for full-PDF and screen understanding, set the following in PowerShell before starting JARVIS:

```powershell
$env:JARVIS_VISION_PROVIDER = "gemini"
$env:GEMINI_VISION_MODEL = "gemini-3.8-flash"
# GEMINI_API_KEY must already be configured
```

With Gemini selected, JARVIS can analyse complete PDFs up to 14 MB inline (in addition to local selectable-text extraction) and can inspect the visible Windows desktop. Screen captures and PDFs are sent to Google's API and may consume quota or incur charges. Set `JARVIS_VISION_PROVIDER=ollama` to keep image analysis local; local Qwen3-VL and GLM-OCR handle screen and embedded-image tasks respectively, but local PDF page rendering and full-page OCR are not yet implemented. Set it to `none` to disable vision.

The `computer_use` tool operates the visible primary Windows screen only. It requires an explicit user-directed session, an observe-before-act cycle, bounded mouse/keyboard sequences and post-action screenshot verification. It does not run shell commands or interact with hidden windows. Destructive actions, saving over existing files, solver submission and other irreversible actions require user confirmation.


### Optional cloud speech (ears and mouth)

To use Gemini's speech transcription and neural TTS while retaining local Whisper/Piper fallbacks, set these PowerShell variables before starting JARVIS:

```powershell
$env:JARVIS_AUDIO_PROVIDER = "gemini"
$env:GEMINI_TRANSCRIBE_MODEL = "gemini-3.5-transcribe"
$env:GEMINI_TTS_MODEL = "gemini-3.8-flash-lite-tts"
$env:GEMINI_TTS_VOICE = "Kore"
# GEMINI_API_KEY must already be configured
```

Cloud transcription uploads the captured WAV to the Gemini Files API, requests a verbatim transcript with engineering/market vocabulary hints, then attempts to delete the temporary upload. Cloud TTS sends the response text to Gemini and plays the returned audio. If a cloud request fails, JARVIS falls back to local Whisper/Piper or Windows speech. Audio and spoken response text are transmitted to Google while cloud mode is enabled and may consume API quota. Set `JARVIS_AUDIO_PROVIDER=local` to keep voice processing on-device.
