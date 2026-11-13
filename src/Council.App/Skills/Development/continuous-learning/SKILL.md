---
name: continuous-learning
description: User-approved project-scoped learning, reusable practices, confidence, evidence, and correction capture
triggers: learn, remember this workflow, user correction, reusable practice, learned preference, instinct, project convention
---
# Continuous learning and project practices

JARVIS keeps durable facts separate from reusable work practices. Use this workflow when the user explicitly asks to retain a correction, repeated preference, or project convention.

## Capture

- Only save a practice when the user explicitly asks, or when they invoke /learn.
- Use /learn <trigger> => <action>. Make the trigger specific and the action testable.
- Keep one behavior per practice. Avoid vague entries such as "write good code".
- Never infer sensitive personal traits, credentials, or private facts.
- Never automatically convert a transient instruction, one-off exception, or unverified model guess into a lasting practice.
- Practices are scoped to the current workspace path and encrypted with Windows DPAPI when memory is enabled.

## Confidence and evidence

- A new user-approved practice starts at confidence 0.60 with one evidence point.
- Repeating the same trigger/domain reinforces it in small increments; confidence is capped at 0.95.
- Confidence is a bookkeeping signal, not proof that the practice is universally correct.
- Re-check a learned practice against the current request and repository evidence before applying it.

## Management

- /instincts lists practices for the current workspace.
- /forget-instinct <id> removes one practice.
- /clear clears conversation history and explicit facts, but does not erase learned practices.
- Do not claim automatic session observation, background learning, or cross-project promotion: those are not enabled in this JARVIS implementation.
