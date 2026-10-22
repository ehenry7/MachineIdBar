# Execution Guardrails

## Resource Constraints

| Item | Limit | Reason |
|---|---|---|
| `max_parallel_teammates` | 1 | pipeline is sequential — only one teammate active at a time |
| `total_wall_clock_budget` | 15 min | upper bound for one full dev cycle (implement → review → build → test) |
| `total_token_budget` | 150k tokens | budget across all roles in one cycle |
| `dev_agent_token_budget` | 40k | dev-agent reads full source and produces diffs |
| `reviewer_token_budget` | 40k | reviewer reads full source + diff |
| `tester_token_budget` | 30k | tester runs builds and reports results |
| `max_review_rounds` | 2 | prevents infinite review-fix loops |
| `max_test_rounds` | 2 | prevents infinite build-fix loops |

## Behavioral Constraints

- **Leader-as-orchestrator only**: the Leader dispatches teammates and integrates outputs. The Leader does NOT write code, review code, or run builds.
- **Pipeline isolation**: each stage receives only the prior stage's output — dev-agent does not see reviewer's feedback until the Leader relays it, tester does not see reviewer's findings.
- **Loop control**: when reviewer returns REQUEST-CHANGES or tester returns FAIL, the Leader relays the feedback to dev-agent as new input. The Leader does NOT mediate or soften the feedback.
- **No live-system testing without user consent**: the tester may launch the exe on the local machine, but must NOT deploy to other machines or modify startup entries without explicit user approval.
- **Preserve existing functionality**: no teammate may remove or alter the topmost-forcing logic, borderless window style, or clipboard copy behavior unless that is the explicit task.

## Failure Handling

### (a) Teammate failure

| Failure mode | Response |
|---|---|
| Teammate timeout | Retry once. On 2nd timeout, mark role's section as `[ROLE MISSING — timed out]` and proceed with partial report. |
| Malformed output (schema mismatch) | Re-dispatch with schema inlined + "your previous output was malformed" preamble. Max 1 retry. On 2nd failure, mark `[ROLE MISSING — malformed output]`. |
| dev-agent refuses (claims task is impossible) | Re-dispatch with task broken into smaller steps. If still refuses, surface to user. |
| reviewer refuses (finds nothing) | Re-dispatch with Mandatory rule restated ("you MUST output at least 3 findings"). If still refuses, mark `[REVIEWER INCONCLUSIVE]`. |
| tester refuses (cannot run build) | Check if PyInstaller is installed. If missing, report to user. If installed but build fails, report failure to Leader for loop-back to dev-agent. |

### (b) Input over-scale degradation

| Trigger condition | Degraded mode |
|---|---|
| Source files > 1000 LOC total | Reduce reviewer to focused checks on changed lines only; skip full-file review. Warn user. |
| Multiple unrelated tasks in one request | Leader splits into sequential cycles, one task per cycle. Warn user that each task runs as a separate pipeline. |
| PyInstaller not installed | Skip build & test step. Report code changes + review only. Tag report `INCOMPLETE: build skipped — PyInstaller not found`. |
| Critical dependency missing per pre-flight | User already chose to proceed in Step 0 — record the missing dep in the Final Report. |

### Escalation rules

- If 2+ roles return `[ROLE MISSING]`, the run is **FAILED** — emit a partial report with explicit "FAILED: insufficient role coverage" header.
- If `total_wall_clock_budget` is exceeded, halt in-flight teammate, emit partial outputs, tag report `INCOMPLETE: budget exceeded`.
- If `max_review_rounds` or `max_test_rounds` is exceeded, halt the loop and surface the unresolved issue to the user with all prior attempts' outputs.
