# 12 Kiến Trúc Data Pipeline — Data Architecture Reference

> Nguồn: series "Data Engineer mà chỉ biết ETL thôi thì chưa đủ" (Code4Life).
> File này là **spec kiến trúc tham chiếu**: khi bạn paste source project vào workspace,
> tôi sẽ thiết kế lại cấu trúc theo tài liệu này.

---

## 0. Tổng quan: bản đồ 12 kiến trúc

| # | Kiến trúc | Bản chất | Công nghệ đại diện |
|---|-----------|----------|--------------------|
| 1 | Batch Data Pipeline | Xử lý theo lịch (scheduled) | Airflow, Spark |
| 2 | Real-time Streaming Pipeline | Xử lý event liên tục | Kafka, Flink, Spark Streaming |
| 3 | Lambda Architecture | Batch + Speed + Serving | Hadoop, Spark, Kafka |
| 4 | Kappa Architecture | Chỉ stream, đơn giản hoá Lambda | Kafka + Flink |
| 5 | Lakehouse Architecture | Delta/Iceberg trên object storage | Delta Lake, Apache Iceberg |
| 6 | ELT Pipeline Architecture | Load → transform trong warehouse | Snowflake, BigQuery, dbt |
| 7 | Data Lake Pipeline | Landing → Clean → Curated | S3/HDFS, Delta |
| 8 | Data Warehouse Pipeline | Staging → Mart → BI | Snowflake, BigQuery, Redshift |
| 9 | Data Mesh Architecture | Domain-owned data product | Data-as-a-Product |
| 10 | Feature Store Pipeline | Feature reuse train/serve | Feast, Tecton, Michelangelo |
| 11 | ML Training Pipeline | Train → registry → eval | Kubeflow, MLflow, Airflow |
| 12 | Real-time ML Inference Pipeline | Preemptive, ms-latency | TF Serving, TorchServe, KServe |

---

## 1. Batch Data Pipeline

- Xử lý dữ liệu theo lịch (cron / event trigger), throughput cao, latency lớn.
- Luồng: Source → Extract → Transform (clean/aggregate) → Load → Warehouse/Report.
- Phù hợp: ETL truyền thống, báo cáo định kỳ, dữ liệu lịch sử.
- Công nghệ: Airflow, Dagster, Prefect, Luigi, Spark Batch, dbt.
- Rủi ro: job chồng nhau, backfill khó, không real-time.

## 2. Real-time Streaming Pipeline

- Xử lý event/nhận ký liên tục, latency mili-giây tới giây.
- Luồng: Producer → Message Broker (Kafka) → Stream Processor (Flink) → Sink (DB/OLAP/Alert).
- Đặc điểm: partitioned, ordered per-key, at-least-once + idempotent sink, xử lý late event.
- Công nghệ: Kafka/Pulsar, Flink, Kafka Streams, Spark Structured Streaming, Debezium (CDC).
- Phù hợp: fraud detection, recommendation, IoT, monitoring real-time.

## 3. Lambda Architecture

- 3 tầng: **Batch Layer** (source of truth, full recompute) + **Speed Layer** (real-time view) + **Serving Layer** (merge trả về query).
- Ưu: chính xác lịch sử + realtime. Nhược: phức tạp, code trùng lặp giữa batch và speed layer.

## 4. Kappa Architecture

- **Chỉ một stream processing pipeline duy nhất**, bỏ batch layer.
- Raw store (log) + Curated store; stream xử lý → curated → serving.
- Ưu: đơn giản hóa Lambda, giảm độ phức tạp pipeline, hỗ trợ replay để tính lại lịch sử.
- Phù hợp: real-time analytics. Công nghệ: Kafka + Flink / Spark Streaming.

## 5. Lakehouse Architecture

- Hợp nhất Data Lake (schema thưa, chi phí thấp) và Data Warehouse (ACID, schema enforcement).
- Công nghệ: Delta Lake, Apache Iceberg, Apache Hudi, Trino, Spark.
- Cơ chế: ACID transaction, time travel, schema evolution, zero-copy partitioning.


## 6. ELT Pipeline Architecture

- **Khác ETL**: dữ liệu được **load vào storage trước, sau đó mới transform** bằng SQL/processing engine ngay trong warehouse.
- Luồng: Sources → Ingestion (Fivetran/Airbyte/Stitch/Python) → Data Warehouse (Landing / Staging / Transform / Mart, quản lý bằng dbt) → BI & Data Science.
- Reverse ETL: đẩy dữ liệu ngược về app/CRM (Census, Hightouch).
- Observability: SODA, Monte Carlo, Datafold. Orchestration: Prefect, Airflow, Dagster.
- Ưu: tăng tốc khi dữ liệu lớn, hỗ trợ semi-structured, phổ biến trong modern data stack.

## 7. Data Lake Pipeline

- Lưu dữ liệu thô trong object storage, hỗ trợ structured / semi-structured / unstructured.
- 4 tầng thành phần:
  - **Ingestion Layer** — nhận streaming + batch (structured/unstructured).
  - **Storage Layer** — 3 zone: `Landing Zone` (raw đúng như nguồn) → `Clean Zone` (chuẩn hóa, dedupe) → `Curated Zone` (sẵn sàng analytics, transform/enrich/model).
  - **Processing Layer** — Data processing pipeline: Validate / Clean / Standardize → Transform / Enrich / Model.
  - **Cataloging & Search Layer** — metadata, lineage, discovery (DataHub, Glue, Amundsen, OpenMetadata).
- **Consumption Layer** — BI, dashboard, ML, self-service analytics.
- Ưu: petabyte-scale, chi phí thấp; kết hợp được streaming pipeline. Ví dụ: AWS S3 Data Lake, Hadoop HDFS.

## 8. Data Warehouse Pipeline

- Dữ liệu được xử lý và lưu trong kho **tối ưu cho analytics** (columnar, aggregate, index).
- Thành phần:
  - **Data sources** — CRMs, ERPs, SQL/NoSQL databases, IoT devices, social media/websites, spreadsheets, flat files.
  - **Staging area (ETL)** — vùng đệm, làm sạch & chuẩn hóa trước khi load.
  - **Storage layer** — Data warehouse + Metadata manager (catalog) + **Data marts** theo chủ đề.
  - **Presentation layer** — BI tools, reporting tools, business applications, operational systems (truy cập qua **APIs** hoặc **SQL**).
- Hỗ trợ truy vấn SQL analytics nhanh, chuẩn hóa theo schema, phân tích dữ liệu lịch sử.
- Ví dụ: Snowflake, BigQuery, Redshift.

## 9. Data Mesh Architecture

- Dữ liệu **quản lý theo domain của từng team**; mỗi domain chịu trách nhiệm data product của mình.
- Thành phần: nhiều **Data Warehouse (A/B/C)** phía trên → **Data Infrastructure as-a-Platform** → các **Domain 1..5**, mỗi domain có chuỗi **E → T → L**.
- Liên kết chéo giữa domain theo nguyên tắc **Data-as-a-Product** (contract, schema, ownership rõ ràng).
- Lợi ích: giảm bottleneck của central data team, tăng khả năng mở rộng, tạo data platform self-service, phù hợp tổ chức nhiều team dữ liệu, nhiều công ty lớn áp dụng.
- Yêu cầu đi kèm: data contract, data quality, observability, data catalog.

## 10. Feature Store Pipeline

- Lưu trữ & phục vụ **feature dùng chung cho ML models** (online + offline).
- 3 pipeline quanh **Feature Store** (Feast/Tecton/Michelangelo):
  1. **#1 Feature Engineering Pipeline** — API → Feature Engineering → Data Validation → Feature Store.
  2. **#2 Training Pipeline** — Feature Store → Training Pipeline → ML Platform & W&B → phản hồi lại Feature Store.
  3. **#3 Batch Prediction Pipeline** — Feature Store → Batch Prediction → Bucket/Object Storage → API & Web App → UI/Web App.
- **Model Monitoring** đọc từ bucket và ghi ngược lại Feature Store.
- Ưu: reuse feature giữa nhiều model, đảm bảo **train/serve consistency**, hỗ trợ offline training + online inference, phù hợp AI quy mô lớn.
- Luồng tổng: data ingestion → feature engineering → feature store → (train | predict).

## 11. ML Training Pipeline (Machine Learning Pipeline in Production)

- Chuỗi thành phần (trong khung **Orchestration**):
  - `Client` → `Preprocessing` (client data) → `ML model` → `Prediction` trả về client.
  - `Client` → **Ground-truth data** → `Ground-truth storage` → `Evaluation & Monitoring`.
  - `ML model` → `Predictions history` → `Evaluation & Monitoring`.
  - `Database` → `Preprocessing` → `Feature store` (data) → `ML model`.
- Vai trò: chuẩn bị dữ liệu cho training, gồm data cleaning + feature engineering + model training; hỗ trợ distributed cluster; tự động hoá bằng workflow orchestration; lưu model vào **model registry**; phục vụ recommendation & AI.
- Công cụ: Kubeflow, MLflow, Airflow. Model monitoring: W&B.

## 12. Real-time ML Inference Pipeline

**Traditional Design**
- Multidimensional input data frames (Sensors, Camera, LiDAR) → `Pick New Frame` → Neural Network (Non-preemptive FIFO loop, **Priority inversion**) → Device Layer (CPUs, GPUs) → Decisions.
- Nhược: FIFO không ưu tiên, head-of-line blocking, latency không kiểm soát được.

**New Real-time Architecture**
- Multidimensional input data frames + **Physical context** → `Data Slicing and Priority Allocation` → `Deduplication` → **Prioritized queues** → `Batching and Utility Maximization` → **Preemptive (Async) Neural Network** (Device Layer CPUs/GPUs) → Decisions.
- Ưu: preemption, ưu tiên theo độ khẩn cấp, batching tối ưu utility, loại bỏ dữ liệu trùng lặp, latency milliseconds.

**Đặc điểm chung**
- Model deploy để predict real-time; nhận input từ application hoặc streaming pipeline.
- Truy xuất feature từ **feature store hoặc cache**.
- Phục vụ: recommendation, search ranking, fraud detection.
- Scale bằng **container orchestration**.
- Công nghệ: TensorFlow Serving, TorchServe, KServe.

---

## Yêu cầu khi tôi bỏ project vào

Để tôi refactor theo đúng kiến trúc trên, bạn gửi kèm:
1. Source code (repo/folder) + ngôn ngữ/framework đang dùng.
2. Kiến trúc hiện tại, phần nào muốn giữ lại.
3. Phạm vi: chỉ **dựng skeleton + wiring**, hay **di chuyển code thật** từng module.

Tôi sẽ map từng phần tương ứng:
`ingestion/` (7, 8, 6) → `storage/` (landing → clean → curated) → `processing/` (5) → `serving/` (3, 4) → `domains/` (9) → `ml/` (10, 11, 12).

