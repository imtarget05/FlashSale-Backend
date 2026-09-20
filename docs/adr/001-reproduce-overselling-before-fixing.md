# ADR-001: Treat Overselling as an Evidence-Backed Failure, Not a Guess

- **Status**: Accepted
- **Date**: 2026-09-19
- **Deciders**: Project owner + engineering agent

## Context
The naive Phase 1 endpoint is suspected of allowing overselling under concurrent load.
We must not introduce Redis, queues, or locking "just in case" — engineering principles in
`AGENTS.md` require a reproduced problem before a solution.

## Decision
Build a reproducible concurrency experiment (`load-tests/concurrency/oversell_demo.py`)
that resets stock to a known value, fires a simultaneous burst of purchase requests, and
audits the database against an inventory conservation law:
`final_stock == initial_stock − accepted_units`.

## Consequences
- **Positive**: The race condition is now a measured fact (50/50 accepted, stock 10→9,
  see `docs/benchmarks/phase2-oversell-experiment.md`), not an opinion. Any future fix can
  be validated against the same harness.
- **Negative**: Requires a running database to audit; the harness is intentionally simple
  (stdlib Python) rather than k6 so it runs anywhere.
- **Trade-off accepted**: We spend a phase on measurement before any fix. This is the cost
  of an evidence-driven architecture story.