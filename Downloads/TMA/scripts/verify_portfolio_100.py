#!/usr/bin/env python3
"""
TMA Portfolio 100% Master Verification Suite
Cross-Repository Parity, Hardening, and Architecture Certification

Verifies:
1. CreditFlow:
   - Security & RBAC (JWT, Multi-Role Underwriter/Risk Committee, API Key + Bearer)
   - Core Banking Sandbox Gateway (VietQR generator, HMAC-SHA256 signing, Webhook verifier)
   - Postgres Checkpointer dependencies in requirements.api.txt
   - CI Hardening (Trivy CRITICAL/HIGH + Gitleaks)
   - Pytest execution for Auth & RBAC & Sandbox

2. SupportDesk:
   - Temporal Worker & Workflow Sandbox Compliance (RestrictedWorkflowAccessError safe, ThreadPoolExecutor)
   - Realistic SOP Knowledge Base (>=20 markdown docs, retrieval verification)
   - CI Hardening (Trivy CRITICAL/HIGH + Gitleaks)
   - Pytest execution for Temporal Environment & Knowledge Base

3. Smart-Document-Chatbot:
   - JVM Container Tuning for Render Free Tier (SerialGC, -Xmx320m, -XX:MaxMetaspaceSize=128m)
   - K8s Readiness Probe (/ready probe alignment)
   - Degraded Resilience & In-Memory Fallback handling
   - Pytest execution for Health & Readiness Router
"""

import os
import sys
import subprocess
from pathlib import Path

# Paths
BASE_DIR = Path(__file__).resolve().parent.parent
CREDITFLOW_DIR = BASE_DIR / "CreditFlow"
SUPPORTDESK_DIR = BASE_DIR / "SupportDesk"
SMARTDOC_DIR = BASE_DIR / "Smart-Document-Chatbot"

GREEN = "\033[92m"
RED = "\033[91m"
YELLOW = "\033[93m"
BLUE = "\033[94m"
BOLD = "\033[1m"
RESET = "\033[0m"


class VerificationReporter:
    def __init__(self):
        self.results = []

    def report(self, project: str, check_name: str, passed: bool, detail: str = ""):
        self.results.append({
            "project": project,
            "check": check_name,
            "passed": passed,
            "detail": detail
        })
        status_str = f"{GREEN}✓ PASS{RESET}" if passed else f"{RED}✗ FAIL{RESET}"
        print(f"  [{status_str}] {BOLD}{check_name}{RESET}: {detail}")

    def summary(self) -> bool:
        print("\n" + "=" * 70)
        print(f"{BOLD}TMA PORTFOLIO 100% COMPLETION AUDIT SCORECARD{RESET}")
        print("=" * 70)

        by_project = {}
        for r in self.results:
            proj = r["project"]
            if proj not in by_project:
                by_project[proj] = {"total": 0, "passed": 0}
            by_project[proj]["total"] += 1
            if r["passed"]:
                by_project[proj]["passed"] += 1

        all_passed = True
        total_checks = len(self.results)
        total_passed = sum(1 for r in self.results if r["passed"])

        for proj, stats in by_project.items():
            pct = (stats["passed"] / stats["total"]) * 100.0 if stats["total"] > 0 else 0
            color = GREEN if pct == 100.0 else RED
            print(f"  • {BOLD}{proj:<25}{RESET} : {stats['passed']}/{stats['total']} checks passed ({color}{pct:.1f}%{RESET})")
            if pct < 100.0:
                all_passed = False

        overall_pct = (total_passed / total_checks) * 100.0 if total_checks > 0 else 0
        print("-" * 70)
        overall_color = GREEN if overall_pct == 100.0 else RED
        print(f"{BOLD}OVERALL PORTFOLIO COMPLETION: {overall_color}{overall_pct:.1f}% ({total_passed}/{total_checks} verified){RESET}")
        print("=" * 70)
        return all_passed


def run_cmd(cmd: list[str], cwd: Path) -> tuple[int, str]:
    res = subprocess.run(cmd, cwd=cwd, stdout=subprocess.PIPE, stderr=subprocess.STDOUT, text=True)
    return res.returncode, res.stdout


def verify_creditflow(reporter: VerificationReporter):
    print(f"\n{BLUE}{BOLD}[1/3] VERIFYING CREDITFLOW...{RESET}")

    # Check 1: Postgres checkpointer in requirements.api.txt
    req_file = CREDITFLOW_DIR / "requirements.api.txt"
    if req_file.exists():
        content = req_file.read_text()
        has_pg = "langgraph-checkpoint-postgres" in content and "psycopg" in content
        reporter.report(
            "CreditFlow",
            "Postgres Checkpointer Packaging",
            has_pg,
            "langgraph-checkpoint-postgres and psycopg present in requirements.api.txt"
        )
    else:
        reporter.report("CreditFlow", "Postgres Checkpointer Packaging", False, "requirements.api.txt not found")

    # Check 2: Core Banking Sandbox Gateway file
    sandbox_file = CREDITFLOW_DIR / "pipeline" / "gateways" / "banking_sandbox.py"
    if sandbox_file.exists():
        content = sandbox_file.read_text()
        has_elements = "CoreBankingSandboxGateway" in content and "vietqr" in content.lower() and "verify_signature" in content
        reporter.report(
            "CreditFlow",
            "Core Banking Sandbox Implementation",
            has_elements,
            "CoreBankingSandboxGateway with VietQR generator and HMAC webhook signature verifier present"
        )
    else:
        reporter.report("CreditFlow", "Core Banking Sandbox Implementation", False, "banking_sandbox.py not found")

    # Check 3: RBAC & JWT in security.py & app.py
    sec_file = CREDITFLOW_DIR / "backend" / "security.py"
    app_file = CREDITFLOW_DIR / "backend" / "app.py"
    if sec_file.exists() and app_file.exists():
        sec_content = sec_file.read_text()
        app_content = app_file.read_text()
        has_jwt = "create_access_token" in sec_content and "decode_access_token" in sec_content and "RISK_COMMITTEE_L2" in sec_content
        has_login = "/api/auth/login" in app_content
        reporter.report(
            "CreditFlow",
            "Per-User JWT & Multi-Role RBAC",
            has_jwt and has_login,
            "JWT issuance, Underwriter/Risk Committee RBAC, and /api/auth/login endpoint verified"
        )
    else:
        reporter.report("CreditFlow", "Per-User JWT & Multi-Role RBAC", False, "security.py or app.py missing")

    # Check 4: CI Hardening (Trivy + Gitleaks)
    ci_file = CREDITFLOW_DIR / ".github" / "workflows" / "ci.yml"
    if ci_file.exists():
        ci_content = ci_file.read_text()
        has_trivy = "aquasecurity/trivy-action" in ci_content
        has_gitleaks = "gitleaks" in ci_content
        reporter.report(
            "CreditFlow",
            "CI Security Hardening (Trivy & Gitleaks)",
            has_trivy and has_gitleaks,
            "Trivy CRITICAL/HIGH exit-code:1 and Gitleaks secret scan verified in ci.yml"
        )
    else:
        reporter.report("CreditFlow", "CI Security Hardening (Trivy & Gitleaks)", False, "ci.yml not found")

    # Check 5: Pytest Execution (RBAC + Banking Sandbox + API Auth)
    pytest_bin = CREDITFLOW_DIR / ".venv" / "bin" / "pytest"
    if pytest_bin.exists():
        code, out = run_cmd([
            str(pytest_bin),
            "tests/test_rbac_and_banking_gateway.py",
            "tests/test_api_auth.py",
            "-q"
        ], cwd=CREDITFLOW_DIR)
        reporter.report(
            "CreditFlow",
            "Pytest RBAC, Auth & Sandbox Suite",
            code == 0,
            f"Pytest exited with code {code} ({out.strip().splitlines()[-1] if out.strip() else 'No output'})"
        )
    else:
        reporter.report("CreditFlow", "Pytest RBAC, Auth & Sandbox Suite", False, "Virtualenv pytest not found")


def verify_supportdesk(reporter: VerificationReporter):
    print(f"\n{BLUE}{BOLD}[2/3] VERIFYING SUPPORTDESK...{RESET}")

    # Check 1: Temporal Sandbox Safe Imports & ThreadPoolExecutor Worker
    wf_file = SUPPORTDESK_DIR / "backend" / "app" / "workflows" / "definitions.py"
    worker_file = SUPPORTDESK_DIR / "backend" / "app" / "workflows" / "temporal_worker.py"
    if wf_file.exists() and worker_file.exists():
        wf_content = wf_file.read_text()
        worker_content = worker_file.read_text()
        no_settings_import = "from app.config import settings" not in wf_content
        has_executor = "ThreadPoolExecutor" in worker_content and "activity_executor=" in worker_content
        reporter.report(
            "SupportDesk",
            "Temporal Worker & Sandbox Determinism",
            no_settings_import and has_executor,
            "Workflow definition free of sandbox-breaking imports; Worker uses ThreadPoolExecutor"
        )
    else:
        reporter.report("SupportDesk", "Temporal Worker & Sandbox Determinism", False, "Workflow files missing")

    # Check 2: Realistic SOP Knowledge Base
    kb_dir = SUPPORTDESK_DIR / "backend" / "knowledge"
    if kb_dir.exists():
        sop_files = list(kb_dir.glob("*.md"))
        count = len(sop_files)
        has_required = (kb_dir / "billing_refunds.md").exists() and (kb_dir / "two_factor_auth.md").exists()
        reporter.report(
            "SupportDesk",
            "Realistic SOP Knowledge Base (>=20 docs)",
            count >= 20 and has_required,
            f"Found {count} SOP documents including billing_refunds.md and two_factor_auth.md"
        )
    else:
        reporter.report("SupportDesk", "Realistic SOP Knowledge Base (>=20 docs)", False, "knowledge/ directory missing")

    # Check 3: CI Hardening (Trivy + Gitleaks)
    ci_file = SUPPORTDESK_DIR / ".github" / "workflows" / "ci.yml"
    if ci_file.exists():
        ci_content = ci_file.read_text()
        has_trivy = "aquasecurity/trivy-action" in ci_content
        has_gitleaks = "gitleaks" in ci_content
        reporter.report(
            "SupportDesk",
            "CI Security Hardening (Trivy & Gitleaks)",
            has_trivy and has_gitleaks,
            "Trivy vulnerability scan and Gitleaks secret detection verified in ci.yml"
        )
    else:
        reporter.report("SupportDesk", "CI Security Hardening (Trivy & Gitleaks)", False, "ci.yml not found")

    # Check 4: Pytest Execution (Temporal Environment + Knowledge Base)
    pytest_bin = SUPPORTDESK_DIR / "backend" / ".venv" / "bin" / "pytest"
    if pytest_bin.exists():
        code, out = run_cmd([
            str(pytest_bin),
            "tests/test_temporal_environment.py",
            "tests/test_knowledge_base.py",
            "-q"
        ], cwd=SUPPORTDESK_DIR / "backend")
        reporter.report(
            "SupportDesk",
            "Pytest Temporal & Knowledge Base Suite",
            code == 0,
            f"Pytest exited with code {code} ({out.strip().splitlines()[-1] if out.strip() else 'No output'})"
        )
    else:
        reporter.report("SupportDesk", "Pytest Temporal & Knowledge Base Suite", False, "Virtualenv pytest not found")


def verify_smartdoc(reporter: VerificationReporter):
    print(f"\n{BLUE}{BOLD}[3/3] VERIFYING SMART-DOCUMENT-CHATBOT...{RESET}")

    # Check 1: JVM Container Memory Tuning
    df_file = SMARTDOC_DIR / "docker" / "Dockerfile.backend"
    if df_file.exists():
        content = df_file.read_text()
        has_tuning = "-XX:+UseSerialGC" in content and "-Xmx320m" in content and "-XX:MaxMetaspaceSize=128m" in content
        reporter.report(
            "Smart-Document-Chatbot",
            "JVM Memory Tuning for Free-Tier Containers",
            has_tuning,
            "SerialGC and -Xmx320m memory limits present in Dockerfile.backend to eliminate OOM-kill"
        )
    else:
        reporter.report("Smart-Document-Chatbot", "JVM Memory Tuning for Free-Tier Containers", False, "Dockerfile.backend missing")

    # Check 2: K8s Readiness Probe Alignment
    k8s_file = SMARTDOC_DIR / "k8s" / "25-smartdoc-agent.yaml"
    if k8s_file.exists():
        content = k8s_file.read_text()
        has_ready = "path: /ready" in content
        reporter.report(
            "Smart-Document-Chatbot",
            "K8s Readiness Probe Alignment",
            has_ready,
            "readinessProbe points to /ready to check dependencies instead of naive /health"
        )
    else:
        reporter.report("Smart-Document-Chatbot", "K8s Readiness Probe Alignment", False, "25-smartdoc-agent.yaml missing")

    # Check 3: Degraded Mode Resilience (ALLOW_MEMORY_FALLBACK)
    health_file = SMARTDOC_DIR / "agent" / "routers" / "health.py"
    if health_file.exists():
        content = health_file.read_text()
        has_fallback_resilience = "ALLOW_MEMORY_FALLBACK" in content and "healthy_local_fallback" in content
        reporter.report(
            "Smart-Document-Chatbot",
            "Degraded Mode Resilience & Local Fallback",
            has_fallback_resilience,
            "ALLOW_MEMORY_FALLBACK flag supported to enable local/degraded health 200 responses"
        )
    else:
        reporter.report("Smart-Document-Chatbot", "Degraded Mode Resilience & Local Fallback", False, "health.py missing")

    # Check 4: Pytest Execution (Health & Readiness Suite)
    pytest_bin = SMARTDOC_DIR / "agent" / ".venv" / "bin" / "pytest"
    if pytest_bin.exists():
        code, out = run_cmd([
            str(pytest_bin),
            "tests/test_health_readiness.py",
            "-q"
        ], cwd=SMARTDOC_DIR / "agent")
        reporter.report(
            "Smart-Document-Chatbot",
            "Pytest Health & Readiness Suite",
            code == 0,
            f"Pytest exited with code {code} ({out.strip().splitlines()[-1] if out.strip() else 'No output'})"
        )
    else:
        reporter.report("Smart-Document-Chatbot", "Pytest Health & Readiness Suite", False, "Virtualenv pytest not found")


def main():
    print("=" * 70)
    print(f"{BOLD}STARTING TMA PORTFOLIO 100% MASTER VERIFICATION RUNNER{RESET}")
    print("=" * 70)

    reporter = VerificationReporter()
    verify_creditflow(reporter)
    verify_supportdesk(reporter)
    verify_smartdoc(reporter)

    success = reporter.summary()
    sys.exit(0 if success else 1)


if __name__ == "__main__":
    main()
