# Lab Oracle compatibility — PostgreSQL là authoritative, Oracle là profile tùy chọn

## 1. Tổng quan

FlashSale sử dụng **PostgreSQL** làm cơ sở dữ liệu authoritative cho bảng `payment_records`. JD Sacombank nhắc đến "Database: Oracle và NoSQL". Lab này đối chiếu khả năng tương thích Oracle nếu sau này cần chuyển sang hoặc hỗ trợ song song.

## 2. Hiện tại trong repository

| Thành phần | Trạng thái | Ghi chú |
|-----------|-----------|---------|
| PostgreSQL driver | `org.postgresql:postgresql` (runtime) | Đã kiểm thử local và integration |
| ojdbc8 driver | `com.oracle.database.jdbc:ojdbc8` (runtime, optional) | Có trong `pom.xml`, **chưa wired vào DataSource** |
| Spring profile Oracle | `src/main/resources/application-oracle.properties` | Template, có placeholder cho URL/user/password |
| Hibernate dialect | `PostgreSQLDialect` (mặc định) | Cần đổi thành `OracleDialect` khi bật profile oracle |
| DDL | `spring.jpa.hibernate.ddl-auto=validate` | Không auto-create, migration hand-off |

## 3. application-oracle.properties (profile template)

```properties
spring.datasource.url=${ORACLE_JDBC_URL:jdbc:oracle:thin:@//localhost:1521/ORCLPDB1}
spring.datasource.username=${ORACLE_USER:}
spring.datasource.password=${ORACLE_PASSWORD:}
spring.datasource.driver-class-name=oracle.jdbc.OracleDriver

spring.jpa.database-platform=org.hibernate.dialect.OracleDialect
spring.jpa.hibernate.ddl-auto=validate
```

**Lưu ý**: Profile chỉ bật khi chạy với `--spring.profiles.active=oracle`. Các giá trị credential đọc từ biến môi trường, **không hard-code**.

## 4. Những gì cần làm để DEMONSTRATED Oracle

| Mục | Trạng thái | Hành động cần thiết |
|-----|-----------|-------------------|
| Oracle JDBC driver | Có trong pom (optional) | ✅ |
| DataSource wiring | Chưa có | Kích hoạt profile oracle, cung cấp ORACLE_JDBC_URL/USER/PASSWORD |
| Hibernate dialect Oracle | Chưa có | Thêm `OracleDialect` trong profile |
| Migration Oracle | Chưa có | Viết migration script tương thích Oracle (tipe NUMBER, VARCHAR2, CLOB) |
| EXPLAIN PLAN / query plan | Chưa có | Chạy `EXPLAIN PLAN FOR` trên Oracle test database |
| Integration test Oracle | Chưa có | Cần Oracle-compatible runtime (Testcontainers Oracle image) |

**Đánh giá hiện tại**: `PARTIAL` — driver và profile template có sẵn, nhưng không có Oracle runtime test.

## 5. NoSQL (MongoDB)

| Thành phần | Trạng thái |
|-----------|-----------|
| spring-boot-starter-data-mongodb | Có, dùng cho audit projection |
| PaymentAuditRepository | Có, integration-tested với Testcontainers MongoDB |
| Document PaymentAudit | Có |

**Đánh giá**: `DEMONSTRATED` — MongoDB audit projection đã triển khai và kiểm thử integration.

## 6. Kết luận

- **PostgreSQL**: `DEMONSTRATED` — authoritative, có schema, index, idempotency concurrency test.
- **MongoDB (NoSQL)**: `DEMONSTRATED` — audit projection, có integration test.
- **Oracle**: `PARTIAL` — driver + profile template có sẵn, cần runtime test để nâng lên `DEMONSTRATED`.
