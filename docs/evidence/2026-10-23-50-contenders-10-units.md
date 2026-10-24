# 50 contenders for 10 units: inventory conservation evidence

## Business question and invariant

If 50 buyers request one unit each during a flash sale with only 10 units available, does the service accept exactly 10, reject the remaining 40, persist exactly 10 orders, and leave no negative or phantom PostgreSQL stock? Overselling would create obligations the business cannot fulfil; underfilling would discard legitimate demand.

For this experiment, the required settled state is **10 accepted responses, 40 HTTP 409 responses, zero transport failures, 10 newly persisted orders, and PostgreSQL stock 0**. The harness fails if any count differs. This is a correctness check for one bounded burst, not a production capacity or availability target.

## Provenance and input

| Item | Evidence |
| --- | --- |
| Source commit | [`ef1f827ca6036a324cc23ac2f6dd310689332a52`](https://github.com/imtarget05/FlashSale-Backend/commit/ef1f827ca6036a324cc23ac2f6dd310689332a52) on `main` |
| Exact CI run | [Run 39982588513](https://github.com/imtarget05/FlashSale-Backend/actions/runs/39982588513), completed `success` on that SHA |
| Exact job | [`concurrency-harness`, job 120513438758](https://github.com/imtarget05/FlashSale-Backend/actions/runs/39982588513/job/120513438758), completed `success` |
| Raw artifact | [`harness-result`, artifact 12513484331](https://api.github.com/repos/imtarget05/FlashSale-Backend/actions/artifacts/12513484331), containing `result.txt` and `api.log` |
| Workflow and harness | [CI definition](https://github.com/imtarget05/FlashSale-Backend/blob/ef1f827ca6036a324cc23ac2f6dd310689332a52/.github/workflows/ci.yml#L617-L707) and [Python harness](https://github.com/imtarget05/FlashSale-Backend/blob/ef1f827ca6036a324cc23ac2f6dd310689332a52/load-tests/concurrency/oversell_demo.py) |

The GitHub Actions `ubuntu-latest` job starts PostgreSQL `15-alpine`, Redis `7-alpine`, and one .NET 10 `Order.Api` process on `localhost:5065` in `Development`. It explicitly selects `Messaging__Provider=InMemory`; the API's consumer runs in that process. The job generates a one-run STAFF password, logs in, sets product `#1` stock to 10 in PostgreSQL, resynchronizes Redis through the authorized operations endpoint, then releases 50 Python threads through a barrier. Each sends quantity 1. The harness waits three seconds before reading PostgreSQL stock and the order-count delta. The artifact reports zero orders before the burst.

## Observed result

The following values are transcribed from the **same** `result.txt` artifact, not from k6 or another run:

| Measure | Raw value |
| --- | ---: |
| Concurrent requests | 50 |
| Responses classified accepted by the harness | 10 |
| HTTP 409 rejected | 40 |
| Transport/other response failures | 0 |
| Client-observed request p95 | 192 ms |
| Initial PostgreSQL stock | 10 |
| Accepted units | 10 |
| Final PostgreSQL stock | 0 |
| Newly persisted orders | 10 |
| Harness verdict | `RESULT: PASS - exact fill, 409 rejects, persisted orders and stock verified.` |

The `result.txt` SHA-256 is `b29da55712595d4897d4b816c02a58dc1fbfd1205f4b851c604a78e444adcb65`. A fresh download of artifact 12513484331 matched the inspected `result.txt` and `api.log` byte for byte on 2026-10-23.

## Reproduce and review

From the repository root, use the CI job's service setup and credential flow in `.github/workflows/ci.yml`; do not reuse a committed password. Once PostgreSQL, Redis and the single API process are healthy and `OPS_TOKEN` contains a freshly minted STAFF bearer token, run the exact CI experiment:

```sh
python3 -m unittest discover -s load-tests/concurrency -p 'test_oversell_demo.py'
python3 load-tests/concurrency/oversell_demo.py \
  --stock 10 --requests 50 --psql-container '' --settle-seconds 3 \
  --ops-token "$OPS_TOKEN"
```

To inspect the immutable result instead of triggering another burst:

```sh
gh run view 39982588513 --repo imtarget05/FlashSale-Backend
gh run download 39982588513 --repo imtarget05/FlashSale-Backend \
  --name harness-result --dir /tmp/ops-50-10-evidence
cat /tmp/ops-50-10-evidence/result.txt
```

## Claim boundary and next gate

**Permitted claim:** “On GitHub Actions at main SHA `ef1f827`, a synthetic single-process 50-request burst against PostgreSQL and Redis produced 10 responses classified as accepted, 40 HTTP 409 rejects, 10 new persisted orders, and final database stock 0; the harness reported p95 192 ms.”

The script labels its accepted line `200 accepted`, but its classifier counts **any HTTP 2xx**, including asynchronous 202. The raw output does not break accepted responses down by exact status, so this dossier does not claim all ten returned 200 or that each order completed payment. The test sends one burst with one API process and an InMemory broker; it does not exercise RabbitMQ delivery, an independent worker, restart, network partition, or multi-replica contention. Redis is synchronized **before** the burst, but the harness does not assert its **post-burst** value. The 192 ms is a client-observed p95 for this synthetic run, not an SLO or forecast for production traffic. An additional real-broker crash/replay drill and repeatable multi-replica load profile are required before making wider reliability or performance claims.
