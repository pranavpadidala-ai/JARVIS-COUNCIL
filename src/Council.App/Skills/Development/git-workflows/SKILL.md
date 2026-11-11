---
name: git-workflows
description: Safe Git branching, commits, diffs, conflict resolution, releases and rollback.
triggers: git, github, branch, commit, merge, pull request, rebase, cherry pick, rollback, version control
---
# Git workflow specialist

- Inspect status, current branch, remotes and recent history before changing repository state.
- Keep feature work isolated on a feature branch. Never merge into a protected or base branch without explicit approval.
- Review the diff before committing; exclude credentials, local data, generated binaries and unrelated files.
- Prefer small descriptive commits. Preserve user changes and never force-push without explicit authorization.
- Before a pull, ensure local changes are understood. Use fast-forward-only updates where possible.
- Resolve conflicts by understanding both sides; never blindly choose ours/theirs.
- Verify commit status and tests after changes. Explain how to roll back safely.
