# JARVIS Build Status

Last updated: 2026-11-13

## Phase 1 — First usable desktop JARVIS

Status: **merged and CI-verified**.

- Default branch: `bounded-autonomous-loop`.
- Merge commit: `d7e86b767c1f638b8982275632a51e20f0dd44a5`.
- Desktop chat uses the shared bounded `ToolCallingAssistant` runtime.
- Local Ollama, Gemini, OpenAI and explicitly allowlisted OmniRoute provider selection.
- On-demand voice input/output and active-turn cancellation.
- DPAPI-protected local conversation history and explicit fact memory.
- Project-scoped explicit learning commands: `/learn`, `/instincts`, `/forget-instinct`.
- Desktop tools include the existing workspace, Office, web research, specialist-agent, market-monitoring, Abaqus diagnostic and visible-computer-use capabilities, subject to their existing safety gates.
- Windows .NET build: 0 warnings, 0 errors.
- Tests: 299 passed, 0 failed, 0 skipped (CI run 43453631992).

### First-run instructions

From the user's existing local clone, update the default branch and launch:

```powershell
git pull origin bounded-autonomous-loop
Set-ExecutionPolicy -Scope Process Bypass
.\scripts\Install-LocalModels.ps1
.\scripts\Start-JarvisDesktop.ps1
```

The model installer may take time because it downloads the local chat, vision/OCR and speech models. For a smaller first test, the user can set `JARVIS_AI_PROVIDER=ollama` and install only the chat model before launching. The desktop launcher builds the WPF app and starts the Command Center.

Teaching commands:
- `/remember <fact>` — save a personal preference/fact.
- `/learn <trigger> => <action>` — save a repeatable project practice.
- `/memories` and `/instincts` — review saved knowledge.
- `/forget <id>` and `/forget-instinct <id>` — remove an item.
- `/skills` and `/agents` — inspect JARVIS capabilities.
- `/clear` — clear conversation and explicit facts while retaining learned practices.

This is explicit memory-based personalization, not fine-tuning of the underlying language model. The first desktop build has passed CI but has not yet been exercised on the user's physical laptop; local model speed, microphone permissions and audio drivers still need first-run confirmation.

## Phase 2 — Persistent local development agent

Status: **next implementation phase**.

Goal: let JARVIS make progress on user-approved development tasks on the Windows laptop without requiring an active ChatGPT tab or desktop chat window.

Required work:
1. Background host with a low-CPU idle loop and graceful shutdown/restart recovery.
2. Durable task queue with explicit approval state. Creating a task must never silently authorize execution.
3. Single-instance and cross-process queue locking so desktop chat and the background host cannot overwrite each other's task state.
4. Clean-Git preflight, checkpoint, bounded execution, build/test validation, commit-on-success and rollback-on-failure.
5. Resume/pause/retry policy, persistent progress logs and user-visible status/major-failure notifications.
6. Windows logon startup/install/uninstall scripts and a health/diagnostic command.
7. Tests for duplicate hosts, restart recovery, approval enforcement, cancellation and rollback.

Safety constraints:
- Only tasks explicitly approved by the user may run in the background.
- Never place trades, submit payments, expose credentials, or perform destructive/non-development actions from the background host.
- Never run GUI/Abaqus solver work unattended; those remain explicit interactive actions.
- Keep API keys, local models, conversations, recordings, embeddings and task data out of Git.

## Phase 3 — Desktop workflow completion

- File drop/attachment and workspace selection.
- Live task/agent panels backed by real runtime state (no placeholder claims).
- Searchable conversation and knowledge-base views.
- Better Office document review and controlled editing of existing files.
- Clear model/vision/audio readiness diagnostics.

## Phase 4 — Personal voice and knowledge learning

- Inventory and pilot the user's M4A recordings before processing all 1,392 files.
- Verify the user's own voice segments; exclude other speakers unless they have explicitly consented.
- Build a local searchable knowledge/style profile from approved recordings and exported AI prompt histories.
- Evaluate Telugu/English voice synthesis and only then consider voice fine-tuning.
- Keep all personal datasets local and outside Git.

## Phase 5 — Phone, watch and wider automation

- Validate the Android companion on the user's phone.
- Extend the authenticated queue bridge and read-only monitoring.
- Evaluate watch notifications/control after the phone bridge is stable.

## Session handoff rules

- Always audit the current default branch, all branches, open PRs and latest CI before modifying code.
- Check whether a failing issue was already fixed on another branch before implementing a duplicate fix.
- Merge only verified work; preserve unverified work on a separate branch.
- Update this file when a phase changes state or a significant decision is made.
- Do not claim local-device validation unless the user has run it or a supported remote test has actually been performed.
