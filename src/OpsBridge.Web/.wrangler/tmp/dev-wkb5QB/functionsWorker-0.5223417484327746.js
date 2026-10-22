var __defProp = Object.defineProperty;
var __name = (target, value) => __defProp(target, "name", { value, configurable: true });

// .wrangler/tmp/pages-cmfAoS/functionsWorker-0.5223417484327746.mjs
var __defProp2 = Object.defineProperty;
var __name2 = /* @__PURE__ */ __name((target, value) => __defProp2(target, "name", { value, configurable: true }), "__name");
var onRequest = /* @__PURE__ */ __name2(async (context) => {
  const url = new URL(context.request.url);
  if (context.request.method === "OPTIONS") {
    return new Response(null, {
      status: 204,
      headers: {
        "Access-Control-Allow-Origin": url.origin,
        "Access-Control-Allow-Methods": "GET, POST, PUT, DELETE, PATCH, OPTIONS",
        "Access-Control-Allow-Headers": "Content-Type, Authorization",
        "Access-Control-Allow-Credentials": "true"
      }
    });
  }
  if (context.env.API_BACKEND_URL && !context.env.API_BACKEND_URL.includes("127.0.0.1")) {
    const subPath2 = url.pathname.replace(/^\/api\/v1/, "");
    const target = `${context.env.API_BACKEND_URL.replace(/\/$/, "")}/api/v1${subPath2}${url.search}`;
    try {
      const resp = await fetch(target, {
        method: context.request.method,
        headers: context.request.headers,
        body: ["GET", "HEAD"].includes(context.request.method) ? void 0 : context.request.body
      });
      return resp;
    } catch (err) {
      const isExplicitDemo = context.env.DEMO_MODE === "true" || url.searchParams.get("demo") === "true";
      if (!isExplicitDemo) {
        return new Response(
          JSON.stringify({
            error: "UpstreamBackendUnavailable",
            message: `Kh\xF4ng th\u1EC3 k\u1EBFt n\u1ED1i t\u1EDBi backend t\u1EA1i ${context.env.API_BACKEND_URL}. D\u1ECBch v\u1EE5 t\u1EA1m th\u1EDDi kh\xF4ng kh\u1EA3 d\u1EE5ng.`,
            detail: String(err?.message || err)
          }),
          {
            status: 503,
            headers: {
              "Content-Type": "application/json",
              "Access-Control-Allow-Origin": url.origin,
              "Access-Control-Allow-Credentials": "true"
            }
          }
        );
      }
    }
  }
  const subPath = url.pathname.replace(/^\/api\/v1/, "");
  const jsonHeaders = {
    "Content-Type": "application/json",
    "Access-Control-Allow-Origin": url.origin,
    "Access-Control-Allow-Credentials": "true",
    "X-OpsBridge-Sandbox": "true",
    "X-OpsBridge-Mode": "sandbox"
  };
  const replayCookie = context.request.headers.get("Cookie")?.match(/(?:^|;\s*)opsbridge_demo_replayed_at=([0-9]+)/);
  const replayTimestamp = replayCookie ? Number(replayCookie[1]) : NaN;
  const demoReplayAt = Number.isFinite(replayTimestamp) && replayTimestamp > 0 && replayTimestamp <= Date.now() ? new Date(replayTimestamp).toISOString() : void 0;
  const purchaseOrders = [
    {
      id: "po-1001",
      orderNumber: "PO-2026-1001",
      supplierCode: "SUP-BAO-LOC-TEA",
      supplierName: "HTX Tr\xE0 Cao Nguy\xEAn B\u1EA3o L\u1ED9c",
      currency: "VND",
      orderedAt: new Date(Date.now() - 6048e5).toISOString(),
      status: "Fulfilled",
      sourceSystem: "SAP_S4HANA",
      totalAmount: 15e6,
      lines: [
        {
          id: "pol-01",
          lineNumber: 1,
          itemSku: "SKU-TEA-OOLONG-01",
          description: "Tr\xE0 Oolong Th\u01B0\u1EE3ng H\u1EA1ng (Bao 50kg)",
          orderedQuantity: 100,
          unitOfMeasure: "BAO",
          unitPrice: 15e4,
          totalAmount: 15e6
        }
      ]
    },
    {
      id: "po-1002",
      orderNumber: "PO-2026-1002",
      supplierCode: "SUP-VINAMILK-ENT",
      supplierName: "C\xF4ng ty C\u1ED5 ph\u1EA7n S\u1EEFa Vi\u1EC7t Nam (Vinamilk B2B)",
      currency: "VND",
      orderedAt: new Date(Date.now() - 5184e5).toISOString(),
      status: "Fulfilled",
      sourceSystem: "ORACLE_NETSUITE",
      totalAmount: 14e6,
      lines: [
        {
          id: "pol-02",
          lineNumber: 1,
          itemSku: "SKU-MILK-FRESH-1L",
          description: "S\u1EEFa T\u01B0\u01A1i Thanh Tr\xF9ng 100% (Th\xF9ng 12 H\u1ED9p 1L)",
          orderedQuantity: 500,
          unitOfMeasure: "THUNG",
          unitPrice: 28e3,
          totalAmount: 14e6
        }
      ]
    },
    {
      id: "po-1003",
      orderNumber: "PO-2026-1003",
      supplierCode: "SUP-TAIWAN-BOBA",
      supplierName: "C\xF4ng ty TNHH Nguy\xEAn Li\u1EC7u Tr\xE2n Ch\xE2u \u0110\xE0i Loan",
      currency: "VND",
      orderedAt: new Date(Date.now() - 432e6).toISOString(),
      status: "PartiallyFulfilled",
      sourceSystem: "SAP_S4HANA",
      totalAmount: 6e7,
      lines: [
        {
          id: "pol-03",
          lineNumber: 1,
          itemSku: "SKU-BOBA-BLACK-20K",
          description: "Tr\xE2n Ch\xE2u \u0110en D\u1EBBo Cao C\u1EA5p (Bao 20kg)",
          orderedQuantity: 200,
          unitOfMeasure: "BAO",
          unitPrice: 3e5,
          totalAmount: 6e7
        }
      ]
    },
    {
      id: "po-1004",
      orderNumber: "PO-2026-1004",
      supplierCode: "SUP-SAIGON-LOGISTICS",
      supplierName: "Cty TNHH Ti\u1EBFp V\u1EADn & Bao B\xEC S\xE0i G\xF2n",
      currency: "VND",
      orderedAt: new Date(Date.now() - 3456e5).toISOString(),
      status: "Fulfilled",
      sourceSystem: "MICROSOFT_DYNAMICS",
      totalAmount: 45e6,
      lines: [
        {
          id: "pol-04",
          lineNumber: 1,
          itemSku: "SKU-FILM-SEAL-700",
          description: "M\xE0ng \xC9p Mi\u1EC7ng Ly Nh\u1EF1a In Logo (Cu\u1ED9n 2,500 n\u1EAFp)",
          orderedQuantity: 1e3,
          unitOfMeasure: "CUON",
          unitPrice: 45e3,
          totalAmount: 45e6
        }
      ]
    },
    {
      id: "po-1005",
      orderNumber: "PO-2026-1005",
      supplierCode: "SUP-DAIKIN-COLD",
      supplierName: "Daikin Cold Chain Equipment Vietnam",
      currency: "VND",
      orderedAt: new Date(Date.now() - 2592e5).toISOString(),
      status: "Issued",
      sourceSystem: "ORACLE_NETSUITE",
      totalAmount: 185e5,
      lines: [
        {
          id: "pol-05",
          lineNumber: 1,
          itemSku: "SKU-FILTER-COLD-01",
          description: "B\u1ED9 L\u1ECDc Kh\xF4ng Kh\xED & Kh\u1EED Khu\u1EA9n Kho L\u1EA1nh",
          orderedQuantity: 10,
          unitOfMeasure: "SET",
          unitPrice: 185e4,
          totalAmount: 185e5
        }
      ]
    }
  ];
  const goodsReceipts = [
    {
      id: "gr-5001",
      receiptNumber: "GR-2026-5001",
      purchaseOrderNumber: "PO-2026-1001",
      supplierCode: "SUP-BAO-LOC-TEA",
      receivedAt: new Date(Date.now() - 3456e5).toISOString(),
      status: "Verified",
      warehouseCode: "WH-HCM-CENTRAL",
      sourceSystem: "WMS_MANHATTAN",
      lines: [
        {
          id: "grl-01",
          lineNumber: 1,
          itemSku: "SKU-TEA-OOLONG-01",
          receivedQuantity: 100,
          unitOfMeasure: "BAO",
          allocatedQuantity: 100,
          availableQuantity: 0
        }
      ]
    },
    {
      id: "gr-5002",
      receiptNumber: "GR-2026-5002",
      purchaseOrderNumber: "PO-2026-1002",
      supplierCode: "SUP-VINAMILK-ENT",
      receivedAt: new Date(Date.now() - 2592e5).toISOString(),
      status: "Verified",
      warehouseCode: "WH-HCM-COLD-02",
      sourceSystem: "WMS_MANHATTAN",
      lines: [
        {
          id: "grl-02",
          lineNumber: 1,
          itemSku: "SKU-MILK-FRESH-1L",
          receivedQuantity: 500,
          unitOfMeasure: "THUNG",
          allocatedQuantity: 500,
          availableQuantity: 0
        }
      ]
    },
    {
      id: "gr-5003",
      receiptNumber: "GR-2026-5003",
      purchaseOrderNumber: "PO-2026-1003",
      supplierCode: "SUP-TAIWAN-BOBA",
      receivedAt: new Date(Date.now() - 1728e5).toISOString(),
      status: "DamagedReturnRecorded",
      warehouseCode: "WH-HCM-CENTRAL",
      sourceSystem: "WMS_MANHATTAN",
      lines: [
        {
          id: "grl-03",
          lineNumber: 1,
          itemSku: "SKU-BOBA-BLACK-20K",
          receivedQuantity: 160,
          // Shortage: 40 bags damaged during container transport
          unitOfMeasure: "BAO",
          allocatedQuantity: 160,
          availableQuantity: 0
        }
      ]
    },
    {
      id: "gr-5004",
      receiptNumber: "GR-2026-5004",
      purchaseOrderNumber: "PO-2026-1004",
      supplierCode: "SUP-SAIGON-LOGISTICS",
      receivedAt: new Date(Date.now() - 864e5).toISOString(),
      status: "Verified",
      warehouseCode: "WH-BINH-DUONG-HUB",
      sourceSystem: "WMS_MANHATTAN",
      lines: [
        {
          id: "grl-04",
          lineNumber: 1,
          itemSku: "SKU-FILM-SEAL-700",
          receivedQuantity: 1e3,
          unitOfMeasure: "CUON",
          allocatedQuantity: 1e3,
          availableQuantity: 0
        }
      ]
    }
  ];
  const invoices = [
    {
      id: "inv-8801",
      invoiceNumber: "INV-2026-8801",
      purchaseOrderNumber: "PO-2026-1001",
      supplierCode: "SUP-BAO-LOC-TEA",
      supplierName: "HTX Tr\xE0 Cao Nguy\xEAn B\u1EA3o L\u1ED9c",
      currency: "VND",
      invoiceDate: new Date(Date.now() - 2592e5).toISOString(),
      status: "Matched",
      subTotal: 15e6,
      taxAmount: 15e5,
      discountAmount: 0,
      additionalCharges: 0,
      totalAmount: 165e5,
      sourceSystem: "EINVOICE_VNPT",
      lines: [
        {
          id: "invl-01",
          lineNumber: 1,
          itemSku: "SKU-TEA-OOLONG-01",
          description: "Tr\xE0 Oolong Th\u01B0\u1EE3ng H\u1EA1ng (Bao 50kg)",
          invoicedQuantity: 100,
          unitOfMeasure: "BAO",
          unitPrice: 15e4,
          taxRate: 0.1,
          lineTotal: 15e6,
          matchedQuantity: 100
        }
      ]
    },
    {
      id: "inv-8802",
      invoiceNumber: "INV-2026-8802",
      purchaseOrderNumber: "PO-2026-1002",
      supplierCode: "SUP-VINAMILK-ENT",
      supplierName: "C\xF4ng ty C\u1ED5 ph\u1EA7n S\u1EEFa Vi\u1EC7t Nam (Vinamilk B2B)",
      currency: "VND",
      invoiceDate: new Date(Date.now() - 1728e5).toISOString(),
      status: "ApprovedWithVariance",
      subTotal: 141e5,
      // +100,000 VND fuel surcharge on cold chain
      taxAmount: 141e4,
      discountAmount: 0,
      additionalCharges: 1e5,
      totalAmount: 1551e4,
      sourceSystem: "EINVOICE_VIETTEL",
      lines: [
        {
          id: "invl-02",
          lineNumber: 1,
          itemSku: "SKU-MILK-FRESH-1L",
          description: "S\u1EEFa T\u01B0\u01A1i Thanh Tr\xF9ng 100% (Th\xF9ng 12 H\u1ED9p 1L)",
          invoicedQuantity: 500,
          unitOfMeasure: "THUNG",
          unitPrice: 28200,
          taxRate: 0.1,
          lineTotal: 141e5,
          matchedQuantity: 500
        }
      ]
    },
    {
      id: "inv-8803",
      invoiceNumber: "INV-2026-8803",
      purchaseOrderNumber: "PO-2026-1003",
      supplierCode: "SUP-TAIWAN-BOBA",
      supplierName: "C\xF4ng ty TNHH Nguy\xEAn Li\u1EC7u Tr\xE2n Ch\xE2u \u0110\xE0i Loan",
      currency: "VND",
      invoiceDate: new Date(Date.now() - 864e5).toISOString(),
      status: "Discrepancy",
      subTotal: 6e7,
      // Billed for 200 bags, but GR only accepted 160 bags
      taxAmount: 6e6,
      discountAmount: 0,
      additionalCharges: 0,
      totalAmount: 66e6,
      sourceSystem: "EINVOICE_MISA",
      lines: [
        {
          id: "invl-03",
          lineNumber: 1,
          itemSku: "SKU-BOBA-BLACK-20K",
          description: "Tr\xE2n Ch\xE2u \u0110en D\u1EBBo Cao C\u1EA5p (Bao 20kg)",
          invoicedQuantity: 200,
          unitOfMeasure: "BAO",
          unitPrice: 3e5,
          taxRate: 0.1,
          lineTotal: 6e7,
          matchedQuantity: 160
        }
      ]
    },
    {
      id: "inv-8804",
      invoiceNumber: "INV-2026-8804",
      purchaseOrderNumber: "PO-2026-1004",
      supplierCode: "SUP-SAIGON-LOGISTICS",
      supplierName: "Cty TNHH Ti\u1EBFp V\u1EADn & Bao B\xEC S\xE0i G\xF2n",
      currency: "VND",
      invoiceDate: new Date(Date.now() - 432e5).toISOString(),
      status: "Discrepancy",
      subTotal: 52e6,
      // Supplier arbitrarily raised unit price from 45,000 to 52,000 (+15.5%)
      taxAmount: 52e5,
      discountAmount: 0,
      additionalCharges: 0,
      totalAmount: 572e5,
      sourceSystem: "EINVOICE_VNPT",
      lines: [
        {
          id: "invl-04",
          lineNumber: 1,
          itemSku: "SKU-FILM-SEAL-700",
          description: "M\xE0ng \xC9p Mi\u1EC7ng Ly Nh\u1EF1a In Logo (Cu\u1ED9n 2,500 n\u1EAFp)",
          invoicedQuantity: 1e3,
          unitOfMeasure: "CUON",
          unitPrice: 52e3,
          taxRate: 0.1,
          lineTotal: 52e6,
          matchedQuantity: 1e3
        }
      ]
    },
    {
      id: "inv-8805",
      invoiceNumber: "INV-2026-8805",
      purchaseOrderNumber: "PO-2026-1005",
      supplierCode: "SUP-DAIKIN-COLD",
      supplierName: "Daikin Cold Chain Equipment Vietnam",
      currency: "VND",
      invoiceDate: new Date(Date.now() - 144e5).toISOString(),
      status: "PendingReconciliation",
      subTotal: 185e5,
      taxAmount: 185e4,
      discountAmount: 0,
      additionalCharges: 0,
      totalAmount: 2035e4,
      sourceSystem: "EINVOICE_VIETTEL",
      lines: [
        {
          id: "invl-05",
          lineNumber: 1,
          itemSku: "SKU-FILTER-COLD-01",
          description: "B\u1ED9 L\u1ECDc Kh\xF4ng Kh\xED & Kh\u1EED Khu\u1EA9n Kho L\u1EA1nh",
          invoicedQuantity: 10,
          unitOfMeasure: "SET",
          unitPrice: 185e4,
          taxRate: 0.1,
          lineTotal: 185e5,
          matchedQuantity: 0
        }
      ]
    }
  ];
  const discrepancies = [
    {
      id: "disc-01",
      reconciliationResultId: "recon-8803",
      type: "QuantityMismatch",
      severity: "High",
      itemSku: "SKU-BOBA-BLACK-20K",
      expectedValue: "160 BAO (Kho th\u1EF1c nh\u1EADn t\u1EA1i GR-2026-5003)",
      actualValue: "200 BAO (H\xF3a \u0111\u01A1n INV-2026-8803)",
      varianceAmount: 12e6,
      status: "Open",
      details: "Thi\u1EBFu 40 bao tr\xE2n ch\xE2u do r\xE1ch r\u01B0\u1EDBi bao b\xEC khi b\u1ED1c d\u1EE1 t\u1EA1i c\u1ED5ng kho. Nh\xE0 cung c\u1EA5p c\u1EA7n xu\u1EA5t h\xF3a \u0111\u01A1n \u0111i\u1EC1u ch\u1EC9nh (Credit Note) gi\u1EA3m 12,000,000 VND."
    },
    {
      id: "disc-02",
      reconciliationResultId: "recon-8804",
      type: "PriceMismatch",
      severity: "Critical",
      itemSku: "SKU-FILM-SEAL-700",
      expectedValue: "45,000 VND / Cu\u1ED9n (PO-2026-1004)",
      actualValue: "52,000 VND / Cu\u1ED9n (INV-2026-8804)",
      varianceAmount: 7e6,
      status: "UnderReview",
      details: "Nh\xE0 cung c\u1EA5p t\u1EF1 \xFD t\u0103ng \u0111\u01A1n gi\xE1 15.5% m\xE0 kh\xF4ng c\xF3 v\u0103n b\u1EA3n ph\xEA duy\u1EC7t procurement. \u0110\xE3 t\u1EA1m kh\xF3a thanh to\xE1n \u0111\u1EC3 K\u1EBF to\xE1n tr\u01B0\u1EDFng \u0111\u1ED1i so\xE1t."
    },
    {
      id: "disc-03",
      reconciliationResultId: "recon-8802",
      type: "PriceMismatch",
      severity: "Low",
      itemSku: "SKU-MILK-FRESH-1L",
      expectedValue: "28,000 VND / Th\xF9ng",
      actualValue: "28,200 VND / Th\xF9ng",
      varianceAmount: 1e5,
      status: "ApprovedWithVariance",
      details: "Ph\u1EE5 ph\xED container l\u1EA1nh \u0111\u1EB7c th\xF9 trong \u0111\u1EE3t n\u1EAFng n\xF3ng. Ch\xEAnh l\u1EC7ch 0.71% n\u1EB1m trong h\u1EA1n m\u1EE9c dung sai cho ph\xE9p (<1.0%). \u0110\xE3 duy\u1EC7t thanh to\xE1n."
    }
  ];
  const ingestedEvents = [
    {
      id: "evt-9011",
      sourceSystem: "SAP_S4HANA",
      eventType: "PURCHASE_ORDER_ISSUED",
      entityId: "PO-2026-1005",
      payload: '{"orderNumber":"PO-2026-1005","totalAmount":18500000,"lines":1}',
      status: "Processed",
      receivedAt: new Date(Date.now() - 36e4).toISOString(),
      durationMs: 42
    },
    {
      id: "evt-9012",
      sourceSystem: "WMS_MANHATTAN",
      eventType: "GOODS_RECEIPT_POSTED",
      entityId: "GR-2026-5004",
      payload: '{"receiptNumber":"GR-2026-5004","receivedQuantity":1000}',
      status: "Processed",
      receivedAt: new Date(Date.now() - 72e4).toISOString(),
      durationMs: 38
    },
    {
      id: "evt-9013",
      sourceSystem: "EINVOICE_VIETTEL",
      eventType: "INVOICE_XML_INGESTED",
      entityId: "INV-2026-8805",
      payload: '{"invoiceNumber":"INV-2026-8805","vatRate":0.10,"totalAmount":20350000}',
      status: "Processed",
      receivedAt: new Date(Date.now() - 12e5).toISOString(),
      durationMs: 51
    },
    {
      id: "evt-9014",
      sourceSystem: "EINVOICE_VNPT",
      eventType: "INVOICE_XML_INGESTED",
      entityId: "INV-2026-8804",
      payload: '{"invoiceNumber":"INV-2026-8804","unitPrice":52000}',
      status: "FlaggedDiscrepancy",
      receivedAt: new Date(Date.now() - 18e5).toISOString(),
      durationMs: 64
    }
  ];
  const deadLetterEvents = [
    {
      id: "dlq-001",
      eventId: "evt-dlq-corrupted-tax",
      eventType: "INVOICE_HMAC_VALIDATION_FAILED",
      topic: "ap.invoices.raw",
      payload: '{"invoiceNumber":"INV-ERR-9901","supplierCode":"SUP-UNKNOWN","checksum":"invalid_hmac_sha256"}',
      reason: "SecurityException: Invalid HMAC Signature or corrupt webhook header",
      retryCount: 3,
      failedAt: new Date(Date.now() - 144e5).toISOString(),
      isReplayed: Boolean(demoReplayAt),
      replayedAt: demoReplayAt,
      replayedBy: demoReplayAt ? "sandbox.reviewer" : void 0
    },
    {
      id: "dlq-002",
      eventId: "evt-dlq-missing-po-ref",
      eventType: "RECONCILIATION_DEPENDENCY_TIMEOUT",
      topic: "reconciliation.jobs",
      payload: '{"invoiceId":"inv-orphan-01","retryTarget":"SAP_ODATA_GATEWAY"}',
      reason: "TimeoutException: SAP OData Gateway latency > 15,000ms during peak batch",
      retryCount: 4,
      failedAt: new Date(Date.now() - 288e5).toISOString(),
      isReplayed: true,
      replayedAt: new Date(Date.now() - 36e5).toISOString(),
      replayedBy: "admin_system"
    }
  ];
  if (subPath === "/ai/chat" && context.request.method === "POST") {
    let reqBody = {};
    try {
      reqBody = await context.request.json();
    } catch {
    }
    const userMessage = (reqBody.message || reqBody.prompt || "").trim();
    const history = reqBody.history || [];
    const cfToken = reqBody.cloudflareApiToken || context.env.CLOUDFLARE_API_TOKEN;
    const cfAccount = reqBody.cloudflareAccountId || context.env.CLOUDFLARE_ACCOUNT_ID;
    const geminiKey = reqBody.geminiApiKey || context.env.GEMINI_API_KEY;
    if (context.env.AI && typeof context.env.AI.run === "function") {
      try {
        const aiMessages = [
          {
            role: "system",
            content: "B\u1EA1n l\xE0 OpsBridge AI Copilot \u2014 Chuy\xEAn gia V\u1EADn h\xE0nh Kho & K\u1EBF to\xE1n \u0110\u1ED1i So\xE1t 3 B\xEAn (PO - GR - INV). Tr\u1EA3 l\u1EDDi b\u1EB1ng ti\u1EBFng Vi\u1EC7t chuy\xEAn nghi\u1EC7p, s\xFAc t\xEDch v\xE0 ch\xEDnh x\xE1c."
          },
          ...history.map((h) => ({
            role: h.sender === "user" ? "user" : "assistant",
            content: h.text
          })),
          { role: "user", content: userMessage }
        ];
        const aiRes = await context.env.AI.run("@cf/meta/llama-3.1-8b-instruct", {
          messages: aiMessages,
          max_tokens: 1024
        });
        if (aiRes?.response) {
          return new Response(
            JSON.stringify({
              reply: aiRes.response,
              providerUsed: "Cloudflare Workers AI (@cf/meta/llama-3.1-8b-instruct)"
            }),
            { headers: jsonHeaders }
          );
        }
      } catch (err) {
        console.warn("Cloudflare Workers AI binding failed:", err);
      }
    }
    if (cfAccount && cfToken) {
      try {
        const cfResp = await fetch(
          `https://api.cloudflare.com/client/v4/accounts/${cfAccount}/ai/run/@cf/meta/llama-3.1-8b-instruct`,
          {
            method: "POST",
            headers: {
              Authorization: `Bearer ${cfToken}`,
              "Content-Type": "application/json"
            },
            body: JSON.stringify({
              messages: [
                {
                  role: "system",
                  content: "B\u1EA1n l\xE0 OpsBridge AI Copilot \u2014 Chuy\xEAn gia V\u1EADn h\xE0nh Kho & K\u1EBF to\xE1n \u0110\u1ED1i So\xE1t 3 B\xEAn (PO - GR - INV). Tr\u1EA3 l\u1EDDi b\u1EB1ng ti\u1EBFng Vi\u1EC7t chuy\xEAn nghi\u1EC7p, s\xFAc t\xEDch v\xE0 ch\xEDnh x\xE1c."
                },
                ...history.map((h) => ({
                  role: h.sender === "user" ? "user" : "assistant",
                  content: h.text
                })),
                { role: "user", content: userMessage }
              ]
            })
          }
        );
        if (cfResp.ok) {
          const cfData = await cfResp.json();
          const reply = cfData.result?.response;
          if (reply) {
            return new Response(
              JSON.stringify({
                reply,
                providerUsed: "Cloudflare Workers AI REST API"
              }),
              { headers: jsonHeaders }
            );
          }
        }
      } catch (err) {
        console.warn("Cloudflare REST AI call failed:", err);
      }
    }
    if (geminiKey) {
      try {
        const gResp = await fetch(
          `https://generativelanguage.googleapis.com/v1beta/models/gemini-2.5-flash:generateContent?key=${geminiKey}`,
          {
            method: "POST",
            headers: { "Content-Type": "application/json" },
            body: JSON.stringify({
              contents: [
                {
                  parts: [
                    {
                      text: `B\u1EA1n l\xE0 OpsBridge AI Copilot \u2014 Chuy\xEAn gia V\u1EADn h\xE0nh Kho & K\u1EBF to\xE1n \u0110\u1ED1i So\xE1t 3 B\xEAn. Tr\u1EA3 l\u1EDDi c\xE2u h\u1ECFi b\u1EB1ng ti\u1EBFng Vi\u1EC7t th\xE2n thi\u1EC7n, s\xFAc t\xEDch: "${userMessage}"`
                    }
                  ]
                }
              ]
            })
          }
        );
        if (gResp.ok) {
          const gData = await gResp.json();
          const gText = gData.candidates?.[0]?.content?.parts?.[0]?.text;
          if (gText) {
            return new Response(
              JSON.stringify({
                reply: gText,
                providerUsed: "Google Gemini 2.5 Flash"
              }),
              { headers: jsonHeaders }
            );
          }
        }
      } catch (err) {
        console.warn("Gemini API call failed:", err);
      }
    }
    const q = userMessage.toLowerCase();
    let replyText = "";
    if (/^(hi|hello|hey|chào|xin chào|chao|alo|xin chao)/i.test(q) || q === "hi" || q === "hello") {
      replyText = `\u{1F44B} **Xin ch\xE0o b\u1EA1n! T\xF4i l\xE0 OpsBridge AI Copilot** \u2014 Tr\u1EE3 l\xFD Chuy\xEAn gia \u0110\u1ED1i So\xE1t Kho & H\xF3a \u0110\u01A1n (Three-Way Matching Platform).

T\xF4i c\xF3 th\u1EC3 h\u1ED7 tr\u1EE3 b\u1EA1n gi\u1EA3i quy\u1EBFt c\xE1c b\xE0i to\xE1n sau:
1. \u{1F4CA} **Quy tr\xECnh \u0110\u1ED1i so\xE1t 3 b\xEAn:** \u0110\u1ED1i chi\u1EBFu \u0110\u01A1n mua (PO), Phi\u1EBFu nh\u1EADp kho (GR) v\xE0 H\xF3a \u0111\u01A1n NCC (INV).
2. \u{1F50D} **Ph\xE2n t\xEDch sai l\u1EC7ch h\xF3a \u0111\u01A1n:** T\xECm nguy\xEAn nh\xE2n ch\xEAnh l\u1EC7ch s\u1ED1 l\u01B0\u1EE3ng (Quantity Mismatch) ho\u1EB7c l\u1EC7ch \u0111\u01A1n gi\xE1 (Price Mismatch).
3. \u2699\uFE0F **Ch\xEDnh s\xE1ch Dung sai (Tolerance):** C\u1EA5u h\xECnh bi\xEAn \u0111\u1ED9 sai s\u1ED1 gi\xE1 v\xE0 s\u1ED1 l\u01B0\u1EE3ng cho ph\xE9p.
4. \u{1F510} **Ph\xE2n quy\u1EC1n IAM (4 Vai tr\xF2):** Gi\u1EA3i th\xEDch quy\u1EC1n xem/thao t\xE1c v\xE0 c\xE1c m\u1EE5c b\u1ECB \u1EA9n c\u1EE7a **Finance Reviewer, Operator, Administrator, Auditor**.

H\xF4m nay b\u1EA1n c\u1EA7n t\xF4i h\u1ED7 tr\u1EE3 ki\u1EC3m tra h\xF3a \u0111\u01A1n n\xE0o hay nghi\u1EC7p v\u1EE5 g\xEC?`;
    } else if (q.includes("ph\xE2n quy\u1EC1n") || q.includes("vai tr\xF2") || q.includes("role") || q.includes("iam") || q.includes("4 ng\u01B0\u1EDDi") || q.includes("\u1EA9n") || q.includes("kh\xF4ng cho xem") || q.includes("finance reviewer") || q.includes("operator") || q.includes("auditor") || q.includes("admin")) {
      replyText = `\u{1F510} **Ma Tr\u1EADn Ph\xE2n Quy\u1EC1n IAM & Ki\u1EC3m So\xE1t Hi\u1EC3n Th\u1ECB (4 Vai Tr\xF2) theo chu\u1EA9n SOX:**

| Vai Tr\xF2 | Th\u1EA9m Quy\u1EC1n \u0110\u01B0\u1EE3c Thao T\xE1c | \u26D4 **B\u1ECA \u1EA8N / KH\xD4NG CHO XEM (RESTRICTED)** |
| :--- | :--- | :--- |
| **1. Finance Reviewer** *(K\u1EBF to\xE1n / Duy\u1EC7t chi)* | \u2022 Xem Dashboard, PO, GR, INV, Reconciliation<br>\u2022 Ph\xEA duy\u1EC7t (Approve) / T\u1EEB ch\u1ED1i (Reject) c\xE1c kho\u1EA3n l\u1EC7ch h\xF3a \u0111\u01A1n<br>\u2022 Y\xEAu c\u1EA7u NCC xu\u1EA5t h\xF3a \u0111\u01A1n \u0111i\u1EC1u ch\u1EC9nh | \u{1F6AB} **\u1EA8n Menu "T\xEDch h\u1EE3p h\u1EC7 th\u1ED1ng" (\`/integrations\`):** Kh\xF4ng can thi\u1EC7p k\u1EBFt n\u1ED1i ERP, OData webhook, Replay DLQ.<br>\u{1F6AB} **\u1EA8n n\xFAt T\u1EA1o PO & T\u1EA1o GR:** Tu\xE2n th\u1EE7 nguy\xEAn t\u1EAFc SOX (Segregation of Duties) \u2014 ng\u01B0\u1EDDi duy\u1EC7t thanh to\xE1n kh\xF4ng \u0111\u01B0\u1EE3c t\u1EF1 t\u1EA1o \u0111\u01A1n mua ho\u1EB7c nh\u1EADn h\xE0ng. |
| **2. Operator** *(V\u1EADn h\xE0nh / Mua h\xE0ng / Th\u1EE7 kho)* | \u2022 T\u1EA1o \u0110\u01A1n mua PO, Import CSV<br>\u2022 L\u1EADp Phi\u1EBFu nh\u1EADp kho GR<br>\u2022 N\u1EA1p h\xF3a \u0111\u01A1n NCC (INV)<br>\u2022 Nh\u1EA5n n\xFAt **"Execute 3-Way Match"**<br>\u2022 V\u1EADn h\xE0nh Replay Dead-Letter Queue | \u{1F6AB} **\u1EA8n Menu "X\u1EED l\xFD sai l\u1EC7ch" (\`/discrepancies\`):** Operator kh\xF4ng \u0111\u01B0\u1EE3c t\u1EF1 \xFD duy\u1EC7t xu\u1EA5t ti\u1EC1n cho h\xF3a \u0111\u01A1n l\u1EC7ch.<br>\u{1F6AB} **\u1EA8n Menu "Nh\u1EADt k\xFD ki\u1EC3m to\xE1n" (\`/audit\`):** Kh\xF4ng c\xF3 quy\u1EC1n xem nh\u1EADt k\xFD compliance t\xE0i ch\xEDnh. |
| **3. Administrator** *(Qu\u1EA3n tr\u1ECB vi\xEAn h\u1EC7 th\u1ED1ng)* | \u2022 To\xE0n quy\u1EC1n (Full Access) tr\xEAn m\u1ECDi ph\xE2n h\u1EC7<br>\u2022 C\u1EA5u h\xECnh ch\xEDnh s\xE1ch dung sai (Tolerance Policy)<br>\u2022 Qu\u1EA3n tr\u1ECB k\u1EBFt n\u1ED1i SAP, Oracle, RabbitMQ, DLQ<br>\u2022 Kh\u1EAFc ph\u1EE5c s\u1EF1 c\u1ED1 kh\u1EA9n c\u1EA5p | \u26A0\uFE0F **Kh\xF4ng \u1EA9n m\xE0n h\xECnh**, nh\u01B0ng m\u1ECDi h\xE0nh vi can thi\u1EC7p v\xE0o t\xE0i ch\xEDnh \u0111\u1EC1u b\u1ECB ghi l\u1EA1i trong **Live Audit Trail** b\u1EA5t bi\u1EBFn \u0111\u1EC3 ki\u1EC3m to\xE1n \u0111\u1ED9c l\u1EADp \u0111\u1ED1i chi\u1EBFu. |
| **4. Auditor** *(Ki\u1EC3m to\xE1n vi\xEAn \u0111\u1ED9c l\u1EADp / N\u1ED9i b\u1ED9)* | \u2022 **Ch\u1EC9 \u0111\u1ECDc 100% (Strictly Read-Only)**<br>\u2022 Xem to\xE0n b\u1ED9 PO, GR, INV, \u0110\u1ED1i so\xE1t 3 b\xEAn<br>\u2022 Xem chi ti\u1EBFt l\u1ECBch s\u1EED ai \u0111\xE3 duy\u1EC7t/t\u1EEB ch\u1ED1i h\xF3a \u0111\u01A1n l\u1EC7ch<br>\u2022 Xem tr\u1ECDn v\u1EB9n Nh\u1EADt k\xFD ki\u1EC3m to\xE1n (\`/audit\`) | \u{1F6AB} **\u1EA8n Menu "T\xEDch h\u1EE3p h\u1EC7 th\u1ED1ng" (\`/integrations\`):** Kh\xF4ng tham gia v\u1EADn h\xE0nh k\u1EF9 thu\u1EADt.<br>\u{1F6AB} **\u1EA8n/Kh\xF3a to\xE0n b\u1ED9 n\xFAt Ghi/Thao t\xE1c:** \u1EA8n n\xFAt "Execute 3-Way Match", \u1EA8n n\xFAt "T\u1EA1o PO", \u1EA8n n\xFAt "T\u1EA1o GR", Kh\xF3a c\xE1c n\xFAt "Approve/Reject". Ki\u1EC3m to\xE1n \u0111\u1ED9c l\u1EADp tuy\u1EC7t \u0111\u1ED1i kh\xF4ng tr\u1EF1c ti\u1EBFp can thi\u1EC7p d\u1EEF li\u1EC7u. |`;
    } else if (q.includes("quy tr\xECnh") || q.includes("3 b\xEAn") || q.includes("3-way") || q.includes("l\xE0 g\xEC") || q.includes("\u0111\u1ED1i so\xE1t")) {
      replyText = `\u{1F4CC} **Quy tr\xECnh \u0110\u1ED1i so\xE1t 3 B\xEAn (Three-Way Matching) g\u1ED3m 4 m\u1EAFt x\xEDch:**
1. **\u0110\u01A1n mua h\xE0ng (PO - Purchase Order):** B\u1ED9 ph\u1EADn Mua h\xE0ng l\u1EADp th\u1ECFa thu\u1EADn v\u1EDBi Nh\xE0 cung c\u1EA5p (s\u1ED1 l\u01B0\u1EE3ng cam k\u1EBFt \u0111\u1EB7t mua & \u0111\u01A1n gi\xE1 h\u1EE3p \u0111\u1ED3ng).
2. **Phi\u1EBFu nh\u1EADp kho (GR - Goods Receipt):** Th\u1EE7 kho ki\u1EC3m \u0111\u1EBFm s\u1ED1 l\u01B0\u1EE3ng th\u1EF1c nh\u1EADn t\u1EA1i c\u1EEDa kho khi xe giao t\u1EDBi.
3. **H\xF3a \u0111\u01A1n NCC (INV - Supplier Invoice):** NCC g\u1EEDi h\xF3a \u0111\u01A1n t\xE0i ch\xEDnh \u0111\xF2i thanh to\xE1n.
4. **\u0110\u1ED9ng c\u01A1 \u0111\u1ED1i chi\u1EBFu t\u1EF1 \u0111\u1ED9ng (3-Way Engine):**
   - *S\u1ED1 l\u01B0\u1EE3ng (INV)* \u27F7 *S\u1ED1 l\u01B0\u1EE3ng th\u1EF1c nh\u1EADn (GR)*: Ph\xE1t hi\u1EC7n thi\u1EBFu h\u1EE5t, h\u01B0 h\u1ECFng trong giao h\xE0ng.
   - *\u0110\u01A1n gi\xE1 (INV)* \u27F7 *\u0110\u01A1n gi\xE1 th\u1ECFa thu\u1EADn (PO)*: Ng\u0103n ch\u1EB7n NCC t\u1EF1 \xFD n\xE2ng gi\xE1 tr\xE1i h\u1EE3p \u0111\u1ED3ng.
N\u1EBFu t\u1EA5t c\u1EA3 kh\u1EDBp 100% ho\u1EB7c n\u1EB1m trong **Bi\xEAn \u0111\u1ED9 dung sai (Tolerance)**, h\xF3a \u0111\u01A1n \u0111\u01B0\u1EE3c duy\u1EC7t thanh to\xE1n t\u1EF1 \u0111\u1ED9ng!`;
    } else if (q.includes("8803") || q.includes("tr\xE2n ch\xE2u \u0111\xE0i loan") || q.includes("thi\u1EBFu") && q.includes("h\xE0ng")) {
      replyText = `\u{1F50D} **Ph\xE2n t\xEDch Chuy\xEAn s\xE2u H\xF3a \u0111\u01A1n INV-2026-8803 (Thi\u1EBFu s\u1ED1 l\u01B0\u1EE3ng):**
- **Nh\xE0 cung c\u1EA5p:** C\xF4ng ty TNHH Nguy\xEAn Li\u1EC7u Tr\xE2n Ch\xE2u \u0110\xE0i Loan (SUP-TAIWAN-BOBA) (PO-2026-1003).
- **M\xE3 \u0111\u1ED1i so\xE1t:** INV-2026-8803 \xB7 PO-2026-1003 \xB7 GR-2026-5003
- **Lo\u1EA1i sai l\u1EC7ch:** QuantityMismatch \xB7 **M\u1EE9c \u0111\u1ED9:** High \xB7 **Tr\u1EA1ng th\xE1i:** Open
- **M\u1EB7t h\xE0ng:** Tr\xE2n Ch\xE2u \u0110en D\u1EBBo Cao C\u1EA5p (Bao 20kg) (SKU-BOBA-BLACK-20K).
- **Hi\u1EC7n tr\u1EA1ng (theo b\u1EA3n ghi tr\xEAn m\xE0n h\xECnh):**
  + H\xF3a \u0111\u01A1n NCC \u0111\xF2i thanh to\xE1n: **200 bao** (t\u1ED5ng 60.000.000 VN\u0110).
  + Phi\u1EBFu nh\u1EADp kho GR-2026-5003 th\u1EF1c nh\u1EADn: **160 bao** (th\u1EF1c nh\u1EADn 48.000.000 VN\u0110).
  + Bi\xEAn b\u1EA3n kho x\xE1c nh\u1EADn r\xE1ch 40 bao do va \u0111\u1EADp trong container v\u1EADn chuy\u1EC3n.
- **L\u1EC7ch (Quantity Mismatch):** Thi\u1EBFu **40 bao**, ch\xEAnh l\u1EC7ch gi\xE1 tr\u1ECB **12.000.000 VN\u0110**.
- **Khuy\u1EBFn ngh\u1ECB cho Finance Reviewer:**
  \u{1F449} Ch\u1ECDn **Reject** ho\u1EB7c **Adjusted** (Y\xEAu c\u1EA7u NCC ph\xE1t h\xE0nh Credit Note gi\u1EA3m 12.000.000 VN\u0110 tr\u01B0\u1EDBc khi gi\u1EA3i ng\xE2n). Tuy\u1EC7t \u0111\u1ED1i kh\xF4ng b\u1EA5m thanh to\xE1n to\xE0n b\u1ED9 200 bao!`;
    } else if (q.includes("8804") || q.includes("daikin") || q.includes("t\u0103ng") && q.includes("gi\xE1")) {
      replyText = `\u{1F6A8} **Ph\xE2n t\xEDch H\xF3a \u0111\u01A1n INV-2026-8804 (T\u0103ng \u0111\u01A1n gi\xE1 tr\xE1i th\u1ECFa thu\u1EADn):**
- **Nh\xE0 cung c\u1EA5p:** Daikin Cold Chain Equipment Vietnam (PO-2026-1004).
- **M\u1EB7t h\xE0ng:** T\u1EE7 C\u1EA5p \u0110\xF4ng Tr\xE0 S\u1EEFa C\xF4ng Nghi\u1EC7p 1200L.
- **Hi\u1EC7n tr\u1EA1ng:**
  + \u0110\u01A1n gi\xE1 th\u1ECFa thu\u1EADn trong PO: **45.000.000 VN\u0110**.
  + \u0110\u01A1n gi\xE1 tr\xEAn h\xF3a \u0111\u01A1n NCC \u0111\xF2i: **52.000.000 VN\u0110** (T\u0103ng +15.5%).
- **L\u1EC7ch (Price Mismatch):** V\xEAnh **7.000.000 VN\u0110** (v\u01B0\u1EE3t xa ng\u01B0\u1EE1ng dung sai 1.0%).
- **H\xE0nh \u0111\u1ED9ng h\u1EC7 th\u1ED1ng:**
  \u{1F449} \u0110\xE3 **T\u1EF1 \u0111\u1ED9ng Kh\xF3a l\u1EC7nh chi tr\u1EA3**. C\u1EA7n Finance Reviewer ph\u1ED1i h\u1EE3p b\u1ED9 ph\u1EADn Mua h\xE0ng l\xE0m vi\u1EC7c l\u1EA1i v\u1EDBi NCC!`;
    } else if (q.includes("8802") || q.includes("vinamilk") || q.includes("dung sai") || q.includes("tolerance")) {
      replyText = `\u2699\uFE0F **Ch\xEDnh s\xE1ch Dung Sai (Tolerance Engine) & H\xF3a \u0111\u01A1n INV-2026-8802:**
H\u1EC7 th\u1ED1ng cho ph\xE9p c\u1EA5u h\xECnh 3 ch\u1EC9 s\u1ED1 linh ho\u1EA1t:
1. **Price Tolerance (%):** M\u1EB7c \u0111\u1ECBnh 1.0% (cho ph\xE9p l\xE0m tr\xF2n ph\xED v\u1EADn chuy\u1EC3n/thu\u1EBF).
2. **Qty Tolerance (%):** T\u1EF7 l\u1EC7 % dung sai s\u1ED1 l\u01B0\u1EE3ng.
3. **Qty Absolute Tolerance:** S\u1ED1 l\u01B0\u1EE3ng hao h\u1EE5t cho ph\xE9p \u0111\u1ED1i v\u1EDBi h\xE0ng r\u1EDDi/n\xF4ng s\u1EA3n (vd: 1-2 bao).

*V\xED d\u1EE5 th\u1EF1c t\u1EBF H\xF3a \u0111\u01A1n INV-2026-8802 (Vinamilk B2B):*
- Ph\xE1t sinh ph\u1EE5 ph\xED b\u1EA3o qu\u1EA3n l\u1EA1nh 100.000 VN\u0110 tr\xEAn t\u1ED5ng \u0111\u01A1n 14.000.000 VN\u0110 (+0.71%).
- V\xEC **0.71% < 1.0% (Price Tolerance)** n\xEAn h\u1EC7 th\u1ED1ng t\u1EF1 \u0111\u1ED9ng g\u1EAFn tr\u1EA1ng th\xE1i **ApprovedWithTolerance** v\xE0 cho ph\xE9p xu\u1EA5t ti\u1EC1n!`;
    }
    const unknownInvoice = userMessage.match(/inv[-\s]?\d{4}[-\s]?\d{4}/i);
    if (unknownInvoice && !/inv[-\s]?2026[-\s]?(?:880[1-5])/i.test(userMessage)) {
      replyText = `\u{1F4A1} **Kh\xF4ng x\xE1c \u0111\u1ECBnh:** T\xF4i kh\xF4ng c\xF3 b\u1EA3n ghi chu\u1EA9n n\xE0o \u0111\u1EC3 tr\xEDch d\u1EABn cho m\xE3 h\xF3a \u0111\u01A1n tr\xEAn. Vui l\xF2ng cung c\u1EA5p m\xE3 h\xF3a \u0111\u01A1n ch\xEDnh x\xE1c (v\xED d\u1EE5: INV-2026-8803) ho\u1EB7c ki\u1EC3m tra l\u1EA1i tr\xEAn m\xE0n h\xECnh \u0110\u1ED1i so\xE1t 3 b\xEAn.`;
    } else {
      replyText = `\u{1F4A1} **OpsBridge AI Copilot \u2014 Ph\u1EA3n h\u1ED3i theo b\u1ED1i c\u1EA3nh:**
V\u1EC1 c\xE2u h\u1ECFi: "${userMessage}".
- Trong h\u1EC7 th\u1ED1ng OpsBridge, m\u1ECDi giao d\u1ECBch ch\u1EE9ng t\u1EEB gi\u1EEFa Mua h\xE0ng (PO), Kho v\u1EADn (GR) v\xE0 T\xE0i ch\xEDnh (INV) \u0111\u01B0\u1EE3c t\u1EF1 \u0111\u1ED9ng b\u1EA3o v\u1EC7 qua c\u01A1 ch\u1EBF ki\u1EC3m so\xE1t 3 b\xEAn v\xE0 ph\xE2n quy\u1EC1n nghi\xEAm ng\u1EB7t theo chu\u1EA9n ki\u1EC3m to\xE1n SOX.
- B\u1EA1n c\xF3 th\u1EC3 chuy\u1EC3n \u0111\u1ED5i gi\u1EEFa 4 vai tr\xF2 (**Finance Reviewer**, **Operator**, **Administrator**, **Auditor**) \u1EDF g\xF3c ph\u1EA3i ph\xEDa tr\xEAn \u0111\u1EC3 tr\u1EA3i nghi\u1EC7m giao di\u1EC7n v\xE0 c\xE1c quy\u1EC1n h\u1EA1n chuy\xEAn bi\u1EC7t.
- N\u1EBFu b\u1EA1n c\u1EA7n ki\u1EC3m tra chi ti\u1EBFt h\xF3a \u0111\u01A1n c\u1EE5 th\u1EC3 (INV-8801, 8802, 8803, 8804) hay th\u1EAFc m\u1EAFc v\u1EC1 ph\xE2n quy\u1EC1n, vui l\xF2ng \u0111\u1EB7t c\xE2u h\u1ECFi cho t\xF4i!`;
    }
    return new Response(
      JSON.stringify({
        reply: replyText,
        providerUsed: "OpsBridge Edge Intelligence Engine"
      }),
      { headers: jsonHeaders }
    );
  }
  if (subPath === "/auth/login") {
    let reqBody = {};
    if (context.request.method === "POST") {
      try {
        reqBody = await context.request.json();
      } catch {
      }
    }
    const username = reqBody.username || "alice_finance";
    let role = reqBody.role || "FinanceReviewer";
    if (role.toUpperCase() === "ADMIN") role = "Admin";
    else if (role.toUpperCase() === "OPERATOR") role = "Operator";
    else if (role.toUpperCase().includes("FINANCE")) role = "FinanceReviewer";
    else if (role.toUpperCase().includes("AUDIT")) role = "Auditor";
    const user = {
      id: "usr-" + Math.abs(username.split("").reduce((acc, c) => acc + c.charCodeAt(0), 100)),
      username,
      role,
      email: `${username.toLowerCase()}@opsbridge.internal`
    };
    return new Response(
      JSON.stringify({
        accessToken: "ey-opsbridge-demo-jwt-" + btoa(JSON.stringify({ sub: username, role })),
        tokenType: "Bearer",
        expiresIn: 86400,
        user
      }),
      { headers: jsonHeaders }
    );
  }
  if (subPath === "/auth/google" && context.request.method === "POST") {
    let reqBody = {};
    try {
      reqBody = await context.request.json();
    } catch {
    }
    const credential = reqBody.credential;
    let payload = {};
    if (credential) {
      try {
        const base64Url = credential.split(".")[1];
        const base64 = base64Url.replace(/-/g, "+").replace(/_/g, "/");
        const jsonPayload = decodeURIComponent(
          atob(base64).split("").map((c) => "%" + ("00" + c.charCodeAt(0).toString(16)).slice(-2)).join("")
        );
        payload = JSON.parse(jsonPayload);
      } catch {
        payload = { email: "operator@gmail.com", name: "Google Operator", sub: "google-user-01" };
      }
    }
    const username = payload.name || (payload.email ? payload.email.split("@")[0] : "google_user");
    const user = {
      id: "usr-google-" + (payload.sub || Date.now()),
      username,
      role: "FinanceReviewer",
      email: payload.email || `${username}@gmail.com`,
      picture: payload.picture
    };
    return new Response(
      JSON.stringify({
        accessToken: "ey-opsbridge-google-jwt-" + btoa(JSON.stringify({ sub: username, email: user.email, role: user.role })),
        tokenType: "Bearer",
        expiresIn: 86400,
        user
      }),
      { headers: jsonHeaders }
    );
  }
  if (subPath === "/auth/me") {
    return new Response(
      JSON.stringify({
        id: "usr-01",
        username: "alice_finance",
        role: "FinanceReviewer",
        email: "alice_finance@opsbridge.internal"
      }),
      { headers: jsonHeaders }
    );
  }
  if (subPath === "/dashboard/metrics") {
    return new Response(
      JSON.stringify({
        totalBatches: 52,
        totalInvoices: invoices.length,
        matchedInvoices: invoices.filter((i) => i.status === "Matched" || i.status === "ApprovedWithVariance").length,
        discrepantInvoices: invoices.filter((i) => i.status === "Discrepancy").length,
        openDiscrepancies: discrepancies.filter((d) => d.status === "Open" || d.status === "UnderReview").length,
        activeConnectors: 5,
        deadLetterCount: deadLetterEvents.filter((d) => !d.isReplayed).length
      }),
      { headers: jsonHeaders }
    );
  }
  if (subPath.startsWith("/purchase-orders")) {
    const parts = subPath.split("/").filter(Boolean);
    if (parts.length > 1) {
      const orderParam = parts[1];
      const found = purchaseOrders.find((p) => p.orderNumber === orderParam || p.id === orderParam) || purchaseOrders[0];
      return new Response(JSON.stringify(found), { headers: jsonHeaders });
    }
    if (context.request.method === "POST") {
      let body = {};
      try {
        body = await context.request.json();
      } catch {
      }
      const newPo = {
        id: "po-" + Date.now(),
        orderNumber: body.orderNumber || `PO-2026-${Math.floor(Math.random() * 9e3 + 1e3)}`,
        supplierCode: body.supplierCode || "SUP-GENERAL-01",
        supplierName: body.supplierName || "Nh\xE0 Cung C\u1EA5p T\u1ED5ng H\u1EE3p",
        currency: body.currency || "VND",
        orderedAt: (/* @__PURE__ */ new Date()).toISOString(),
        status: "Issued",
        sourceSystem: "WEB_PORTAL",
        totalAmount: 2e7,
        lines: body.lines || [
          {
            id: "pol-new-01",
            lineNumber: 1,
            itemSku: "SKU-SAMPLE-01",
            description: "Nguy\xEAn v\u1EADt li\u1EC7u m\u1EABu",
            orderedQuantity: 100,
            unitOfMeasure: "PCS",
            unitPrice: 2e5,
            totalAmount: 2e7
          }
        ]
      };
      return new Response(JSON.stringify(newPo), { status: 201, headers: jsonHeaders });
    }
    return new Response(JSON.stringify(purchaseOrders), { headers: jsonHeaders });
  }
  if (subPath.startsWith("/goods-receipts")) {
    const parts = subPath.split("/").filter(Boolean);
    if (parts.length > 1) {
      const grParam = parts[1];
      const found = goodsReceipts.find((g) => g.receiptNumber === grParam || g.id === grParam) || goodsReceipts[0];
      return new Response(JSON.stringify(found), { headers: jsonHeaders });
    }
    if (context.request.method === "POST") {
      let body = {};
      try {
        body = await context.request.json();
      } catch {
      }
      const newGr = {
        id: "gr-" + Date.now(),
        receiptNumber: body.receiptNumber || `GR-2026-${Math.floor(Math.random() * 9e3 + 1e3)}`,
        purchaseOrderNumber: body.purchaseOrderNumber || "PO-2026-1001",
        supplierCode: body.supplierCode || "SUP-BAO-LOC-TEA",
        receivedAt: (/* @__PURE__ */ new Date()).toISOString(),
        status: "Verified",
        warehouseCode: "WH-HCM-CENTRAL",
        sourceSystem: "WEB_PORTAL",
        lines: body.lines || [
          {
            id: "grl-new-01",
            lineNumber: 1,
            itemSku: "SKU-TEA-OOLONG-01",
            receivedQuantity: 100,
            unitOfMeasure: "BAO",
            allocatedQuantity: 100,
            availableQuantity: 0
          }
        ]
      };
      return new Response(JSON.stringify(newGr), { status: 201, headers: jsonHeaders });
    }
    return new Response(JSON.stringify(goodsReceipts), { headers: jsonHeaders });
  }
  if (subPath.startsWith("/invoices")) {
    const parts = subPath.split("/").filter(Boolean);
    if (parts.length > 1) {
      const invParam = parts[1];
      const found = invoices.find((i) => i.id === invParam || i.invoiceNumber === invParam) || invoices[0];
      return new Response(JSON.stringify(found), { headers: jsonHeaders });
    }
    if (context.request.method === "POST") {
      const contentType = context.request.headers.get("content-type") || "";
      if (contentType.includes("text/csv")) {
        return new Response(
          JSON.stringify({ count: 5, message: "Nh\u1EADp th\xE0nh c\xF4ng 5 h\xF3a \u0111\u01A1n t\u1EEB file CSV \u0111\u1ED1i so\xE1t." }),
          { status: 200, headers: jsonHeaders }
        );
      }
      let body = {};
      try {
        body = await context.request.json();
      } catch {
      }
      const newInv = {
        id: "inv-" + Date.now(),
        invoiceNumber: body.invoiceNumber || `INV-2026-${Math.floor(Math.random() * 9e3 + 1e3)}`,
        purchaseOrderNumber: body.purchaseOrderNumber || "PO-2026-1001",
        supplierCode: body.supplierCode || "SUP-BAO-LOC-TEA",
        supplierName: body.supplierName || "HTX Tr\xE0 Cao Nguy\xEAn B\u1EA3o L\u1ED9c",
        currency: body.currency || "VND",
        invoiceDate: (/* @__PURE__ */ new Date()).toISOString(),
        status: "PendingReconciliation",
        subTotal: 15e6,
        taxAmount: 15e5,
        discountAmount: 0,
        additionalCharges: 0,
        totalAmount: 165e5,
        sourceSystem: "WEB_PORTAL",
        lines: body.lines || [
          {
            id: "invl-new-01",
            lineNumber: 1,
            itemSku: "SKU-TEA-OOLONG-01",
            description: "Tr\xE0 Oolong Th\u01B0\u1EE3ng H\u1EA1ng (Bao 50kg)",
            invoicedQuantity: 100,
            unitOfMeasure: "BAO",
            unitPrice: 15e4,
            taxRate: 0.1,
            lineTotal: 15e6,
            matchedQuantity: 0
          }
        ]
      };
      return new Response(JSON.stringify(newInv), { status: 201, headers: jsonHeaders });
    }
    return new Response(JSON.stringify(invoices), { headers: jsonHeaders });
  }
  if (subPath === "/reconciliation/run" && context.request.method === "POST") {
    let body = {};
    try {
      body = await context.request.json();
    } catch {
    }
    const invoiceId = body.invoiceId || "inv-8805";
    const targetInv = invoices.find((i) => i.id === invoiceId) || invoices[0];
    const isMatched = targetInv.id === "inv-8801" || targetInv.id === "inv-8802" || targetInv.id === "inv-8805";
    const reconId = "recon-" + Date.now();
    const orderedQty = 100;
    const receivedQty = isMatched ? 100 : 80;
    const invoicedQty = 100;
    const unitPrice = targetInv.lines[0]?.unitPrice || 15e4;
    const totalVariance = isMatched ? 0 : targetInv.totalAmount * 0.2;
    const reconResult = {
      id: reconId,
      batchId: "batch-live-01",
      purchaseOrderNumber: targetInv.purchaseOrderNumber,
      invoiceNumber: targetInv.invoiceNumber,
      status: isMatched ? "Matched" : "Discrepancy",
      totalQuantityOrdered: orderedQty,
      totalQuantityReceived: receivedQty,
      totalQuantityInvoiced: invoicedQty,
      totalAmountVariance: totalVariance,
      createdAt: (/* @__PURE__ */ new Date()).toISOString(),
      reconciledAt: (/* @__PURE__ */ new Date()).toISOString(),
      reconciledBy: "alice_finance",
      totalInvoicedAmount: targetInv.totalAmount,
      totalMatchedAmount: isMatched ? targetInv.totalAmount : targetInv.totalAmount * 0.8,
      totalVarianceAmount: totalVariance,
      variancePercentage: isMatched ? 0 : 20,
      lines: [
        {
          id: "recon-line-01",
          lineNumber: 1,
          itemSku: targetInv.lines[0]?.itemSku || "SKU-DEFAULT",
          orderedQuantity: orderedQty,
          receivedQuantity: receivedQty,
          invoicedQuantity: invoicedQty,
          matchedQuantity: receivedQty,
          orderedUnitPrice: unitPrice,
          invoicedUnitPrice: unitPrice,
          status: isMatched ? "Matched" : "Discrepancy",
          discrepancyType: isMatched ? void 0 : "QuantityMismatch"
        }
      ],
      allocations: [
        {
          id: "alloc-" + Date.now() + "-1",
          reconciliationResultId: reconId,
          goodsReceiptLineId: "grl-" + (isMatched ? "1001" : "1003"),
          supplierInvoiceLineId: targetInv.lines[0]?.id || "invl-01",
          allocatedQuantity: receivedQty,
          unitPrice,
          allocatedAt: (/* @__PURE__ */ new Date()).toISOString()
        }
      ],
      discrepancies: isMatched ? [] : [
        {
          id: "disc-live-" + Date.now(),
          reconciliationResultId: reconId,
          type: "QuantityMismatch",
          severity: "High",
          itemSku: targetInv.lines[0]?.itemSku || "SKU-DEFAULT",
          expectedValue: "80 BAO (Th\u1EF1c nh\u1EADn t\u1EA1i kho)",
          actualValue: "100 BAO (H\xF3a \u0111\u01A1n NCC y\xEAu c\u1EA7u)",
          varianceAmount: totalVariance,
          status: "Open",
          details: "Ph\xE1t hi\u1EC7n ch\xEAnh l\u1EC7ch 20 \u0111\u01A1n v\u1ECB h\xE0ng h\xF3a gi\u1EEFa th\u1EF1c t\u1EBF ki\u1EC3m \u0111\u1EBFm nh\u1EADp kho (80 bao) v\xE0 h\xF3a \u0111\u01A1n NCC g\u1EEDi sang (100 bao)."
        }
      ]
    };
    return new Response(JSON.stringify(reconResult), { headers: jsonHeaders });
  }
  if (subPath.startsWith("/reconciliation/results/")) {
    const parts = subPath.split("/").filter(Boolean);
    const resultId = parts[2] || "recon-" + Date.now();
    const mockResult = {
      id: resultId,
      batchId: "batch-20261021-01",
      purchaseOrderNumber: "PO-2026-1001",
      invoiceNumber: "INV-2026-8801",
      status: "Matched",
      totalQuantityOrdered: 100,
      totalQuantityReceived: 100,
      totalQuantityInvoiced: 100,
      totalAmountVariance: 0,
      createdAt: (/* @__PURE__ */ new Date()).toISOString(),
      reconciledAt: new Date(Date.now() - 36e5).toISOString(),
      reconciledBy: "system.reconciliation.worker",
      totalInvoicedAmount: 165e5,
      totalMatchedAmount: 165e5,
      totalVarianceAmount: 0,
      variancePercentage: 0,
      lines: [],
      allocations: [
        {
          id: "alloc-prev-01",
          reconciliationResultId: resultId,
          goodsReceiptLineId: "grl-1001",
          supplierInvoiceLineId: "invl-1001",
          allocatedQuantity: 100,
          unitPrice: 15e4,
          allocatedAt: (/* @__PURE__ */ new Date()).toISOString()
        }
      ],
      discrepancies: []
    };
    return new Response(JSON.stringify(mockResult), { headers: jsonHeaders });
  }
  if (subPath.startsWith("/reconciliation/discrepancies")) {
    if (subPath.includes("/review") && context.request.method === "POST") {
      let body = {};
      try {
        body = await context.request.json();
      } catch {
      }
      const reviewDecision = {
        id: "rev-" + Date.now(),
        discrepancyId: subPath.split("/")[2],
        action: body.action || "ApprovedWithTolerance",
        decidedBy: "alice_finance",
        decidedAt: (/* @__PURE__ */ new Date()).toISOString(),
        notes: body.notes || "Ph\xEA duy\u1EC7t dung sai theo \u1EE7y quy\u1EC1n t\xE0i ch\xEDnh."
      };
      return new Response(JSON.stringify(reviewDecision), { headers: jsonHeaders });
    }
    const urlObj = new URL(context.request.url);
    const statusQuery = urlObj.searchParams.get("status");
    let result = discrepancies;
    if (statusQuery && statusQuery !== "ALL") {
      result = discrepancies.filter((d) => d.status === statusQuery);
    }
    return new Response(JSON.stringify(result), { headers: jsonHeaders });
  }
  if (subPath.startsWith("/reconciliation/batches")) {
    return new Response(
      JSON.stringify([
        {
          id: "batch-01",
          batchNumber: "BATCH-20261021-01",
          executedAt: new Date(Date.now() - 72e5).toISOString(),
          status: "Completed",
          totalInvoices: 124,
          matchedCount: 119,
          discrepancyCount: 5,
          totalValue: 245e6,
          initiatedBy: "system.cron"
        },
        {
          id: "batch-02",
          batchNumber: "BATCH-20261020-02",
          executedAt: new Date(Date.now() - 936e5).toISOString(),
          status: "Completed",
          totalInvoices: 98,
          matchedCount: 96,
          discrepancyCount: 2,
          totalValue: 182e6,
          initiatedBy: "alice_finance"
        }
      ]),
      { headers: jsonHeaders }
    );
  }
  if (subPath === "/reconciliation/audit-trail") {
    return new Response(
      JSON.stringify([
        {
          id: "aud-001",
          entityType: "SupplierInvoice",
          entityId: "INV-2026-8801",
          action: "AUTO_MATCHED_3WAY",
          userId: "reconciliation.worker",
          timestamp: new Date(Date.now() - 36e4).toISOString(),
          details: "\u0110\u1ED1i so\xE1t t\u1EF1 \u0111\u1ED9ng 3 b\xEAn ho\xE0n h\u1EA3o gi\u1EEFa PO-2026-1001, GR-2026-5001 v\xE0 h\xF3a \u0111\u01A1n VAT. Ch\xEAnh l\u1EC7ch 0 VND."
        },
        {
          id: "aud-002",
          entityType: "Discrepancy",
          entityId: "DISC-03",
          action: "VARIANCE_APPROVED",
          userId: "alice_finance",
          timestamp: new Date(Date.now() - 12e5).toISOString(),
          details: "Duy\u1EC7t dung sai gi\xE1 0.71% (100,000 VND) cho Vinamilk B2B do ph\xE1t sinh b\u1EA3o qu\u1EA3n l\u1EA1nh theo h\u1EE3p \u0111\u1ED3ng khung."
        },
        {
          id: "aud-003",
          entityType: "DeadLetterEvent",
          entityId: "evt-dlq-missing-po-ref",
          action: "EVENT_REPLAYED",
          userId: "admin_system",
          timestamp: new Date(Date.now() - 36e5).toISOString(),
          details: "Th\u1EF1c hi\u1EC7n replay webhook OData sau khi c\u1EA5u h\xECnh l\u1EA1i timeout m\u1EA1ng 30s."
        },
        {
          id: "aud-004",
          entityType: "SupplierInvoice",
          entityId: "INV-2026-8804",
          action: "PAYMENT_LOCKED",
          userId: "system.invariant.guard",
          timestamp: new Date(Date.now() - 72e5).toISOString(),
          details: "T\u1EF1 \u0111\u1ED9ng kh\xF3a l\u1EC7nh chi tr\u1EA3: \u0110\u01A1n gi\xE1 h\xF3a \u0111\u01A1n (52,000 VND) v\u01B0\u1EE3t gi\xE1 PO k\xFD k\u1EBFt (45,000 VND) t\u1EDBi 15.5%."
        },
        {
          id: "aud-005",
          entityType: "PurchaseOrder",
          entityId: "PO-2026-1005",
          action: "PO_APPROVED",
          userId: "procurement.manager",
          timestamp: new Date(Date.now() - 144e5).toISOString(),
          details: "Ph\xEA duy\u1EC7t ng\xE2n s\xE1ch mua s\u1EAFm thi\u1EBFt b\u1ECB kho Daikin 18,500,000 VND."
        }
      ]),
      { headers: jsonHeaders }
    );
  }
  if (subPath.startsWith("/connectors")) {
    return new Response(
      JSON.stringify([
        { id: "conn-01", name: "SAP S/4HANA OData v4", type: "ERP", status: "Healthy", lastSyncAt: new Date(Date.now() - 12e4).toISOString() },
        { id: "conn-02", name: "Oracle NetSuite REST API", type: "ERP", status: "Healthy", lastSyncAt: new Date(Date.now() - 18e4).toISOString() },
        { id: "conn-03", name: "VNPT / Viettel e-Invoice Connector", type: "EInvoice", status: "Healthy", lastSyncAt: new Date(Date.now() - 6e4).toISOString() },
        { id: "conn-04", name: "Manhattan WMS Warehouse Stream", type: "WMS", status: "Healthy", lastSyncAt: new Date(Date.now() - 9e4).toISOString() },
        { id: "conn-05", name: "RabbitMQ Event Mesh (ap.invoices.raw)", type: "MessageBroker", status: "Active", lastSyncAt: (/* @__PURE__ */ new Date()).toISOString() }
      ]),
      { headers: jsonHeaders }
    );
  }
  if (subPath.startsWith("/ingestion/events")) {
    return new Response(JSON.stringify(ingestedEvents), { headers: jsonHeaders });
  }
  if (subPath.startsWith("/events/dead-letter")) {
    return new Response(JSON.stringify(deadLetterEvents), { headers: jsonHeaders });
  }
  if (subPath.startsWith("/events/replay/")) {
    if (context.request.method !== "POST") {
      return new Response(JSON.stringify({ message: "POST required for replay preview." }), {
        status: 405,
        headers: jsonHeaders
      });
    }
    const parts = subPath.split("/").filter(Boolean);
    const eventId = parts[2];
    const event = deadLetterEvents.find((item) => item.eventId === eventId);
    if (!event) {
      return new Response(JSON.stringify({ message: "Dead-letter event not found." }), {
        status: 404,
        headers: jsonHeaders
      });
    }
    if (event.isReplayed) {
      return new Response(JSON.stringify({ message: "This sample event is already marked replayed." }), {
        status: 409,
        headers: jsonHeaders
      });
    }
    return new Response(
      JSON.stringify({
        message: `Sandbox preview: ${eventId} is marked replayed for this browser. No RabbitMQ message was sent.`,
        simulated: true,
        replayedBy: "sandbox.reviewer",
        replayedAt: (/* @__PURE__ */ new Date()).toISOString()
      }),
      {
        status: 200,
        headers: {
          ...jsonHeaders,
          "Set-Cookie": `opsbridge_demo_replayed_at=${Date.now()}; Path=/api/v1; Max-Age=3600; HttpOnly; SameSite=Lax${url.protocol === "https:" ? "; Secure" : ""}`
        }
      }
    );
  }
  return new Response(
    JSON.stringify({
      status: "healthy",
      service: "OpsBridge Enterprise Telemetry",
      route: url.pathname,
      timestamp: (/* @__PURE__ */ new Date()).toISOString()
    }),
    { headers: jsonHeaders }
  );
}, "onRequest");
var routes = [
  {
    routePath: "/api/v1/:path*",
    mountPath: "/api/v1",
    method: "",
    middlewares: [],
    modules: [onRequest]
  }
];
function lexer(str) {
  var tokens = [];
  var i = 0;
  while (i < str.length) {
    var char = str[i];
    if (char === "*" || char === "+" || char === "?") {
      tokens.push({ type: "MODIFIER", index: i, value: str[i++] });
      continue;
    }
    if (char === "\\") {
      tokens.push({ type: "ESCAPED_CHAR", index: i++, value: str[i++] });
      continue;
    }
    if (char === "{") {
      tokens.push({ type: "OPEN", index: i, value: str[i++] });
      continue;
    }
    if (char === "}") {
      tokens.push({ type: "CLOSE", index: i, value: str[i++] });
      continue;
    }
    if (char === ":") {
      var name = "";
      var j = i + 1;
      while (j < str.length) {
        var code = str.charCodeAt(j);
        if (
          // `0-9`
          code >= 48 && code <= 57 || // `A-Z`
          code >= 65 && code <= 90 || // `a-z`
          code >= 97 && code <= 122 || // `_`
          code === 95
        ) {
          name += str[j++];
          continue;
        }
        break;
      }
      if (!name)
        throw new TypeError("Missing parameter name at ".concat(i));
      tokens.push({ type: "NAME", index: i, value: name });
      i = j;
      continue;
    }
    if (char === "(") {
      var count = 1;
      var pattern = "";
      var j = i + 1;
      if (str[j] === "?") {
        throw new TypeError('Pattern cannot start with "?" at '.concat(j));
      }
      while (j < str.length) {
        if (str[j] === "\\") {
          pattern += str[j++] + str[j++];
          continue;
        }
        if (str[j] === ")") {
          count--;
          if (count === 0) {
            j++;
            break;
          }
        } else if (str[j] === "(") {
          count++;
          if (str[j + 1] !== "?") {
            throw new TypeError("Capturing groups are not allowed at ".concat(j));
          }
        }
        pattern += str[j++];
      }
      if (count)
        throw new TypeError("Unbalanced pattern at ".concat(i));
      if (!pattern)
        throw new TypeError("Missing pattern at ".concat(i));
      tokens.push({ type: "PATTERN", index: i, value: pattern });
      i = j;
      continue;
    }
    tokens.push({ type: "CHAR", index: i, value: str[i++] });
  }
  tokens.push({ type: "END", index: i, value: "" });
  return tokens;
}
__name(lexer, "lexer");
__name2(lexer, "lexer");
function parse(str, options) {
  if (options === void 0) {
    options = {};
  }
  var tokens = lexer(str);
  var _a = options.prefixes, prefixes = _a === void 0 ? "./" : _a, _b = options.delimiter, delimiter = _b === void 0 ? "/#?" : _b;
  var result = [];
  var key = 0;
  var i = 0;
  var path = "";
  var tryConsume = /* @__PURE__ */ __name2(function(type) {
    if (i < tokens.length && tokens[i].type === type)
      return tokens[i++].value;
  }, "tryConsume");
  var mustConsume = /* @__PURE__ */ __name2(function(type) {
    var value2 = tryConsume(type);
    if (value2 !== void 0)
      return value2;
    var _a2 = tokens[i], nextType = _a2.type, index = _a2.index;
    throw new TypeError("Unexpected ".concat(nextType, " at ").concat(index, ", expected ").concat(type));
  }, "mustConsume");
  var consumeText = /* @__PURE__ */ __name2(function() {
    var result2 = "";
    var value2;
    while (value2 = tryConsume("CHAR") || tryConsume("ESCAPED_CHAR")) {
      result2 += value2;
    }
    return result2;
  }, "consumeText");
  var isSafe = /* @__PURE__ */ __name2(function(value2) {
    for (var _i = 0, delimiter_1 = delimiter; _i < delimiter_1.length; _i++) {
      var char2 = delimiter_1[_i];
      if (value2.indexOf(char2) > -1)
        return true;
    }
    return false;
  }, "isSafe");
  var safePattern = /* @__PURE__ */ __name2(function(prefix2) {
    var prev = result[result.length - 1];
    var prevText = prefix2 || (prev && typeof prev === "string" ? prev : "");
    if (prev && !prevText) {
      throw new TypeError('Must have text between two parameters, missing text after "'.concat(prev.name, '"'));
    }
    if (!prevText || isSafe(prevText))
      return "[^".concat(escapeString(delimiter), "]+?");
    return "(?:(?!".concat(escapeString(prevText), ")[^").concat(escapeString(delimiter), "])+?");
  }, "safePattern");
  while (i < tokens.length) {
    var char = tryConsume("CHAR");
    var name = tryConsume("NAME");
    var pattern = tryConsume("PATTERN");
    if (name || pattern) {
      var prefix = char || "";
      if (prefixes.indexOf(prefix) === -1) {
        path += prefix;
        prefix = "";
      }
      if (path) {
        result.push(path);
        path = "";
      }
      result.push({
        name: name || key++,
        prefix,
        suffix: "",
        pattern: pattern || safePattern(prefix),
        modifier: tryConsume("MODIFIER") || ""
      });
      continue;
    }
    var value = char || tryConsume("ESCAPED_CHAR");
    if (value) {
      path += value;
      continue;
    }
    if (path) {
      result.push(path);
      path = "";
    }
    var open = tryConsume("OPEN");
    if (open) {
      var prefix = consumeText();
      var name_1 = tryConsume("NAME") || "";
      var pattern_1 = tryConsume("PATTERN") || "";
      var suffix = consumeText();
      mustConsume("CLOSE");
      result.push({
        name: name_1 || (pattern_1 ? key++ : ""),
        pattern: name_1 && !pattern_1 ? safePattern(prefix) : pattern_1,
        prefix,
        suffix,
        modifier: tryConsume("MODIFIER") || ""
      });
      continue;
    }
    mustConsume("END");
  }
  return result;
}
__name(parse, "parse");
__name2(parse, "parse");
function match(str, options) {
  var keys = [];
  var re = pathToRegexp(str, keys, options);
  return regexpToFunction(re, keys, options);
}
__name(match, "match");
__name2(match, "match");
function regexpToFunction(re, keys, options) {
  if (options === void 0) {
    options = {};
  }
  var _a = options.decode, decode = _a === void 0 ? function(x) {
    return x;
  } : _a;
  return function(pathname) {
    var m = re.exec(pathname);
    if (!m)
      return false;
    var path = m[0], index = m.index;
    var params = /* @__PURE__ */ Object.create(null);
    var _loop_1 = /* @__PURE__ */ __name2(function(i2) {
      if (m[i2] === void 0)
        return "continue";
      var key = keys[i2 - 1];
      if (key.modifier === "*" || key.modifier === "+") {
        params[key.name] = m[i2].split(key.prefix + key.suffix).map(function(value) {
          return decode(value, key);
        });
      } else {
        params[key.name] = decode(m[i2], key);
      }
    }, "_loop_1");
    for (var i = 1; i < m.length; i++) {
      _loop_1(i);
    }
    return { path, index, params };
  };
}
__name(regexpToFunction, "regexpToFunction");
__name2(regexpToFunction, "regexpToFunction");
function escapeString(str) {
  return str.replace(/([.+*?=^!:${}()[\]|/\\])/g, "\\$1");
}
__name(escapeString, "escapeString");
__name2(escapeString, "escapeString");
function flags(options) {
  return options && options.sensitive ? "" : "i";
}
__name(flags, "flags");
__name2(flags, "flags");
function regexpToRegexp(path, keys) {
  if (!keys)
    return path;
  var groupsRegex = /\((?:\?<(.*?)>)?(?!\?)/g;
  var index = 0;
  var execResult = groupsRegex.exec(path.source);
  while (execResult) {
    keys.push({
      // Use parenthesized substring match if available, index otherwise
      name: execResult[1] || index++,
      prefix: "",
      suffix: "",
      modifier: "",
      pattern: ""
    });
    execResult = groupsRegex.exec(path.source);
  }
  return path;
}
__name(regexpToRegexp, "regexpToRegexp");
__name2(regexpToRegexp, "regexpToRegexp");
function arrayToRegexp(paths, keys, options) {
  var parts = paths.map(function(path) {
    return pathToRegexp(path, keys, options).source;
  });
  return new RegExp("(?:".concat(parts.join("|"), ")"), flags(options));
}
__name(arrayToRegexp, "arrayToRegexp");
__name2(arrayToRegexp, "arrayToRegexp");
function stringToRegexp(path, keys, options) {
  return tokensToRegexp(parse(path, options), keys, options);
}
__name(stringToRegexp, "stringToRegexp");
__name2(stringToRegexp, "stringToRegexp");
function tokensToRegexp(tokens, keys, options) {
  if (options === void 0) {
    options = {};
  }
  var _a = options.strict, strict = _a === void 0 ? false : _a, _b = options.start, start = _b === void 0 ? true : _b, _c = options.end, end = _c === void 0 ? true : _c, _d = options.encode, encode = _d === void 0 ? function(x) {
    return x;
  } : _d, _e = options.delimiter, delimiter = _e === void 0 ? "/#?" : _e, _f = options.endsWith, endsWith = _f === void 0 ? "" : _f;
  var endsWithRe = "[".concat(escapeString(endsWith), "]|$");
  var delimiterRe = "[".concat(escapeString(delimiter), "]");
  var route = start ? "^" : "";
  for (var _i = 0, tokens_1 = tokens; _i < tokens_1.length; _i++) {
    var token = tokens_1[_i];
    if (typeof token === "string") {
      route += escapeString(encode(token));
    } else {
      var prefix = escapeString(encode(token.prefix));
      var suffix = escapeString(encode(token.suffix));
      if (token.pattern) {
        if (keys)
          keys.push(token);
        if (prefix || suffix) {
          if (token.modifier === "+" || token.modifier === "*") {
            var mod = token.modifier === "*" ? "?" : "";
            route += "(?:".concat(prefix, "((?:").concat(token.pattern, ")(?:").concat(suffix).concat(prefix, "(?:").concat(token.pattern, "))*)").concat(suffix, ")").concat(mod);
          } else {
            route += "(?:".concat(prefix, "(").concat(token.pattern, ")").concat(suffix, ")").concat(token.modifier);
          }
        } else {
          if (token.modifier === "+" || token.modifier === "*") {
            throw new TypeError('Can not repeat "'.concat(token.name, '" without a prefix and suffix'));
          }
          route += "(".concat(token.pattern, ")").concat(token.modifier);
        }
      } else {
        route += "(?:".concat(prefix).concat(suffix, ")").concat(token.modifier);
      }
    }
  }
  if (end) {
    if (!strict)
      route += "".concat(delimiterRe, "?");
    route += !options.endsWith ? "$" : "(?=".concat(endsWithRe, ")");
  } else {
    var endToken = tokens[tokens.length - 1];
    var isEndDelimited = typeof endToken === "string" ? delimiterRe.indexOf(endToken[endToken.length - 1]) > -1 : endToken === void 0;
    if (!strict) {
      route += "(?:".concat(delimiterRe, "(?=").concat(endsWithRe, "))?");
    }
    if (!isEndDelimited) {
      route += "(?=".concat(delimiterRe, "|").concat(endsWithRe, ")");
    }
  }
  return new RegExp(route, flags(options));
}
__name(tokensToRegexp, "tokensToRegexp");
__name2(tokensToRegexp, "tokensToRegexp");
function pathToRegexp(path, keys, options) {
  if (path instanceof RegExp)
    return regexpToRegexp(path, keys);
  if (Array.isArray(path))
    return arrayToRegexp(path, keys, options);
  return stringToRegexp(path, keys, options);
}
__name(pathToRegexp, "pathToRegexp");
__name2(pathToRegexp, "pathToRegexp");
var escapeRegex = /[.+?^${}()|[\]\\]/g;
function* executeRequest(request) {
  const requestPath = new URL(request.url).pathname;
  for (const route of [...routes].reverse()) {
    if (route.method && route.method !== request.method) {
      continue;
    }
    const routeMatcher = match(route.routePath.replace(escapeRegex, "\\$&"), {
      end: false
    });
    const mountMatcher = match(route.mountPath.replace(escapeRegex, "\\$&"), {
      end: false
    });
    const matchResult = routeMatcher(requestPath);
    const mountMatchResult = mountMatcher(requestPath);
    if (matchResult && mountMatchResult) {
      for (const handler of route.middlewares.flat()) {
        yield {
          handler,
          params: matchResult.params,
          path: mountMatchResult.path
        };
      }
    }
  }
  for (const route of routes) {
    if (route.method && route.method !== request.method) {
      continue;
    }
    const routeMatcher = match(route.routePath.replace(escapeRegex, "\\$&"), {
      end: true
    });
    const mountMatcher = match(route.mountPath.replace(escapeRegex, "\\$&"), {
      end: false
    });
    const matchResult = routeMatcher(requestPath);
    const mountMatchResult = mountMatcher(requestPath);
    if (matchResult && mountMatchResult && route.modules.length) {
      for (const handler of route.modules.flat()) {
        yield {
          handler,
          params: matchResult.params,
          path: matchResult.path
        };
      }
      break;
    }
  }
}
__name(executeRequest, "executeRequest");
__name2(executeRequest, "executeRequest");
var pages_template_worker_default = {
  async fetch(originalRequest, env, workerContext) {
    let request = originalRequest;
    const handlerIterator = executeRequest(request);
    let data = {};
    let isFailOpen = false;
    const next = /* @__PURE__ */ __name2(async (input, init) => {
      if (input !== void 0) {
        let url = input;
        if (typeof input === "string") {
          url = new URL(input, request.url).toString();
        }
        request = new Request(url, init);
      }
      const result = handlerIterator.next();
      if (result.done === false) {
        const { handler, params, path } = result.value;
        const context = {
          request: new Request(request.clone()),
          functionPath: path,
          next,
          params,
          get data() {
            return data;
          },
          set data(value) {
            if (typeof value !== "object" || value === null) {
              throw new Error("context.data must be an object");
            }
            data = value;
          },
          env,
          waitUntil: workerContext.waitUntil.bind(workerContext),
          passThroughOnException: /* @__PURE__ */ __name2(() => {
            isFailOpen = true;
          }, "passThroughOnException")
        };
        const response = await handler(context);
        if (!(response instanceof Response)) {
          throw new Error("Your Pages function should return a Response");
        }
        return cloneResponse(response);
      } else if ("ASSETS") {
        const response = await env["ASSETS"].fetch(request);
        return cloneResponse(response);
      } else {
        const response = await fetch(request);
        return cloneResponse(response);
      }
    }, "next");
    try {
      return await next();
    } catch (error) {
      if (isFailOpen) {
        const response = await env["ASSETS"].fetch(request);
        return cloneResponse(response);
      }
      throw error;
    }
  }
};
var cloneResponse = /* @__PURE__ */ __name2((response) => (
  // https://fetch.spec.whatwg.org/#null-body-status
  new Response(
    [101, 204, 205, 304].includes(response.status) ? null : response.body,
    response
  )
), "cloneResponse");
var drainBody = /* @__PURE__ */ __name2(async (request, env, _ctx, middlewareCtx) => {
  try {
    return await middlewareCtx.next(request, env);
  } finally {
    try {
      if (request.body !== null && !request.bodyUsed) {
        const reader = request.body.getReader();
        while (!(await reader.read()).done) {
        }
      }
    } catch (e) {
      console.error("Failed to drain the unused request body.", e);
    }
  }
}, "drainBody");
var middleware_ensure_req_body_drained_default = drainBody;
function reduceError(e) {
  return {
    name: e?.name,
    message: e?.message ?? String(e),
    stack: e?.stack,
    cause: e?.cause === void 0 ? void 0 : reduceError(e.cause)
  };
}
__name(reduceError, "reduceError");
__name2(reduceError, "reduceError");
var jsonError = /* @__PURE__ */ __name2(async (request, env, _ctx, middlewareCtx) => {
  try {
    return await middlewareCtx.next(request, env);
  } catch (e) {
    const error = reduceError(e);
    const body = JSON.stringify(error);
    const headers = {
      "Content-Type": "application/json",
      "MF-Experimental-Error-Stack": "true"
    };
    const encoded = encodeURIComponent(body);
    if (encoded.length <= 8192) {
      headers["MF-Experimental-Error-Stack-Payload"] = encoded;
    }
    return new Response(body, { status: 500, headers });
  }
}, "jsonError");
var middleware_miniflare3_json_error_default = jsonError;
var __INTERNAL_WRANGLER_MIDDLEWARE__ = [
  middleware_ensure_req_body_drained_default,
  middleware_miniflare3_json_error_default
];
var middleware_insertion_facade_default = pages_template_worker_default;
var __facade_middleware__ = [];
function __facade_register__(...args) {
  __facade_middleware__.push(...args.flat());
}
__name(__facade_register__, "__facade_register__");
__name2(__facade_register__, "__facade_register__");
function __facade_invokeChain__(request, env, ctx, dispatch, middlewareChain) {
  const [head, ...tail] = middlewareChain;
  const middlewareCtx = {
    dispatch,
    next(newRequest, newEnv) {
      return __facade_invokeChain__(newRequest, newEnv, ctx, dispatch, tail);
    }
  };
  return head(request, env, ctx, middlewareCtx);
}
__name(__facade_invokeChain__, "__facade_invokeChain__");
__name2(__facade_invokeChain__, "__facade_invokeChain__");
function __facade_invoke__(request, env, ctx, dispatch, finalMiddleware) {
  return __facade_invokeChain__(request, env, ctx, dispatch, [
    ...__facade_middleware__,
    finalMiddleware
  ]);
}
__name(__facade_invoke__, "__facade_invoke__");
__name2(__facade_invoke__, "__facade_invoke__");
var __Facade_ScheduledController__ = class ___Facade_ScheduledController__ {
  static {
    __name(this, "___Facade_ScheduledController__");
  }
  constructor(scheduledTime, cron, noRetry) {
    this.scheduledTime = scheduledTime;
    this.cron = cron;
    this.#noRetry = noRetry;
  }
  scheduledTime;
  cron;
  static {
    __name2(this, "__Facade_ScheduledController__");
  }
  #noRetry;
  noRetry() {
    if (!(this instanceof ___Facade_ScheduledController__)) {
      throw new TypeError("Illegal invocation");
    }
    this.#noRetry();
  }
};
function wrapExportedHandler(worker) {
  if (__INTERNAL_WRANGLER_MIDDLEWARE__ === void 0 || __INTERNAL_WRANGLER_MIDDLEWARE__.length === 0) {
    return worker;
  }
  for (const middleware of __INTERNAL_WRANGLER_MIDDLEWARE__) {
    __facade_register__(middleware);
  }
  const fetchDispatcher = /* @__PURE__ */ __name2(function(request, env, ctx) {
    if (worker.fetch === void 0) {
      throw new Error("Handler does not export a fetch() function.");
    }
    return worker.fetch(request, env, ctx);
  }, "fetchDispatcher");
  return {
    ...worker,
    fetch(request, env, ctx) {
      const dispatcher = /* @__PURE__ */ __name2(function(type, init) {
        if (type === "scheduled" && worker.scheduled !== void 0) {
          const controller = new __Facade_ScheduledController__(
            Date.now(),
            init.cron ?? "",
            () => {
            }
          );
          return worker.scheduled(controller, env, ctx);
        }
      }, "dispatcher");
      return __facade_invoke__(request, env, ctx, dispatcher, fetchDispatcher);
    }
  };
}
__name(wrapExportedHandler, "wrapExportedHandler");
__name2(wrapExportedHandler, "wrapExportedHandler");
function wrapWorkerEntrypoint(klass) {
  if (__INTERNAL_WRANGLER_MIDDLEWARE__ === void 0 || __INTERNAL_WRANGLER_MIDDLEWARE__.length === 0) {
    return klass;
  }
  for (const middleware of __INTERNAL_WRANGLER_MIDDLEWARE__) {
    __facade_register__(middleware);
  }
  return class extends klass {
    #fetchDispatcher = /* @__PURE__ */ __name2((request, env, ctx) => {
      this.env = env;
      this.ctx = ctx;
      if (super.fetch === void 0) {
        throw new Error("Entrypoint class does not define a fetch() function.");
      }
      return super.fetch(request);
    }, "#fetchDispatcher");
    #dispatcher = /* @__PURE__ */ __name2((type, init) => {
      if (type === "scheduled" && super.scheduled !== void 0) {
        const controller = new __Facade_ScheduledController__(
          Date.now(),
          init.cron ?? "",
          () => {
          }
        );
        return super.scheduled(controller);
      }
    }, "#dispatcher");
    fetch(request) {
      return __facade_invoke__(
        request,
        this.env,
        this.ctx,
        this.#dispatcher,
        this.#fetchDispatcher
      );
    }
  };
}
__name(wrapWorkerEntrypoint, "wrapWorkerEntrypoint");
__name2(wrapWorkerEntrypoint, "wrapWorkerEntrypoint");
var WRAPPED_ENTRY;
if (typeof middleware_insertion_facade_default === "object") {
  WRAPPED_ENTRY = wrapExportedHandler(middleware_insertion_facade_default);
} else if (typeof middleware_insertion_facade_default === "function") {
  WRAPPED_ENTRY = wrapWorkerEntrypoint(middleware_insertion_facade_default);
}
var middleware_loader_entry_default = WRAPPED_ENTRY;

// ../../../../../../opt/homebrew/lib/node_modules/wrangler/templates/middleware/middleware-ensure-req-body-drained.ts
var drainBody2 = /* @__PURE__ */ __name(async (request, env, _ctx, middlewareCtx) => {
  try {
    return await middlewareCtx.next(request, env);
  } finally {
    try {
      if (request.body !== null && !request.bodyUsed) {
        const reader = request.body.getReader();
        while (!(await reader.read()).done) {
        }
      }
    } catch (e) {
      console.error("Failed to drain the unused request body.", e);
    }
  }
}, "drainBody");
var middleware_ensure_req_body_drained_default2 = drainBody2;

// ../../../../../../opt/homebrew/lib/node_modules/wrangler/templates/middleware/middleware-miniflare3-json-error.ts
function reduceError2(e) {
  return {
    name: e?.name,
    message: e?.message ?? String(e),
    stack: e?.stack,
    cause: e?.cause === void 0 ? void 0 : reduceError2(e.cause)
  };
}
__name(reduceError2, "reduceError");
var jsonError2 = /* @__PURE__ */ __name(async (request, env, _ctx, middlewareCtx) => {
  try {
    return await middlewareCtx.next(request, env);
  } catch (e) {
    const error = reduceError2(e);
    const body = JSON.stringify(error);
    const headers = {
      "Content-Type": "application/json",
      "MF-Experimental-Error-Stack": "true"
    };
    const encoded = encodeURIComponent(body);
    if (encoded.length <= 8192) {
      headers["MF-Experimental-Error-Stack-Payload"] = encoded;
    }
    return new Response(body, { status: 500, headers });
  }
}, "jsonError");
var middleware_miniflare3_json_error_default2 = jsonError2;

// .wrangler/tmp/bundle-qmSTZl/middleware-insertion-facade.js
var __INTERNAL_WRANGLER_MIDDLEWARE__2 = [
  middleware_ensure_req_body_drained_default2,
  middleware_miniflare3_json_error_default2
];
var middleware_insertion_facade_default2 = middleware_loader_entry_default;

// ../../../../../../opt/homebrew/lib/node_modules/wrangler/templates/middleware/common.ts
var __facade_middleware__2 = [];
function __facade_register__2(...args) {
  __facade_middleware__2.push(...args.flat());
}
__name(__facade_register__2, "__facade_register__");
function __facade_invokeChain__2(request, env, ctx, dispatch, middlewareChain) {
  const [head, ...tail] = middlewareChain;
  const middlewareCtx = {
    dispatch,
    next(newRequest, newEnv) {
      return __facade_invokeChain__2(newRequest, newEnv, ctx, dispatch, tail);
    }
  };
  return head(request, env, ctx, middlewareCtx);
}
__name(__facade_invokeChain__2, "__facade_invokeChain__");
function __facade_invoke__2(request, env, ctx, dispatch, finalMiddleware) {
  return __facade_invokeChain__2(request, env, ctx, dispatch, [
    ...__facade_middleware__2,
    finalMiddleware
  ]);
}
__name(__facade_invoke__2, "__facade_invoke__");

// .wrangler/tmp/bundle-qmSTZl/middleware-loader.entry.ts
var __Facade_ScheduledController__2 = class ___Facade_ScheduledController__2 {
  constructor(scheduledTime, cron, noRetry) {
    this.scheduledTime = scheduledTime;
    this.cron = cron;
    this.#noRetry = noRetry;
  }
  scheduledTime;
  cron;
  static {
    __name(this, "__Facade_ScheduledController__");
  }
  #noRetry;
  noRetry() {
    if (!(this instanceof ___Facade_ScheduledController__2)) {
      throw new TypeError("Illegal invocation");
    }
    this.#noRetry();
  }
};
function wrapExportedHandler2(worker) {
  if (__INTERNAL_WRANGLER_MIDDLEWARE__2 === void 0 || __INTERNAL_WRANGLER_MIDDLEWARE__2.length === 0) {
    return worker;
  }
  for (const middleware of __INTERNAL_WRANGLER_MIDDLEWARE__2) {
    __facade_register__2(middleware);
  }
  const fetchDispatcher = /* @__PURE__ */ __name(function(request, env, ctx) {
    if (worker.fetch === void 0) {
      throw new Error("Handler does not export a fetch() function.");
    }
    return worker.fetch(request, env, ctx);
  }, "fetchDispatcher");
  return {
    ...worker,
    fetch(request, env, ctx) {
      const dispatcher = /* @__PURE__ */ __name(function(type, init) {
        if (type === "scheduled" && worker.scheduled !== void 0) {
          const controller = new __Facade_ScheduledController__2(
            Date.now(),
            init.cron ?? "",
            () => {
            }
          );
          return worker.scheduled(controller, env, ctx);
        }
      }, "dispatcher");
      return __facade_invoke__2(request, env, ctx, dispatcher, fetchDispatcher);
    }
  };
}
__name(wrapExportedHandler2, "wrapExportedHandler");
function wrapWorkerEntrypoint2(klass) {
  if (__INTERNAL_WRANGLER_MIDDLEWARE__2 === void 0 || __INTERNAL_WRANGLER_MIDDLEWARE__2.length === 0) {
    return klass;
  }
  for (const middleware of __INTERNAL_WRANGLER_MIDDLEWARE__2) {
    __facade_register__2(middleware);
  }
  return class extends klass {
    #fetchDispatcher = /* @__PURE__ */ __name((request, env, ctx) => {
      this.env = env;
      this.ctx = ctx;
      if (super.fetch === void 0) {
        throw new Error("Entrypoint class does not define a fetch() function.");
      }
      return super.fetch(request);
    }, "#fetchDispatcher");
    #dispatcher = /* @__PURE__ */ __name((type, init) => {
      if (type === "scheduled" && super.scheduled !== void 0) {
        const controller = new __Facade_ScheduledController__2(
          Date.now(),
          init.cron ?? "",
          () => {
          }
        );
        return super.scheduled(controller);
      }
    }, "#dispatcher");
    fetch(request) {
      return __facade_invoke__2(
        request,
        this.env,
        this.ctx,
        this.#dispatcher,
        this.#fetchDispatcher
      );
    }
  };
}
__name(wrapWorkerEntrypoint2, "wrapWorkerEntrypoint");
var WRAPPED_ENTRY2;
if (typeof middleware_insertion_facade_default2 === "object") {
  WRAPPED_ENTRY2 = wrapExportedHandler2(middleware_insertion_facade_default2);
} else if (typeof middleware_insertion_facade_default2 === "function") {
  WRAPPED_ENTRY2 = wrapWorkerEntrypoint2(middleware_insertion_facade_default2);
}
var middleware_loader_entry_default2 = WRAPPED_ENTRY2;
export {
  __INTERNAL_WRANGLER_MIDDLEWARE__2 as __INTERNAL_WRANGLER_MIDDLEWARE__,
  middleware_loader_entry_default2 as default
};
//# sourceMappingURL=functionsWorker-0.5223417484327746.js.map
