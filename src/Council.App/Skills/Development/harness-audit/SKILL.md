---
name: harness-audit
description: Audit an AI assistant harness for tool coverage, safety, memory, reliability, routing, evaluation, and documentation consistency
triggers: harness audit, audit jarvis, agent reliability, self audit, quality assessment, capability review, tool coverage, safety review
---
# Agent harness audit

Review the running assistant as a system, not just as a model prompt.

## Audit dimensions

1. **Core loop:** cancellation, bounded tool rounds, malformed model output, retry behavior, and error reporting.
2. **Tools:** registry coverage, argument validation, path boundaries, timeouts, rate limits, and truthful success reporting.
3. **Security:** secrets redaction, untrusted external content, SSRF, least privilege, explicit approvals, and safe rollback.
4. **Memory:** opt-in behavior, encryption, retention, project scope, deletion semantics, and separation of facts from learned practices.
5. **Skills and context:** discoverability, routing accuracy, context budgets, prompt-injection resistance, and documented unsupported capabilities.
6. **Model routing:** local/cloud selection, explicit model IDs, cost controls, and no silent paid fallback.
7. **Verification:** focused and full tests, regression coverage, CI, platform-specific checks, and artifact inspection.
8. **Operations:** startup diagnostics, logs, recovery after interruption, background task visibility, and phone/desktop control boundaries.
9. **Documentation:** setup instructions, environment variables, permission model, and claims that match actual code.

## Output

For each finding, include severity, evidence (file/type/test), user impact, and a concrete remediation. Verify critical findings before reporting them. Prioritize changes that improve reliability or safety on constrained hardware; avoid adding duplicate runtimes or heavyweight dependencies.
