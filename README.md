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
```

The normal startup is the interactive JARVIS assistant. If no provider key is configured, it starts in mock mode so the local application and tools can still be exercised.

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

The default local brain is Qwen 3.5 2B Q4_K_M (about 1.9 GB). JARVIS also has a separate Moondream vision model. The installer configures Ollama for one loaded model, one parallel request, a 30-second keep-alive, Flash Attention and an 8-bit KV cache to limit RAM use.

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
- **Eyes:** Moondream 2 through Ollama (about 1.7 GB); PDF image analysis remains separate from the chat model.
- **Ears:** Whisper small multilingual through Whisper.net (about 466 MB). It is loaded only when you use `/voice`, then disposed.
- **Mouth:** Piper neural TTS with medium-quality English, Hindi and Telugu voices. Only the selected voice is loaded to synthesize each reply.

The 8 GB profile keeps one Ollama model loaded at a time, serializes specialist teams on local Ollama, uses a 4,096-token context, one inference slot, Flash Attention and an 8-bit KV cache. Whisper.net's CPU runtime requires Windows 11 (or Windows Server 2022) and the Microsoft Visual C++ 2022 x64 runtime; older Windows versions fall back to Windows speech recognition. Whisper and Piper are invoked only when needed; they are not kept resident. This trades some model reload latency for memory headroom. Do not run multiple local inference servers simultaneously.

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
- **Office and research-paper tutor:** inspect Excel, Word, PowerPoint and selectable-text PDF files. `office_workspace` action `analyze_paper` extracts page-labelled PDF text in quick mode (up to 30 pages / 18,000 characters) or deep mode (up to 100 pages / 48,000 characters), with optional page ranges. The assistant uses this to produce structured, source-cited paper briefings and deep reviews. It can create new Excel workbooks and Word research notes; existing files are never overwritten. PDF extraction uses layout-aware text ordering, heuristic cues for possible captions/equations/numeric rows, and per-page dimensions, word counts, and embedded raster-image counts. The local Ollama Moondream vision model analyses up to four extractable embedded PNG/JPEG images per request (8 MB total) for plots, schematics, scanned page images, and legible labels. This does not render complete PDF pages or vector-only plots/charts, perform general OCR, or reliably reconstruct table cells/equation formatting; unsupported or unreadable image content is reported rather than guessed.
- **Web research:** `web_search` discovers public sources; `web_open` reads public HTML/plain-text pages. Search results and page contents are untrusted; verify important claims against the source. Search queries are sent to DuckDuckGo.
- **Autonomous development:** create, list and explicitly run persistent coding tasks. Each run requires a clean Git working tree, creates a checkpoint, uses the safe workspace executor, runs `dotnet build` and `dotnet test`, commits only after success, and rolls back failed attempts.
- **Voice:** `/voice` uses on-demand local Whisper small multilingual transcription; `/speak on` and `/speak off` use local Piper neural speech when installed, with Windows speech fallback. No always-on wake-word listener.
- **Desktop launcher:** opens only Notepad, Calculator, File Explorer and Task Manager.
- **Abaqus diagnostics:** reports whether a supported Abaqus launcher is detected.
- **Phone companion:** authenticated queue-only commands over Tailscale; the laptop user must type exact `YES` before a phone request executes.

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
| `JARVIS_BRIDGE_BIND` | Laptop Tailscale IPv4 address |
| `JARVIS_BRIDGE_TOKEN` | Private phone-bridge bearer token |
| `JARVIS_BRIDGE_PORT` | Phone bridge port; defaults to 47821 |
| `GEMINI_API_KEY`, `OPENAI_API_KEY` | Provider credentials |
| `OLLAMA_BASE_URL`, `OLLAMA_MODEL` | Local Ollama endpoint and model |
| `OMNIROUTE_BASE_URL`, `OMNIROUTE_API_KEY`, `OMNIROUTE_MODEL`, `OMNIROUTE_FREE_MODELS` | OmniRoute gateway configuration |

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

The default vision provider remains local Ollama/Moondream. To opt into Gemini for higher-quality engineering PDF, figure and screen understanding, set the following in PowerShell before starting JARVIS:

```powershell
$env:JARVIS_VISION_PROVIDER = "gemini"
$env:GEMINI_VISION_MODEL = "gemini-3.8-flash"
# GEMINI_API_KEY must already be configured
```

With Gemini selected, JARVIS can analyse complete PDFs up to 14 MB inline (in addition to local selectable-text extraction) and can inspect the visible Windows desktop. Screen captures and PDFs are sent to Google's API and may consume quota or incur charges. Set `JARVIS_VISION_PROVIDER=ollama` to keep image analysis local; local Moondream analyses embedded images but does not provide full-PDF OCR. Set it to `none` to disable vision.

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
