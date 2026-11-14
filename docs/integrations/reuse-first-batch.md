# Reuse-first external integrations (batch 1)

This batch keeps JARVIS Council as the single assistant/controller and pins selected upstream repositories as Git submodules. Upstream source is not copied into the .NET projects unless the repository license permits it and a specific module is adapted and tested.

## Included source repositories

| Local path | Upstream | Pinned commit | License | JARVIS role |
|---|---|---|---|---|
| \`integrations/openjarvis\` | https://github.com/open-jarvis/OpenJarvis | \`76836f4eba5e0d25cf53b56c2f475a2c2583c1c9\` | Apache-2.0 | Optional intelligence/agent/memory/trace/learning source donor. Not auto-installed or started; evaluate modules selectively to avoid duplicating JARVIS providers, memory and agent runtime. |
| \`integrations/indian-trader\` | https://github.com/Ritik-Sharma-dev/indian-trader-skill | \`5ab5b33dac29eaddf9d76cbc039bd84176b65aef\` | MIT | Direct CLI reuse through the registered \`indian_trader\` tool. Fixed read-only command allowlist, no shell, no broker or order access. |
| \`integrations/indian-trading-skills\` | https://github.com/kishorkukreja/finance-trading-skills | \`4a1d5b2a870cd0eaaae947220197ae3bee5c9458\` | MIT | Reference skill corpus. JARVIS-native finance skills remain the active instructions; Claude-specific scripts/tool calls are not auto-executed. |

Submodules are pinned to exact commits. Update only after reviewing upstream changes and licenses. Initialize them with:

\`\`\`powershell
.\\scripts\\Initialize-ExternalIntegrations.ps1
\`\`\`

This script checks the Indian Trader offline test suite. It does not install packages, compile OpenJarvis, download models, or start any background process. Python 3.8+ is required only for the Indian Trader CLI tool.

## Existing integrations retained (not duplicated)

- **OmniRoute:** already has a native .NET provider, explicit model ID and free-model allowlist. The upstream gateway is not vendored because JARVIS already integrates with its API and the upstream repository is exceptionally large.
- **DhanHQ:** native JARVIS tools already provide read-only account data, instrument resolution and optional market feeds. The separate DhanHQ skill repository includes live order placement, so it is not added to the runtime. JARVIS continues to expose no order placement.
- **Indian trading skills:** JARVIS already has native adaptations for day trading, options, FII/DII flows, market breadth, news, VCP screening and backtest review. The source submodule is retained for comparison, not loaded as untrusted runtime prompts.

## Ultron / Altron: inspect and clean-room port

Repository: https://github.com/Abdulatif228/Ultron

The public repository and its architecture/readme files are accessible for inspection. The repository does not declare a license, and its own publishing guidance says a license should be added. Public visibility is not a license grant. Therefore this repository is deliberately **not** copied, forked into this project, or added as a submodule. No Altron source files are redistributed here.

The documented architecture was reviewed at a functional level. JARVIS already has equivalents for AI planning, bounded tool execution, on-demand speech recognition/TTS, explicit screen analysis, application launch, local memory, cancellation and confirmation-gated actions. The notable gaps to implement independently in JARVIS are an opt-in wake-word listener and a Windows system-tray controller, with microphone capture disabled by default and a visible stop/mute control. Any future implementation will be original .NET code using JARVIS's existing permission model; it will not copy Altron source.

## Safety and runtime boundaries

- The Indian Trader adapter accepts only fixed read-only commands and bounded arguments. It never invokes the upstream MCP server or arbitrary shell commands.
- The tool returns analytical output, not guaranteed trading signals or personalized financial advice. Data sources may be delayed or unavailable; verify with official exchanges/broker data.
- No live order placement, automated trading, account rotation, credential scraping, or automatic install of upstream code is enabled.
- OpenJarvis is a source donor only in this batch. Its native Rust extension may require MSVC and substantial build dependencies; do not install those solely to make the submodule available.
- Treat all upstream code, skills, data and model outputs as untrusted until reviewed and tested.
