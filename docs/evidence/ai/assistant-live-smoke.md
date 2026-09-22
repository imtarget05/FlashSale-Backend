# Evidence: AI Product Assistant — live smoke with real Ollama/Qwen (v2.1)

Date: 2026-09-23 · Repo: FlashSale-Backend · Branch: main
Runner: local macOS (Ollama 0.33.0, model `qwen3:4b`,2.5 GB Q4_K_M)

## What was proven (spec §10)

| Gate | Result |
|---|---|
| Real inference endpoint | `POST /v1/chat/completions` with `Authorization: Bearer ollama` (dummy key; Ollama does not validate) → 200 |
| API endpoint | `POST /api/assistant/product` (JWT required) → 200 with real model output |
| Anonymous | → 401 |
| Empty question | → 400 (`invalid_question`) |
| Grounding | every `recommendedProductId` resolves via `GET /api/products/{id}` → 200; invented ids dropped by the guard |
| Model tag | `"model":"qwen3:4b"` |
| Latency reported | measured **18s–70s** per call (qwen3 REASONS before answering; `think`/`reasoning_effort` not honored by Ollama's OpenAI endpoint → per-attempt timeout = 120s) |
| Token usage reported | real figures, e.g. `{"prompt":141,"completion":724}` (snake_case mapping) |
| Rate limit | Redis window key `flashsale:ai:rl:{userId}:{unixMinute}` observed with `count=1`; preload to 5 → next request **429 in0s** (model NOT called) |
| Metrics | `flashsale.ai.assistant.requests` / `.latency` / `.tokens` present in `/internal/metrics`, tagged `outcome=Ok`, `model=qwen3:4b` |
| OpenAPI | `/openapi/v1.json` documents `/api/assistant/product` + `Assistant` tag |

## Sample response (verbatim)

```json
{"answer":"iPhone 15 Pro Max","recommendedProducts":[{"productId":1,"reason":"High-end smartphone ideal for daily use with advanced features and strong performance"}],"model":"qwen3:4b","latencyMs":17996,"tokens":{"prompt":141,"completion":724}}
```

## Test totals at this commit

- Unit: **75/75** (incl.16 new assistant/grounding tests)
- Integration: **19/19**
- Live AI smoke (`scripts/ai-assistant-smoke.sh`): **19/19 PASS,0 FAIL**
- Live auth smoke (v1.0 regression gate, `48` checks): PASS (run recorded in session log)

## How to re-run

```bash
# prerequisites: ollama serve && ollama pull qwen3:4b
# compose PG/Redis/RabbitMQ up; then:
bash scripts/ai-assistant-smoke.sh
```
