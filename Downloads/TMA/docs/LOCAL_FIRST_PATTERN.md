# Local-First Fallback Pattern (standard, Phase 1D)

Principle: every external service MUST have a zero-dependency local default
so CI, tests, and demos run with no keys, no network, no paid services.
Production opts into managed services via env flags; defaults stay local.

| Concern | Local default | Production opt-in | Owner |
|---|---|---|---|
| LLM | stub / Ollama qwen3:8b / LM Studio | Cloudflare Workers AI | all 3 |
| Embeddings | BoW hashed / BGE local | CF bge-base / HF ST | SupportDesk/SmartDoc |
| Vector search | in-process BM25 / LlamaIndex | Qdrant Cloud / Azure AI Search | SupportDesk/SmartDoc |
| DB | SQLite (WAL) + Alembic | Neon PG / Azure PG Flexible | all 3 |
| Ledger | SQLite inference_logs (stdlib) | Neon branch (psycopg) | CreditFlow |
| Workflow | in-process runner (same stages/order/retry) | Temporal / Airflow | SupportDesk/SmartDoc |
| Queue | in-memory / SQLite outbox | Kafka local / Service Bus prod | SupportDesk |
| Secrets | .env | Key Vault + UAI | all 3 |

Rules: (1) local runner executes EXACT same stage definitions as durable
runner — single `definitions.py` source; (2) local mode must state its
honest trade (no durability/history) in the module docstring;
(3) CI always runs the local path; managed path gated behind explicit flags
(LIVE_TESTS=1 / TEMPORAL_ENABLED=true); (4) never silently degrade — log
which backend is active at startup.
