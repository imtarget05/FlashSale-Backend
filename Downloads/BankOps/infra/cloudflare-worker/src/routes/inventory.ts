/**
 * Inventory routes – mirror FastAPI /inventory/*
 */

import { Hono } from "hono";
import { Env } from "../index";
import { db } from "../db/client";
import { JwtPayload } from "../middleware/auth";

export const inventoryRoutes = new Hono<{ Bindings: Env }>();

function getUser(c: { get: (key: string) => unknown }): JwtPayload {
  return c.get("user") as JwtPayload;
}

interface InvItem {
  id: string;
  sku: string;
  name: string;
  unit: string;
  tracked_serial: boolean;
  active: boolean;
}

interface InvLocation {
  id: string;
  code: string;
  name: string;
  active: boolean;
}

interface InvBalance {
  item_id: string;
  location_id: string;
  quantity: string;
  version: number;
}

interface InvAsset {
  id: string;
  serial: string;
  item_id: string;
  location_id: string;
  assignee_demo: string | null;
  status: string;
}

interface InvMovement {
  id: string;
  request_id: string;
  line_id: string;
  item_id: string;
  location_id: string;
  quantity_signed: string;
  posted_at: string | null;
  created_at: string;
}

interface InvRequestRow {
  id: string;
  kind: string;
  status: string;
  maker_id: string;
  approver_id: string | null;
  source_id: string | null;
  dest_id: string | null;
  reason: string | null;
  version: number;
  created_at: string;
}

interface InvLineRow {
  id: string;
  request_id: string;
  item_id: string;
  quantity: string;
  serial: string | null;
}

// GET /inventory/items
inventoryRoutes.get("/items", async (c) => {
  const items = await db<InvItem>(c.env.DATABASE_URL)`
    SELECT id, sku, name, unit, tracked_serial, active
    FROM inv_items
    ORDER BY name ASC
  `;
  return c.json({ items, simulation: true });
});

// GET /inventory/locations
inventoryRoutes.get("/locations", async (c) => {
  const locations = await db<InvLocation>(c.env.DATABASE_URL)`
    SELECT id, code, name, active FROM inv_locations ORDER BY code ASC
  `;
  return c.json({ locations, simulation: true });
});

// GET /inventory/balances
inventoryRoutes.get("/balances", async (c) => {
  const locationId = c.req.query("location_id");
  const sql = db<InvBalance>(c.env.DATABASE_URL);
  const balances = locationId
    ? await sql`
        SELECT item_id, location_id, quantity, version
        FROM inv_balances
        WHERE location_id = ${locationId}::uuid
      `
    : await sql`
        SELECT item_id, location_id, quantity, version FROM inv_balances
      `;
  return c.json({ balances, simulation: true });
});

// GET /inventory/assets
inventoryRoutes.get("/assets", async (c) => {
  const assets = await db<InvAsset>(c.env.DATABASE_URL)`
    SELECT id, serial, item_id, location_id, assignee_demo, status
    FROM inv_assets
    ORDER BY serial ASC
  `;
  return c.json({ assets, simulation: true });
});

// GET /inventory/movements
inventoryRoutes.get("/movements", async (c) => {
  const movements = await db<InvMovement>(c.env.DATABASE_URL)`
    SELECT id, request_id, line_id, item_id, location_id, quantity_signed, posted_at, created_at
    FROM inv_movements
    ORDER BY created_at DESC
    LIMIT 200
  `;
  return c.json({ movements, simulation: true });
});

async function formatStockRequest(databaseUrl: string, r: InvRequestRow) {
  const lines = await db<InvLineRow>(databaseUrl)`
    SELECT id, request_id, item_id, quantity, serial
    FROM inv_request_lines
    WHERE request_id = ${r.id}::uuid
  `;
  return {
    id: r.id,
    kind: r.kind,
    status: r.status,
    maker_id: r.maker_id,
    approver_id: r.approver_id,
    source_id: r.source_id,
    dest_id: r.dest_id,
    reason: r.reason,
    version: r.version,
    created_at: r.created_at,
    lines: lines.map((l) => ({
      item_id: l.item_id,
      quantity: String(l.quantity),
      serial: l.serial,
    })),
    simulation: true,
  };
}

// GET /inventory/requests
inventoryRoutes.get("/requests", async (c) => {
  const status = c.req.query("status");
  const sql = db<InvRequestRow>(c.env.DATABASE_URL);
  const rows = status
    ? await sql`
        SELECT id, kind, status, maker_id, approver_id,
               source_id, dest_id, reason, version, created_at
        FROM inv_requests
        WHERE status = ${status}
        ORDER BY created_at DESC
        LIMIT 100
      `
    : await sql`
        SELECT id, kind, status, maker_id, approver_id,
               source_id, dest_id, reason, version, created_at
        FROM inv_requests
        ORDER BY created_at DESC
        LIMIT 100
      `;
  const requests = await Promise.all(
    rows.map((r) => formatStockRequest(c.env.DATABASE_URL, r))
  );
  return c.json({ requests, simulation: true });
});

// GET /inventory/requests/:id
inventoryRoutes.get("/requests/:id", async (c) => {
  const id = c.req.param("id");
  const rows = await db<InvRequestRow>(c.env.DATABASE_URL)`
    SELECT id, kind, status, maker_id, approver_id,
           source_id, dest_id, reason, version, created_at
    FROM inv_requests
    WHERE id = ${id}::uuid
    LIMIT 1
  `;
  if (!rows.length) {
    return c.json({ error: { code: "NOT_FOUND", message: "Request not found" } }, 404);
  }
  return c.json(await formatStockRequest(c.env.DATABASE_URL, rows[0]));
});

// POST /inventory/requests
inventoryRoutes.post("/requests", async (c) => {
  const user = getUser(c);
  const body = await c.req.json<{
    kind: string;
    source_id?: string | null;
    dest_id?: string | null;
    reason?: string | null;
    lines: Array<{ item_id: string; quantity: string | number; serial?: string | null }>;
  }>();

  if (!body.kind || !Array.isArray(body.lines) || body.lines.length === 0) {
    return c.json(
      { error: { code: "VALIDATION_ERROR", message: "kind and lines are required" } },
      422
    );
  }

  const reqId = crypto.randomUUID();
  const sql = db(c.env.DATABASE_URL);
  await sql`
    INSERT INTO inv_requests
      (id, kind, status, maker_id, source_id, dest_id, reason, version, created_at, updated_at, created_by)
    VALUES
      (${reqId}::uuid, ${body.kind}, 'draft', ${user.sub}::uuid,
       ${body.source_id ?? null}, ${body.dest_id ?? null}, ${body.reason ?? null},
       1, NOW(), NOW(), ${user.sub}::uuid)
  `;

  for (const line of body.lines) {
    const lineId = crypto.randomUUID();
    await sql`
      INSERT INTO inv_request_lines
        (id, request_id, item_id, quantity, serial)
      VALUES
        (${lineId}::uuid, ${reqId}::uuid, ${line.item_id}::uuid, ${String(line.quantity)},
         ${line.serial ?? null})
    `;
  }

  const rows = await db<InvRequestRow>(c.env.DATABASE_URL)`
    SELECT id, kind, status, maker_id, approver_id,
           source_id, dest_id, reason, version, created_at
    FROM inv_requests WHERE id = ${reqId}::uuid LIMIT 1
  `;
  return c.json(await formatStockRequest(c.env.DATABASE_URL, rows[0]), 201);
});

// POST /inventory/requests/:id/submit
inventoryRoutes.post("/requests/:id/submit", async (c) => {
  const id = c.req.param("id");
  const rows = await db<InvRequestRow>(c.env.DATABASE_URL)`
    SELECT id, kind, status, maker_id, approver_id,
           source_id, dest_id, reason, version, created_at
    FROM inv_requests WHERE id = ${id}::uuid LIMIT 1
  `;
  if (!rows.length) {
    return c.json({ error: { code: "NOT_FOUND", message: "Request not found" } }, 404);
  }
  const req = rows[0];
  if (req.status !== "draft") {
    return c.json(
      { error: { code: "INVALID_STATE", message: "Only draft can be submitted" } },
      409
    );
  }
  await db(c.env.DATABASE_URL)`
    UPDATE inv_requests
    SET status = 'submitted', submitted_at = NOW(), version = version + 1, updated_at = NOW()
    WHERE id = ${id}::uuid
  `;
  const updated = await db<InvRequestRow>(c.env.DATABASE_URL)`
    SELECT id, kind, status, maker_id, approver_id,
           source_id, dest_id, reason, version, created_at
    FROM inv_requests WHERE id = ${id}::uuid LIMIT 1
  `;
  return c.json(await formatStockRequest(c.env.DATABASE_URL, updated[0]));
});

// POST /inventory/requests/:id/approve
inventoryRoutes.post("/requests/:id/approve", async (c) => {
  const user = getUser(c);
  const id = c.req.param("id");
  const rows = await db<InvRequestRow>(c.env.DATABASE_URL)`
    SELECT id, kind, status, maker_id, approver_id,
           source_id, dest_id, reason, version, created_at
    FROM inv_requests WHERE id = ${id}::uuid LIMIT 1
  `;
  if (!rows.length) {
    return c.json({ error: { code: "NOT_FOUND", message: "Request not found" } }, 404);
  }
  const req = rows[0];
  if (req.status !== "submitted") {
    return c.json(
      { error: { code: "INVALID_STATE", message: "Only submitted requests can be approved" } },
      409
    );
  }
  if (req.maker_id === user.sub) {
    return c.json(
      { error: { code: "MAKER_CHECKER_VIOLATION", message: "Maker cannot approve own request" } },
      403
    );
  }

  const lines = await db<InvLineRow>(c.env.DATABASE_URL)`
    SELECT id, request_id, item_id, quantity, serial
    FROM inv_request_lines WHERE request_id = ${id}::uuid
  `;
  const sql = db(c.env.DATABASE_URL);

  for (const line of lines) {
    const qty = Number(line.quantity);
    if (req.kind === "issue" || req.kind === "transfer") {
      const srcLoc = req.source_id!;
      await sql`
        INSERT INTO inv_movements
          (id, request_id, line_id, item_id, location_id, quantity_signed, posted_at, created_at, updated_at)
        VALUES
          (${crypto.randomUUID()}::uuid, ${id}::uuid, ${line.id}::uuid, ${line.item_id}::uuid, ${srcLoc}::uuid, ${-qty}, NOW(), NOW(), NOW())
      `;
      await sql`
        UPDATE inv_balances
        SET quantity = quantity - ${qty}, version = version + 1
        WHERE item_id = ${line.item_id}::uuid AND location_id = ${srcLoc}::uuid
      `;
    }
    if (req.kind === "receipt" || req.kind === "transfer") {
      const dstLoc = req.dest_id!;
      await sql`
        INSERT INTO inv_movements
          (id, request_id, line_id, item_id, location_id, quantity_signed, posted_at, created_at, updated_at)
        VALUES
          (${crypto.randomUUID()}::uuid, ${id}::uuid, ${line.id}::uuid, ${line.item_id}::uuid, ${dstLoc}::uuid, ${qty}, NOW(), NOW(), NOW())
      `;
      await sql`
        INSERT INTO inv_balances (location_id, item_id, quantity, version)
        VALUES (${dstLoc}::uuid, ${line.item_id}::uuid, ${qty}, 1)
        ON CONFLICT (location_id, item_id) DO UPDATE
        SET quantity = inv_balances.quantity + ${qty},
            version = inv_balances.version + 1
      `;
    }
  }

  await sql`
    UPDATE inv_requests
    SET status = 'posted', approver_id = ${user.sub}::uuid,
        version = version + 1, updated_at = NOW()
    WHERE id = ${id}::uuid
  `;
  const updated = await db<InvRequestRow>(c.env.DATABASE_URL)`
    SELECT id, kind, status, maker_id, approver_id,
           source_id, dest_id, reason, version, created_at
    FROM inv_requests WHERE id = ${id}::uuid LIMIT 1
  `;
  return c.json(await formatStockRequest(c.env.DATABASE_URL, updated[0]));
});

// POST /inventory/requests/:id/reject
inventoryRoutes.post("/requests/:id/reject", async (c) => {
  const user = getUser(c);
  const id = c.req.param("id");
  const rows = await db<InvRequestRow>(c.env.DATABASE_URL)`
    SELECT id, kind, status, maker_id, approver_id,
           source_id, dest_id, reason, version, created_at
    FROM inv_requests WHERE id = ${id}::uuid LIMIT 1
  `;
  if (!rows.length) {
    return c.json({ error: { code: "NOT_FOUND", message: "Request not found" } }, 404);
  }
  const req = rows[0];
  if (req.status !== "submitted") {
    return c.json(
      { error: { code: "INVALID_STATE", message: "Only submitted requests can be rejected" } },
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
    UPDATE inv_requests
    SET status = 'rejected', approver_id = ${user.sub}::uuid, version = version + 1, updated_at = NOW()
    WHERE id = ${id}::uuid
  `;
  const updated = await db<InvRequestRow>(c.env.DATABASE_URL)`
    SELECT id, kind, status, maker_id, approver_id,
           source_id, dest_id, reason, version, created_at
    FROM inv_requests WHERE id = ${id}::uuid LIMIT 1
  `;
  return c.json(await formatStockRequest(c.env.DATABASE_URL, updated[0]));
});
