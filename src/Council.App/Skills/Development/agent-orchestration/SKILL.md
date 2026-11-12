---
name: agent-orchestration
description: Delegate bounded tasks to native JARVIS specialist agents with isolated contexts and per-agent read-only tool permissions
triggers: plan feature, architecture review, delegate task, parallel work, multi agent, orchestrate implementation, specialist review, agent team
---
# Native specialist-agent orchestration

JARVIS has a real delegated-agent runtime exposed through the \`specialist_agents\` tool. Agents are separate model invocations with fresh message histories, not just role labels inside one prompt.

## Available specialists

- \`planner\`: implementation plans and acceptance criteria
- \`architect\`: design, interfaces, data flow and trade-offs
- \`tdd-guide\`: regression-test design and red/green/refactor strategy
- \`code-reviewer\`: correctness, maintainability and regression review
- \`security-reviewer\`: trust boundaries, secrets, permissions and attack surfaces
- \`build-error-resolver\`: root-cause diagnosis from build/test logs and source
- \`researcher\`: public-source research and evidence synthesis
- \`doc-updater\`: documentation consistency and proposed text
- \`structural-engineering\`: engineering/FE modelling review using supplied project evidence

## Invoking agents

- \`action=list\`: inspect the catalogue and permissions.
- \`action=run\`: run one agent with \`agent\`, \`task\`, and optional \`context\`.
- \`action=team\`: run 1–4 independent agents concurrently with \`agents\`, \`task\`, and optional shared \`context\`.
- Console command \`/agents\` lists the same catalogue.

Provide focused tasks and only the relevant source excerpts, logs, requirements, or data. Agents have isolated contexts and do not inherit the main conversation automatically.

## Security boundaries

Each agent has a code-enforced read-only allowlist. Reviewers/planners can inspect workspace files; the researcher can search/open public web pages; the structural engineering agent can inspect workspace files and read-only Abaqus status. Agents cannot create/edit/delete files, run shell commands, commit code, or invoke the autonomous development executor. The runtime caps each invocation at four model rounds and six tool calls, limits input context, and caps a parallel team at four agents.

The configured JARVIS provider/default model is used for all agents today. Separate per-agent model routing, long-running background agents, persistent inter-agent state, and write-capable agents are not implemented. Do not claim that different providers/models or separate machines are being used.

## Recommended delegation pattern

1. For substantial changes, ask \`planner\` to create an ordered plan and acceptance criteria.
2. Ask \`architect\` to validate the design before large structural changes.
3. Ask \`tdd-guide\` to identify tests before implementation.
4. Keep implementation in the main assistant's guarded development workflow.
5. Run \`code-reviewer\` and, for sensitive changes, \`security-reviewer\` on the actual diff and relevant tests.
6. Use \`build-error-resolver\` with exact build/test logs when validation fails.
7. Use \`doc-updater\` to reconcile docs with the verified implementation.

Parallelize only independent analyses. Never run multiple writers against the same file or shared working tree. Agent recommendations are not approvals; the main workflow and user approval rules remain in force.
