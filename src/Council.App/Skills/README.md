# JARVIS Agent Skill Library

JARVIS uses the portable Agent Skills pattern: each Markdown skill has a small YAML-style header (`name`, `description`, optional `triggers`) followed by task instructions. The runtime discovers skills recursively and loads up to two matching skills for each user turn.

## Current skill areas

- **Engineering:** structural engineering, CAD/BIM, standards compliance, technical calculations, scientific computing.
- **Finance and Indian markets:** day-trading workflows, NSE/BSE equity research, portfolio monitoring, market breadth, FII/DII flows, India market news, technical analysis, options education, backtest review and macro scenarios.
- **Research and learning:** literature review, web research, technical calculations, PDF research, research tutoring, paper writing, knowledge retrieval.
- **Software development:** software engineering, systematic debugging, test-driven development, code review, Git workflows, API integration, agent evaluation, iterative retrieval, agent orchestration, verification loops, harness audits, context optimization and continuous learning.
- **Data and Office:** data analysis, Excel, Word, PowerPoint, PDF and general Office workflows.
- **Productivity:** meeting intelligence, multilingual voice profile, audio processing, personal knowledge, email communication, scheduling, project management, phone companion.
- **Automation and safety:** autonomous agents, browser workflows, local AI operations, cloud storage, safe agent operations.
- **Design:** diagrams and visualization, frontend design, image and media workflows.

## How skill routing works

1. JARVIS scans Markdown files in this directory.
2. It matches user wording against each skill's explicit triggers, description, name and category.
3. It loads the two highest-scoring relevant skills into the current model request.
4. The skills guide reasoning; actual actions still require a registered tool.
5. Skills are not model weights and do not grant new computer, browser, Office or cloud permissions.
6. Specialist agents are separate model invocations with isolated message histories. Their tool access is enforced by the runtime using per-agent allowlists; skills alone do not create agents.

Use `/skills` in the JARVIS console to list discovered skills. Add a new skill by creating a folder and a `SKILL.md` file with `name`, `description` and `triggers` metadata. The project copies all Markdown files under `Skills` to the application output directory.

## Important limitations

- Imported skill instructions cannot make an unsupported operation possible. The current Office tool still has limited operations; consult the Office-specific skill before claiming an edit or file creation.
- Scripts, binaries, macros, hooks, MCP servers and installers from public repositories are **not** automatically executed or installed. They require separate security and compatibility review.
- Private user recordings, personal profiles, API keys and other secrets must stay out of this repository.
- Review skill updates and test the runtime after adding new skills.


## ECC-inspired workflow additions

JARVIS includes native adaptations of selected Everything Claude Code practices: evidence-backed project learning, bounded iterative retrieval, explicit verification loops, context-budget discipline, role-based review passes, and harness self-audits. These are implemented as JARVIS skills and local workflows rather than importing Claude Code hooks, shell scripts, plugin manifests, or model-specific commands.

Project practices are deliberately opt-in: use `/learn <trigger> => <action>` to save one, `/instincts` to inspect them, and `/forget-instinct <id>` to remove one. They are encrypted with the same Windows-user DPAPI protection as local memory and are isolated by workspace path.


## Native specialist-agent runtime

The app exposes a `specialist_agents` tool to the main assistant and a `/agents` console command. Current agents:

- `planner`, `architect`, `tdd-guide`
- `code-reviewer`, `security-reviewer`, `build-error-resolver`
- `researcher`, `doc-updater`, `structural-engineering`

Use `specialist_agents` with `action=list`, `action=run` plus `agent` and `task`, or `action=team` plus comma-separated `agents` and a shared `task`. A team runs up to four independent agents concurrently. Each agent starts with a fresh context, sees only task/context explicitly supplied to it, and can call only its registered read-only tool allowlist. The main assistant retains write/execute permissions through the separate guarded workflow.

All agents currently use the configured provider and default model. Per-agent model routing, persistent agent-to-agent memory, background agent jobs, and write-capable delegated agents are not enabled. Do not describe those as implemented.


## Indian market monitoring

The `Finance/PortfolioMonitor` skill uses the registered `indian_market` tool. It stores only the user's manually supplied holdings/watchlist in the local application-data folder and fetches the latest available daily close from TejHQ. This is an end-of-day monitor, not a real-time feed or broker integration. The `Finance/DayTrading` and related skills are JARVIS-native adaptations of selected public workflows; they do not add an intraday feed, broker connection, or order execution. See `SOURCES.md` for upstream attribution.
