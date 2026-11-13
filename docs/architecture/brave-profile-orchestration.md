# Brave profile orchestration and compliant provider failover

## Goal

Give JARVIS a modular browser-profile manager for legitimate account separation, isolated browser tasks, and reliable state continuity. This is distinct from provider quota management: browser login sessions must not be used as a mechanism to evade provider rate limits, free-tier caps, or account restrictions.

## 1. Isolated Brave profiles

The browser manager may create and manage named Brave profiles using distinct, stable `--user-data-dir` directories. Example logical profiles:

- `personal`
- `work`
- `research`
- `testing`

A deployment may define additional profiles, including profiles associated with services the user is authorized to access. Do not hard-code a nine-account Claude/ChatGPT/Gemini rotation pool.

Requirements:

- Resolve the Brave executable from an explicit setting or known installation locations; never assume a path.
- Store profile metadata (profile ID, purpose, user-visible label, data directory, created-at, last-used-at) in JARVIS application data, not source control.
- Keep each data directory separate and stable; validate paths and prevent traversal outside the configured JARVIS browser-data root unless the user explicitly configures another location.
- Never copy cookies, browser databases, local storage, tokens, or session secrets between profiles.
- Do not scrape browser credential stores or export authentication state.
- Do not log URLs containing secrets, cookies, authorization headers, or page contents by default.
- Use a single-instance/lock strategy per profile to avoid corrupting Chromium profile databases.
- Support launch, inspect process status, graceful close, and cleanup of temporary non-persistent profiles.
- Headless mode is permitted for public browsing, testing, and tasks supported by the target service's terms. Some sites require a visible browser or interactive sign-in; report this rather than attempting to bypass CAPTCHA, MFA, bot checks, or access controls.
- For GUI tasks, use a visible, user-authorized session with observe-before-act and screenshot verification. Do not attempt to control hidden windows.

Example launch shape (arguments are assembled by the implementation, never from untrusted page content):

```text
brave.exe --user-data-dir="<dedicated-profile-directory>" --headless=new
```

## 2. Browser profile manager interface

Provide a small service abstraction:

- `ListProfiles()`
- `CreateProfile(label, purpose)`
- `LaunchProfile(profileId, mode)`
- `GetProfileStatus(profileId)`
- `CloseProfile(profileId)`
- `DeleteTemporaryProfile(profileId)`

Profile modes:

- `Headless`: only for compatible, permitted browser workflows.
- `Visible`: interactive work requiring user sign-in, consent, or visual confirmation.

A profile launch must never imply that a user is authenticated or that a task is authorized. The task's own permissions and confirmation requirements still apply.

## 3. Durable task checkpoint and handoff

When a browser or provider task is interrupted, persist a versioned checkpoint containing:

- task ID, objective, acceptance criteria, and user-granted permissions;
- completed steps and exact next action;
- relevant non-secret URLs and page titles;
- output artifacts and content hashes;
- provider route and model alias (not credentials);
- Git commit and working-tree state where relevant;
- retry count, error classification, and idempotency key;
- timestamp and checkpoint schema version.

Never include cookies, browser profile database contents, passwords, API keys, OAuth refresh tokens, or raw authorization headers. Store sensitive credentials only in the approved OS credential vault or provider-specific OAuth flow. A checkpoint can identify a profile by opaque profile ID, but must not serialize its authentication state.

Before retrying a browser write or external action, reconcile whether the prior attempt succeeded. Do not duplicate submissions, purchases, messages, or other side effects after an ambiguous timeout.

## 4. Compliant cross-provider failover

Provider failover must use documented APIs, supported OAuth integrations, or other explicitly permitted interfaces. It must not switch among consumer website login profiles to bypass quotas or free-tier limits.

Routing rules:

1. Prefer the configured route according to the user's explicit policy.
2. Use provider-reported quota/reset information and documented rate-limit headers where available.
3. On 429/quota exhaustion, honor Retry-After/reset time and mark that route unavailable until eligible again.
4. On transient 5xx/network failures, use bounded exponential backoff and retry only idempotent operations.
5. On authentication errors, pause that route and request reauthentication through the official flow; never harvest or replay browser cookies.
6. Select another independently authorized API route only if its terms, credentials, model access, and user cost policy permit the request.
7. Never silently switch to a paid route. Enforce per-request and monthly cost caps.
8. When all permitted routes are unavailable, preserve the checkpoint, report the cause and next eligible reset where known, and offer a safe resume path.

A provider's consumer web subscription is not automatically an API entitlement. The implementation must not assume that a ChatGPT, Claude, or Gemini website login can be used as an API credential.

## 5. Security and privacy

- Use Windows Credential Manager/DPAPI or official OAuth token storage for secrets.
- Do not commit browser data directories, profile metadata containing sensitive details, tokens, or local state files.
- Default browser task telemetry to metadata only; make page-content capture explicit and task-scoped.
- Restrict remote phone commands to queue-only behavior and existing local approval rules.
- Keep destructive and irreversible browser actions behind the existing confirmation policy.
- Make profile creation and deletion visible to the user; never delete a persistent profile as an automatic cleanup action.

## 6. Acceptance tests

- Two named profiles launch with distinct `--user-data-dir` paths and do not share cookies or local storage.
- Concurrent launch of the same profile is rejected or safely serialized.
- Invalid profile IDs and path traversal attempts are rejected.
- Temporary profiles can be removed; persistent profiles are not automatically deleted.
- Headless-incompatible pages return a clear limitation and do not trigger CAPTCHA/MFA bypass.
- Checkpoints contain no secrets or browser authentication state.
- Simulated 429 honors reset/Retry-After and does not switch to another consumer login.
- An independently configured permitted API route can take over with the same task checkpoint.
- Paid routes remain blocked unless explicitly enabled within the user's cost limits.
- Exhausted route pools preserve task state and provide a resumable status.
- Ambiguous external writes are reconciled before retry to prevent duplicate side effects.

## 7. Explicit non-goals

- Rotating among multiple Claude, ChatGPT, or Gemini consumer accounts to evade free-tier limits, rate limits, or account restrictions.
- CAPTCHA, MFA, bot-detection, access-control, or paywall bypass.
- Cookie/session scraping, copying, replay, or exporting.
- Silent paid-model fallback or hidden external side effects.
