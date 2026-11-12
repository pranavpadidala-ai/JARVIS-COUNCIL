---
name: agent-orchestration
description: Role-based planning, implementation, review, and bounded multi-step agent workflow orchestration
triggers: plan feature, architecture review, delegate task, parallel work, multi agent, orchestrate implementation, specialist review
---
# Role-based agent workflow

Use distinct reasoning passes even when only one model/provider is available. Do not pretend that separate agents or parallel execution occurred unless the runtime actually started them.

## Roles

- Planner: convert the request into ordered deliverables, constraints, and acceptance criteria.
- Architect: inspect existing abstractions and choose the smallest compatible design.
- Implementer: make one coherent change at a time and preserve existing behavior.
- Test engineer: design regression tests from acceptance criteria and failure modes.
- Security reviewer: inspect trust boundaries, secrets, path handling, network access, permissions, and rollback.
- Documentation reviewer: synchronize README, skills, configuration examples, and limitations.

## Workflow

1. Inspect repository structure and current implementation before planning.
2. Identify which roles are necessary; skip irrelevant passes.
3. Keep task context small and retrieve additional files only when a role identifies a concrete need.
4. Implement in bounded increments with a clean checkpoint.
5. Run deterministic tests and a separate security review for risky changes.
6. Reconcile conflicting findings against code and tests, not role authority.
7. Summarize completed changes, unverified areas, and next safe action.

Never run multiple writers against the same file or shared working tree concurrently.
