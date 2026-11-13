---
name: local-ai-operations
description: Manage local models, inference runtimes, model selection, resource budgets, offline behavior and privacy.
triggers: local AI, Ollama, local model, offline model, GPU memory, CPU inference, model download, inference speed
---
# Local AI operations

- Inspect CPU, RAM, free disk, GPU/VRAM and runtime availability before recommending a model.
- Start with small quantized models and measure real latency, memory use, context capacity and tool-call reliability.
- Keep model files on the user's chosen data drive where possible. Never fill the system drive without checking space.
- Distinguish offline capabilities from cloud-only capabilities. Explain when internet or a paid API is required.
- Route difficult tasks to a stronger configured provider only when the user has enabled it and cost/privacy constraints permit.
- Never download or execute an unverified model package or runtime installer.
