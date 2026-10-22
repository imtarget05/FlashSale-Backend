# ADR-022: LLM Observability with Langfuse via OpenTelemetry in Phase VI

## Status
Accepted (Architectural Design for Phase VI)

## Context
As FlashSale and OpsBridge evolve, Phase VI introduces AI-assisted exception handling and customer inquiry processing (e.g., `IAssistantProvider` per ADR-012). LLM operations introduce new operational failure modes:
1. High token consumption and unexpected cost spikes during traffic peaks.
2. Unpredictable inference latency (p95 > 2s) blocking customer checkout or fulfillment workers.
3. Model drift and prompt quality degradation.

To manage AI operations reliably across the monorepo, a unified LLM observability platform is required. **Langfuse** is the monorepo standard (already active in `SmartDocumentChatbot` and `QuanLyKho`).

## Decision
1. **Clean Architecture Compliance**:
   - `FlashSale.Application` declares the port: `ILlmObservabilityGateway` (no external SDK dependency).
   - `FlashSale.Infrastructure` implements the adapter using OpenTelemetry standard OTLP exporter pointing to Langfuse (`https://cloud.langfuse.com/api/public/otel` or private K8s cluster `langfuse-server.langfuse.svc.cluster.local:3000`).
2. **Fail-Safe & Non-Blocking**:
   - Tracing must execute asynchronously with in-memory buffering.
   - If Langfuse is unreachable, order processing and inventory fulfillment MUST continue without throwing exceptions.
3. **Data Sanitization**:
   - Personal Identifiable Information (PII) such as customer credit cards, passwords, or order voucher amounts must be redacted before span export.

## Consequences
- **Positive**: Complete alignment with monorepo observability standard. Unified view of LLM costs and latency.
- **Trade-off**: Requires configuration of `LANGFUSE_PUBLIC_KEY` and `LANGFUSE_SECRET_KEY` when Phase VI AI providers are enabled.
