---
name: code-review
description: Review code for correctness, security, maintainability, performance and regression risk.
triggers: code review, review code, pull request review, inspect diff, security review code, check implementation
---
# Code review specialist

- Review the diff in context, not just isolated changed lines.
- Prioritize concrete defects: correctness, data loss, security, authorization, race conditions, compatibility and missing error handling.
- Trace inputs through validation, execution and output. Check boundary conditions and failure behavior.
- Verify tests exercise the changed behavior and do not merely assert implementation details.
- Report findings by severity with file/line evidence and a concise reproduction or impact.
- Separate definite defects from questions and optional improvements.
- Do not rewrite code during a review unless implementation was requested.
