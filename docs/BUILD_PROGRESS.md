# JARVIS Build Progress

Last updated: 2026-11-14

## Current default branch

- Repository: `pranavpadidala-ai/JARVIS-COUNCIL`
- Integration branch: `bounded-autonomous-loop`
- Latest merged commit at the start of this batch: `c34f4f5b474159d65d2b40738029ee40f5da224a` (PR #26).
- PR #21 — durable task checkpoints and restart recovery — merged and supersedes earlier checkpoint experiments.
- PR #22 — pinned OpenJarvis, Indian Trader and Indian Trading Skills source repositories; added native read-only Indian Trader CLI tool — merged.
- PR #23 — isolated Brave profiles, native system tray, read-only Windows UI Automation inspection — merged.
- PR #24 — bounded Playwright automation on JARVIS-owned Brave profiles over loopback-only CDP — merged.
- PR #25 — optional offline sherpa-onnx wake-word detection, tray mute control and local model installer — merged.
- PR #26 — offline BM25-style retrieval for conversation history, saved facts and learned practices — merged.
- The post-merge Windows workflow for PR #26 is run `43591660628`; its default-branch validation was still in progress when this file was updated. PR #26's exact feature head passed Windows CI first.

## Recent validation results

| Change | .NET tests | Other checks | Result |
|---|---:|---|---|
| PR #23 desktop tray/browser profiles/UIA | 336 | Windows Release build, PowerShell parsing | Passed |
| PR #24 Playwright browser automation | 342 | 201 Indian Trader tests, Windows Release build | Passed |
| PR #25 offline wake-word | 342 | 201 Indian Trader tests, 3 Python token-generator tests, PowerShell parsing | Passed |
| PR #26 memory relevance ranking | 349 | 201 Indian Trader tests, 3 Python token-generator tests, PowerShell parsing | Passed |

These results are CI validation on hosted Windows runners. They do not prove that the user's physical microphone, Brave profile, phone, Tailscale network, Ollama installation or Abaqus setup has been exercised.

## Implemented capability inventory

### Desktop assistant
- WPF Command Center and chat over the shared bounded tool-calling runtime.
- Local Ollama and optional Gemini, OpenAI and explicitly allowlisted OmniRoute providers.
- Windows tray, hide/restore/exit controls, telemetry and desktop task queue.
- Optional tray-controlled start-on-Windows is being added in the current PR; it is not merged yet.
- DPAPI-protected conversation history and explicit facts.
- User-approved project practices with confidence/evidence tracking.
- Offline relevance ranking for old conversations, facts and practices.
- Optional offline wake-word service; disabled by default and not yet hardware-smoke-tested.

### Browser and desktop interaction
- Isolated Brave profiles with local profile data and explicit close/delete confirmation.
- Optional Playwright .NET attach over a loopback-only debugging endpoint; first actions are limited to inspection, public navigation, a single accessible search box and exact ordinary links.
- Read-only FlaUI UI Automation inspector for the foreground app; screenshot-verified mouse/keyboard control remains available.
- Do not assume that custom-drawn Abaqus surfaces expose complete accessibility metadata.

### Research, Office and engineering
- Public web search/page reading, specialist agents and local skills.
- PDF paper analysis with page labels, OCR and separate figure/table review when local models are installed.
- Creation of new Office workbooks, documents and presentations; do not claim general editing of arbitrary existing Office files.
- Abaqus installation/status diagnostic and guarded visible UI operation. Deeper Abaqus-specific modelling/solver automation still needs focused validation.

### Indian-market tools
- Local Indian holdings/watchlist and EOD monitor.
- Dhan read-only holdings, positions and optional live feed when configured.
- Indian Trader analytics CLI exposed through a fixed read-only allowlist.
- No automatic broker order placement, payment or trade execution.

### Phone and background work
- Authenticated Tailscale phone-command bridge with a durable approval inbox.
- Persistent development task worker and Windows logon installer, opt-in only. The background worker runs only explicitly approved development tasks.
- Android companion and bridge still require end-to-end validation on the user's actual phone/laptop.

## Current priorities

1. Finish opt-in Windows desktop startup: build once, create a current-user Startup shortcut, launch hidden into tray, keep microphone/wake-word and background agent off.
2. Complete Windows CI for the desktop-startup PR and merge only its exact green head.
3. Have the user pull the merged branch and smoke-test the desktop, tray and optional wake-word microphone on the physical laptop.
4. Validate Android/Tailscale command round-trip on the user's actual devices.
5. Add focused Abaqus workflows incrementally: observe, inspect, bounded action, verify, and ask before solver jobs or destructive/save operations.
6. Improve paper/Office workflows without claiming full arbitrary-document editing; keep output files new unless overwrite is explicitly approved.
7. Continue monitoring Indian-market data provenance and timestamps; keep the broker interface read-only.

## Hardware and privacy rules

- One local LLM inference slot; do not run multiple local inference servers.
- Load Whisper, vision, OCR and other specialist models only when required.
- Wake-word is opt-in, CPU-only, one-thread and visibly muteable. Never enable it during startup unless the user explicitly checks the tray setting.
- Desktop auto-start must not start the background development worker or capture audio.
- Browser CDP endpoints stay bound to loopback and are disabled on ordinary Brave launches.
- Browser automation does not submit forms, enter credentials, trade, pay, upload/download or perform destructive actions.
- All user facts/practices stay explicit and encrypted locally. Relevance ranking is lexical and deterministic, not semantic understanding or model fine-tuning.
