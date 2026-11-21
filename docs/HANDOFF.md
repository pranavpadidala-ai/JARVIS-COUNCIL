# JARVIS handoff (21 Nov 2026)

This note picks up where the cloud work stopped, so you can carry on in Claude Code on the laptop.

## Ground rules
- JARVIS is built for free. Use local models (Ollama), free tiers and open-source tools. Anything paid stays off unless the owner approves it.
- Never reset, clean or delete local work in `E:\JARVIS` without asking first. Don't touch `E:\JARVIS\Models`.
- Don't merge into the default branch `bounded-autonomous-loop` without asking. Don't force-push.
- No automatic live trading. Ask before purchases, account changes or large model downloads.
- The laptop has an i3, 8 GB of RAM and Windows PowerShell 5.1. Run one heavy job at a time, with breaks in between, and keep only one Ollama model loaded.

## Branches and PRs
| Branch | What it is | State |
|---|---|---|
| `recovery/laptop-work-2026-11-21` | The laptop's unsaved work, saved as is | Pushed |
| `integration/first-usable-2026-11` | The laptop work merged with `bounded-autonomous-loop` (367/367 tests pass) | Base for the fixes below |
| `fix/desktop-usable-2026-11` | Desktop fixes, [PR #28](https://github.com/pranavpadidala-ai/JARVIS-COUNCIL/pull/28) into the integration branch | Waiting for the owner's OK to merge |
| `feature/claude-provider-2026-11` | Optional Claude provider, built on the fix branch | Pushed. Open a PR after #28 merges |

## What PR #28 does
- **Shortcut:** `scripts/Install-JarvisDesktopShortcut.ps1` makes `JARVIS.lnk`, which opens `Council.Desktop.exe` directly, and `Update JARVIS.lnk`, which rebuilds in a visible window and pauses on errors.
- **Garbled text:** the garbled characters in MainWindow and ChatWindow are fixed, and CI now checks for them.
- **Model box:** a dark drop-down that lists installed Ollama models, chat models first.
- **Providers:** `DesktopProviderFactory` starts with Ollama and falls back to Gemini, then OpenAI, if their keys are set. `JARVIS_PROVIDER_FALLBACK=off` turns fallback off.
- **Model folder:** the installer and the app use the same folder: `JARVIS_MODELS_ROOT`, then the repo's `Models\LocalAI`, then `%LOCALAPPDATA%\JARVIS`.
- **Rebuilds:** `Start-JarvisDesktop.ps1` refuses to rebuild while JARVIS is running.

## Claude provider (`feature/claude-provider-2026-11`)
- `ClaudeProvider` uses the official Anthropic SDK. The default model is `claude-opus-5-5`, overridden by `CLAUDE_MODEL`.
- It is used only with `JARVIS_AI_PROVIDER=claude` and `ANTHROPIC_API_KEY`. It is never added as an automatic fallback, because it is paid.

## Test results (on the laptop, separate clone `E:\JARVIS-check`)
- `fix/desktop-usable-2026-11` @ c7e2777: the build has 0 errors and 1 existing warning (CS1998 in `WorkspaceToolTests.cs:131`), all 380 tests pass, and the garbled-text check passes.
- `feature/claude-provider-2026-11` @ 8478f01: the build has 0 errors, and all 387 tests pass. The tests make no API calls.
- **GitHub Actions doesn't run:** jobs are refused with "recent account payments have failed or your spending limit needs to be increased". Until billing is fixed, test on the laptop.

## Open questions for the owner
1. Merge PR #28 into `integration/first-usable-2026-11`?
2. Home screen: the dashboard from `docs/screenshots/jarvis-ui.png`, or a simple chat screen?

## Next steps
1. After #28 merges, run `.\scripts\Install-JarvisDesktopShortcut.ps1 -BuildNow` on the laptop. Open JARVIS from the new shortcut and check that the text is readable and the model drop-down works.
2. Open a PR for `feature/claude-provider-2026-11` into the integration branch.
3. Check tools, memory, the task queue, approvals, browser automation and the bounded autonomous loop end to end.
4. Check that the cloud model IDs are correct: `gemini-3.6-flash`, `gpt-5.2`, `gemini-3.8-flash` (vision).
5. Done: the old generator `scripts/Install-JARVIS-BrowserAutomation.ps1` and `src/Council.App/Program.cs.backup` were removed with the owner's OK (2026-11-21).
6. Ollama settings are already light: `OLLAMA_MAX_LOADED_MODELS=1`, `NUM_PARALLEL=1` and a 30 s keep-alive. No change is needed.

## Local model research (from the "Pick the best local AI models" work)
- `qwen3.5:4b` is installed (3.4 GB) but hasn't been benchmarked yet. To benchmark it:
  - Use `num_predict` 300, `num_ctx` 2048 and `think: false`.
  - Send one prompt at a time.
  - Start only when at least 2.5 GB of RAM is free.
  - The first attempt kept the CPU at 100% for several minutes and left about 1.1 GB free.
- **Planned lineup**, with only one model loaded at a time:

  | Role | Model |
  |---|---|
  | Main chat | `qwen3.5:4b` |
  | Fallback, low RAM | `qwen3.5:2b-q4_K_M` |
  | Document OCR | `glm-ocr` |
  | Vision | `qwen3-vl:2b` |

## Civil engineering plan
1. Write IS 456, IS 800, IS 875 and IS 1893 calculators as C# tools, so the numbers come from code rather than the model.
2. Build a local code library with free, local embeddings, so answers cite clauses.
3. Later, fine-tune the 4B model on free Colab.
4. Make a 30-question civil test set. Example: if Mu = 180 kNm and Mu,lim ≈ 129 kNm, the beam needs double reinforcement.
