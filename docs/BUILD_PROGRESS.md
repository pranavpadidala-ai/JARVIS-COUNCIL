# JARVIS Build Progress

Last updated: 2026-11-14

## Current integration state

- Integration branch: `bounded-autonomous-loop`
- Current integration branch: `bounded-autonomous-loop`; latest merged commit in this session: `c29e576168201abd20ea5c42db47bcbf990915fd` (PR #18).
- PR #13 (one-command local-first desktop installer) merged as `e934593b14bd25294289e593a0f38fbcb79e72a3`. Its feature-branch Windows CI passed; post-merge integration validation is running (workflow run `43577451045`).
- PR #18 (authenticated loopback-only local MCP development bridge) merged as `c29e576168201abd20ea5c42db47bcbf990915fd`. Exact PR head `8cc531ad0608c8833826eca6ddebf50546e6e46c` passed Windows .NET 8 build, PowerShell syntax validation and the automated suite; PR validation run `43576656479` succeeded. Local Windows PowerShell 5.1 bridge/Ollama/Qwen smoke test was reported successful by the user.
- Post-merge integration validation for PR #18 is running (workflow run `43577487130`). Do not mark the latest integration head fully green until it completes.
- PR #14's Android debug APK and .NET checks both passed on its old head, but its base became stale. A replacement PR #19 was opened from the current integration head to re-enable Android CI for `bounded-autonomous-loop`; its workflow result is pending.
- PR #17 (durable checkpoints and Brave profiles) is green on its feature head, but targets `unified/jarvis-available`, not the primary `bounded-autonomous-loop`. Do not merge until its intended integration target and dependency relationship are reconciled.

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
