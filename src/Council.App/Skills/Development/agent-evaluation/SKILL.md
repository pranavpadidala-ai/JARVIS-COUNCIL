---
name: agent-evaluation
description: Evaluate model and agent quality using task suites, scoring rubrics, regression checks, pass-at-k reliability and cost tracking
triggers: evaluate AI, benchmark, model comparison, agent evaluation, quality score, regression benchmark, hallucination test, eval harness, pass at k
---
# Agent evaluation and eval harness

Treat evaluations as the unit tests of AI-assisted behavior. Define expected behavior before changing prompts, tools, routing, or agent workflows.

## Eval types

- Capability eval: a task that the current assistant could not reliably complete before the change.
- Regression eval: a previously supported task that must continue to work.
- Safety eval: an adversarial or malformed request that must be refused, bounded, or routed to approval.
- Operational eval: latency, cancellation, resource use, offline behavior, and cost/provider selection.

## Graders

1. Code-based: deterministic assertions, unit tests, build results, output schema validation, and file/artifact inspection.
2. Model-based: rubric scoring for open-ended explanations, research synthesis, or code quality. Treat scores as advisory and retain evidence.
3. Human: required for security-sensitive changes, irreversible actions, ambiguous requirements, and release decisions.

Prefer deterministic checks whenever possible. A model grader must not approve its own unsafe or unverified action.

## Reliability metrics

- pass@1: success on the first attempt.
- pass@k: at least one success within k independent attempts.
- pass^k: all k attempts succeed; use this stricter measure for critical workflows.
- Track sample size. A small number of successes is not a reliable estimate of production reliability.

## Workflow

1. Write acceptance criteria and expected outputs before implementation.
2. Capture a baseline commit and existing test results.
3. Run capability, regression, safety, and operational evals after the change.
4. Record failures with the exact prompt, provider/model, tool results, and environment; redact secrets and private data.
5. Fix the underlying defect, not merely the test assertion.
6. Re-run the full relevant suite and compare against baseline.
7. Report measured outcomes, untested dimensions, and any human review still required.

Never invent eval results, claim pass@k from a single run, or treat an LLM's self-assessment as deterministic proof.
