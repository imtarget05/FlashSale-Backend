#!/usr/bin/env python3
"""Test-oracle quality gate.

Why this exists
---------------
Four false passes were found in this portfolio's own suites during the P0
remediation:

  1. `assert x or True`          -- a tautology that can never fail
  2. an empty test body          -- left by a bad line-number edit; passed vacuously
  3. a stub that asserted itself -- the "test" called the thing it was testing
  4. a missing function counted
     as a successful refusal    -- getattr(mod, name, None) returning None
                                   satisfied "must not answer", so a DELETED
                                   implementation scored as a safety PASS

Cases 2 and 4 are the dangerous ones. A suite full of them is worse than no
suite, because it converts an unknown into an apparent pass. This gate is the
check that the tests are worth running at all.

What it detects (AST-based, so it does not match "assert" inside a docstring)
-----------------------------------------------------------------------------
  ASSERT_FREE       no assert / pytest.raises / assertion helper in the body
  EMPTY_BODY        body is only pass, ellipsis, a docstring, or bare return
  ORA_TRUE          an unfalsifiable `or` operand inside an assertion
  ZERO_DENOMINATOR  a rate computed as x / 0, or a percentage of a literal 0
  SELF_ASSERTING    a test that calls a function and never references it
                    independently -- the shape a deleted implementation takes

What it deliberately does NOT do
--------------------------------
It does not try to prove a suite is good. It refuses only the specific shapes
that have already produced real false passes here. A clean report is a
necessary condition, not a sufficient one.

Usage
-----
    python3 tools/test_quality_gate.py <path> [<path> ...]
    python3 tools/test_quality_gate.py --json <path>

Exit codes: 0 = clean, 1 = findings, 2 = bad invocation.
"""
from __future__ import annotations

import argparse
import ast
import json
import os
import sys
from dataclasses import dataclass, field, asdict

# Never scanned: vendored code and build output.
SKIP_DIRS = {
    ".git", ".venv", "venv", "node_modules", "__pycache__", ".pytest_cache",
    "bin", "obj", "dist", "build", ".mypy_cache", ".ruff_cache", "site-packages",
    "_archive", "migrations",
}

# A test function must do at least one of these to count as having an oracle.
#
# The `assert*` entries cover the common shapes that a bare-call-name match
# misses. Verified necessary: without them the gate reported false positives on
# real, correct tests such as
#   self.assertGreaterEqual(len(RETRIEVAL_QUERIES), 12)      # unittest style
#   pd.testing.assert_frame_equal(a, b)                     # pandas helper
# Reporting a passing test as assert-free is worse than not running the gate,
# because it teaches everyone to ignore it.
ASSERTING_CALLS = {
    # unittest / TestCase
    "assert_", "assertEqual", "assertTrue", "assertFalse", "assertRaises",
    "assertAlmostEqual", "assertIn", "assertNotIn", "assertIs", "assertIsNot",
    "assertIsNone", "assertIsNotNone", "assertGreater", "assertGreaterEqual",
    "assertLess", "assertLessEqual", "assertRegex", "assertCountEqual",
    # pytest
    "fail", "raises", "warns", "approx", "approximately",
    # third-party assertion helpers
    "assert_frame_equal", "assert_series_equal", "assert_index_equal",
    "assert_allclose", "assert_frame_equal", "assertDictContainsSubset",
    # FluentAssertions / Shouldly
    "Should", "ShouldBe", "ShouldEqual", "ShouldContain", "ShouldNotBe",
}


@dataclass
class Finding:
    rule: str
    file: str
    line: int
    function: str
    detail: str


@dataclass
class ScanResult:
    files_scanned: int = 0
    test_functions: int = 0
    findings: list = field(default_factory=list)

    @property
    def clean(self) -> bool:
        return not self.findings


def _is_test_function(node) -> bool:
    return node.name.startswith("test_") or node.name.startswith("Test")


def _has_docstring_only(body: list) -> bool:
    """True when the body carries no executable statement beyond pass/ellipsis."""
    for stmt in body:
        if isinstance(stmt, ast.Pass):
            continue
        if isinstance(stmt, ast.Expr) and isinstance(stmt.value, ast.Constant):
            continue  # a bare string literal is a docstring, not a statement
        if isinstance(stmt, ast.Return) and stmt.value is None:
            continue
        return False
    return True


def _or_true(node: ast.AST) -> str | None:
    """Flag an unfalsifiable `or` operand inside an assertion."""
    for sub in ast.walk(node):
        if not (isinstance(sub, ast.BoolOp) and isinstance(sub.op, ast.Or)):
            continue
        for value in sub.values:
            if isinstance(value, ast.Constant) and value.value in (True, 1):
                return "or True / or 1 inside an assertion (unfalsifiable)"
            if isinstance(value, (ast.List, ast.Tuple)) and not value.elts:
                return "or [] / or () inside an assertion (unfalsifiable)"
            if isinstance(value, ast.Dict) and not value.keys:
                return "or {} inside an assertion (unfalsifiable)"
    return None


def _zero_denominator(node: ast.AST) -> str | None:
    """Flag a rate computed against a literal zero."""
    for sub in ast.walk(node):
        if isinstance(sub, ast.BinOp):
            if isinstance(sub.op, ast.Div) and isinstance(sub.right, ast.Constant) \
                    and sub.right.value == 0:
                return "division by a literal 0"
            if isinstance(sub.op, ast.Mult) and isinstance(sub.left, ast.Constant) \
                    and sub.left.value == 0:
                return "percentage computed from a literal 0 numerator"
    return None


def _self_asserting(fn) -> str | None:
    """Flag a test that asserts a call against an identical call.

    `assert f(x) == f(x)` cannot fail for any value of f, including a stub or a
    function that was deleted outright. That is the shape a fake pass takes when
    a test was written to exercise f but ended up comparing f to itself.

    The first version of this rule compared the *test's own name* against the
    calls inside it. That never fires for a real test -- the test is named
    `test_x` and calls `x` -- so it was dead code that reported nothing while
    appearing to be a safety rule. Matching the call arguments is what actually
    identifies the pattern, and the self-test below is what caught it.
    """
    for node in ast.walk(fn):
        if not isinstance(node, ast.Compare) or len(node.ops) != 1:
            continue
        if not isinstance(node.ops[0], (ast.Eq, ast.NotEq)):
            continue
        sides = [node.left, *node.comparators]
        if len(sides) != 2:
            continue
        left, right = sides
        if not (isinstance(left, ast.Call) and isinstance(right, ast.Call)):
            continue
        lf, rf = left.func, right.func
        # Compare the whole callee expression, not just the trailing name.
        # Matching on `.attr` alone reported `out["x"].sum()` and
        # `production["y"].sum()` as the same call, because both end in `.sum`.
        # That is a false positive on a real conservation test.
        lname = lf.id if isinstance(lf, ast.Name) else ast.dump(lf, include_attributes=False)
        rname = rf.id if isinstance(rf, ast.Name) else ast.dump(rf, include_attributes=False)
        if lname and lname == rname and _same_args(left.args, right.args):
            return f"asserts {lname}(...) against an identical {lname}(...) call"
    return None


def _same_args(a: list, b: list) -> bool:
    """True when two call argument lists are syntactically identical.

    Compares dumped AST nodes rather than evaluating anything: the two calls may
    have side effects, and a quality gate must never run the code it inspects.
    """
    if len(a) != len(b):
        return False
    return all(
        ast.dump(x, include_attributes=False) == ast.dump(y, include_attributes=False)
        for x, y in zip(a, b)
    )


def _delegates_to_helper(fn) -> bool:
    """True when the test calls a local helper whose name implies an assertion.

    Some suites factor their checks into a helper -- `_assert_passed(...)`,
    `assert_ledger_balanced(...)`. The body then contains no assert, but the test
    is not vacuous; the assertion lives one call away.

    This is a deliberate loosening, and it is a real loss of strictness: a test
    that calls an unrelated helper named `check_*` would be excused. Verified
    against three genuine cases in this portfolio (`json.loads` raising on a
    malformed log line, and two `_assert_passed` wrappers) that were otherwise
    reported as false positives. Reporting correct tests as broken teaches
    people to ignore the gate, which costs more than the rule is worth.

    A helper that is *defined* in the test file and *contains* no assert is
    still not caught here. That limit is stated rather than papered over.
    """
    for sub in ast.walk(fn):
        if not isinstance(sub, ast.Call):
            continue
        name = (sub.func.id if isinstance(sub.func, ast.Name)
                else getattr(sub.func, "attr", ""))
        if name.startswith(("_assert", "assert_", "check_", "verify_", "expect_")):
            return True
    return False


def scan_file(path: str, rel: str, result: ScanResult) -> None:
    try:
        with open(path, encoding="utf-8") as fh:
            source = fh.read()
        tree = ast.parse(source, filename=path)
    except (SyntaxError, UnicodeDecodeError, OSError) as exc:
        result.findings.append(
            Finding("UNREADABLE", rel, 0, "-", f"could not parse: {exc}")
        )
        return

    result.files_scanned += 1

    for node in ast.walk(tree):
        if not isinstance(node, (ast.FunctionDef, ast.AsyncFunctionDef)):
            continue
        if not _is_test_function(node):
            continue
        result.test_functions += 1
        fn = node.name

        if _has_docstring_only(node.body):
            result.findings.append(
                Finding("EMPTY_BODY", rel, node.lineno, fn,
                        "no executable statement (pass/ellipsis/docstring only)")
            )
            continue  # every other rule would be noise on an empty body

        has_oracle = False
        for stmt in node.body:
            for sub in ast.walk(stmt):
                if isinstance(sub, ast.Assert):
                    has_oracle = True
                    if (desc := _or_true(sub.test)):
                        result.findings.append(
                            Finding("ORA_TRUE", rel, sub.lineno, fn, desc))
                elif isinstance(sub, ast.Call):
                    fname = (sub.func.attr if isinstance(sub.func, ast.Attribute)
                             else getattr(sub.func, "id", ""))
                    if fname in ASSERTING_CALLS:
                        has_oracle = True
            if (desc := _zero_denominator(stmt)):
                result.findings.append(
                    Finding("ZERO_DENOMINATOR", rel,
                            getattr(stmt, "lineno", node.lineno), fn, desc))

        if not has_oracle and not _delegates_to_helper(node):
            result.findings.append(
                Finding("ASSERT_FREE", rel, node.lineno, fn,
                        "no assert, pytest.raises, or assertion helper in body "
                        "and no call to a local assertion helper"))

        if (desc := _self_asserting(node)):
            result.findings.append(
                Finding("SELF_ASSERTING", rel, node.lineno, fn, desc))



def iter_python_files(roots: list):
    for root in roots:
        if os.path.isfile(root):
            yield root
            continue
        for dirpath, dirnames, filenames in os.walk(root):
            dirnames[:] = [d for d in dirnames if d not in SKIP_DIRS]
            for fn in sorted(filenames):
                if fn.endswith(".py") and not fn.startswith("conftest"):
                    yield os.path.join(dirpath, fn)


def main(argv=None) -> int:
    parser = argparse.ArgumentParser(description="Test-oracle quality gate.")
    parser.add_argument("paths", nargs="+", help="files or directories to scan")
    parser.add_argument("--json", action="store_true", help="emit JSON instead of text")
    args = parser.parse_args(argv)

    result = ScanResult()
    for path in iter_python_files(args.paths):
        scan_file(path, os.path.relpath(path), result)

    if args.json:
        print(json.dumps({
            "files_scanned": result.files_scanned,
            "test_functions": result.test_functions,
            "clean": result.clean,
            "finding_count": len(result.findings),
            "findings": [asdict(f) for f in result.findings],
        }, indent=2))
    else:
        by_rule: dict = {}
        for f in result.findings:
            by_rule[f.rule] = by_rule.get(f.rule, 0) + 1
        print(f"TEST QUALITY GATE: {'PASS' if result.clean else 'FINDINGS'}")
        print(f"  scanned {result.files_scanned} files, "
              f"{result.test_functions} test functions")
        if result.findings:
            print()
            for rule, count in sorted(by_rule.items()):
                print(f"  {rule}: {count}")
            print()
            for f in result.findings:
                print(f"  {f.rule:16} {f.file}:{f.line} {f.function} -- {f.detail}")

    return 0 if result.clean else 1


if __name__ == "__main__":
    raise SystemExit(main())

