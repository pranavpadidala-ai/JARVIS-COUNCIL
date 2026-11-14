# JARVIS Build Status

Last updated: 2026-11-14

## Phase 1 — First usable desktop JARVIS

Status: **implemented and Windows CI-verified; physical-laptop smoke test still pending.**

- Default branch: `bounded-autonomous-loop`.
- Desktop chat uses the shared bounded tool-calling runtime.
- Local Ollama plus optional Gemini, OpenAI and explicitly allowlisted OmniRoute.
- WPF Command Center, chat, local tray, hide/restore/exit and task queue.
- DPAPI-protected local conversation history and explicit user-managed fact memory.
- Explicit project practices via `/learn`, `/instincts`, `/forget-instinct`.
- BM25-style offline memory retrieval selects relevant history, facts and project practices without embeddings or a vector database.
- On-demand Whisper transcription and Piper/Windows speech output.
- Optional offline sherpa-onnx wake-word listener, disabled by default and controlled from the tray.
- Windows build and automated tests passed on the latest PR #26 head: 349 .NET tests, plus 201 Indian Trader and 3 wake-phrase token-generation tests. Post-merge default-branch validation is tracked separately.
- Local Ollama performance, wake-word microphone accuracy, audio-driver handoff and the phone/Abaqus hardware paths still require the user's own laptop/device tests.

### First-run instructions

From the existing local clone:

```powershell
git pull origin bounded-autonomous-loop
Set-ExecutionPolicy -Scope Process Bypass
.\scripts\Setup-JarvisDesktop.ps1
```

This installs the small local brain and launches the desktop. Optional vision/OCR/voice models are separate. To install the offline wake-word model later:

```powershell
.\scripts\Install-WakeWord.ps1
```

Wake-word remains off until the user right-clicks the JARVIS tray icon and checks **Listen for Hey JARVIS**.

### Optional Windows sign-in startup

The current PR adds `scripts/Install-JarvisDesktopStartup.ps1`. After it is merged, running that script creates a per-user Windows Startup shortcut. It starts the desktop hidden in the tray on sign-in, without rebuilding, opening the microphone, enabling wake-word or starting the background development agent. Use `-Uninstall` to remove the shortcut. This optional change is not yet merged.

### Teaching commands

- `/remember <fact>` — save a personal fact or preference.
- `/learn <trigger> => <action>` — save a repeatable project practice.
- `/memories` and `/instincts` — review saved knowledge.
- `/forget <id>` and `/forget-instinct <id>` — remove an item.
- `/skills` and `/agents` — inspect capabilities.
- `/clear` — clear conversation and explicit facts while retaining learned practices.

This is explicit memory-based personalization, not fine-tuning of the underlying language model.

## Phase 2 — Persistent local development agent

Status: **implemented, opt-in, automated-test coverage exists; actual long-running use on the user's laptop not yet verified.**

- Durable task queue, checkpoints and restart recovery.
- One-shot user approval before a task can execute in the background.
- Optional Windows logon task installer and low-CPU idle queue polling.
- Git checkpoints, build/test validation and bounded execution.
- Desktop task queue supports status review, approval and revocation.
- The desktop auto-start shortcut is separate and does not install/start this worker.

## Phase 3 — Desktop workflow and browser completion

Status: **partially implemented.**

- Tray and optional browser profile management integrated.
- Playwright CDP attach is limited to JARVIS-owned profiles, loopback-only and explicitly enabled.
- FlaUI provides read-only foreground control metadata. Screenshot verification remains necessary, especially for engineering software.
- Optional Windows desktop startup is in the current unmerged PR.
- Editing arbitrary existing Office documents and broad browser form automation are not claimed as complete.

## Phase 4 — Voice and learning

Status: **partially implemented.**

- On-demand Whisper speech recognition and local Piper/Windows speech output.
- Optional CPU-only local wake-word detection, tray-muted by default.
- Memory retrieval ranks old conversations, explicit facts and learned practices with deterministic local scoring.
- Real-room false-positive/false-negative performance and microphone handoff need testing on the user's device.
- Personal voice fine-tuning/dataset work remains future work and requires consent-aware review.

## Phase 5 — Phone, engineering and wider automation

Status: **partially implemented.**

- Phone bridge and Android companion source exist; end-to-end testing on the user's devices remains required.
- Abaqus diagnostics and visible computer interaction exist; deeper Abaqus-specific modelling/solver workflows remain incomplete and must stay user-confirmed.
- Indian market analysis and monitoring exist; broker actions remain read-only.
- Smartwatch and hardware kill-switch integration remain future work.

## Validation policy

- A hosted Windows CI pass proves compilation and automated tests on that runner, not end-to-end operation on the user's laptop.
- Merge only exact PR heads whose restore, Release build, PowerShell validation and test steps all pass.
- Do not state a feature is hardware-verified until the relevant physical device or application has been tested.
