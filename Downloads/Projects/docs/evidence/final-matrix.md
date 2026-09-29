# Final Verification Matrix

Generated: `2026-09-29T07:04:46.565784+00:00`

Frozen baseline SHA-256: `f2a7c054b85f34961091401da8881b1dfb29aecdd004766df5c47811830cf137`

**Baseline intact:** YES

**Total claims:** 37  
**Counts:** FAILED=3 · NOT_RUN=26 · PARTIAL=5 · VERIFIED=3

| Claim | Repo | § | Design | Runtime | Artifact |
|---|---|---|---|---|---|
| `D3-CREDITFLOW-BATCH` | CreditFlow | 1 | VERIFIED | **NOT_RUN** | `—` |
| `D3-FACTORY-BATCH` | Factory | 1 | VERIFIED | **VERIFIED** | `tests/BATCH.xml` |
| `D3-APEX-BATCH` | ApexInspect | 1 | VERIFIED | **VERIFIED** | `tests/FULL.xml` |
| `D3-FLASHSALE-BATCH` | FlashSale | 1 | VERIFIED | **NOT_RUN** | `—` |
| `D3-MAIA-BATCH` | MAIA | 1 | VERIFIED | **NOT_RUN** | `maia-clean-head/tests/FULL-QDRANT.xml` |
| `D3-MINIERP-BATCH` | MiniERP | 1 | VERIFIED | **NOT_RUN** | `—` |
| `D3-FLASHSALE-STREAMING` | FlashSale | 2 | VERIFIED | **NOT_RUN** | `—` |
| `D3-MAIA-STREAMING` | MAIA | 2 | VERIFIED | **NOT_RUN** | `maia-clean-head/streaming/OUTBOX.xml` |
| `D3-MINIERP-ELT` | MiniERP | 6 | VERIFIED | **NOT_RUN** | `—` |
| `D3-CREDITFLOW-MLTRAINING` | CreditFlow | 11 | VERIFIED | **NOT_RUN** | `—` |
| `D3-FLASHSALE-REALTIME` | FlashSale | 12 | VERIFIED | **NOT_RUN** | `—` |
| `D3-MAIA-REALTIME` | MAIA | 12 | VERIFIED | **NOT_RUN** | `maia-clean-head/rag/CORRECTIVE-RAG.xml` |
| `D3-FACTORY-LAKEHOUSE` | Factory | 5 | PARTIAL | **FAILED** | `tests/LAKEHOUSE.xml` |
| `D3-FACTORY-DATALAKE` | Factory | 7 | PARTIAL | **PARTIAL** | `tests/DATALAKE.xml` |
| `D3-MAIA-DATALAKE` | MAIA | 7 | PARTIAL | **NOT_RUN** | `maia-clean-head/rag/CORRECTIVE-RAG.xml` |
| `D3-APEX-DATALAKE` | ApexInspect | 7 | PARTIAL | **FAILED** | `tests/FULL.xml` |
| `D3-MAIA-ELT-PARTIAL` | MAIA | 6 | PARTIAL | **NOT_RUN** | `maia-clean-head/rag/CORRECTIVE-RAG.xml` |
| `D3-FACTORY-DW` | Factory | 8 | PARTIAL | **PARTIAL** | `tests/DW.xml` |
| `D3-MINIERP-DW` | MiniERP | 8 | PARTIAL | **NOT_RUN** | `—` |
| `D3-CREDITFLOW-MESH` | CreditFlow | 9 | PARTIAL | **NOT_RUN** | `—` |
| `D3-FACTORY-MESH` | Factory | 9 | PARTIAL | **PARTIAL** | `tests/MESH.xml` |
| `D3-APEX-MESH` | ApexInspect | 9 | PARTIAL | **PARTIAL** | `tests/FULL.xml` |
| `D3-MAIA-MESH` | MAIA | 9 | PARTIAL | **NOT_RUN** | `maia-clean-head/mcp/MCP-PROTOCOL.xml` |
| `D3-MINIERP-MESH` | MiniERP | 9 | PARTIAL | **NOT_RUN** | `—` |
| `D3-HELPDESK-MESH` | Helpdesk | 9 | PARTIAL | **NOT_RUN** | `—` |
| `D3-MAIA-LAMBDA` | MAIA | 3 | PARTIAL | **NOT_RUN** | `maia-clean-head/mcp/MCP-PROTOCOL.xml` |
| `D3-MAIA-MCP-CAPABILITY` | MAIA | 3 | PARTIAL | **NOT_RUN** | `maia-clean-head/mcp/MCP-PROTOCOL.xml` |
| `D3-MAIA-KAPPA` | MAIA | 4 | PARTIAL | **NOT_RUN** | `maia-clean-head/streaming/OUTBOX.xml` |
| `D3-CREDITFLOW-FEATURESTORE` | CreditFlow | 10 | PARTIAL | **NOT_RUN** | `—` |
| `D3-APEX-MLTRAINING` | ApexInspect | 11 | PARTIAL | **FAILED** | `tests/FULL.xml` |
| `D3-APEX-REALTIME` | ApexInspect | 12 | PARTIAL | **PARTIAL** | `benchmarks/baseline-latency.json` |
| `D3-APEX-AUDIT` | ApexInspect | 9 | VERIFIED | **VERIFIED** | `audit/audit-chain-verification.json` |
| `D3-CREDITFLOW-REALTIME` | CreditFlow | 12 | PARTIAL | **NOT_RUN** | `—` |
| `D3-MINIERP-REALTIME` | MiniERP | 12 | PARTIAL | **NOT_RUN** | `—` |
| `D3-FLASHSALE-MLTRAINING` | FlashSale | 11 | PARTIAL | **NOT_RUN** | `—` |
| `D3-MINIERP-MLTRAINING` | MiniERP | 11 | PARTIAL | **NOT_RUN** | `—` |
| `D3-FLASHSALE-REALTIME-ALT` | FlashSale | 12 | PARTIAL | **NOT_RUN** | `—` |