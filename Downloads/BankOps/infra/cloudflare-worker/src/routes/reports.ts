/**
 * Reports routes – mirror FastAPI /reports/*
 */

import { Hono } from "hono";
import { Env } from "../index";
import { db } from "../db/client";

export const reportsRoutes = new Hono<{ Bindings: Env }>();

interface MovementRow {
  item_id: string;
  quantity_signed: string;
  posted_at: string | null;
}

interface InvReqRow {
  kind: string;
  created_at: string | null;
}

interface JournalEntryRow {
  journal_id: string;
  posted_at: string | null;
  account_code: string;
  debit: string;
  credit: string;
}

interface BalanceItemRow {
  item_id: string;
  sku: string;
  name: string;
  quantity: string;
}

function toDayString(isoOrNull: string | null): string {
  if (!isoOrNull) return new Date().toISOString().slice(0, 10);
  return new Date(isoOrNull).toISOString().slice(0, 10);
}

async function computeInventoryDaily(
  databaseUrl: string,
  dateFrom?: string,
  dateTo?: string
) {
  const moves = await db<MovementRow>(databaseUrl)`
    SELECT item_id, quantity_signed, posted_at FROM inv_movements
  `;
  const reqs = await db<InvReqRow>(databaseUrl)`
    SELECT kind, created_at FROM inv_requests
  `;

  const buckets = new Map<
    string,
    { inbound: number; outbound: number; net: number; receipt: number; issue: number }
  >();

  const ensure = (day: string) => {
    let b = buckets.get(day);
    if (!b) {
      b = { inbound: 0, outbound: 0, net: 0, receipt: 0, issue: 0 };
      buckets.set(day, b);
    }
    return b;
  };

  for (const m of moves) {
    const day = toDayString(m.posted_at);
    if (dateFrom && day < dateFrom.slice(0, 10)) continue;
    if (dateTo && day > dateTo.slice(0, 10)) continue;
    const b = ensure(day);
    const qty = Number(m.quantity_signed);
    if (qty >= 0) b.inbound += qty;
    else b.outbound += -qty;
    b.net += qty;
  }

  for (const r of reqs) {
    const day = toDayString(r.created_at);
    if (dateFrom && day < dateFrom.slice(0, 10)) continue;
    if (dateTo && day > dateTo.slice(0, 10)) continue;
    const b = ensure(day);
    if (r.kind === "receipt") b.receipt += 1;
    else if (r.kind === "issue") b.issue += 1;
  }

  return Array.from(buckets.entries())
    .sort(([a], [b]) => a.localeCompare(b))
    .map(([bucket, v]) => ({
      bucket,
      inbound_qty: String(v.inbound),
      outbound_qty: String(v.outbound),
      net_qty: String(v.net),
      receipt_count: v.receipt,
      issue_count: v.issue,
    }));
}

async function computeCashflowDaily(
  databaseUrl: string,
  dateFrom?: string,
  dateTo?: string
) {
  const rows = await db<JournalEntryRow>(databaseUrl)`
    SELECT j.id as journal_id, j.posted_at, e.account_code, e.debit, e.credit
    FROM cash_journals j
    JOIN cash_entries e ON e.journal_id = j.id
  `;

  const buckets = new Map<
    string,
    { inAmt: number; outAmt: number; journals: Set<string> }
  >();

  for (const r of rows) {
    const day = toDayString(r.posted_at);
    if (dateFrom && day < dateFrom.slice(0, 10)) continue;
    if (dateTo && day > dateTo.slice(0, 10)) continue;
    let b = buckets.get(day);
    if (!b) {
      b = { inAmt: 0, outAmt: 0, journals: new Set<string>() };
      buckets.set(day, b);
    }
    b.journals.add(r.journal_id);
    if (r.account_code.startsWith("SIM_CASH:")) {
      b.inAmt += Number(r.debit);
      b.outAmt += Number(r.credit);
    }
  }

  return Array.from(buckets.entries())
    .sort(([a], [b]) => a.localeCompare(b))
    .map(([bucket, v]) => ({
      bucket,
      total_in: v.inAmt.toFixed(2),
      total_out: v.outAmt.toFixed(2),
      net: (v.inAmt - v.outAmt).toFixed(2),
      journal_count: v.journals.size,
    }));
}

// GET /reports/inventory-daily
reportsRoutes.get("/inventory-daily", async (c) => {
  const buckets = await computeInventoryDaily(
    c.env.DATABASE_URL,
    c.req.query("date_from"),
    c.req.query("date_to")
  );
  return c.json({ buckets, unit: "quantity", simulation: true });
});

// GET /reports/inventory-monthly
reportsRoutes.get("/inventory-monthly", async (c) => {
  const daily = await computeInventoryDaily(
    c.env.DATABASE_URL,
    c.req.query("date_from"),
    c.req.query("date_to")
  );
  const months = new Map<
    string,
    { inbound: number; outbound: number; net: number; receipt: number; issue: number }
  >();
  for (const b of daily) {
    const key = b.bucket.slice(0, 7);
    const m = months.get(key) ?? {
      inbound: 0,
      outbound: 0,
      net: 0,
      receipt: 0,
      issue: 0,
    };
    m.inbound += Number(b.inbound_qty);
    m.outbound += Number(b.outbound_qty);
    m.net += Number(b.net_qty);
    m.receipt += b.receipt_count;
    m.issue += b.issue_count;
    months.set(key, m);
  }
  const buckets = Array.from(months.entries())
    .sort(([a], [b]) => a.localeCompare(b))
    .map(([bucket, v]) => ({
      bucket,
      inbound_qty: String(v.inbound),
      outbound_qty: String(v.outbound),
      net_qty: String(v.net),
      receipt_count: v.receipt,
      issue_count: v.issue,
    }));
  return c.json({ buckets, unit: "quantity", simulation: true });
});

// GET /reports/cashflow-daily
reportsRoutes.get("/cashflow-daily", async (c) => {
  const buckets = await computeCashflowDaily(
    c.env.DATABASE_URL,
    c.req.query("date_from"),
    c.req.query("date_to")
  );
  return c.json({ buckets, currency: "VND", simulation: true });
});

// GET /reports/cashflow-monthly
reportsRoutes.get("/cashflow-monthly", async (c) => {
  const daily = await computeCashflowDaily(
    c.env.DATABASE_URL,
    c.req.query("date_from"),
    c.req.query("date_to")
  );
  const months = new Map<
    string,
    { inAmt: number; outAmt: number; count: number }
  >();
  for (const b of daily) {
    const key = b.bucket.slice(0, 7);
    const m = months.get(key) ?? { inAmt: 0, outAmt: 0, count: 0 };
    m.inAmt += Number(b.total_in);
    m.outAmt += Number(b.total_out);
    m.count += b.journal_count;
    months.set(key, m);
  }
  const buckets = Array.from(months.entries())
    .sort(([a], [b]) => a.localeCompare(b))
    .map(([bucket, v]) => ({
      bucket,
      total_in: v.inAmt.toFixed(2),
      total_out: v.outAmt.toFixed(2),
      net: (v.inAmt - v.outAmt).toFixed(2),
      journal_count: v.count,
    }));
  return c.json({ buckets, currency: "VND", simulation: true });
});

// GET /reports/stagnation
reportsRoutes.get("/stagnation", async (c) => {
  const watchDays = Number(c.req.query("watch_days") ?? 30);
  const stagnantDays = Number(c.req.query("stagnant_days") ?? 60);
  const criticalDays = Number(c.req.query("critical_days") ?? 90);

  const [balances, moves] = await Promise.all([
    db<BalanceItemRow>(c.env.DATABASE_URL)`
      SELECT b.item_id, i.sku, i.name, b.quantity
      FROM inv_balances b
      JOIN inv_items i ON i.id = b.item_id
      WHERE b.quantity > 0
    `,
    db<MovementRow>(c.env.DATABASE_URL)`
      SELECT item_id, quantity_signed, posted_at
      FROM inv_movements
      WHERE quantity_signed < 0
    `,
  ]);

  const lastIssue = new Map<string, Date>();
  for (const m of moves) {
    if (!m.posted_at) continue;
    const d = new Date(m.posted_at);
    const prev = lastIssue.get(m.item_id);
    if (!prev || d > prev) lastIssue.set(m.item_id, d);
  }

  const now = new Date();
  const items = balances.map((b) => {
    const last = lastIssue.get(b.item_id);
    const days = last
      ? Math.floor((now.getTime() - last.getTime()) / (1000 * 60 * 60 * 24))
      : null;
    let severity = "normal";
    if (days === null || days >= criticalDays) severity = "critical";
    else if (days >= stagnantDays) severity = "stagnant";
    else if (days >= watchDays) severity = "watch";
    return {
      item_id: b.item_id,
      sku: b.sku,
      name: b.name,
      quantity: String(b.quantity),
      days_since_last_issue: days,
      severity,
    };
  });

  return c.json({
    items,
    thresholds: {
      watch: watchDays,
      stagnant: stagnantDays,
      critical: criticalDays,
    },
    simulation: true,
  });
});
