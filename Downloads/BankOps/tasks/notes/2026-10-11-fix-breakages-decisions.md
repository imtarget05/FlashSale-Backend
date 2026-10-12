# BankOps Fix-Breakages Tradeoffs & Decisions

Date: 2026-10-11
Source: parallel read-only scan (3 slices) + QA_REPORT.md Vong 7 + writing-plans skill

## Quyet dinh

1. **P0 truoc, P1 sau**: Docker/compose/DB config (Task 1-3) phai xanh truoc vi moi test live phu thuoc. Khong sua API khi chua chay duoc `alembic upgrade head`.
2. **Write-path API la nut that co chai**: 15 contract + 19 acceptance test 404 vi thieu POST submit/approve. Khong "mock cho xanh" — them endpoint that kem idempotency/If-Match theo docs/06.
3. **Khong sua QA_REPORT.md**: bao cao la snapshot lich su (Vong 7). Ghi nhan doan STALE (CI 3/3 do, poetry.lock, initdb) trong plan, khong viet lai lich su.
4. **Frontend Fake-only la co y giu lai**: Task 10 tach lop HTTP repo moi, khong xoa Fake (giup QA chay offline). AppShell nav fix rieng de khong vo IA.
5. **Worker giu sleep-loop o Task 9**: thay bang lease/outbox toi thieu, khong lam full job-queue (YAGNI).
6. **No placeholders**: moi task co test code + lenh verify cu the. Cam `TBD/TODO`.

## Tradeoff

- Refresh/revoke/rate-limit (Task 6) lam toi thieu (JWT rotation + blacklist memory/DB + slowapi) thay vi Redis — tranh them ha tang.
- Error envelope + request-id (Task 7) ap dung o middleware, khong sua tung route — it diff nhat.
- Seed path fix (Task 9) dung bien moi `SAMPLE_DATA_DIR` env override thay vi doi parents[] — tuong thich ca host + container.
- iOS BUNDLE_ID mismatch: chi sua Info/plist + doc, khong chay flutter build (can thiet bi).

## Nguon skill

- Skill goc doc tu `/Users/mainguyenbinhtan/.config/opencode/skills/writing-plans/SKILL.md` (symlink `vibe-coding/superpowers` hong, tim thay ban that o skills/writing-plans).
- Repo khong co `repo-harness-plan/SKILL.md` (glob 0 hit) → bo qua theo /vibe-plan muc 1.
