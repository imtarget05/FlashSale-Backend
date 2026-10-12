/**
 * Integrations routes – mirror FastAPI /integrations/*
 */

import { Hono } from "hono";
import { Env } from "../index";
import { db } from "../db/client";
import { JwtPayload } from "../middleware/auth";

export const integrationsRoutes = new Hono<{ Bindings: Env }>();

function getUser(c: { get: (key: string) => unknown }): JwtPayload {
  return c.get("user") as JwtPayload;
}

interface ConnectionRow {
  id: string;
  name: string;
  type: string;
  status: string;
}

interface RunRow {
  id: string;
  connection_id: string | null;
  status: string;
  valid_count: number;
  invalid_count: number;
  mapping_version: number;
  created_at: string;
}

interface StagingRow {
  row_index: number;
  source_row_key: string;
  raw_json: Record<string, unknown>;
  valid: boolean;
  errors_json: string[];
}

// GET /integrations/connections
integrationsRoutes.get("/connections", async (c) => {
  try {
    const connections = await db<ConnectionRow>(c.env.DATABASE_URL)`
      SELECT id, name, type, status FROM int_connections ORDER BY name ASC
    `;
    return c.json({ connections, simulation: true });
  } catch {
    return c.json({
      connections: [
        {
          id: "conn-csv-default",
          name: "CSV/JSON Staging Importer",
          type: "file_upload",
          status: "connected",
        },
      ],
      simulation: true,
    });
  }
});

// GET /integrations/runs
integrationsRoutes.get("/runs", async (c) => {
  try {
    const runs = await db<RunRow>(c.env.DATABASE_URL)`
      SELECT id, connection_id, status, valid_count, invalid_count, mapping_version, created_at
      FROM int_runs ORDER BY created_at DESC LIMIT 50
    `;
    return c.json({ runs, simulation: true });
  } catch {
    return c.json({ runs: [], simulation: true });
  }
});

// GET /integrations/runs/:id/preview
integrationsRoutes.get("/runs/:id/preview", async (c) => {
  const runId = c.req.param("id");
  const runs = await db<RunRow>(c.env.DATABASE_URL)`
    SELECT id, connection_id, status, valid_count, invalid_count, mapping_version, created_at
    FROM int_runs WHERE id = ${runId}::uuid LIMIT 1
  `;
  if (!runs.length) {
    return c.json({ error: { code: "NOT_FOUND", message: "Run not found" } }, 404);
  }
  const rows = await db<StagingRow>(c.env.DATABASE_URL)`
    SELECT row_index, source_row_key, raw_json, valid, errors_json
    FROM int_staging_rows WHERE run_id = ${runId}::uuid ORDER BY row_index ASC
  `;
  return c.json({
    run_id: runId,
    valid_count: runs[0].valid_count,
    invalid_count: runs[0].invalid_count,
    deduplicated: false,
    mapping_version: runs[0].mapping_version,
    rows: rows.map((r) => ({
      row_index: r.row_index,
      source_row_key: r.source_row_key,
      data: r.raw_json,
      valid: r.valid,
      errors: r.errors_json ?? [],
    })),
    simulation: true,
  });
});

// POST /integrations/google-sheets/fetch
integrationsRoutes.post("/google-sheets/fetch", async (c) => {
  const body = await c.req.json<{ sheet_id?: string; tab?: string }>().catch(() => ({ sheet_id: "", tab: "Sheet1" }));
  const sheetId = (body.sheet_id ?? "").trim();
  if (sheetId.length < 5) {
    return c.json(
      { error: { code: "INVALID_SHEET_ID", message: "sheet_id không hợp lệ" } },
      422
    );
  }
  const runId = crypto.randomUUID();
  return c.json({
    run_id: runId,
    valid_count: 1,
    invalid_count: 0,
    deduplicated: false,
    mapping_version: 1,
    rows: [
      {
        row_index: 1,
        source_row_key: `sheet-${sheetId}-1`,
        data: {
          sku: "DEMO-PAPER-A4",
          location_code: "DEMO-HN",
          quantity: "15",
        },
        valid: true,
        errors: [],
      },
    ],
    simulation: true,
  });
});

// POST /integrations/runs/:id/confirm
integrationsRoutes.post("/runs/:id/confirm", async (c) => {
  const user = getUser(c);
  const runId = c.req.param("id");
  return c.json({
    run_id: runId,
    status: "confirmed",
    confirmed_by: user.sub,
    draft_requests: [],
    simulation: true,
  });
});
