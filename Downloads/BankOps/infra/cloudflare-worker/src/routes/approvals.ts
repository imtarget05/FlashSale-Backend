/**
 * Approvals routes – mirror FastAPI /approvals/*
 */

import { Hono } from "hono";
import { Env } from "../index";
import { db } from "../db/client";

export const approvalsRoutes = new Hono<{ Bindings: Env }>();

interface InvPendingRow {
  id: string;
  kind: string;
  status: string;
  maker_id: string;
  reason: string | null;
  maker_name: string | null;
}

interface CashPendingRow {
  id: string;
  kind: string;
  status: string;
  maker_id: string;
  amount: string;
  currency: string;
  reason: string | null;
  maker_name: string | null;
}

// GET /approvals/inbox – danh sách request đang chờ duyệt (kho + quỹ)
approvalsRoutes.get("/inbox", async (c) => {
  const [invRows, cashRows] = await Promise.all([
    db<InvPendingRow>(c.env.DATABASE_URL)`
      SELECT r.id, r.kind, r.status, r.maker_id, r.reason,
             COALESCE(u.display_name_demo, u.username, r.maker_id::text) as maker_name
      FROM inv_requests r
      LEFT JOIN iam_users u ON u.id = r.maker_id
      WHERE r.status = 'submitted'
      ORDER BY r.created_at DESC
    `,
    db<CashPendingRow>(c.env.DATABASE_URL)`
      SELECT r.id, r.kind, r.status, r.maker_id, r.amount, b.currency, r.reason,
             COALESCE(u.display_name_demo, u.username, r.maker_id::text) as maker_name
      FROM cash_requests r
      LEFT JOIN iam_users u ON u.id = r.maker_id
      LEFT JOIN cash_boxes b ON b.id = r.cashbox_id
      WHERE r.status = 'submitted'
      ORDER BY r.created_at DESC
    `,
  ]);

  const approvals = [
    ...invRows.map((r) => ({
      id: r.id,
      kind: "inventory",
      sub_kind: r.kind,
      status: r.status,
      maker_id: r.maker_id,
      maker: r.maker_name ?? r.maker_id,
      impact: `Kho (${r.kind})`,
      detail: r.reason || r.id,
    })),
    ...cashRows.map((r) => {
      const sign = r.kind === "payment" ? "-" : "+";
      return {
        id: r.id,
        kind: "cash",
        sub_kind: r.kind,
        status: r.status,
        maker_id: r.maker_id,
        maker: r.maker_name ?? r.maker_id,
        impact: `${sign}${r.amount} ${r.currency ?? "VND"}`,
        detail: r.reason || r.id,
      };
    }),
  ];

  return c.json({ approvals, total: approvals.length, simulation: true });
});
