# CLAUDE.md - Instructions for Claude Code

You are working on **HotMic**, a Windows audio routing application. Keep responses concise and implementation-focused.

## Primary References

- `AGENTS.md` for architecture rules, threading constraints, workflow, testing policy, and WSL limitations.
- `docs/README.md` for the documentation index.
- `docs/technical/README.md` for DSP/analysis/visualization specs and code-reference format.
- `docs/architecture/` for system design and data-flow docs.
- `README.md` for product scope and user-facing requirements.

## UI/Core Consistency

- Never allow the UI to show a state the core layer will not execute.
- If core enforces or clamps parameter values, update the UI immediately (auto-switch) so backend behavior matches what the user sees.

## Spec Hygiene

Do not duplicate spec content here. When behavior changes, update the relevant doc under `docs/technical/` (DSP), `docs/architecture/` (system design), or `docs/reference/` (feature references) and keep its code references accurate.

## Testing

See `AGENTS.md` Testing Policy for full guidance. Key points:

- **Interface & workflow tests encouraged** - Test real user workflows: plugin lifecycle, config roundtrips, chain operations, preset loading, parameter clamping
- **Tests encode correct behavior** - When a test fails, fix the production code, not the test
- **Math verification** - DSP algorithms tested against pre-computed reference values (Python/NumPy)
- **Pre-computed, not re-implemented** - Expected values from external tools, not re-implementing the formula
- **Concurrency smoke tests** - Verify thread safety of lock-free patterns under concurrent access
- **Deterministic and specific** - Fixed inputs, concrete expected values, not just presence checks

## Pre-commit CI check

**Run `make ci` before committing any change.** This runs the same lint, format, typecheck, and test steps as GitHub Actions. Do not commit if it fails.
