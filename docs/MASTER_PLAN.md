# JARVIS master build plan

## Product direction

JARVIS is the user's single personal assistant and orchestration layer. External repositories are capability references or optional adapters; none becomes the whole product. The design is Windows-first, local-first where practical, cloud-assisted when the laptop cannot reasonably run a specialist model.

The current laptop target is an Intel i3-1005G1 with 8 GB RAM. Do not assume a dedicated NVIDIA GPU or enough memory to keep multiple large models resident. Load heavy local models on demand, and provide cloud/API alternatives with clear privacy and quota boundaries.

## Architecture layers

1. **Experience** — Windows Command Center desktop shell (WPF) plus chat runtime; phone remote interface and push-to-talk voice later.
2. **Assistant runtime** — conversation loop, task router, model selection, fallback, cancellation, session state and long-running task queue.
3. **Agent runtime** — tool registry, skills, scoped subagents, task planning, progress, retry, checkpoints and recovery.
4. **Knowledge** — conversation memory, project memory, local file index, source citations and retrieval.
5. **Specialist skills** — engineering/research, computer use, Indian markets, productivity and document production.
6. **Connectors** — web search, GitHub, Google Drive, broker-authorised market data, calendar/email and phone-to-PC transport.
7. **Safety and operations** — permissions, audit trail, secrets handling, workspace boundaries, backups, kill switch and health monitoring.

## Reuse decisions

| Reference project / component | Intended reuse |
|---|---|
| OpenJarvis | Local-first architecture, modular inference backends, agent and skill design patterns |
| Ultron | Auditable tool loop, permission gates, session persistence, MCP and computer-use safety patterns |
| OmniRoute | Optional OpenAI-compatible model gateway; provider quota and terms remain external |
| Docling | First local document-ingestion candidate for PDF/Office parsing, tables and layout |
| OXR | Specialist parsing evaluation candidate; local GPU requirements are beyond the current laptop, so use only after hardware/hosting/privacy review |
| Windows UI Automation + screenshot/mouse/keyboard | Desktop control foundation; semantic controls first, visual interaction as fallback |
| whisper.cpp + Piper | Candidate offline speech-to-text and text-to-speech engines |

Do not copy entire projects blindly. Reuse small, auditable modules and preserve JARVIS's .NET core unless a separate runtime has a clear advantage.

## Build sequence

### Phase 1 — usable assistant foundation
- [x] Interactive chat instead of an automatic smoke-test launch.
- [x] Local persistent conversation history.
- [x] Gemini and OpenAI provider support.
- [x] OpenAI-compatible gateway adapter for OmniRoute-style endpoints.
- [x] Sequential provider fallback.
- [x] Explicit development command with confirmation, Git checkpoint, build/test and rollback.
- [x] Create the first Windows Command Center desktop shell using the user's reference dashboard as the visual target.\n- [ ] Connect dashboard panels, navigation, task cards and chat input to live runtime services.\n- [ ] Verify on the user's Windows laptop and improve first-run setup.

### Phase 2 — dependable agent runtime
- [ ] Unified tool contracts and structured tool results.
- [ ] Persistent task queue with pause/resume/cancel.
- [ ] Multi-file change plans with a pre-change snapshot and rollback.
- [ ] Build/test error diagnosis and bounded repair loops.
- [ ] Skills manifest, versioning, trust levels and per-skill permissions.
- [ ] MCP client and connector lifecycle.

### Phase 3 — research and engineering
- [ ] Local document ingestion using Docling or an equivalent supported runtime.
- [ ] Page-level citations, table/formula extraction and source traceability.
- [ ] Research-paper tutor with short explanation, deep-dive mode and notes.
- [ ] Engineering skill pack: structural codes, section properties, Abaqus workflows, ETABS and CAD file handling.
- [ ] Specialist model selection based on document type and available hardware.
- [ ] Abaqus adapter with safe project copy, preflight checks, GUI automation and post-run verification.

### Phase 4 — computer and voice
- [ ] Windows screen capture and accessibility-tree inspection.
- [ ] UI Automation actions and visual fallback with bounded mouse/keyboard actions.
- [ ] App-specific adapters for Abaqus and common office applications.
- [ ] Offline speech recognition and speech output.
- [ ] Push-to-talk and wake-word evaluation only after reliable cancellation/privacy controls.
- [ ] Phone-to-PC remote commands over an authenticated private network.
- [ ] Watch microphone/audio integration only if the phone/watch exposes supported APIs.

### Phase 5 — Indian markets
- [ ] NSE/BSE market context and official data connectors where terms permit.
- [ ] Watchlists, alerts, fundamentals, technical analysis and option-chain analysis.
- [ ] FII/DII, corporate actions, news and event context.
- [ ] Backtesting with transaction costs, slippage and out-of-sample checks.
- [ ] Paper trading and risk limits before any broker integration.
- [ ] Broker market-data integration only through authorised APIs.
- [ ] Real order placement remains off by default and requires a separate explicit approval design.

### Phase 6 — personal automation and delivery
- [ ] Word, Excel, PowerPoint, PDF and diagram generation.
- [ ] Google Drive and home-storage sync with conflict handling.
- [ ] Calendar, email and scheduled briefings through authorised connectors.
- [ ] Background jobs, notification levels and idle-time execution.
- [ ] Backup/restore, encrypted secrets, audit export and hardware kill switch.

## Non-negotiable safeguards

- Never commit API keys, tokens, broker credentials or personal documents.
- Treat web pages, repositories, documents and model outputs as untrusted input.
- Keep file and shell operations inside explicit workspaces with path validation.
- Require confirmation for destructive operations, external sharing, financial actions and sensitive data transfer.
- Record tool calls and outcomes without logging secrets.
- Do not claim a task succeeded until the underlying tool reports success and any required verification passes.
- Keep real-money order placement disabled while building market analysis and paper-trading features.

## Definition of "ready"

JARVIS is not considered complete just because the chat window works. Each module must have automated tests, clear setup instructions, an explicit permission model, graceful failure behaviour and a working end-to-end acceptance test on the user's Windows machine.
