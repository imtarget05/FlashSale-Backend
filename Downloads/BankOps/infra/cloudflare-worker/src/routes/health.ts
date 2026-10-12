/**
 * Health routes – mirror /health/live và /health/ready từ FastAPI
 */

import { Hono } from "hono";
import { Env } from "../index";
import { db } from "../db/client";

export const healthRoutes = new Hono<{ Bindings: Env }>();

healthRoutes.get("/live", (c) =>
  c.json({
    status: "alive",
    timestamp: new Date().toISOString(),
    version: "1.0.0",
    runtime: "cloudflare-workers",
  })
);

healthRoutes.get("/ready", async (c) => {
  let dbStatus = "unknown";
  try {
    await db(c.env.DATABASE_URL)`SELECT 1`;
    dbStatus = "ready";
  } catch (err) {
    dbStatus = `not_ready: ${err instanceof Error ? err.message : String(err)}`;
  }

  const isReady = dbStatus === "ready";
  return c.json(
    {
      status: isReady ? "ready" : "degraded",
      timestamp: new Date().toISOString(),
      version: "1.0.0",
      runtime: "cloudflare-workers",
      services: { database: dbStatus },
    },
    isReady ? 200 : 503
  );
});
