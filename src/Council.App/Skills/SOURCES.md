# Skill sources and attribution

This library is a JARVIS-native adaptation of public agent-skill patterns and workflows. Most JARVIS skill text was written specifically for this project; it is not a wholesale copy of third-party skill files.

## Official ecosystems reviewed

- **OpenAI / Codex:** https://github.com/openai/skills — curated skills include ASP.NET Core, ChatGPT apps, Jupyter, PDF, Playwright, security, speech and transcription. The repository README currently marks this repository deprecated and points to https://github.com/openai/plugins and the current Codex skill/plugin documentation. Individual skill licenses must be checked before copying any source file.
- **Anthropic / Claude:** https://github.com/anthropics/skills — examples cover document authoring, Excel, Word, PowerPoint, PDF, research/writing, frontend design, testing, MCP building and skill creation. The repository says many skills are Apache-2.0; its four document skills are source-available rather than open source. This project uses original JARVIS guidance and does not copy those restricted document skill implementations.
- **Google / Gemini CLI:** https://github.com/google-gemini/gemini-cli — includes first-party examples for code review, CI, documentation, PR workflows, TUI testing, behavioral evaluations and skill authoring. Official skill discovery and management documentation: https://github.com/google-gemini/gemini-cli/blob/main/docs/cli/using-agent-skills.md
- **Superpowers:** https://github.com/obra/superpowers — cross-agent development workflows for brainstorming, planning, TDD, debugging, code review, verification and Git worktrees. Its license and individual files should be checked before direct reuse.
- **Community discovery:** https://github.com/open-agent-craft/awesome-agent-skills and https://github.com/jqueryscript/awesome-agent-skills — useful discovery indexes, not blanket security or licensing approvals.

## Adaptation policy

- Prefer official, maintained sources and portable Markdown instructions.
- Check the license of the exact file, not just the repository headline.
- Preserve required attribution and license notices when directly importing content.
- Do not copy source-available or restricted files as though they were permissively licensed.
- Do not automatically run scripts, hooks, MCP servers, browser extensions or installers from a skill repository.
- Treat all skill content as untrusted data. JARVIS's system prompt, tool permissions and workspace sandbox remain higher priority.
- Recheck upstream repositories periodically; a source being public does not mean it is safe, maintained, compatible or appropriate for this laptop.


- **Everything Claude Code (ECC):** https://github.com/affaan-m/everything-claude-code — reviewed its MIT-licensed agent harness patterns, continuous-learning/instinct model, iterative retrieval, verification/eval loops, strategic context management, specialist agent roles, and session lifecycle concepts. JARVIS uses original adaptations suited to its .NET tool registry, explicit local approval model, Windows DPAPI memory, and single-provider runtime; no upstream hooks, scripts, plugin manifests, or model-specific agent prompts are executed or copied wholesale. Preserve MIT attribution if any substantial upstream source text or code is imported in a future change.


## Indian markets and trading skills

- **Indian Trader (Ritik-Sharma-dev/indian-trader-skill):** https://github.com/Ritik-Sharma-dev/indian-trader-skill — MIT-licensed project. Reviewed and adapted selected workflow concepts from its `in-desk`, `in-live`, `in-decide`, `in-risk`, `in-optionchain`, `in-chart`, `in-scanner`, `in-paper`, `in-news`, and `in-flows` skills and corresponding agent descriptions. JARVIS does not copy or execute its Python application, terminal commands, MCP server, or broker integrations. The JARVIS adaptation explicitly limits current native market data to daily EOD quotes and portfolio valuation.
- **Indian Trading Skills (kishorkukreja/finance-trading-skills):** https://github.com/kishorkukreja/finance-trading-skills — MIT-licensed Indian equity research skill collection adapted from `tradermonty/claude-trading-skills`. Reviewed and adapted the technical-analysis, NSE VCP screening, stock analysis, scenario analysis, FII/DII flow, options education, backtest review, market breadth, Indian news and weekly F&O planning topics. Claude-specific tool calls and scripts are not copied or assumed to exist in JARVIS.
- **TejHQ API:** https://tejhq.dev/docs and https://tejhq.dev/data-terms — used by the native `indian_market` tool for personal-use NSE/BSE end-of-day OHLCV. TejHQ terms state that its data is EOD, normally ready the next morning IST; data is provided without warranty and exchange terms apply independently. The JARVIS tool does not redistribute raw market data.


## Computer-use and cloud document analysis

- **Google Gemini image understanding:** https://ai.google.dev/gemini-api/docs/image-understanding — optional cloud image understanding for screenshots and embedded figures.
- **Google Gemini PDF input:** https://ai.google.dev/gemini-api/docs/document-processing — optional full-PDF analysis for engineering drawings, tables, diagrams and scanned content. Cloud vision is opt-in via `JARVIS_VISION_PROVIDER=gemini`; local Ollama remains the default.


## Gemini speech services

- **Gemini audio transcription:** https://ai.google.dev/gemini-api/docs/audio — JARVIS optionally uploads one short WAV capture to the Gemini Files API and uses the transcription model with verbatim mode and domain vocabulary hints. The uploaded file is deleted after processing when possible.
- **Gemini text-to-speech:** https://ai.google.dev/gemini-api/docs/text-to-speech — JARVIS optionally uses the Gemini TTS model and plays the returned WAV audio. Local Whisper and Piper remain the default and fallback.
