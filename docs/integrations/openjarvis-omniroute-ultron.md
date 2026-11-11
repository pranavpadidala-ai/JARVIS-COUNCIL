# JARVIS integration: OpenJarvis, OmniRoute and Ultron/Altron

This document records the first .NET-native integration slice. JARVIS remains the Windows controller and execution authority; upstream projects are capability references or external services, not replacements for the existing solution.

## Architecture decisions

- **OpenJarvis (Stanford):** adopt the local-first inference-engine abstraction, explicit agent-loop boundaries, persistent memory/retrieval direction, and evaluation/trace principles. Do not transplant its Python/Rust stack into the .NET 8 solution or run several inference runtimes concurrently on the 8 GB laptop.
- **OmniRoute:** use its OpenAI-compatible Chat Completions endpoint as a provider behind `IAIProvider`. OmniRoute is a gateway, not a free API grant; it requires eligible provider connections and its own gateway key. Free quotas and provider terms can change.
- **Ultron/Altron (Abdulatif228):** use its README-described voice, TTS, app/window control, plan cancellation, local file search and diagnostics as feature requirements. The first native .NET slice now includes on-demand Windows speech input/output and a strict built-in-app launcher. Wake-word listening, screen analysis and general window automation remain future work. Do not copy its Python implementation: its README does not provide a reusable software license, and machine-specific launch sequences are not portable.
- **Existing JARVIS:** retain Council.Dev, tool registry, safe workspace tools, skills, Office/Abaqus tools and current provider adapters.

## OmniRoute free-only configuration

The JARVIS adapter intentionally blocks the `auto` alias. It requires an explicit model ID of the form `provider-prefix/model-id` and an exact ID in `OMNIROUTE_FREE_MODELS`. This prevents JARVIS from silently asking OmniRoute to choose an unknown-cost route. It does not independently audit the upstream provider's billing terms: choose only a currently free provider connection in the OmniRoute dashboard and use that provider's exact model ID.

1. Start OmniRoute locally and open its dashboard at `http://localhost:20128`.
2. Connect only a provider/account whose current plan and terms you have checked.
3. Copy the local gateway API key from OmniRoute's Endpoints/API settings.
4. Check the live model list at `http://localhost:20128/v1/models` using that key.
5. Copy the exact model ID returned by the model list.

PowerShell example (replace placeholders; do not commit these values):

```powershell
$env:JARVIS_AI_PROVIDER = "omniroute"
$env:OMNIROUTE_BASE_URL = "http://localhost:20128/v1"
$env:OMNIROUTE_API_KEY = "YOUR_LOCAL_OMNIROUTE_GATEWAY_KEY"
$env:OMNIROUTE_MODEL = "YOUR_FREE_PROVIDER_PREFIX/YOUR_MODEL_ID"
$env:OMNIROUTE_FREE_MODELS = "YOUR_FREE_PROVIDER_PREFIX/YOUR_MODEL_ID"
dotnet run --project src/Council.App/Council.App.csproj
```

Multiple explicitly approved free model IDs can be separated with semicolons. The adapter rejects `auto`, model IDs outside the exact allowlist, non-loopback plain HTTP gateways, and missing configuration. A remote gateway must use HTTPS. The gateway key is not the same as an upstream provider API key.

## Optional local conversation history

Conversation persistence is opt-in. Set `$env:JARVIS_MEMORY_ENABLED = "true"` before starting JARVIS. The default file is:

`%LOCALAPPDATA%\JARVIS-Council\Memory\conversation.jsonl`

You can override it with `JARVIS_MEMORY_PATH`. The `/clear` command clears the persisted history too. The store keeps user/assistant turns locally and can retrieve up to five older messages by deterministic keyword overlap for a new request. This is not embedding-based semantic search, document retrieval, or an autonomous learning loop. The file is plain text on the local machine, so do not use it to store passwords, API keys, or other secrets. Keep memory disabled on shared Windows accounts.

## Windows voice and desktop controls

The current Windows-native voice slice uses the System.Speech APIs:

- `/voice` opens the default microphone on demand and captures one spoken request (up to 20 seconds). It is command-triggered push-to-talk, not an always-listening service.
- `/speak on` enables spoken assistant replies through the installed Windows speech voice; `/speak off` disables them.
- Pressing Escape during voice capture or spoken output cancels that active audio operation. It does not currently interrupt an in-progress AI/provider request or arbitrary processes.
- Voice input requires a working microphone and an installed Windows speech-recognition engine/language. If Windows has no compatible recognizer, JARVIS reports the failure and text input remains available.

The desktop_control tool can launch only Notepad, Calculator, File Explorer, and Task Manager. It rejects arbitrary executable paths, extra command arguments, scripts, shell commands, and unsupported actions. It cannot unlock Windows or bypass Windows Hello/PIN. These restrictions are intentional.

## Deliberately not implemented in this slice

Always-on wake-word listening, screen capture/vision, general window manipulation, app closing/process termination, Android remote control, watch support, and home-network control remain future work. They need separate native modules, explicit permissions, stop/cancel handling, and device-specific tests. OpenJarvis's Python agents and Ultron/Altron's machine-specific scripts are not installed by this change.

## Validation status

The GitHub Actions workflow runs Windows .NET 8 restore, build, and the existing xUnit test suite. The memory retrieval change passed 168 tests. The new voice and desktop-control changes are being validated in the latest CI run; do not treat them as verified until that run completes successfully. No merge into the default branch is performed.
