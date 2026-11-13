# Durable task recovery and Brave profile manager

## Scope

This implementation turns the existing persisted development-task queue into a restart-recoverable queue and adds isolated Brave browser profiles. It does not rotate consumer AI website accounts or copy browser authentication state.

## Durable task checkpoints

- The existing task index remains in the user's local application data.
- Each running, paused, or failed task gets a separate versioned JSON checkpoint under `%LOCALAPPDATA%\JARVIS\checkpoints`.
- Writes use a temporary file followed by an atomic replacement.
- Checkpoints preserve task objective, completed steps, next action, provider route/model alias when known, Git commit/tree status when known, build/test log references, retry count, failure classification, idempotency key, artifact hash references, and optional opaque Brave profile ID.
- Credentials, API keys, OAuth tokens, cookies, browser databases, and session state are not checkpoint fields.
- On startup, tasks left in Running state are converted to Paused and restored from their latest checkpoint where available. The user can inspect and explicitly resume them.
- Artifact hashing is workspace-scoped; paths escaping the configured workspace are rejected.

The current autonomous executor records provider identity and the clean Git baseline. More detailed build/test log and artifact references can be populated by later executor integrations.

## Brave profile manager

The `brave_profiles` tool supports:

- `list`
- `create` with a label, purpose and optional temporary flag
- `launch` with `visible` or `headless` mode and an optional HTTP(S) start URL
- `status`
- `close`
- `delete_temp`

Each profile gets a stable unique data directory beneath `%LOCALAPPDATA%\JARVIS\BraveProfiles\data`. Profile metadata is stored locally. The manager resolves Brave from an explicitly configured `JARVIS_BRAVE_PATH` or standard Windows installation locations. It uses a per-profile lock and distinct Chromium `--user-data-dir` values.

Use visible mode for interactive sign-in, consent and GUI work. Headless mode is only for compatible public browsing/testing. A launch does not assert that a site authenticated the user or authorized the task.

## Provider policy

Provider fallback remains separate from browser profiles. Only documented API or officially supported OAuth integrations may be routed. Quota/reset signals must be honored; paid routes require explicit opt-in and configured cost limits. Browser consumer subscriptions are not API credentials.

## Validation

The Windows .NET 8 GitHub Actions workflow builds the full solution and runs the test suite. The new recovery and profile-manager tests cover restart recovery, artifact path boundaries, distinct profile directories, temporary-only cleanup, and rejection of non-HTTP(S) launch URLs. A passing CI run and a first local Windows launch are still required before calling the feature fully validated.
