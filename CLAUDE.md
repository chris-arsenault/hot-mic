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

- **No long-term behavior tests** - Don't rely on unit tests to control behavior
- **Math verification allowed** - Testing DSP algorithms against pre-computed reference values is encouraged
- **Pre-computed, not re-implemented** - Expected values must come from external tools (Python/NumPy), not from re-implementing the formula in the test
- **Direct production outputs** - Call the production method under test and compare to constants; no roundtrips or internal consistency checks
- **Deterministic and specific** - Use fixed inputs and assert against concrete expected values, not just presence checks
