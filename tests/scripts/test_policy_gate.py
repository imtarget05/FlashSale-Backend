#!/usr/bin/env python3
"""Unit tests for scripts/policy-gate.py check functions (imported directly)."""

from __future__ import annotations

import importlib.util
import sys
from pathlib import Path

REPO_ROOT = Path(__file__).resolve().parents[2]

# policy-gate.py lives in the sibling AKS-SRE-Platform repo
AKS_REPO = REPO_ROOT.parent / "AKS-SRE-Platform"
spec = importlib.util.spec_from_file_location(
    "policy_gate", AKS_REPO / "scripts" / "policy-gate.py"
)
policy_gate = importlib.util.module_from_spec(spec)
spec.loader.exec_module(policy_gate)  # type: ignore[attr-defined]
check_images = policy_gate.check_images
check_pod_spec = policy_gate.check_pod_spec


def reset_failures():
    policy_gate.FAILURES.clear()


def failures_for(func, *args, **kwargs):
    reset_failures()
    func(*args, **kwargs)
    return list(policy_gate.FAILURES)


def make_doc(kind: str, name: str, containers: list[dict]) -> dict:
    return {
        "apiVersion": "apps/v1",
        "kind": kind,
        "metadata": {"name": name},
        "spec": {"template": {"spec": {"containers": containers}}},
    }


def container(image: str, probes: bool = True, resources: bool = True, sc: dict | None = None) -> dict:
    c = {"name": "app", "image": image}
    if probes:
        c["livenessProbe"] = {"httpGet": {"path": "/healthz", "port": 8080}}
        c["readinessProbe"] = {"httpGet": {"path": "/healthz", "port": 8080}}
        c["startupProbe"] = {"httpGet": {"path": "/healthz", "port": 8080}}
    if resources:
        c["resources"] = {"requests": {"cpu": "50m", "memory": "64Mi"}, "limits": {"cpu": "500m", "memory": "256Mi"}}
    if sc is not None:
        c["securityContext"] = sc
    return c


def test_image_placeholder_digest_fails():
    docs = [make_doc("Deployment", "app", [container("acr.azurecr.io/app@sha256:" + "0" * 64)])]
    failures = failures_for(check_images, docs, "test.yaml", require_digest=False)
    assert any(f["rule"] == "image-digest-placeholder" for f in failures), failures


def test_image_real_digest_passes():
    docs = [make_doc("Deployment", "app", [container("acr.azurecr.io/app@sha256:da0223892f59394f2c512bdae5c3f520514950625b22c3d393f2081960d3f1d3")])]
    failures = failures_for(check_images, docs, "test.yaml", require_digest=False)
    assert failures == [], failures


def test_image_latest_tag_fails():
    docs = [make_doc("Deployment", "app", [container("acr.azurecr.io/app:latest")])]
    failures = failures_for(check_images, docs, "test.yaml", require_digest=False)
    assert any(f["rule"] == "image-tag" for f in failures), failures


def test_image_version_tag_passes_without_require_digest():
    docs = [make_doc("Deployment", "app", [container("acr.azurecr.io/app:v1.2.3")])]
    failures = failures_for(check_images, docs, "test.yaml", require_digest=False)
    assert failures == [], failures


def test_image_version_tag_fails_with_require_digest():
    docs = [make_doc("Deployment", "app", [container("acr.azurecr.io/app:v1.2.3")])]
    failures = failures_for(check_images, docs, "test.yaml", require_digest=True)
    assert any(f["rule"] == "image-digest" for f in failures), failures


def test_missing_probes_fails():
    docs = [make_doc("Deployment", "app", [container("acr.azurecr.io/app:v1.0.0", probes=False)])]
    failures = failures_for(check_pod_spec, docs, "test.yaml")
    assert any(f["rule"] == "probes" for f in failures), failures


def test_non_root_fails():
    docs = [make_doc("Deployment", "app", [container("acr.azurecr.io/app:v1.0.0", sc={"runAsNonRoot": False})])]
    failures = failures_for(check_pod_spec, docs, "test.yaml")
    assert any(f["rule"] == "securityContext" for f in failures), failures


def test_missing_resources_fails():
    docs = [make_doc("Deployment", "app", [container("acr.azurecr.io/app:v1.0.0", resources=False)])]
    failures = failures_for(check_pod_spec, docs, "test.yaml")
    assert any(f["rule"] == "resources" for f in failures), failures


def test_allow_privilege_escalation_fails():
    docs = [make_doc("Deployment", "app", [container("acr.azurecr.io/app:v1.0.0", sc={"allowPrivilegeEscalation": True})])]
    failures = failures_for(check_pod_spec, docs, "test.yaml")
    assert any("allowPrivilegeEscalation" in f["detail"] for f in failures), failures
