#!/usr/bin/env python3
"""Pin the prod Kustomize overlay images to one immutable commit SHA.

Used by the CI `gitops-update` job (ADR-011: Git is the deployment contract).

Why a file instead of an inline heredoc: the previous inline version rewrote
only `order-api` and matched `- name: <registry>/order-api`. Once base became
registry-neutral (`name: order-api`), that pattern stopped existing, and the
job would have silently pushed an overlay with NO registry rewrite — the exact
"green CI, broken desired state" failure this script is meant to prevent.
Every failure mode here is a non-zero exit, never a silent no-op.
"""
import re
import sys

# Every workload image we own. Third-party images (postgres/redis/rabbitmq) are
# intentionally NOT rewritten: they are upstream-pinned by tag in base.
WANTED = ("order-api", "order-worker")


def pin(path: str, acr: str, sha: str) -> int:
    if not acr or not sha:
        raise SystemExit("refusing to write an empty registry or tag")
    if "/" in acr or ":" in acr:
        raise SystemExit(f"ACR_LOGIN_SERVER must be a bare host, got {acr!r}")
    if not re.fullmatch(r"[0-9a-f]{40}", sha):
        raise SystemExit(f"{sha!r} is not a full 40-char commit SHA")

    out, in_images, current = [], False, None
    for line in open(path).read().splitlines(keepends=True):
        # A column-0 key ends the `images:` block; indentation is the scope.
        if re.match(r"^\S", line):
            in_images = line.startswith("images:")
            current = None
            out.append(line)
            continue

        if not in_images:
            out.append(line)
            continue

        m = re.match(r"^(\s*)-\s+name:\s+(\S+)\s*$", line)
        if m:
            current = m.group(2) if m.group(2) in WANTED else None
            out.append(line)
            continue

        if current:
            # lambda, not r'\1' + value: a SHA starting with a digit would be
            # read as a higher group number (\1 + "1de..." -> group 11).
            line = re.sub(
                r"^(\s*)newName:.*$",
                lambda mm, c=current: mm.group(1) + f"newName: {acr}/{c}",
                line,
            )
            line = re.sub(r"^(\s*)newTag:.*$",
                          lambda mm: mm.group(1) + "newTag: " + sha, line)
        out.append(line)

    text = "".join(out)
    for c in WANTED:
        if f"newName: {acr}/{c}" not in text:
            raise SystemExit(f"overlay has no `- name: {c}` entry to pin")
    if text.count(f"newTag: {sha}") != len(WANTED):
        raise SystemExit(f"expected {len(WANTED)} pins to {sha}, found "
                         f"{text.count(f'newTag: {sha}')}")

    open(path, "w").write(text)
    print(f"pinned {len(WANTED)} images to {acr}/{sha[:8]}")
    return 0


if __name__ == "__main__":
    if len(sys.argv) != 4:
        raise SystemExit("usage: gitops-pin-overlay.py <kustomization.yaml> <acr-host> <sha>")
    sys.exit(pin(*sys.argv[1:4]))
