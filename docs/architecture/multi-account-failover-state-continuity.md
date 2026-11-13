# Multi-Account AI Failover and State Continuity

Status: approved architecture requirement; implementation pending.

## Goal

Keep long-running JARVIS tasks recoverable when a model endpoint becomes unavailable, approaches a documented quota, returns a transient error, or loses its session. Preserve verified progress and continue with the next eligible model/provider without repeating completed work.

This is an orchestration feature of the existing JARVIS runtime, development-task queue, provider adapters, encrypted local state, and Git checkpoint workflow. It is not a separate assistant or a reason to duplicate the existing task queue.

## Policy and account boundaries

- Use documented provider APIs, configured gateways (including explicitly allowlisted OmniRoute routes), and local Ollama as preferred execution paths.
- Support multiple credentials/accounts only when the provider explicitly permits the intended use. Never rotate accounts, browser profiles, cookies, or API keys to evade provider rate limits, free-tier caps, or access controls.
- Do not scrape browser cookies, extract session tokens, bypass MFA/CAPTCHA, impersonate a user, or automate consumer web UIs as an undocumented API.
- A provider quota response is authoritative. The 95% threshold is advisory only when reliable usage telemetry exists; never fabricate a usage percentage from message counts.
- Respect Retry-After, reset windows, provider terms, model availability, user cost limits, and configured allowlists. Do not silently move a task to a paid model or billable route.
- Keep credentials in Windows Credential Manager / DPAPI-backed secret storage. Never put keys, cookies, tokens, or raw authentication headers in state packets, logs, Git, or prompts.

## Runtime model

Introduce a provider-pool abstraction above the existing provider adapters:

- Provider pool: ordered eligible routes for a task (for example, Gemini API -> OpenAI API -> local Ollama, subject to user configuration).
- Route: provider, model ID, credential reference, capabilities, cost class, quota/health state, and eligibility policy.
- Account identity is an opaque local alias; secrets remain in the OS credential vault.
- Routing policy supports preferred provider, allowed fallbacks, local-only mode, maximum cost, model capability requirements, and whether cross-provider fallback is allowed.
- Specialist agents and the main assistant should use the same routing policy unless a task explicitly pins a provider/model.

## State machine

READY -> ACTIVE -> NEAR_LIMIT | RATE_LIMITED | TRANSIENT_FAILURE | AUTH_REQUIRED | PROVIDER_UNAVAILABLE -> HANDOFF_PENDING -> RESUMING -> ACTIVE -> COMPLETED

Terminal/paused states: POOL_EXHAUSTED, USER_APPROVAL_REQUIRED, CANCELLED, FAILED.

Rules:
1. Before each model call, choose the highest-priority eligible route that satisfies capability and cost constraints.
2. If trustworthy usage telemetry reaches the configured threshold (default 95%), mark the route NEAR_LIMIT and stop assigning new work to it. Finish or checkpoint the current safe unit of work.
3. On HTTP 429/quota exhaustion, parse provider error details and Retry-After/reset time. Mark the route unavailable until reset or provider recovery.
4. On transient network/server errors, use bounded exponential backoff with jitter; do not burn through every account for a temporary outage.
5. On authentication failure, mark AUTH_REQUIRED; do not repeatedly retry or attempt to repair credentials automatically.
6. If no eligible route remains, persist the task as POOL_EXHAUSTED and notify the user with the reason, provider pool, and reset estimates when known.
7. Never switch to a paid route unless the user has explicitly enabled that route and its cost ceiling.

## Durable handoff packet

Persist a versioned, structured packet locally before changing routes. Suggested fields:

- schema_version, task_id, run_id, created_utc
- user_goal and acceptance_criteria
- current_phase and exact_next_action
- completed_steps with evidence and timestamps
- pending_steps and unresolved_questions
- decisions_and_constraints
- workspace_root and relevant file paths
- changed_files with hashes and concise change summaries
- last_verified_git_commit and working_tree_status
- last_build_result, last_test_result, and relevant log excerpts
- tool_operations with operation IDs, status, and idempotency/reconciliation instructions
- artifacts and their paths/hashes
- provider-independent task context
- failed attempts and known dead ends
- route history and sanitized error classifications

Never include secrets, raw cookies, access tokens, or unnecessary full conversation transcripts. Prefer concise evidence-backed state over a generic chat summary.

## Handoff and resume procedure

1. Stop accepting new model work on the unhealthy route.
2. Allow an in-flight read-only call to finish if safe; otherwise cancel it. For writes or GUI actions, reconcile the operation before retrying.
3. Save the handoff packet atomically and flush it to durable storage.
4. Verify the latest checkpoint and inspect Git status/diff. Do not discard uncommitted user changes.
5. Select the next eligible route according to the configured pool and task requirements.
6. Build bootstrap context from the packet plus only the files/evidence needed for the next action.
7. Ask the replacement model to acknowledge the current state and propose the next action; JARVIS validates that action against acceptance criteria, tool permissions, and existing completed work.
8. Resume from the last verified checkpoint. Do not blindly replay a tool call whose outcome is unknown.
9. Record route transition, packet version, validation outcome, and final result in the local audit log.

## Duplicate-work and idempotency protection

- Every task step and external tool operation gets a stable operation ID.
- Before repeating a step, compare its recorded status with current files, hashes, Git diff, test output, and external state where safely readable.
- Treat an interrupted write as OUTCOME_UNKNOWN until reconciled; do not assume it failed.
- Reuse completed outputs when hashes/evidence still match.
- Run existing build/test validation after code changes. Commit only through the current guarded development workflow after successful validation.
- Failed attempts must follow the existing rollback/checkpoint rules and must not overwrite unrelated user work.

## Observability and user experience

Expose in the JARVIS UI:
- active provider/model and opaque account alias
- route health: READY, NEAR_LIMIT, RATE_LIMITED, AUTH_REQUIRED, UNAVAILABLE
- last error classification and reset time (if supplied)
- handoff count, last checkpoint, and current task phase
- whether fallback is local, free, or potentially billable

Default behavior: automatic handoff among explicitly authorized, policy-compliant routes. Ask the user only when all eligible routes are exhausted, credentials need user action, the only remaining route exceeds the cost policy, or an irreversible/ambiguous operation needs approval.

## Configuration (proposed)

- JARVIS_ROUTING_ENABLED
- JARVIS_ROUTING_PREFERRED_PROVIDER
- JARVIS_ROUTING_FALLBACK_ORDER
- JARVIS_ROUTING_ALLOW_CROSS_PROVIDER
- JARVIS_ROUTING_MAX_COST
- JARVIS_ROUTING_NEAR_LIMIT_PERCENT (default 95; only used with trustworthy telemetry)
- JARVIS_ROUTING_RETRY_MAX_ATTEMPTS
- JARVIS_ROUTING_ALLOW_LOCAL_FALLBACK
- JARVIS_ROUTING_STATE_PATH

All settings should have safe defaults and be documented. Do not infer or silently enable paid access.

## Acceptance tests

- A simulated 429 with Retry-After marks only that route unavailable until reset and selects the next eligible route.
- A simulated quota-exhaustion response triggers handoff without losing task state.
- A transient 5xx/network failure uses bounded retry before route failover.
- An authentication error pauses that route and never attempts credential extraction or login bypass.
- A 95% threshold is applied only when the adapter supplies reliable usage telemetry.
- A handoff packet round-trips through encrypted durable storage and contains no secrets.
- A replacement route resumes at the exact next incomplete action.
- A completed step is not repeated after handoff.
- An unknown-outcome write is reconciled before retry.
- A paid route is never selected when cost is disallowed or exceeds the configured ceiling.
- Pool exhaustion persists task state and produces one actionable user notification.
- Existing single-provider, mock, Ollama, Gemini, OpenAI, and OmniRoute flows remain backward-compatible.
