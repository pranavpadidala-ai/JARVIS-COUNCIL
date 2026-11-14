# Open-source ecosystem audit — 14 November 2026

This audit is a reuse-first review of public projects that can improve JARVIS on a Windows laptop with an Intel i3-1005G1 and 8 GB RAM. The objective is a single coordinated assistant, not several competing assistants or background model servers.

## Already integrated into JARVIS

| Project / component | Use in JARVIS | Decision |
|---|---|---|
| OpenJarvis | Intelligence foundation and component reference | Pinned as an Apache-2.0 source donor. Do not run its full runtime alongside the existing .NET assistant. |
| Indian Trader | Indian-market analysis CLI | Pinned under MIT and wrapped by a native read-only JARVIS tool with a fixed allowlist, bounded output, argument-safe process launch and timeout. |
| Indian Trading Skills | Finance/trading research skills | Pinned under MIT; native JARVIS skills remain the primary skill interface. |
| OmniRoute | Explicit cloud-provider gateway | Existing native adapter; exact model IDs and allowlisting are required. It is not a free API entitlement. |
| DhanHQ | Broker holdings, positions, snapshots and optional tick feed | Existing native read-only integration; no order placement. |
| Ultron / Altron | Voice, tray, planner and local-memory feature reference | Public source was inspected, but no software licence was declared. Do not copy its code; independently implement useful behaviours. |
| Brave profile manager | Isolated research/work browser profiles | Selectively ported from the repository's older feature branch. The port adds explicit close/delete confirmation and does not restore duplicate checkpoint code. |
| Windows system tray | Persistent desktop entry point | Added natively to the WPF desktop application. Minimising/closing the dashboard hides it; tray Exit fully shuts down JARVIS. It does not enable microphone listening. |

## Evaluated projects and decisions

### Windows voice assistant reference: ndunl075/Jarvis

Repository: https://github.com/ndunl075/Jarvis

The repository declares MIT and documents a local wake-word/VAD/Whisper/Piper voice pipeline, barge-in, system-tray modes and local Ollama integration. The feature design is relevant, but importing the whole Python/PyInstaller application would create a second assistant, duplicate speech runtimes and configuration, and complicate the existing .NET lifecycle. The system-tray behaviour has instead been implemented natively in WPF. Wake-word and barge-in remain a separate optional integration, not an always-on listener.

Its README gives 8 GB as a minimum and 16 GB as recommended for its default LLM/voice stack. JARVIS should keep its smaller 2B local model profile and load Whisper/TTS/vision only on demand.

### Wake-word engine: sherpa-onnx (selected); openWakeWord (not selected)

Repositories: https://github.com/k2-fsa/sherpa-onnx and https://github.com/dscripka/openWakeWord

**Integrated in this batch:** the .NET desktop app uses sherpa-onnx 1.13.8 with the English GigaSpeech Zipformer KWS model. The C# runtime is Apache-2.0 and its NuGet package supports .NET 8 Windows; the selected model archive's README declares Apache License 2.0. The model is downloaded locally and is not redistributed or committed. The source dataset's terms are separate from the model licence; do not treat dataset terms as a blanket grant for redistributing training audio. The wake-word listener is off by default, starts only from the tray, uses one CPU inference thread, and has a visible mute/stop control. openWakeWord was not selected because its common pretrained models have separate non-commercial ShareAlike licensing.

### Windows accessibility automation: FlaUI

Repository: https://github.com/FlaUI/FlaUI

FlaUI is MIT-licensed; the 5.0.0 UIA3 package targets net6.0-windows7.0 and is compatible with .NET 8 Windows. **Integrated in this batch:** a Windows-only WPF adapter exposes a read-only `windows_accessibility` tool. It inspects only the current foreground window, returns visible control names/types/automation IDs/bounds, does not read text-entry values and does not invoke controls. It supplements the screenshot-based controller; it does not replace screenshot verification or assume every custom-drawn engineering surface exposes accessibility metadata.

### Browser automation: Microsoft Playwright for .NET

Repository: https://github.com/microsoft/playwright-dotnet

**Integrated in this batch:** Microsoft.Playwright 1.64.1 (MIT) attaches to a JARVIS-owned Brave profile via Chromium CDP. The endpoint is optional, bound to 127.0.0.1 and enabled only when the profile is launched with automation=true. The first tool supports bounded page inspection, public HTTP/HTTPS navigation, one accessible search box, and one exact safe hyperlink. It does not click buttons, submit forms, upload/download, enter credentials, perform trading/payment/destructive actions, or install a second browser. CDP is lower fidelity than Playwright's native transport, so screenshot verification remains available.

### Indian-market MCP candidates

- https://github.com/icharshal/india-market-mcp — claims a broad set of NSE/BSE, F&O, technical, fund and market-data tools under MIT, but its public repository currently has very little commit/activity history. It relies on NSE website endpoints and session/rate-limit handling. Do not treat it as a dependable live feed without sustained tests, provenance checks and legal/terms review.
- https://github.com/kcodweb/india-market-mcp — MIT, focused on quotes, option chains, analytics and filings, but similarly has very little repository history.
- JARVIS should prefer the already-integrated Indian Trader and explicitly configured Dhan data APIs. Port independently testable calculations (for example Black–Scholes, Greeks, max pain, PCR, RSI and position sizing) only after checking formula correctness and input/data timestamps. Do not scrape around exchange controls or make execution decisions from unverified free data.

### Projects not approved for source reuse

- https://github.com/psycoks/Jarvis — valuable local-first Windows design references (Playwright, screen vision, Hindi/Hinglish speech and wake-word interaction), but no repository software licence was visible in the reviewed repository page. Treat as all-rights-reserved until a licence is provided.
- https://github.com/putbullet/catchai — interesting wake-word and local voice UX reference, but no software licence was visible in the reviewed repository page. Do not copy code.
- Open-source projects that require a second large LLM, vector database, GPU stack or multi-gigabyte models are not appropriate to run continuously on this laptop. A compatible algorithm or interface may still be independently adapted.

## Integration order

1. Complete and locally smoke-test the isolated Brave profiles and system tray changes on Windows.
2. Add a Windows-only accessibility adapter (FlaUI) that supplements, rather than replaces, visible screenshot-based computer use.
3. Add Playwright-based DOM automation inside JARVIS-owned browser profiles with explicit action/confirmation controls.
4. Add optional wake-word detection only after resolving model licensing and a low-CPU microphone lifecycle; keep it off by default and show an obvious mute indicator.
5. Improve memory retrieval with a compact, local semantic index only if its RAM/disk cost is justified; retain deterministic retrieval as fallback.
6. Expand research into multi-step source verification and page/figure-aware engineering paper review.
7. Test Android/Tailscale commands end-to-end on the user's actual phone and laptop before calling remote control complete.
8. Expand India-market analytics only with timestamped, validated and attributable sources; keep broker actions read-only unless a separate, explicit trading safety design is implemented.

## Hardware and safety rules

- One local LLM inference slot. Do not run multiple Ollama/local inference servers.
- Load local vision, OCR and speech models on demand and unload specialists when finished.
- Background idle services should consume negligible CPU and must expose mute/stop controls.
- Browser and desktop automation should observe before acting, use bounded operations, verify outcomes and seek confirmation for irreversible actions.
- Treat repository code, skills, web pages and market data as untrusted. Inspect dependencies, licences, network behaviour and data provenance before adoption.
