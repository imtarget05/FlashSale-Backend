# Enterprise Validation Matrix

Generated 2026-09-29 18:22 UTC from evidence files. Max 36 points, 12 gates.

Scoring: 0 absent, 1 code exists, 2 happy-path runtime, 3 failure/recovery runtime verified.

No cell is written by hand. A gate only rises when a named test or scenario file exists and passed.

| Project | Reproduce | Invariant | E2E | Idempoten | Failure | Restart | Concurren | Security | Observabi | Performan | Backup | Deploy | Score | Level | Flagship |
|---|---|---|---|---|---|---|---|---|---|---|---|---|---|---|---|
| CreditFlow | 3 | 3 | 3 | 0 | 3 | 0 | 0 | 2 | 2 | 0 | 0 | 3 | 19 | L1 | no |
| Factory | 3 | 3 | 0 | 0 | 0 | 0 | 0 | 2 | 2 | 0 | 0 | 3 | 13 | L1 | no |
| ApexInspect-AI | 3 | 2 | 3 | 0 | 3 | 0 | 0 | 2 | 0 | 0 | 0 | 0 | 13 | L1 | no |
| FlashSale-Backend | 3 | 3 | 3 | 3 | 3 | 3 | 3 | 2 | 0 | 2 | 3 | 0 | 28 | L2 | no |
| MAIA | 3 | 3 | 3 | 0 | 3 | 0 | 0 | 2 | 2 | 0 | 0 | 3 | 19 | L1 | no |
| MiniERP | 0 | 0 | 0 | 0 | 0 | 0 | 0 | 2 | 0 | 0 | 0 | 0 | 2 | L0 | no |
| Enterprise-IT-Helpdesk-Lab | 3 | 2 | 3 | 0 | 3 | 0 | 0 | 2 | 0 | 0 | 0 | 0 | 13 | L1 | no |

## Thresholds

- Portfolio ready: >= 25/36
- Strong enterprise: >= 30/36
- Flagship: >= 33/36 plus invariant/E2E/failure = 3 and security/observability >= 2

## Hard cases executed

| Case | Project | Result | Boundary | Command |
|---|---|---|---|---|
| APEX-03 PLC unavailable | ApexInspect | PASS | SIMULATED | `pytest: tests/test_plc_bridge.py -k 'unavailable or hardware'` |
| APEX-04 audit tamper | ApexInspect | PASS | REAL | `pytest: tests/test_audit_chain.py` |
| CF-01 future leakage | CreditFlow | PASS | REAL | `pytest: tests/test_feature_store.py -k 'future or as_of or no_future'` |
| CF-02 train/serve parity | CreditFlow | PASS | REAL | `pytest: tests/test_features.py` |
| CF-03 threshold by business cost | CreditFlow | PASS | REAL | `pytest: tests/ -k 'threshold or cost'` |
| CF-06 audit tamper | CreditFlow | PASS | REAL | `pytest: tests/ -k 'audit or tamper or chain'` |
| FAC-01 quarantine | Factory | PASS | REAL | `pytest: tests/test_quarantine.py` |
| FAC-03 contracts | Factory | PASS | REAL | `pytest: tests/test_contracts.py` |
| HD-03 alert dedupe | Helpdesk | PASS | REAL | `node: test/ticket-alert.test.js` |
| HD-04 RBAC | Helpdesk | PASS | REAL | `node: test/auth-boundary.test.js` |
| MAIA-01 no-evidence refusal | MAIA | PASS | REAL | `pytest: tests/test_loops.py -k 'no_evidence or invalid_cite' --ignore=tests/test_llama_index_dataplane.py` |
| MAIA-04 cross-tenant | MAIA | PASS | REAL | `pytest: tests/test_tenant_authorization.py` |
| MAIA-05 agent restart | MAIA | PASS | REAL | `pytest: tests/test_langgraph_agent.py -k 'resume or checkpoint or insufficient' --ignore=tests/test_llama_index_dataplane.py` |
| MAIA-08 qdrant unavailable | MAIA | PASS | REAL | `pytest: tests/test_tracing.py -k 'refusal or fallback or offline' --ignore=tests/test_llama_index_dataplane.py` |