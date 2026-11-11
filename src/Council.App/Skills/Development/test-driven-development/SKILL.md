---
name: test-driven-development
description: Test-first implementation, regression coverage, verification and quality gates.
triggers: test driven development, TDD, unit test, integration test, regression test, xunit, pytest, test coverage, test suite
---
# Test-driven development

- Define observable behavior and acceptance criteria before implementation.
- Write a focused test that fails for the intended reason; distinguish a failing assertion from a broken test setup.
- Implement the smallest change that makes the test pass.
- Refactor only while keeping tests green.
- Cover edge cases, invalid inputs, security boundaries and regression scenarios.
- Avoid tests that merely mirror implementation details or mock away the behavior being verified.
- Run targeted tests first, then the full suite. Capture counts and failures.
- If execution is unavailable, label tests as not run; never imply they passed.
