"""Negative controls for the test-oracle quality gate itself.

A gate that has never been shown to fail is an assumption. Each test below
plants exactly one of the four false-pass shapes actually found in this
portfolio and asserts the gate reports it. If a rule is weakened, the
corresponding test fails.

Run:  python3 tools/e2e/test_test_quality_gate.py
"""
import ast
import os
import sys

sys.path.insert(0, os.path.dirname(os.path.dirname(os.path.abspath(__file__))))
from test_quality_gate import ScanResult, scan_file  # noqa: E402

SRC = """
import pytest


def test_honest():
    assert 1 + 1 == 2


def test_tautology():
    x = compute()
    assert x is not None or True


def test_empty_body():
    '''this test asserts nothing at all'''


def test_pass_only():
    value = 5
    pass


def test_bare_pass():
    pass


def test_ellipsis_only():
    ...


def test_no_assertion():
    result = do_something()
    print(result)


def test_zero_denominator():
    total = 100
    failures = 0
    rate = failures / 0
    assert rate == 0


def test_self_asserting():
    assert validate(x) == validate(x)


def test_missing_function_refusal():
    mod = import_module()
    fn = getattr(mod, 'route_query', None)
    assert fn is None


def test_proper_negative_control():
    with pytest.raises(ValueError):
        do_something_bad()
"""


def _scan(source: str) -> ScanResult:
    import tempfile
    with tempfile.NamedTemporaryFile("w", suffix=".py", delete=False) as fh:
        fh.write(source)
        path = fh.name
    try:
        result = ScanResult()
        scan_file(path, "planted.py", result)
        return result
    finally:
        os.unlink(path)


def main() -> int:
    result = _scan(SRC)
    by_fn: dict[str, set] = {}
    for f in result.findings:
        by_fn.setdefault(f.function, set()).add(f.rule)

    # (test function, rule that MUST be reported)
    expectations = [
        ("test_honest", None),
        ("test_tautology", "ORA_TRUE"),
        ("test_empty_body", "EMPTY_BODY"),
        # `value = 5` is a real statement, so this body is NOT empty -- it is
        # assert-free. Asserting EMPTY_BODY here was my own wrong expectation,
        # and the gate was right to disagree.
        ("test_pass_only", "ASSERT_FREE"),
        ("test_bare_pass", "EMPTY_BODY"),
        ("test_ellipsis_only", "EMPTY_BODY"),
        ("test_no_assertion", "ASSERT_FREE"),
        ("test_zero_denominator", "ZERO_DENOMINATOR"),
        ("test_self_asserting", "SELF_ASSERTING"),
        # A getattr(..., None) followed by `assert fn is None` scores a safety
        # PASS when the implementation has been deleted -- the assertion is
        # present, so no structural rule fires, and the test still cannot tell a
        # deleted function from a correct refusal. The gate deliberately does NOT
        # claim to catch this: it is the one case in the original list that
        # needs a per-project oracle, not a general shape. Recorded here as a
        # known limit rather than asserted as a pass, because pretending the
        # gate covers it would be the exact overclaim this tool exists to stop.
        ("test_missing_function_refusal", None),
        ("test_proper_negative_control", None),
    ]

    failures = []
    for fn, must_report in expectations:
        got = by_fn.get(fn, set())
        if must_report is None:
            if got:
                failures.append(f"{fn}: expected clean, got {sorted(got)}")
        elif must_report not in got:
            failures.append(f"{fn}: expected {must_report}, got {sorted(got) or 'nothing'}")

    total = len(expectations)
    ok = total - len(failures)
    print(f"TEST-QUALITY-GATE SELF-TEST: {ok}/{total} correct")
    for f in failures:
        print(f"  MISS {f}")
    if failures:
        print("\nA rule was weakened or is broken -- the gate is not trustworthy.")
        return 1
    print("All planted false-pass shapes are detected; honest tests stay clean.")
    return 0


if __name__ == "__main__":
    raise SystemExit(main())
