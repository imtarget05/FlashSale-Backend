/**
 * Cash Simulation routes – mirror FastAPI /cash-sim/*
 * Module cốt lõi cho nghiệp vụ thu chi mô phỏng.
 */

import { Hono } from "hono";
import { Env } from "../index";
import { db } from "../db/client";
import { JwtPayload } from "../middleware/auth";

export const cashSimRoutes = new Hono<{ Bindings: Env }>();

function getUser(c: { get: (key: string) => unknown }): JwtPayload {
  return c.get("user") as JwtPayload;
}

interface CashBox {
  id: string;
  code: string;
  name: string;
  currency: string;
  active: boolean;
}
interface CashBalance {
  cashbox_id: string;
  cashbox_code: string;
  cashbox_name: string;
  currency: string;
  amount: string;
  version: number;
}
interface CashRequest {
  id: string;
  cashbox_id: string;
  kind: string;
  amount: string;
  currency: string;
  status: string;
  maker_id: string;
  approver_id: string | null;
  reason: string | null;
  version: number;
  submitted_at: string | null;
  created_at: string;
  maker_name: string;
  cashbox_code: string;
  cashbox_name: string;
}
interface RequestRow {
  id: string;
  status: string;
  version: number;
  maker_id: string;
  approver_id: string | null;
  cashbox_id: string;
  kind: string;
  amount: string;
  reason: string | null;
  created_at: string;
}
interface JournalEntry {
  id: string;
  request_id: string;
  posted_at: string;
  correlation_id: string;
  kind: string;
  amount: string;
  currency: string;
  reason: string | null;
  cashbox_id: string;
  cashbox_code: string;
}

// GET /cash-sim/boxes
cashSimRoutes.get("/boxes", async (c) => {
  const boxes = await db<CashBox>(c.env.DATABASE_URL)`
    SELECT id, code, name, currency, active FROM cash_boxes
    WHERE active = true ORDER BY code ASC
  `;
  return c.json({ boxes, simulation: true });
});

// GET /cash-sim/balances
cashSimRoutes.get("/balances", async (c) => {
  const balances = await db<CashBalance>(c.env.DATABASE_URL)`
    SELECT cb.cashbox_id, cb.amount, cb.version,
           b.code as cashbox_code, b.name as cashbox_name, b.currency
    FROM cash_balances cb JOIN cash_boxes b ON b.id = cb.cashbox_id
    ORDER BY b.code ASC
  `;
  return c.json({ balances, simulation: true });
});

// GET /cash-sim/requests
cashSimRoutes.get("/requests", async (c) => {
  const status = c.req.query("status");
  const query = db<CashRequest>(c.env.DATABASE_URL);
  const requests = status
    ? await query`
        SELECT r.id, r.cashbox_id, r.kind, r.amount, b.currency, r.status,
               r.maker_id, r.approver_id, r.reason, r.version,
               r.submitted_at, r.created_at,
               u.display_name_demo as maker_name,
               b.code as cashbox_code, b.name as cashbox_name
        FROM cash_requests r
        JOIN iam_users u ON u.id = r.maker_id
        JOIN cash_boxes b ON b.id = r.cashbox_id
        WHERE r.status = ${status}
        ORDER BY r.created_at DESC LIMIT 100
      `
    : await query`
        SELECT r.id, r.cashbox_id, r.kind, r.amount, b.currency, r.status,
               r.maker_id, r.approver_id, r.reason, r.version,
               r.submitted_at, r.created_at,
               u.display_name_demo as maker_name,
               b.code as cashbox_code, b.name as cashbox_name
        FROM cash_requests r
        JOIN iam_users u ON u.id = r.maker_id
        JOIN cash_boxes b ON b.id = r.cashbox_id
        ORDER BY r.created_at DESC LIMIT 100
      `;
  return c.json({ requests, total: requests.length, simulation: true });
});

// GET /cash-sim/requests/:id
cashSimRoutes.get("/requests/:id", async (c) => {
  const requestId = c.req.param("id");
  const rows = await db<RequestRow>(c.env.DATABASE_URL)`
    SELECT id, status, version, maker_id, approver_id, cashbox_id, kind, amount, reason, created_at
    FROM cash_requests WHERE id = ${requestId}::uuid LIMIT 1
  `;
  if (!rows.length) {
    return c.json({ error: { code: "NOT_FOUND", message: "Request not found" } }, 404);
  }
  return c.json({ ...rows[0], amount: String(rows[0].amount), currency: "VND", simulation: true });
});

// POST /cash-sim/requests
cashSimRoutes.post("/requests", async (c) => {
  const user = getUser(c);
  const body = await c.req.json<{
    cashbox_id: string;
    kind: "receipt" | "payment" | "adjustment";
    amount: number | string;
    currency?: string;
    reason?: string;
  }>();

  if (!body.cashbox_id || !body.kind || body.amount == null) {
    return c.json(
      { error: { code: "VALIDATION_ERROR", message: "cashbox_id, kind, amount are required" } },
      422
    );
  }

  const newId = crypto.randomUUID();
  await db(c.env.DATABASE_URL)`
    INSERT INTO cash_requests
      (id, cashbox_id, kind, amount, status, maker_id, reason, version, created_by, created_at, updated_at)
    VALUES
      (${newId}::uuid, ${body.cashbox_id}::uuid, ${body.kind}, ${String(body.amount)},
       'draft', ${user.sub}::uuid, ${body.reason ?? null}, 1, ${user.sub}::uuid, NOW(), NOW())
  `;
  return c.json(
    {
      id: newId,
      cashbox_id: body.cashbox_id,
      kind: body.kind,
      amount: String(body.amount),
      currency: body.currency ?? "VND",
      status: "draft",
      maker_id: user.sub,
      approver_id: null,
      reason: body.reason ?? null,
      version: 1,
      simulation: true,
    },
    201
  );
});

// POST /cash-sim/requests/:id/submit
cashSimRoutes.post("/requests/:id/submit", async (c) => {
  const requestId = c.req.param("id");
  const rows = await db<RequestRow>(c.env.DATABASE_URL)`
    SELECT id, status, version, maker_id, approver_id, cashbox_id, kind, amount, reason, created_at
    FROM cash_requests WHERE id = ${requestId}::uuid LIMIT 1
  `;
  if (!rows.length) {
    return c.json({ error: { code: "NOT_FOUND", message: "Request not found" } }, 404);
  }

  const req = rows[0];
  if (req.status !== "draft") {
    return c.json(
      { error: { code: "INVALID_STATE", message: `Cannot submit: ${req.status}` } },
      409
    );
  }

  await db(c.env.DATABASE_URL)`
    UPDATE cash_requests
    SET status = 'submitted', submitted_at = NOW(), version = version + 1, updated_at = NOW()
    WHERE id = ${requestId}::uuid
  `;
  return c.json({
    ...req,
    amount: String(req.amount),
    currency: "VND",
    status: "submitted",
    version: req.version + 1,
    simulation: true,
  });
});

// POST /cash-sim/requests/:id/approve
cashSimRoutes.post("/requests/:id/approve", async (c) => {
  const user = getUser(c);
  const requestId = c.req.param("id");

  const rows = await db<RequestRow>(c.env.DATABASE_URL)`
    SELECT id, status, version, maker_id, approver_id, cashbox_id, kind, amount, reason, created_at
    FROM cash_requests WHERE id = ${requestId}::uuid LIMIT 1
  `;
  if (!rows.length) {
    return c.json({ error: { code: "NOT_FOUND", message: "Request not found" } }, 404);
  }

  const req = rows[0];
  if (req.status !== "submitted") {
    return c.json(
      { error: { code: "INVALID_STATE", message: `Cannot approve: ${req.status}` } },
      409
    );
  }
  if (req.maker_id === user.sub) {
    return c.json(
      { error: { code: "MAKER_CHECKER_VIOLATION", message: "Maker cannot approve own request" } },
      403
    );
  }

  const journalId = crypto.randomUUID();
  const correlationId = crypto.randomUUID();
  const amt = Number(req.amount);
  const isReceipt = req.kind === "receipt";
  const delta = isReceipt ? amt : -amt;

  const sql = db(c.env.DATABASE_URL);
  await sql`
    UPDATE cash_requests
    SET status = 'posted', approver_id = ${user.sub}::uuid,
        version = version + 1, updated_at = NOW()
    WHERE id = ${requestId}::uuid
  `;
  await sql`
    INSERT INTO cash_journals (id, request_id, posted_at, correlation_id, created_at, updated_at)
    VALUES (${journalId}::uuid, ${requestId}::uuid, NOW(), ${correlationId}, NOW(), NOW())
  `;
  const cashAccount = `SIM_CASH:${req.cashbox_id}`;
  const offsetAccount = "SIM_OFFSET:DEMO";
  const cashDr = isReceipt ? amt : 0;
  const cashCr = isReceipt ? 0 : amt;
  const offDr = isReceipt ? 0 : amt;
  const offCr = isReceipt ? amt : 0;

  await sql`
    INSERT INTO cash_entries (id, journal_id, account_code, debit, credit)
    VALUES
      (${crypto.randomUUID()}::uuid, ${journalId}::uuid, ${cashAccount}, ${cashDr}, ${cashCr}),
      (${crypto.randomUUID()}::uuid, ${journalId}::uuid, ${offsetAccount}, ${offDr}, ${offCr})
  `;
  await sql`
    INSERT INTO cash_balances (cashbox_id, amount, version)
    VALUES (${req.cashbox_id}::uuid, ${delta}, 1)
    ON CONFLICT (cashbox_id) DO UPDATE
    SET amount = cash_balances.amount + ${delta},
        version = cash_balances.version + 1
  `;
  return c.json({
    ...req,
    amount: String(req.amount),
    currency: "VND",
    status: "posted",
    approver_id: user.sub,
    version: req.version + 1,
    journal_id: journalId,
    simulation: true,
  });
});

// POST /cash-sim/requests/:id/reject
cashSimRoutes.post("/requests/:id/reject", async (c) => {
  const user = getUser(c);
  const requestId = c.req.param("id");

  const rows = await db<RequestRow>(c.env.DATABASE_URL)`
    SELECT id, status, version, maker_id, approver_id, cashbox_id, kind, amount, reason, created_at
    FROM cash_requests WHERE id = ${requestId}::uuid LIMIT 1
  `;
  if (!rows.length) {
    return c.json({ error: { code: "NOT_FOUND", message: "Request not found" } }, 404);
  }
  const req = rows[0];
  if (req.status !== "submitted") {
    return c.json(
      { error: { code: "INVALID_STATE", message: `Cannot reject: ${req.status}` } },
      409
    );
  }
  if (req.maker_id === user.sub) {
    return c.json(
      { error: { code: "MAKER_CHECKER_VIOLATION", message: "Maker cannot reject own request" } },
      403
    );
  }
  await db(c.env.DATABASE_URL)`
    UPDATE cash_requests
    SET status = 'rejected', approver_id = ${user.sub}::uuid, version = version + 1, updated_at = NOW()
    WHERE id = ${requestId}::uuid
  `;
  return c.json({
    ...req,
    amount: String(req.amount),
    currency: "VND",
    status: "rejected",
    approver_id: user.sub,
    version: req.version + 1,
    simulation: true,
  });
});

async function listJournals(databaseUrl: string) {
  const journals = await db<JournalEntry>(databaseUrl)`
    SELECT j.id, j.request_id, j.posted_at, j.correlation_id,
           r.kind, r.amount, b.currency, r.reason, r.cashbox_id, b.code as cashbox_code
    FROM cash_journals j
    JOIN cash_requests r ON r.id = j.request_id
    JOIN cash_boxes b ON b.id = r.cashbox_id
    ORDER BY j.posted_at DESC LIMIT 200
  `;
  return journals.map((j) => ({
    ...j,
    amount: String(j.amount),
  }));
}

// GET /cash-sim/journals (and legacy /journal alias)
cashSimRoutes.get("/journals", async (c) => {
  const journals = await listJournals(c.env.DATABASE_URL);
  return c.json({ journals, total: journals.length, simulation: true });
});

cashSimRoutes.get("/journal", async (c) => {
  const journals = await listJournals(c.env.DATABASE_URL);
  return c.json({ journals, entries: journals, total: journals.length, simulation: true });
});
