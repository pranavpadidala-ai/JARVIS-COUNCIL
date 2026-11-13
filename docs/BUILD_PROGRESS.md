# JARVIS Build Progress

Last updated: 2026-11-13

## Current integration state

- Integration branch: `bounded-autonomous-loop`
- Current integration head at this update: `73c505a06cd14dd14139294d8e16cfb8f4423660`
- Latest merged feature: desktop development-task queue and approval controls (PR #10, merge commit `2279eca5ce9aa3f538e04a5610b606826cb5ac81`)
- Latest feature-branch validation: PR #10 head `e1d41194ddf803615573bab9e761b2f2a819c322`, Windows .NET 8 Release build passed, PowerShell syntax validation passed, **310 tests passed / 0 failed** (workflow run `43470127221`).
- Integration validation after PR #10 and the progress documentation update is running: workflow run `43470686430`. Do not describe the current integration head as fully green until this run completes successfully.

## Merged and verified capabilities

1. **First usable desktop shell and chat (PR #6)**
   - WPF Command Center and chat window.
   - Desktop chat uses the same tool-calling assistant runtime as the console app.
   - Local conversation history, explicit encrypted facts and project-scoped learned practices.
   - On-demand microphone input and speech output.
   - Local Ollama, Gemini, OpenAI and allowlisted OmniRoute provider choices.

2. **Persistent development worker (PR #7)**
   - Durable development task queue and optional Windows logon worker.
   - Clean Git tree and checkpoint requirements; build/test validation before commit.
   - One-shot approval, revocation before claim, fresh approval after failure/interruption.
   - Foreground and background execution locks.
   - Background worker is not automatically installed or started on the user's laptop.

3. **First-use dashboard and teaching shortcuts (PR #9)**
   - Accurate dashboard capability descriptions.
   - Shortcuts for chat, task creation, teaching preferences/workflows and reviewing memory.

4. **Phone companion integration (PR #8)**
   - Desktop phone inbox with review, explicit approve/run and reject.
   - Cross-process durable queue and one-time claim to prevent duplicate execution.
   - Tailscale-only listener, bearer-token authentication, request-size/queue bounds.
   - Pairing script `scripts/Configure-JarvisPhoneBridge.ps1` creates a random token and configures the local firewall/URL reservation.
   - Android companion source in `android/JarvisPhone`; pairing instructions are in README.
   - PR validation: 307 tests passed / 0 failed.

5. **Desktop development-task queue (PR #10)**
   - Create tasks without approving or running them.
   - Review status, current step, attempts and approval state.
   - Approve one background run, revoke an unclaimed approval, or open a foreground run request in JARVIS chat.
   - Uses the same task-store path as the interactive tool and background worker, including `JARVIS_TASK_STORE_PATH`.
   - PR validation: 310 tests passed / 0 failed.

## Important repository audit notes

- The previous `System.IO` missing-using CI issue was already fixed on the integration branch; do not repeat that fix.
- The 305-test integration run briefly had one flaky terminal-controller test failure; rerunning the same commit passed. No source change was required for that transient failure.
- Phone-inbox CI initially found an unescaped XAML ampersand and missing `System.IO` imports. These were corrected; the final phone PR run passed.
- Older feature branches remain behind the integration branch. Re-compare before considering any cherry-pick; do not reintroduce their obsolete implementations.
- A new architecture requirement was added in `docs/architecture/multi-account-failover-state-continuity.md`. Treat it as a future requirement, not as implemented functionality. Respect provider terms and rate limits; never rotate accounts to evade quotas or silently route to paid models.

## Current priority: deliver the first JARVIS the user can install, use and teach

The user explicitly chose this order: first a usable JARVIS they can use and train; only then expand the remaining modules.

Next steps, in priority order:

1. **Installation and first launch**
   - Make the Windows setup/launch path reliable and simple.
   - Check .NET 8, Ollama service availability, and the configured local brain model.
   - Avoid launching into a broken provider configuration; give actionable setup errors.
   - Provide a repeatable local-first setup path suitable for the user's 8 GB Windows laptop. Keep large optional vision/OCR/voice downloads separately selectable.
   - Do not claim the application is installed or running on the user's laptop; they have not confirmed a local installation.

2. **Learning experience**
   - Make explicit facts and project practices easy to teach, review and correct.
   - Preserve DPAPI encryption and the distinction between user facts, project practices, conversation history and model fine-tuning.
   - Add clear onboarding examples for `/remember`, `/learn`, `/memories` and `/instincts`.

3. **Phone companion packaging**
   - Add Android build validation and produce a reproducible debug/release APK.
   - Verify pairing end-to-end on the user's actual Tailscale-connected devices before claiming phone control works in practice.

4. **Then expand the remaining modules**
   - Multi-provider failover and durable handoff packets, following the approved architecture document.
   - Specialist engineering-document reading and visible computer use, including guarded Abaqus workflows.
   - Private, consent-aware M4A voice dataset preparation and pilot review.
   - Indian market monitoring/day-trading skill validation with strict financial-action safeguards.

## Session operating rules

- Audit branches, PRs, commits and CI before making changes.
- For every failure, inspect its exact logs and search later commits/branches for an existing fix before editing.
- Merge only after Windows build, PowerShell validation and tests pass on the exact PR head.
- Preserve backup branches and avoid destructive overwrites.
- Keep code changes in large coherent batches; the user is not a regular developer and prefers install/run instructions after the first usable build is ready.
- Do not claim continuous background development by ChatGPT. The optional local JARVIS worker only runs after the user installs/starts it.
