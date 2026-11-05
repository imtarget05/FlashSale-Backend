# ADR-026: ClickHouse làm kho phân tích OLAP cho sự kiện đơn hàng flash sale

- **Status**: Accepted
- **Date**: 2026-11-05

## Context
Hiện hệ thống chỉ có PostgreSQL (OLTP, đơn hàng, kho hàng, idempotency) và Redis (đặt hàng nhanh, резервация tồn kho). Khi flash sale diễn ra, doanh nghiệp cần truy vấn phân tích trên hàng triệu sự kiện đơn hàng (đặt, hủy, trả hàng, doanh thu theo giờ/sản phẩm, tỷ lệ chuyển đổi). Truy vấn aggregation trên PostgreSQL chậm, khóa bảng, ảnh hưởng đến giao dịch OLTP đang chạy.

## Problem
Cần một kho dữ liệu riêng cho workload phân tích (append-only, đọc-heavy, aggregation hàng tỷ dòng), tách khỏi PostgreSQL để không ảnh hưởng đường critical path đặt hàng.

## Options
1. **PostgreSQL chỉ duy nhất** — giữ đơn giản, nhưng SUM/COUNT trên hàng triệu dòng chậm, tranh khóa với OLTP, không mở rộng được.
2. **PostgreSQL + materialized views / read replica** — giảm tải phần nào, nhưng vẫn là hàng-based, aggregation lớn vẫn đắt, không giải quyết gốc rễ.
3. **ClickHouse (chọn)** — cơ sở dữ liệu dạng cột, nén dữ liệu cao, truy vấn aggregation hàng tỷ dòng dưới giây, MergeTree phân vùng theo thời gian, phù hợp sự kiện flash sale append-only.

## Decision
- Thêm service `clickhouse` vào `docker-compose.yml` (image `clickhouse/clickhouse-server:24.8`, cổng 8123 HTTP + 9000 native).
- Schema init tại `src/payment-service-java/docker/init-clickhouse.sql`: bảng `order_events` (MergeTree, phân vùng `toYYYYMM(event_time)`, ordine theo `(event_type, event_time)`) và `daily_order_summary` (AggregatingMergeTree).
- Thêm datasource ClickHouse vào Grafana provisioning để vẽ dashboard doanh số flash sale.
- Môi trường: `.env.example` bổ sung `CLICKHOUSE_*` (ít thay đổi, không commit secret).

## Trade-offs
- Positive: tách OLAP khỏi OLTP, truy vấn báo cáo nhanh, nén dữ liệu giảm chi phí lưu, Grafana có sẵn nguồn dữ liệu.
- Negative: ClickHouse không hỗ trợ update/xoá hiệu quả (chỉ nên append), eventual consistency (sự kiện ghi vào ClickHouse sau khi commit PostgreSQL), thêm một dịch vụ vận hành. Không dùng làm hệ thống ghi chính — PostgreSQL vẫn là source of truth đơn hàng.

## Verification
- `docker compose up -d clickhouse` → `wget --spider -q http://localhost:8123/ping` thành công.
- Truy vấn `SELECT count() FROM flashsale_analytics.order_events` trả về số sự kiện đã ingest.
- Dashboard Grafana đọc được datasource ClickHouse.
