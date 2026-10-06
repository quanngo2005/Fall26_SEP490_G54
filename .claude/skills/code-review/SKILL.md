---
name: code-review
description: Use when reviewing G54 backend or frontend code, running codereview scripts, validating quality gates, or producing BACKEND_REPORT.md / FRONTEND_REPORT.md.
---

# Code Review

## Files

- Standards: `docs/codereview/CODEREVIEW_BACKEND.md`, `docs/codereview/CODEREVIEW_FRONTEND.md`
- Scripts: `scripts/codereview-backend.ps1`, `scripts/codereview-frontend.ps1` (`.sh` wrappers for Unix)
- Generated reports: `docs/codereview/reports/BACKEND_REPORT.md`, `docs/codereview/reports/FRONTEND_REPORT.md`

## Workflow

1. Read the matching standard file.
2. Review changed code for correctness first, then security (load the `security-review` skill), then architecture and style.
3. Run the script:
   - `./scripts/codereview-backend.ps1`
   - `./scripts/codereview-frontend.ps1`
4. Read the generated report. It groups results into Quality Gates, Security - Critical, Security - High, with a summary table.
5. Fix every failed item, then rerun until the report ends with `## ALL CHECKS PASSED`.
6. Walk the `[manual]` items and report findings with `file:line`, severity, risk, and fix.
7. For frontend changes, review the rendered UI against `frontend/docs/DESIGN.md` at desktop, tablet, and mobile widths; check keyboard/focus states and record any intentional design deviations.

## Pass Criteria

- All automated gates pass (build, tests, lint, format).
- Zero Critical and zero High findings (automated and manual).
- Medium findings are listed with a follow-up issue.

## Rules

- Never edit generated reports by hand.
- Never weaken a check or regex in the scripts to make it pass; fix the code.
- If a check is a false positive, explain it and propose a precise script change for human approval.
