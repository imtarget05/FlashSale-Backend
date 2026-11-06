# Lab lý thuyết: Kafka vs ActiveMQ vs RabbitMQ — Broker chọn lẻ cho FlashSale

## 1. Tổng quan

FlashSale hiện tại sử dụng **RabbitMQ** làm message broker chính cho sự kiện thanh toán (payment events) và transactional outbox. Mục tiêu của lab này là đối chiếu lý thuyết giữa Kafka và ActiveMQ — hai broker được nhắc đến trong JD Sacombank ("Kafka hoặc ActiveMQ") — với RabbitMQ đang triển khai, để xác định mức độ khớp yêu cầu kỹ thuật.

## 2. Đối chiếu tính năng

| Tiêu chí | RabbitMQ (hiện có) | Apache Kafka | Apache ActiveMQ |
|----------|-------------------|--------------|-----------------|
| Mô hình | Direct exchange, topic, fanout, headers | Pub/Sub partitioned log | JMS queues, topics, pub/sub |
| Cách lưu trữ | Message trong queue, xóa khi consume | Log phân vùng, immutable, retention theo thời gian/kích thước | Message trong queue/topic, persistence Optional |
| Throughput | Trung bình (~10k–50k msg/s) | Rất cao (~1M+ msg/s) | Trung bình–cao (~10k–100k msg/s) |
| Latency | Thấp (~ms) | Thấp–trung bình | Thấp |
| Ordering | Trong queue | Trong partition | Trong queue/topic |
| Replay | Không hỗ trợ replay native | Hỗ trợ replay bằng offset | Không hỗ trợ replay native |
| Consumer model | Push (ack manually) | Pull (offset-based) | Push/Pull (JMS) |
| Use case phù hợp | Task queue, RPC, routing phức tạp | Event streaming, log aggregation, event sourcing | Enterprise messaging, JMS, legacy integration |

## 3. Tại sao FlashSale chọn RabbitMQ (hiện tại)

1. **Transactional outbox**: Lưu sự kiện thanh toán vào PostgreSQL cùng transaction, rồi relay ra RabbitMQ. Đảm bảo at-least-once mà không cần distributed transaction.
2. **Routing linh hoạt**: Direct exchange và routing key phân biệt `payment.completed` / `payment.failed`, dễ mở rộng DLQ (dead-letter queue) cho sự kiện thất bại.
3. **Latency thấp**: Thanh toán cần phản hồi nhanh (<100ms), RabbitMQ phù hợp hơn Kafka cho request/response-style flow.
4. **Đơn giản vận hành**: Không cần quản lý Zookeeper/KRaft, partition, offset management phức tạp như Kafka.

## 4. Khi nào nên cân nhắc Kafka/ActiveMQ

| Tình huống | Broker khuyến nghị | Lý do |
|-----------|-------------------|-------|
| Sự kiện streaming lớn (ví dụ: stream toàn bộ đơn hàng cho phân tích OLAP ClickHouse) | **Kafka** | Log immutable, replay bằng offset, throughput cao |
| Tích hợp hệ thống kế toán kế thừa sử dụng JMS | **ActiveMQ** | Tuân thủ JMS 1.1/2.0, dễ tích hợp legacy |
| Nhiều consumer group đọc cùng sự kiện | **Kafka** | Consumer group, offset độc lập mỗi group |
| Task queue, retry/DLQ phức tạp | **RabbitMQ** | ACK/nack/requeue/DLQ nativos |

## 5. Kết luận lab

- **Kafka**: Chưa có runtime evidence trong FlashSale. Lý thuyết rõ: phù hợp nếu cần event sourcing, replay, hoặc stream sự kiện vào ClickHouse. Đánh giá `THEORY_ONLY` — chưa triển khai, chưa test producer/consumer.
- **ActiveMQ**: Chưa có runtime evidence. Lý thuyết rõ: phù hợp nếu cần tích hợp JMS/legacy. Đánh giá `THEORY_ONLY`.
- **RabbitMQ**: `DEMONSTRATED` — có integration test, outbox, DLQ, retry, confirm/mandatory routing.

**Khuyến nghị**: Giữ RabbitMQ cho thanh toán. Nếu sau này cần stream sự kiện vào ClickHouse OLAP, bổ sung Kafka làm secondary broker (không thay thế RabbitMQ).
