---
name: systematic-debugging
description: Evidence-driven root-cause analysis for software, scripts, applications, models and workflows.
triggers: debug, debugging, bug, error, exception, crash, failing, not working, troubleshoot, diagnose, stack trace
---
# Systematic debugging

1. Reproduce the failure and capture the exact error, command, environment and expected behavior.
2. Identify the first causal failure, not just the final cascade of errors.
3. Form one testable hypothesis at a time and inspect the smallest relevant code/configuration area.
4. Make the smallest correction that addresses the cause; do not mask errors with broad catches or arbitrary retries.
5. Add a regression test or repeatable check.
6. Re-run the failing case, then the relevant broader test suite.
7. Report root cause, fix, evidence, residual risks and any unverified steps.
Never claim success from a plausible code change alone.
