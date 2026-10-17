# Scope Guard

These rules apply to every task unless the user explicitly overrides them.

## Goal

Deliver only the requested outcome with the smallest change that fully works.

## Before changing files

- Inspect the relevant code, tests, and configuration directly.
- Verify uncertain assumptions before building on them.
- Define a short task contract:
  - **Outcome:** the exact behavior requested.
  - **Non-goals:** what this task will not change.
  - **Files:** the smallest expected set of files to touch.
  - **Proof:** the check that will demonstrate success.
- Prefer one implementation path. Split work only when parts are genuinely independent.

## While implementing

- Reuse existing helpers, patterns, dependencies, and test setup before adding anything.
- Fix bugs at the root cause instead of layering patches around the symptom.
- Add an abstraction, adapter, or configuration layer only when the current requirement or a second real caller needs it.
- Preserve behavior outside the requested scope.
- Do not design for hypothetical future requirements.
- Remove code replaced by the change. Keep the old path only when compatibility is explicitly required.

## Pause before expanding scope

Read-only investigation is always allowed. Get approval before:

- Touching unrelated files or materially expanding the task.
- Adding a dependency, framework, service, or test infrastructure.
- Changing a public API, schema, storage format, or wire format.
- Deleting or overwriting user data, discarding uncommitted work, rewriting history, or dropping data.
- Keeping two implementations of the same behavior active.

## Verification

- Run the narrowest existing checks that exercise the changed behavior.
- Extend the most relevant existing test before creating a new test file.
- Add a test only when changed user-visible behavior lacks coverage, a regression risk exists, or the user requests one.
- Do not add unrelated coverage or new test infrastructure for this task.
- Passing tests do not justify extra abstractions or scope.

## If the work starts growing

Stop when the task begins accumulating future-use layers, workaround stacks, unrelated cleanup, or tests for unstated behavior. Reduce the plan and request approval for any necessary expansion.

## Done means

- The requested behavior works and the acceptance criteria are met.
- Relevant checks pass, with the exact commands and results reported.
- Every touched file is necessary and the diff contains no unrelated changes.
- No debug code, backup copies, dead paths, or scratch files remain.
- Assumptions, limitations, and unverified runtime behavior are stated clearly.

<!-- Adapted from the Scope Guard concept shared by Vox: https://x.com/Voxyz_ai/status/2094039936475484393 -->