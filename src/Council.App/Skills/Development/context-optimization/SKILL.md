---
name: context-optimization
description: Context-window discipline, history retrieval, compaction, model selection, and cost-aware inference
triggers: context window, too much context, compact conversation, summarize state, token usage, model selection, inference cost, prompt efficiency
---
# Context and inference optimization

Preserve task-critical information while avoiding repeated or irrelevant context.

- Start with the current request and the smallest relevant skill set.
- Retrieve historical conversation only when it changes the answer; prefer focused keyword retrieval over replaying the entire transcript.
- For long tasks, maintain a compact working state: objective, constraints, decisions, changed files, test status, blockers, and next action.
- Keep raw logs and large documents outside the prompt; retrieve targeted ranges and summarize with source references.
- Use the local model for bounded/offline/simple work when its quality is adequate. Use a cloud model for difficult reasoning only when configured and authorized.
- Never silently switch to a paid provider, a model outside the user's allowlist, or an external service.
- Before compaction, preserve unresolved decisions and exact verification state. Do not summarize an unverified test as passed.
- Avoid repeating system instructions or injecting unrelated memories and skills.
