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
