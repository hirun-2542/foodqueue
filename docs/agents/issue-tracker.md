# Issue tracker: Local Markdown

This repo has no remote issue tracker yet, so specs and tickets live as local Markdown files. Creating a remote, or editing agent instruction files, is the user's decision — not part of publishing.

## Conventions

- One feature per directory: `<feature-slug>/`.
- Spec: `<feature-slug>/spec.md`
- Plan (byte-identical copy of the approved plan): `<feature-slug>/plan.md`
- Tickets: `<feature-slug>/issues/<NN>-<slug>.md`, numbered from `01` in dependency order, one file per ticket. Each ticket's `Source:` line cites the repo copies `spec.md` / `plan.md`, never session-local storage.
- Triage label: a `Status:` line near the top of the file, per `triage-labels.md`.
- "Publish to the issue tracker" means creating these files, not opening a hosted issue.
- Comments, if any, append under `## Comments` at the end of the file.

## Closing a ticket

- Keep the triage label (`Status: ready-for-agent`) and add `State: closed` below it.
- Tick each acceptance checkbox only against evidence from that ticket's own acceptance list, never a sibling ticket's success.
- Append the resolution and its proof under `## Comments`: name the command that exercised this ticket, then give suite numbers (unit/e2e/typecheck/build) as integration evidence, stating they are not a claim that this ticket added every test. A ticket with no runnable surface gets `application NOT RUN` and the reason.
- Leave the parent `spec.md` unchanged.
