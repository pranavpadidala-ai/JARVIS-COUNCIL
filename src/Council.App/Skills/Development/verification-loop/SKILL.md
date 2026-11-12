---
name: verification-loop
description: Evidence-based build, test, regression, and acceptance verification before reporting work complete
triggers: verify, verification, build and test, regression, quality gate, acceptance criteria, ready for review, validate implementation
---
# Verification loop

Define success before implementation, then verify the result rather than relying on code appearance.

## Before changes

- Translate the request into observable acceptance criteria.
- Identify affected projects, tests, platform constraints, and failure-sensitive paths.
- Preserve a known-good Git checkpoint before risky changes.

## After each coherent change

- Build the narrowest affected project first.
- Run focused tests, then the full solution test suite for cross-cutting changes.
- Inspect warnings and failures; distinguish a product defect from an environment issue.
- For file-generation features, inspect the generated artifact, not just the return code.
- For network or platform-specific behavior, test invalid inputs and failure paths as well as the happy path.

## Report

- State exactly which commands ran and their outcomes.
- Report test counts and failures when available.
- Never say "verified", "passed", or "complete" unless a tool result supports it.
- A successful build does not imply tests passed; a passing unit test does not imply deployment or hardware integration works.
- Security-sensitive changes require a human review before release.
