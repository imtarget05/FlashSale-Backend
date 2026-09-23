# Plan: Cline helper - finish Antigravity dangling plan

> **Status**: Active
> **Created**: 20260923-2239
> **Slug**: cline-helper-antigravity-finish
> **Artifact Level**: work-package
> **Goal**: Làm phụ cho Cline — khép tracking gap của Antigravity/3-repo roadmap (Phase 3B/3C đã có evidence nhưng tasks/current.md chỉ tick 3A), cập nhật tasks/current.md với active goal + next steps, để Cline/Antigravity tiếp Phase 4 (P02 audit gate) không bị lệch tracker.
> **Verification Boundary**: docs/evidence/backup/restore-drill-002-offsite.md + restore-drill-003-integrated.md tồn tại; `tasks/current.md` tick 3B/3C; không đụng code runtime.
> **Rollback Surface**: revert 2 file (plan này + tasks/current.md) via git checkout.
> **Promotion Reason**: unblock Cline read-only → tasked handoff cho Phase 4.
> **Spec**: `docs/spec.md`
> **Research**: See `docs/researches/`
> **Task Contract**: `tasks/contracts/20260923-2239-cline-helper-antigravity-finish.contract.md`
> **Task Review**: `tasks/reviews/20260923-2239-cline-helper-antigravity-finish.review.md`
> **Implementation Notes**: `tasks/notes/20260923-2239-cline-helper-antigravity-finish.notes.md`

## Agentic Routing
- Selected route:
- Routing reason:
- Due diligence:
  - P1 map:
  - P2 trace:
  - P3 decision rationale:

## Workflow Inventory
Complete this inventory before implementation. If any line is unknown, keep the plan in Draft and fill it before projection.

- Active plan: `plans/plan-20260923-2239-cline-helper-antigravity-finish.md`
- Sprint contract: `tasks/contracts/20260923-2239-cline-helper-antigravity-finish.contract.md`
- Sprint review: `tasks/reviews/20260923-2239-cline-helper-antigravity-finish.review.md`
- Implementation notes: `tasks/notes/20260923-2239-cline-helper-antigravity-finish.notes.md`
- Deferred-goal ledger: `tasks/todos.md`
- Current checks: `.ai/harness/checks/latest.json`
- Run snapshots: `.ai/harness/runs/`
- Scope authority: `tasks/contracts/20260923-2239-cline-helper-antigravity-finish.contract.md` `allowed_paths`
- Concurrency rule: `.ai/harness/active-plan` selects the active plan for this worktree when present; `.ai/harness/active-worktree` records the owning worktree. If another worktree already owns active work, open or switch to the matching worktree instead of serializing unrelated plans.
- Execution isolation: approved contract-level work projects through `repo-harness run plan-to-todo --plan plans/plan-20260923-2239-cline-helper-antigravity-finish.md` and may start `repo-harness run contract-worktree start --plan plans/plan-20260923-2239-cline-helper-antigravity-finish.md`.

## Approach
### Strategy
### Trade-offs
| Option | Pros | Cons | Decision |
|--------|------|------|----------|

## Detailed Design
### File Changes
| File | Action | Description |
|------|--------|-------------|

### Code Snippets
### Data Flow

## Risk Assessment
| Risk | Likelihood | Impact | Mitigation |
|------|------------|--------|------------|

## Task Contracts
- Contract file: `tasks/contracts/20260923-2239-cline-helper-antigravity-finish.contract.md`
- Review file: `tasks/reviews/20260923-2239-cline-helper-antigravity-finish.review.md`
- Implementation notes file: `tasks/notes/20260923-2239-cline-helper-antigravity-finish.notes.md`
- Template: `.claude/templates/contract.template.md`
- Verification command: `repo-harness run verify-contract --contract tasks/contracts/20260923-2239-cline-helper-antigravity-finish.contract.md --strict`
- Active plan rule: `.ai/harness/active-plan` is authoritative for this worktree when present; `.ai/harness/active-worktree` records the owning worktree. Do not infer active execution from the latest non-archived plan.

## Handoff

- Checks file: `.ai/harness/checks/latest.json`
- Session handoff: `.ai/harness/handoff/current.md`

## Promotion Gate

- **Merge/PR unit**:
- **Rollback surface**:
- **Verification boundary**:
- **Review/acceptance boundary**:
- **High-risk surface**:
- **Why not checklist row**:

## Evidence Contract

- **State/progress path**:
- **Verification evidence**:
- **Evaluator rubric**:
- **Stop condition**:
- **Rollback surface**:

## Annotations
<!-- [NOTE]: prefixed inline. Claude processes all and revises. -->

## Task Breakdown
- [x] 1. Kiểm tra pending state: `tasks/current.md` (FOUND, 270 dòng, Phase 0-7 DONE) + `.ai/harness/handoff/resume.md` (MISSING — repo chưa opt-in harness, `repo-harness status` = opt-in no).
- [x] 2. Tạo focus plan via `repo-harness run new-plan --slug cline-helper-antigravity-finish` → `plans/plan-20260923-2239-cline-helper-antigravity-finish.md`.
- [x] 3. Cập nhật `tasks/current.md` với active goal + next steps (Cline-helper, Phase 4 P02 gate kế tiếp).
- [x] 4. Khép tracking gap 3B/3C trong `tasks/current.md` (evidence 002/003 đã xác minh tồn tại) — chỉ sửa tracker, không claim code mới.
- [x] 5. Verify: `git status --short`, `git diff --stat`, đọc lại 2 file; acceptance = plan Active + tasks/current.md có Active Goal + 3B/3C ticked với link evidence.

## Acceptance Criteria
- [x] Plan này ở trạng thái Active với goal/verif/rollback điền đầy đủ. (verified 2026-09-23: Status Active, Goal/Verification/Rollback/Promotion đầy đủ dòng 3-10)
- [x] `tasks/current.md` đầu file có `ACTIVE GOAL (2026-09-23)` + next steps trỏ Phase 4 P02 audit gate. (verified: grep line 1 hit)
- [x] Không sửa runtime code; chỉ docs/tracker; `git diff` chỉ chạm plans/ + tasks/. (verified: `git diff --stat` = tasks/current.md 18 insertions; plan file untracked new; evidence 001/002/003 tồn tại)
