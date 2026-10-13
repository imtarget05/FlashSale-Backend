#!/usr/bin/env python3
"""
Generate high-resolution architecture diagrams for 7 LinkedIn portfolio projects
using Mermaid CLI with Chrome headless renderer.
"""
import subprocess
import os
from pathlib import Path

OUTPUT_DIR = Path("/Users/mainguyenbinhtan/Downloads/TMA/linkedin_images/rendered")
OUTPUT_DIR.mkdir(parents=True, exist_ok=True)
PUPPETEER_CONFIG = "/tmp/puppeteer-config.json"

DIAGRAMS = {
    "1_aks_sre_platform": """flowchart TD
    %% Styling Definitions
    classDef client fill:#1e293b,stroke:#38bdf8,stroke-width:2px,color:#f8fafc;
    classDef control fill:#0f172a,stroke:#818cf8,stroke-width:2px,color:#f8fafc;
    classDef pool fill:#1e1b4b,stroke:#c084fc,stroke-width:2px,color:#f8fafc;
    classDef gitops fill:#022c22,stroke:#34d399,stroke-width:2px,color:#f8fafc;
    classDef obs fill:#311042,stroke:#f472b6,stroke-width:2px,color:#f8fafc;
    classDef sec fill:#451a03,stroke:#fb923c,stroke-width:2px,color:#f8fafc;

    subgraph IaC["1. Infrastructure as Code & Azure Perimeter (Terraform)"]
        direction TB
        TF["Terraform IaC (Resource Groups · VNet · Subnets · Private DNS)"]:::sec
        ACR["Private Azure Container Registry (ACR Pull by Digest)"]:::sec
        OIDC["Azure AD Workload Identity (OIDC Federated Token Exchange)"]:::sec
    end

    subgraph AKSCluster["2. Azure Kubernetes Service (AKS Platform Core)"]
        direction TB
        subgraph ControlPlane["Managed Control Plane"]
            APIServer["Kube-API Server (OIDC Auth · Admission Webhooks)"]:::control
        end

        subgraph SystemPool["System Node Pool (CriticalAddonsOnly Taint)"]
            CoreDNS["CoreDNS / Metrics Server"]:::pool
            Overlay["Azure CNI Overlay Network"]:::pool
        end

        subgraph WorkloadPool["User / Workload Node Pool (Auto-Scaling)"]
            Workloads["Workload Pods (FlashSale Backend / Microservices)"]:::client
            KEDA["KEDA Operator (Event-Driven Pod Autoscaler)"]:::pool
            ArgoAgent["Argo CD Application Controller"]:::gitops
        end
    end

    subgraph GitOpsTier["3. GitOps Continuous Delivery & Reconciliation"]
        direction TB
        GitRepo["GitHub Repository (Platform & App Manifests)"]:::gitops
        ArgoServer["Argo CD Server (GitOps Reconcile · Drift Detection)"]:::gitops
    end

    subgraph SREObs["4. SRE Observability & Governance"]
        direction TB
        Monitor["Azure Monitor Container Insights"]:::obs
        Prom["Managed Prometheus (Metrics Scrape)"]:::obs
        Graf["Grafana Dashboards (SLO · Latency · Capacity)"]:::obs
    end

    %% Connections
    GitRepo -->|1. Commit Desired State| ArgoServer
    ArgoServer -->|2. Reconcile Manifests| APIServer
    ACR -->|3. Digest-Verified Image Pull| WorkloadPool
    OIDC -.->|Federated Auth| Workloads
    KEDA -->|4. Scale Pods on Event Lag| Workloads
    Workloads -->|5. Telemetry & Metrics| Prom
    Prom --> Graf
    APIServer --> SystemPool
    APIServer --> WorkloadPool
""",

    "2_apexinspect_ai": """flowchart TD
    %% Styling Definitions
    classDef edge fill:#1e293b,stroke:#38bdf8,stroke-width:2px,color:#f8fafc;
    classDef ai fill:#1e1b4b,stroke:#a855f7,stroke-width:2px,color:#f8fafc;
    classDef hitl fill:#451a03,stroke:#fb923c,stroke-width:2px,color:#f8fafc;
    classDef plc fill:#064e3b,stroke:#34d399,stroke-width:2px,color:#f8fafc;
    classDef db fill:#0f172a,stroke:#94a3b8,stroke-width:2px,color:#f8fafc;

    subgraph EdgeVision["1. Edge Industrial Vision (Assembly Line)"]
        direction TB
        CAM["4K RTSP Industrial Camera"]:::edge
        SAHI["SAHI High-Res Micro-Patching (OpenCV)"]:::edge
        ONNX["ONNX Runtime YOLOv8n (43.36ms / 23.1 FPS Apple Silicon)"]:::edge
    end

    subgraph AgenticCore["2. FastAPI Service & LangGraph SOP Reasoner (:8000)"]
        direction TB
        API["FastAPI Inspection Service (API_KEY Auth)"]:::ai
        Graph["LangGraph StateGraph (Frame Defect State)"]:::ai
        BM25["BM25 Knowledge Retrieval (SOP-001..006 IPC-A-610)"]:::ai
    end

    subgraph HITLTier["3. Human-in-the-Loop Operator Console (:8501)"]
        direction TB
        UI["Streamlit / Render Web Dashboard"]:::hitl
        Overlay["Defect Bounding Box & Risk Grading"]:::hitl
        Decision{"Operator Decision Gate (Approve / Reject)"}:::hitl
    end

    subgraph OTIntegration["4. OT Hardware Control & MES Integration"]
        direction TB
        Modbus["Modbus TCP Bridge :502 (Simulation Safe Guard)"]:::plc
        PLC["PLC Hardware / Diverter Coil 0x00001"]:::plc
        MES["Factory MES Sync (POST /api/v1/mes/action)"]:::plc
        AuditDB[("Neon PostgreSQL: Append-Only AuditLog")]:::db
    end

    %% Dataflow
    CAM -->|1. RTSP Stream| SAHI
    SAHI -->|2. High-Res Patches| ONNX
    ONNX -->|3. Defect Coordinates & Confidence| API
    API --> Graph
    Graph -->|4. Retrieve IPC-A-610 Standard| BM25
    BM25 -->|5. Diagnostic Rules| Graph
    Graph -->|6. High-Risk HITL Interrupt| UI
    UI --> Overlay --> Decision
    Decision -->|7. Verified Approval| Modbus
    Modbus -->|8. Write Coil| PLC
    Decision -->|9. Dispatch MES Action| MES
    Decision -->|10. Cryptographic Audit Record| AuditDB
""",

    "3_creditflow": """flowchart TD
    %% Styling Definitions
    classDef client fill:#1e293b,stroke:#38bdf8,stroke-width:2px,color:#f8fafc;
    classDef api fill:#0f172a,stroke:#818cf8,stroke-width:2px,color:#f8fafc;
    classDef ml fill:#1e1b4b,stroke:#c084fc,stroke-width:2px,color:#f8fafc;
    classDef lg fill:#451a03,stroke:#fb923c,stroke-width:2px,color:#f8fafc;
    classDef ops fill:#022c22,stroke:#34d399,stroke-width:2px,color:#f8fafc;

    subgraph ClientLayer["1. Client Layer & Ingress"]
        direction TB
        SPA["React 18 + Vite SPA (GitHub Pages)"]:::client
        ACA["Azure Container Apps Ingress (HTTPS :8080)"]:::api
    end

    subgraph APILayer["2. FastAPI Gateway & Validation"]
        direction TB
        Auth["Server-Side Auth: X-CreditFlow-API-Key"]:::api
        Pydantic["Pydantic V2 Validation (Zero-Division Guard)"]:::api
        FE["Feature Engineering (5 Derived Financial Ratios)"]:::api
        BundleCheck["Model Bundle Gate: SHA-256 Checksum Verify"]:::api
    end

    subgraph MLServing["3. Cost-Sensitive ML Scoring Engine"]
        direction TB
        Model["StandardScaler + Logistic Regression v001"]:::ml
        CostEngine["Asymmetric Cost Engine (Cost = 5·FN + 1·FP)"]:::ml
        Threshold["Optimal Threshold t = 0.20 (Recall 0.80)"]:::ml
    end

    subgraph Orchestration["4. LangGraph 12-Node Decision Workflow"]
        direction TB
        StateGraph["LangGraph Workflow (Load → Validate → Risk → Decision)"]:::lg
        HITLGate{"Risk Decision Gate"}:::lg
        Interrupt["REVIEW (0.20 <= P < 0.50): HITL Interrupt"]:::lg
        Officer["Credit Officer Approval (Idempotency Key)"]:::lg
        Contract["Execute: Issue Contract HDTD-YYYYMMDD-XXXX"]:::lg
    end

    subgraph AuditMLOps["5. Tamper-Evident Ledger & MLOps Governance"]
        direction TB
        Ledger[("Immutable Ledger: SHA-256 Snapshot Hash")]:::ops
        Drift["PSI Drift Detector (Population Stability Index)"]:::ops
        MLflow["Self-Hosted MLflow Tracking Registry"]:::ops
    end

    %% Flow
    SPA -->|1. Submit Loan Application| ACA
    ACA --> Auth --> Pydantic --> FE --> BundleCheck
    BundleCheck -->|2. In-Memory Inference| Model
    Model --> CostEngine --> Threshold
    Threshold -->|3. Risk State & Features| StateGraph
    StateGraph --> HITLGate
    HITLGate -->|P < 0.20| Contract
    HITLGate -->|0.20 <= P < 0.50| Interrupt
    Interrupt -->|4. Manual Review| Officer
    Officer -->|5. Resume Graph| Contract
    HITLGate -->|P >= 0.50| Reject["Reject Application"]:::lg
    Contract -->|6. Append Record| Ledger
    Reject -->|6. Append Record| Ledger
    Ledger -.->|Daily Feature Stats| Drift
    FE -.->|Logged Parameters| MLflow
""",

    "4_flashsale_backend": """flowchart TD
    %% Styling Definitions
    classDef client fill:#1e293b,stroke:#38bdf8,stroke-width:2px,color:#f8fafc;
    classDef api fill:#0f172a,stroke:#818cf8,stroke-width:2px,color:#f8fafc;
    classDef cache fill:#451a03,stroke:#fb923c,stroke-width:2px,color:#f8fafc;
    classDef queue fill:#1e1b4b,stroke:#a855f7,stroke-width:2px,color:#f8fafc;
    classDef worker fill:#022c22,stroke:#34d399,stroke-width:2px,color:#f8fafc;
    classDef db fill:#064e3b,stroke:#10b981,stroke-width:2px,color:#f8fafc;
    classDef obs fill:#311042,stroke:#f472b6,stroke-width:2px,color:#f8fafc;

    subgraph BuyerTraffic["1. High-Concurrency Traffic (k6 Benchmark)"]
        Buyers["50 Concurrent Buyers Spiking on 10 Stock Items"]:::client
    end

    subgraph WebAPILayer["2. ASP.NET Core 10 Web API (:5099)"]
        direction TB
        IdempMiddleware["Idempotency Middleware (Key Deduplication)"]:::api
        OrderApi["Order.Api (Clean Architecture Controller)"]:::api
    end

    subgraph FastFailCache["3. In-Memory Atomic CAS Pre-Filter"]
        direction TB
        Redis["Redis 7 (Lua Script: EVALSHA atomic_decr.lua)"]:::cache
        Conflict["409 CONFLICT: Fast-Fail in <30ms (0 DB Load)"]:::cache
    end

    subgraph MessageQueue["4. Asynchronous Event Decoupling"]
        RabbitMQ["RabbitMQ 3.12 (AMQP Exchange order.events)"]:::queue
        Accepted["202 ACCEPTED (Async Order Acknowledged)"]:::api
    end

    subgraph BackgroundWorker["5. Durable Background Worker Daemon"]
        direction TB
        Worker["Order.Worker Daemon (At-Least-Once Consumer)"]:::worker
        TimeoutScan["Payment Timeout Scanner (15 min Auto-Cancel & Refund)"]:::worker
    end

    subgraph DatabaseTier["6. Relational Database (Single Source of Truth)"]
        direction TB
        Postgres[("PostgreSQL 16 FlashSaleDb")]:::db
        AtomicGuard["Conditional UPDATE: stock = stock - 1 WHERE stock >= 1"]:::db
    end

    subgraph SREObservability["7. SRE Observability & Metrics"]
        direction TB
        Metrics["Prometheus Scrape (:5000/metrics)"]:::obs
        Grafana["Grafana Dashboards (Throughput · Pool Cap 80/100 · Latency)"]:::obs
    end

    %% Flows
    Buyers -->|1. POST /api/orders + Idempotency-Key| IdempMiddleware
    IdempMiddleware --> OrderApi
    OrderApi -->|2. Atomic 1-RTT Decrement| Redis
    Redis -->|Stock == 0| Conflict
    Redis -->|Stock > 0| RabbitMQ
    RabbitMQ --> Accepted --> Buyers
    RabbitMQ -->|3. Consume Order Message| Worker
    Worker -->|4. Persist EF Core Entity| AtomicGuard
    AtomicGuard --> Postgres
    TimeoutScan -.->|Release Cancelled Stock| Redis
    OrderApi -.-> Metrics
    Worker -.-> Metrics
    Metrics --> Grafana
""",

    "5_minierp_manufacturing_warehouse": """flowchart TD
    %% Styling Definitions
    classDef ui fill:#1e293b,stroke:#38bdf8,stroke-width:2px,color:#f8fafc;
    classDef api fill:#0f172a,stroke:#818cf8,stroke-width:2px,color:#f8fafc;
    classDef plsql fill:#064e3b,stroke:#34d399,stroke-width:2px,color:#f8fafc;
    classDef failsoft fill:#451a03,stroke:#fb923c,stroke-width:2px,color:#f8fafc;

    subgraph ClientShopFloor["1. Shop Floor & Warehouse Terminals"]
        direction TB
        Dashboard["Web Operations Dashboard (Real-time View)"]:::ui
        Scanner["Handheld WMS Scanner (Zebra Label Integration)"]:::ui
        Workstation["Manufacturing Assembly Workstation"]:::ui
    end

    subgraph APITier["2. ASP.NET Core 8 Web API & Security"]
        direction TB
        JWTAuth["JWT Authentication + PBKDF2 Password Hashing"]:::api
        RBAC["RBAC Engine (Operator · Warehouse · Planner · Admin)"]:::api
        Dapper["Dapper ORM (High-Throughput SQL Execution)"]:::api
        IdempGuard["Anti-Duplicate Barcode Scanning Guard"]:::api
    end

    subgraph OracleCore["3. Oracle Database 23c PL/SQL Engine (31 Tables 3NF)"]
        direction TB
        subgraph Operations["ERP_OPERATIONS Package"]
            Inv["Inventory & Material Master"]:::plsql
            BOM["BOM (Bill of Materials)"]:::plsql
            PO_WO["PO & Production Work Orders"]:::plsql
        end
        subgraph Automation["ERP_AUTOMATION Package"]
            AvailCheck["Material Availability Check"]:::plsql
            AutoReserve["Auto Stock Reservation"]:::plsql
            ThresholdAlert["Minimum Stock Safety Alert"]:::plsql
        end
        subgraph Traceability["ERP_TRACEABILITY Package"]
            LotBin["Lot & Bin Management"]:::plsql
            FEFO["FEFO Rule (First Expired First Out)"]:::plsql
            Genealogy["Bi-Directional Genealogy (Raw Material ↔ Finished Good)"]:::plsql
        end
    end

    subgraph FailSoftIntegration["4. Fail-Soft Incident & Event Outbox"]
        direction TB
        AutonomousLog["Autonomous Transaction Error Logger (Independent Audit)"]:::failsoft
        HelpdeskOutbox["Helpdesk Outbox Table (Fail-Soft Retry Queue)"]:::failsoft
        ITSM["Enterprise IT Helpdesk Integration"]:::failsoft
    end

    %% Flows
    Scanner -->|1. Barcode Scan Events| IdempGuard
    Dashboard --> JWTAuth
    Workstation --> JWTAuth
    IdempGuard --> Dapper
    JWTAuth --> RBAC --> Dapper
    Dapper -->|2. Atomic PL/SQL Execution| Operations
    Operations --> Automation
    Operations --> Traceability
    Operations -.->|3. On Business Exception| AutonomousLog
    Operations -.->|4. Emit Incident Event| HelpdeskOutbox
    HelpdeskOutbox -->|5. Background Retry| ITSM
""",

    "6_smart_document_chatbot": """flowchart TD
    %% Styling Definitions
    classDef client fill:#1e293b,stroke:#38bdf8,stroke-width:2px,color:#f8fafc;
    classDef gw fill:#0f172a,stroke:#818cf8,stroke-width:2px,color:#f8fafc;
    classDef store fill:#064e3b,stroke:#34d399,stroke-width:2px,color:#f8fafc;
    classDef ai fill:#1e1b4b,stroke:#c084fc,stroke-width:2px,color:#f8fafc;
    classDef obs fill:#311042,stroke:#f472b6,stroke-width:2px,color:#f8fafc;

    subgraph ClientPresentation["1. Client Presentation (SPA)"]
        SPA["React 18 + TypeScript SPA (Cloudflare Pages)"]:::client
        SSEClient["SSE Progressive Token Renderer"]:::client
    end

    subgraph EnterpriseGateway["2. Spring Boot 3.2 Gateway (:8080 Java 17)"]
        direction TB
        CookieAuth["HttpOnly SameSite Cookie Authentication"]:::gw
        Guardrails["Rate Limiting & Prompt Injection Guard"]:::gw
        MDC["SLF4J MDC Logging (X-Trace-Id · jobId)"]:::gw
        SSEManager["SseStreamManager (Event Deduplication)"]:::gw
    end

    subgraph DurableStorage["3. Durable Storage & Asynchronous Queue"]
        direction TB
        R2["Cloudflare R2 Object Storage (Document Blobs)"]:::store
        NeonDB[("Neon PostgreSQL: Job Queue (FOR UPDATE SKIP LOCKED)")]:::store
    end

    subgraph AIService["4. Python FastAPI & LangGraph RAG Agent (:8000)"]
        direction TB
        Qdrant["Qdrant Cloud (Dense HNSW Cosine Vectors)"]:::ai
        BM25["BM25 Lexical Keyword Search"]:::ai
        RRF["Reciprocal Rank Fusion (RRF Hybrid Search)"]:::ai
        subgraph CRAG["Corrective RAG (CRAG 5-Layer Pipeline)"]
            Grade["1. Relevance Grading"]:::ai
            Refine["2. Knowledge Refinement"]:::ai
            Hallucination["3. Hallucination Guard (Groundedness Check)"]:::ai
            AnswerGrade["4. Answer Grading / Abstain Action"]:::ai
        end
        LLM["Ollama Local-First (:11434) / Cloudflare Workers AI"]:::ai
    end

    subgraph Observability["5. Observability & Tracing"]
        Langfuse["Langfuse Cloud (Token Count · USD Cost · TTFT Span)"]:::obs
    end

    %% Flows - Document Ingestion
    SPA -->|1. Upload Doc| CookieAuth
    CookieAuth -->|2. Store Binary| R2
    CookieAuth -->|3. Enqueue Job| NeonDB
    NeonDB -->|4. Worker Claim - SKIP LOCKED| AIService
    AIService -->|5. Chunk & Embed| Qdrant

    %% Flows - Chat Q&A
    SPA -->|6. POST /chat/stream| CookieAuth
    CookieAuth --> Guardrails --> MDC --> SSEManager
    SSEManager -->|7. Forward Query| RRF
    Qdrant --> RRF
    BM25 --> RRF
    RRF --> Grade --> Refine --> Hallucination --> AnswerGrade
    AnswerGrade -->|8. Generate Tokens| LLM
    LLM -->|9. Token Stream| SSEManager
    SSEManager -->|10. SSE: status → metadata → chunk → complete| SSEClient
    AIService -.->|Tracing Spans| Langfuse
""",

    "7_zero_hallucination_agent": """flowchart TD
    %% Styling Definitions
    classDef client fill:#1e293b,stroke:#38bdf8,stroke-width:2px,color:#f8fafc;
    classDef router fill:#0f172a,stroke:#818cf8,stroke-width:2px,color:#f8fafc;
    classDef rag fill:#064e3b,stroke:#34d399,stroke-width:2px,color:#f8fafc;
    classDef agent fill:#1e1b4b,stroke:#c084fc,stroke-width:2px,color:#f8fafc;
    classDef mcp fill:#451a03,stroke:#fb923c,stroke-width:2px,color:#f8fafc;

    subgraph ClientConsole["1. Enterprise Client & Memory"]
        Console["Streamlit Console / REST API (Azure Container Apps)"]:::client
        Memory[("Long-Term User Memory & Facts (SQLite)")]:::client
    end

    subgraph APIRouter["2. FastAPI Service & Intent Router"]
        direction TB
        PIIGuard["2-Layer PII Scanning Guard (Ingestion + Output)"]:::router
        Router{"Multi-Intent Router"}:::router
        Chitchat["Direct LLM Answer (Chitchat / Greeting)"]:::router
    end

    subgraph GroundedRAG["3. Grounded Hybrid RAG & Evidence Gate"]
        direction TB
        Hybrid["Dense (Qdrant) + Sparse (BM25) via RRF"]:::rag
        EvidenceGate{"Evidence Gate: Similarity >= 0.3"}:::rag
        Abstain["Refuse to Answer (Measured Abstention)"]:::rag
        Citation["Citation Projection [S1], [S2] (File, Section, Snippet)"]:::rag
    end

    subgraph LangGraphHITL["4. LangGraph StateGraph & Action Execution"]
        direction TB
        StateGraph["LangGraph StateGraph (Slot Filling & Action Plan)"]:::agent
        PostgresSaver[("PostgreSQL Durable Checkpointer (PostgresSaver)")]:::agent
        Interrupt["HITL Interrupt Gate: Side-Effect Action Requires Approval"]:::agent
        Resume["Resume Action: POST /agent/chat (Approved)"]:::agent
    end

    subgraph MCPIntegrations["5. Model Context Protocol (MCP) & ITSM Outbox"]
        direction TB
        MCP["MCP JSON-RPC 2.0 Server (Tools Registry)"]:::mcp
        Jira["Jira ITSM Ticket Creation"]:::mcp
        Teams["MS Teams / Email Notifications"]:::mcp
        OutboxWorker["Background Outbox Publisher & Audit Trail"]:::mcp
    end

    %% Flows
    Console --> PIIGuard --> Router
    Router -->|1. Casual Query| Chitchat
    Router -->|2. Policy Question| Hybrid
    Router -->|3. Action Request| StateGraph
    Hybrid --> EvidenceGate
    EvidenceGate -->|No Evidence| Abstain
    EvidenceGate -->|Evidence Found| Citation --> Console
    StateGraph --> PostgresSaver
    StateGraph --> Interrupt
    Interrupt -->|4. Awaiting Human Sign-off| Console
    Console -->|5. Operator Confirms| Resume
    Resume --> MCP
    MCP --> Jira
    MCP --> Teams
    MCP --> OutboxWorker
"""
}

def generate():
    for name, mmd_content in DIAGRAMS.items():
        mmd_file = OUTPUT_DIR / f"{name}.mmd"
        png_file = OUTPUT_DIR / f"{name}.png"
        svg_file = OUTPUT_DIR / f"{name}.svg"
        
        with open(mmd_file, "w") as f:
            f.write(mmd_content)
            
        print(f"[*] Rendering {name}...")
        
        # Render PNG
        cmd_png = [
            "/opt/homebrew/bin/mmdc",
            "-p", PUPPETEER_CONFIG,
            "-i", str(mmd_file),
            "-o", str(png_file),
            "-w", "2000",
            "-b", "#0b0f19"
        ]
        res_png = subprocess.run(cmd_png, capture_output=True, text=True)
        if res_png.returncode != 0:
            print(f"[-] Error rendering PNG for {name}: {res_png.stderr}")
        else:
            print(f"[+] Saved PNG: {png_file}")

        # Render SVG
        cmd_svg = [
            "/opt/homebrew/bin/mmdc",
            "-p", PUPPETEER_CONFIG,
            "-i", str(mmd_file),
            "-o", str(svg_file),
            "-b", "#0b0f19"
        ]
        res_svg = subprocess.run(cmd_svg, capture_output=True, text=True)
        if res_svg.returncode != 0:
            print(f"[-] Error rendering SVG for {name}: {res_svg.stderr}")
        else:
            print(f"[+] Saved SVG: {svg_file}")

if __name__ == "__main__":
    generate()
