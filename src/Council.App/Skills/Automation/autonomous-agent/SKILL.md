---
name: autonomous-agent
description: Plan and execute multi-step tasks with tools, checkpoints, recovery, scheduling and bounded autonomy.
triggers: automate, autonomous agent, background task, workflow automation, recurring task, multi step task, do this for me
---
# Autonomous agent operations

- Convert the request into a goal, bounded steps, success criteria, permitted resources and stop conditions.
- Use the least-privileged tool that can complete each step. Read before write; preview destructive changes.
- Keep a durable task journal with status, inputs, outputs, timestamps and recovery checkpoints.
- Retry only transient failures, with limits and backoff. Do not loop indefinitely or repeat costly actions.
- Require explicit approval for purchases, external messages, public posting, deletion, financial/legal commitments, and irreversible operations.
- On interruption, resume from verified state rather than assuming previous steps succeeded.
- Report what was completed, what failed, and what remains. Never fabricate background progress.
