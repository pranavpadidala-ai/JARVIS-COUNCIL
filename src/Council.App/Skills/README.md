# JARVIS Agent Skill Library

JARVIS uses the portable Agent Skills pattern: each Markdown skill has a small YAML-style header (`name`, `description`, optional `triggers`) followed by task instructions. The runtime discovers skills recursively and loads up to two matching skills for each user turn.

## Current skill areas

- **Engineering:** structural engineering, CAD/BIM, standards compliance, technical calculations, scientific computing.
- **Research and learning:** literature review, web research, technical calculations, PDF research, research tutoring, paper writing, knowledge retrieval.
- **Software development:** software engineering, systematic debugging, test-driven development, code review, Git workflows, API integration, agent evaluation.
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

Use `/skills` in the JARVIS console to list discovered skills. Add a new skill by creating a folder and a `SKILL.md` file with `name`, `description` and `triggers` metadata. The project copies all Markdown files under `Skills` to the application output directory.

## Important limitations

- Imported skill instructions cannot make an unsupported operation possible. The current Office tool still has limited operations; consult the Office-specific skill before claiming an edit or file creation.
- Scripts, binaries, macros, hooks, MCP servers and installers from public repositories are **not** automatically executed or installed. They require separate security and compatibility review.
- Private user recordings, personal profiles, API keys and other secrets must stay out of this repository.
- Review skill updates and test the runtime after adding new skills.
