#!/usr/bin/env python3
"""Hạ tầng evidence E2E dùng chung cho cả 7 repo.

Mọi scenario E2E đều ghi ra bằng `EvidenceRun`, để metadata, SHA-256 và
ranh giới REAL/SIMULATED/MOCKED/NOT_RUN có đúng một hình dạng. Bảy script
E2E viết tay bảy kiểu file khác nhau thì không ai đọc nổi, và tốn nhiều
thời gian hơn là viết lại từ đầu.

Hai quy tắc mà module này cố tình làm khó vi phạm:

1. Không có `status="VERIFIED"` nếu thiếu artifact. `finalise()` ném lỗi nếu
   VERIFIED mà `artifacts` rỗng. Bằng chứng không có file đính kèm thì chỉ
   là một lời tuyên bố.

2. Mọi artifact đều được băm SHA-256. Không chỉ để chống sửa, mà để
   reviewer kiểm chứng lại bằng đúng một lệnh, và để phát hiện ngay khi
   evidence cũ không còn khớp với commit hiện tại (trạng thái STALE).
"""
from __future__ import annotations

import hashlib
import json
import platform
import subprocess
import sys
import time
from pathlib import Path

EVIDENCE_VERSION = "3.0"
ROOT = Path(__file__).resolve().parent.parent.parent
E2E_ROOT = ROOT / "docs" / "evidence" / "e2e"

# Trạng thái hợp lệ. Không thêm trạng thái mới: ma trận cuối cùng và mọi
# báo cáo đều dựa trên đúng năm giá trị này.
STATUSES = {"VERIFIED", "PARTIAL", "FAILED", "NOT_RUN", "STALE"}
BOUNDARIES = {"REAL", "SIMULATED", "MOCKED", "NOT_RUN"}
DIRECTORIES = (
    "creditflow", "factory", "apexinspect", "flashsale", "maia", "minierp",
    "helpdesk", "manufacturing-cross-repo", "ai-cross-repo", "azure",
    "failure-recovery", "security", "performance", "final",
)


def sha256_file(path: Path) -> str:
    digest = hashlib.sha256()
    with Path(path).open("rb") as handle:
        for chunk in iter(lambda: handle.read(1 << 16), b""):
            digest.update(chunk)
    return digest.hexdigest()


def rel(path: Path) -> str:
    path = Path(path)
    return str(path.relative_to(ROOT)) if path.is_relative_to(ROOT) \
        else str(path)


def git_head(repo: Path) -> dict:
    """Lấy commit và trạng thái worktree — bằng chứng chỉ đáng tin khi sạch."""

    def run(*args):
        try:
            return subprocess.run(["git", "-C", str(repo), *args],
                                  capture_output=True, text=True,
                                  timeout=20).stdout.strip()
        except (OSError, subprocess.SubprocessError):
            return ""

    dirty = run("status", "--porcelain")
    return {
        "path": rel(Path(repo)),
        "commit": run("rev-parse", "HEAD"),
        "worktree": "DIRTY_WORKTREE_SNAPSHOT" if dirty else "clean",
        "dirty_files": dirty.splitlines()[:10],
    }


def ensure_structure():
    for name in DIRECTORIES:
        (E2E_ROOT / name).mkdir(parents=True, exist_ok=True)
    return E2E_ROOT


def refresh_hashes():
    """Băm lại toàn bộ cây evidence E2E."""
    ensure_structure()
    files = sorted(p for p in E2E_ROOT.rglob("*")
                   if p.is_file() and p.name != "hashes.sha256")
    lines = [f"{sha256_file(p)}  {p.relative_to(E2E_ROOT)}" for p in files]
    out = E2E_ROOT / "hashes.sha256"
    out.write_text("\n".join(lines) + "\n", encoding="utf-8")
    return out, len(files)



class EvidenceRun:
    """Một lần chạy E2E. Dùng như context manager."""

    def __init__(self, scenario_id: str, repos, execution_location: str,
                 command: str, boundaries: dict | None = None,
                 notes: str = ""):
        if execution_location not in {"AZURE", "LOCAL", "CI"}:
            raise ValueError("execution_location must be AZURE|LOCAL|CI, "
                             f"got {execution_location!r}")
        self.scenario_id = scenario_id
        self.repos = [Path(r) for r in repos]
        self.execution_location = execution_location
        self.command = command
        self.boundaries = boundaries or {}
        self.notes = notes
        self.started = time.time()
        self.started_at = time.strftime("%Y-%m-%dT%H:%M:%SZ",
                                       time.gmtime(self.started))
        self.checks: list[dict] = []
        self.artifacts: list[Path] = []
        self.status = "NOT_RUN"

    def __enter__(self):
        print(f"\n=== {self.scenario_id} ({self.execution_location}) ===")
        return self

    def __exit__(self, exc_type, exc, tb):
        if exc_type is not None:
            # Scenario nổ ra giữa chừng vẫn phải để lại dấu vết. Ghi một
            # scenario FAILED kèm artifact rỗng tốt hơn là im lặng: reviewer
            # cần biết nó đã thử và hỏng ở đâu, không phải rằng nó chưa
            # từng chạy.
            self.status = "FAILED"
            self.checks.append({
                "invariant": "scenario completed without exception",
                "boundary": "REAL",
                "passed": False,
                "expected": "no exception",
                "actual": f"{exc_type.__name__}: {exc}",
                "detail": "raised inside the scenario body",
            })
            try:
                out = self.finalise(E2E_ROOT / "final", status="FAILED")
                print(f"  !! failure evidence: {rel(out)}")
            except Exception:  # noqa: BLE001 - không được che lỗi gốc
                pass
        return False

    def check(self, name: str, passed: bool, detail: str = "",
              boundary: str = "REAL", expected: str = "", actual: str = ""):
        """Ghi một invariant.

        `boundary` bắt buộc nói rõ chạy thật hay không. Một invariant chỉ
        chứng minh được điều gì đó khi ranh giới của nó thật, nên nếu còn
        boundary nào là SIMULATED/MOCKED/NOT_RUN thì kết quả cuối bị hạ
        xuống PARTIAL dù mọi invariant đều xanh.
        """
        if boundary not in BOUNDARIES:
            raise ValueError(
                f"boundary must be one of {sorted(BOUNDARIES)}, "
                f"got {boundary!r}")
        self.checks.append({
            "invariant": name,
            "boundary": boundary,
            "passed": bool(passed),
            "expected": expected,
            "actual": actual,
            "detail": detail,
        })
        mark = "ok  " if passed else "FAIL"
        tag = "" if boundary == "REAL" else f" [{boundary}]"
        print(f"  [{mark}] {name}{tag}" + (f" - {detail}" if detail else ""))
        return passed

    def add_artifact(self, path) -> Path:
        path = Path(path)
        if not path.exists():
            raise FileNotFoundError(f"artifact does not exist: {path}")
        self.artifacts.append(path)
        return path

    def finalise(self, out_dir, status: str | None = None) -> Path:
        if status:
            self.status = status
        else:
            failed = [c for c in self.checks if not c["passed"]]
            unverified = [c for c in self.checks
                          if c["boundary"] in {"SIMULATED", "MOCKED", "NOT_RUN"}]
            if failed:
                self.status = "FAILED"
            elif unverified:
                # Business flow chạy, nhưng một ranh giới chưa chạy thật.
                self.status = "PARTIAL"
            else:
                self.status = "VERIFIED"

        if self.status not in STATUSES:
            raise ValueError(f"invalid status {self.status!r}")
        if self.status == "VERIFIED" and not self.artifacts:
            raise ValueError(
                f"{self.scenario_id}: cannot be VERIFIED with no artifacts. An "
                "evidence claim without a file behind it is a claim, not "
                "evidence."
            )

        out_dir = Path(out_dir)
        out_dir.mkdir(parents=True, exist_ok=True)
        target = out_dir / f"{self.scenario_id.lower()}.json"

        report = {
            "evidence_version": EVIDENCE_VERSION,
            "scenario_id": self.scenario_id,
            "execution_location": self.execution_location,
            "command": self.command,
            "started_at": self.started_at,
            "duration_s": round(time.time() - self.started, 2),
            "status": self.status,
            "repos": [git_head(r) for r in self.repos],
            "boundaries": self.boundaries,
            "checks": self.checks,
            "checks_passed": sum(c["passed"] for c in self.checks),
            "checks_total": len(self.checks),
            "artifacts": [rel(a) for a in self.artifacts],
            "sha256": {rel(a): sha256_file(a) for a in self.artifacts},
            "environment": {
                "python": sys.version.split()[0],
                "platform": platform.platform(),
                "hostname": platform.node(),
            },
            "notes": self.notes,
        }
        target.write_text(json.dumps(report, indent=2), encoding="utf-8")
        print(f"  -> {rel(target)}  [{self.status}] "
              f"{report['checks_passed']}/{report['checks_total']}")
        return target
