---
name: machineidbar-dev-swarm
description: |
  3-role specialization pipeline (dev-agent + reviewer + tester) for developing the MachineIdBar Windows taskbar overlay, ensuring every code change is reviewed and built before reporting done.
  Use when developing, fixing bugs, or adding features to the MachineIdBar project and you want the change reviewed and tested through a pipeline.
  Do NOT use for general Windows desktop development or unrelated Python projects.
version: "0.1"
kind: swarm-skill
roles:
  - id: dev-agent
    kind: ai_agent
    purpose: Implements features and fixes in machine_id_bar.py and setup.py, following existing Python/Tkinter/ctypes conventions
    skills: []
    tools: [python]
  - id: reviewer
    kind: ai_agent
    purpose: Reviews code changes for correctness, Windows API safety, and RDP edge cases before the build step
    skills: []
    tools: []
  - id: tester
    kind: ai_agent
    purpose: Builds the exe with PyInstaller and verifies it launches, copies to clipboard, and handles resolution changes
    skills: []
    tools: [python, pyinstaller]
---

# MachineIdBar Dev Swarm

A specialization pipeline (Pattern C) for developing the MachineIdBar Windows taskbar overlay. Each code change flows through implement → review → build & test, with loop-back on failures. This solves the single-agent failure mode where code is written and committed without independent review or build verification.

## Workflow

0. **Pre-flight: check dependencies** — read [dependencies.yaml](dependencies.yaml) and verify Python and PyInstaller availability.
   Report missing items: `required: true` = likely fails without it; `required: false` = degraded but functional. **User decides** whether to proceed.
   The team can run on inline-persona-only mode if all skills are missing.

1. **Task analysis** — Leader classifies the user request (feature / bugfix / refactor / question). Questions are answered directly; code changes enter the pipeline.

2. **Implementation** — dev-agent reads the source, implements the change, outputs a diff. See [workflow.md](workflow.md) for the full pipeline and [bind.md](bind.md) § Resource Constraints for limits.

3. **Code review** — reviewer examines the diff for correctness, Windows API safety, and RDP edge cases. Verdict: APPROVE / APPROVE-WITH-WARNINGS / REQUEST-CHANGES. On REQUEST-CHANGES, loop back to step 2 (max 2 rounds).

4. **Build & test** — tester runs PyInstaller, launches the exe, verifies clipboard copy and resolution handling. On FAIL, loop back to step 2 (max 2 rounds).

5. **Final: emit Development Report** — Leader integrates all outputs into a single report with changes, review findings, test results, and warnings. See [workflow.md](workflow.md) for the report format.

## Roles

| id | Purpose | When dispatched | Input | Key dependencies | Role file |
|---|---|---|---|---|---|
| dev-agent | Implements features and fixes | Every code-change task | Task description + source files | python | [roles/dev-agent.md](roles/dev-agent.md) |
| reviewer | Reviews code changes | After dev-agent produces a diff | Diff + full source files | none | [roles/reviewer.md](roles/reviewer.md) |
| tester | Builds and tests the exe | After reviewer approves | Reviewed source + project dir | python, pyinstaller | [roles/tester.md](roles/tester.md) |

> Before dispatching each teammate, read the corresponding role file and extract the
> `## Inline Persona for Teammate` section — paste it directly into the dispatch prompt.
> Most adopting agents do NOT auto-load role files for teammates.

## Files

| File | What it contains | When to read |
|---|---|---|
| [workflow.md](workflow.md) | Mermaid diagram, step-by-step pipeline, loop-back rules, Final Report format | Before first dispatch — the complete playbook |
| [bind.md](bind.md) | Resource limits, behavioral constraints, failure handling and degraded modes | When hitting limits, handling failures, or needing degraded-mode rules |
| [roles/\*.md](roles/) | Per-role identity, success criteria, output schema, Inline Persona for Teammate | Before dispatching each teammate — extract Inline Persona |
| [dependencies.yaml](dependencies.yaml) | External skills and tools required to run | **Startup** — verify deps, report missing items, user decides go/no-go |
