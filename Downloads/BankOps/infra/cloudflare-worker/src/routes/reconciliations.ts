/**
 * Reconciliations routes – mirror FastAPI /reconciliations
 */

import { Hono } from "hono";
import { Env } from "../index";
import { db } from "../db/client";

export const reconciliationsRoutes = new Hono<{ Bindings: Env }>();

interface ReconRunRow {
  id: string;
  scope: string;
  status: string;
  auto_fixed: boolean;
  created_at: string;
}

interface ReconExcRow {
  subject: string;
  expected_json: Record<string, unknown>;
  actual_json: Record<string, unknown>;
}

// GET /reconciliations
reconciliationsRoutes.get("/", async (c) => {
  try {
    const runs = await db<ReconRunRow>(c.env.DATABASE_URL)`
      SELECT id, scope, status, auto_fixed, created_at
      FROM recon_runs
      ORDER BY created_at DESC
      LIMIT 50
    `;
    const out = await Promise.all(
      runs.map(async (r) => {
        const excs = await db<ReconExcRow>(c.env.DATABASE_URL)`
          SELECT subject, expected_json, actual_json
          FROM recon_exceptions
          WHERE run_id = ${r.id}::uuid
        `;
        return {
          id: r.id,
          scope: r.scope,
          status: r.status,
          auto_fixed: r.auto_fixed,
          created_at: r.created_at,
          exceptions: excs.map((e) => ({
            subject: e.subject,
            expected: e.expected_json,
            actual: e.actual_json,
          })),
        };
      })
    );
    return c.json({ runs: out, next_cursor: null, limit: 50, simulation: true });
  } catch {
    return c.json({ runs: [], next_cursor: null, limit: 50, simulation: true });
  }
});

// POST /reconciliations
reconciliationsRoutes.post("/", async (c) => {
  const body = await c.req.json<{ scope?: string }>().catch(() => ({ scope: "cash" }));
  const scope = (body.scope ?? "cash").toLowerCase();
  const runId = crypto.randomUUID();
  try {
    await db(c.env.DATABASE_URL)`
      INSERT INTO recon_runs (id, scope, status, auto_fixed, created_at, updated_at)
      VALUES (${runId}::uuid, ${scope}, 'matched', false, NOW(), NOW())
    `;
  } catch {
    // fallback if recon_runs table is not migrated yet
  }
  return c.json(
    {
      id: runId,
      scope,
      status: "matched",
      auto_fixed: false,
      exceptions: [],
      simulation: true,
    },
    201
  );
});
