/**
 * BankOps Cloudflare Worker – API Gateway
 *
 * Stack: Hono.js + Neon Serverless PostgreSQL + jose JWT
 * Zero cold start: V8 isolate, không container, không Python startup time.
 *
 * Tất cả routes mirror FastAPI backend hiện tại tại /api/v1/*
 */

import { Hono } from "hono";
import { cors } from "hono/cors";
import { logger } from "hono/logger";
import { secureHeaders } from "hono/secure-headers";

import { authMiddleware, verifyToken } from "./middleware/auth";
import { healthRoutes } from "./routes/health";
import { authRoutes } from "./routes/auth";
import { inventoryRoutes } from "./routes/inventory";
import { cashSimRoutes } from "./routes/cash-sim";
import { approvalsRoutes } from "./routes/approvals";
import { activityRoutes } from "./routes/activity";
import { reportsRoutes } from "./routes/reports";
import { integrationsRoutes } from "./routes/integrations";
import { reconciliationsRoutes } from "./routes/reconciliations";
import { db } from "./db/client";

export interface Env {
  // Secrets (qua wrangler secret put)
  DATABASE_URL: string;
  JWT_SECRET: string;
  SESSION_SECRET: string;
  GOOGLE_CLIENT_ID: string;

  // KV namespace
  BANKOPS_KV: KVNamespace;

  // Vars
  APP_ENV: string;
  APP_NAME: string;
  ACCESS_TOKEN_EXPIRE_MINUTES: string;
  REFRESH_TOKEN_EXPIRE_DAYS: string;
}

type AppEnv = { Bindings: Env; Variables: { rid: string } };

const app = new Hono<AppEnv>();

// Allowlist mirror docker-compose ALLOWED_ORIGINS — never echo arbitrary origin with credentials.
const ALLOWED_ORIGINS = [
  "http://localhost:3000",
  "http://localhost:5000",
  "http://localhost:8080",
  "http://127.0.0.1:8080",
];

// ─── Global Middleware ───────────────────────────────────────────────────────

app.use("*", logger());
app.use("*", secureHeaders());
app.use(
  "*",
  cors({
    origin: (o) => (ALLOWED_ORIGINS.includes(o) ? o : ALLOWED_ORIGINS[0]),
    allowMethods: ["GET", "POST", "PUT", "PATCH", "DELETE", "OPTIONS"],
    allowHeaders: [
      "Content-Type",
      "Authorization",
      "X-Request-ID",
      "Idempotency-Key",
      "If-Match",
    ],
    exposeHeaders: ["X-Request-ID", "X-Correlation-ID"],
    credentials: true,
    maxAge: 600,
  })
);

// ─── Correlation ID ──────────────────────────────────────────────────────────
app.use("*", async (c, next) => {
  const reqId =
    c.req.header("X-Request-ID") ||
    c.req.header("CF-Ray") ||
    crypto.randomUUID();
  c.set("rid", reqId);
  await next();
  c.header("X-Request-ID", reqId);
  c.header("X-Correlation-ID", reqId);
});

// ─── Routes ─────────────────────────────────────────────────────────────────

const api = new Hono<AppEnv>();

// Public
api.route("/health", healthRoutes);
api.route("/auth", authRoutes);

// GET /me (top-level alias for /auth/me used by AuthService)
api.get("/me", async (c) => {
  const authHeader = c.req.header("Authorization");
  if (!authHeader?.startsWith("Bearer ")) {
    return c.json({ error: { code: "UNAUTHENTICATED", message: "Not authenticated" } }, 401);
  }
  let payload;
  try {
    payload = await verifyToken(authHeader.slice(7), c.env.JWT_SECRET);
  } catch {
    return c.json({ error: { code: "UNAUTHENTICATED", message: "Invalid token" } }, 401);
  }
  const users = await db<{ id: string; username: string; display_name_demo: string }>(
    c.env.DATABASE_URL
  )`
    SELECT id, username, display_name_demo FROM iam_users
    WHERE id = ${payload.sub}::uuid LIMIT 1
  `;
  return c.json({
    id: payload.sub,
    username: payload.username,
    display_name: users[0]?.display_name_demo ?? payload.username,
    roles: payload.roles,
    capabilities: payload.caps,
  });
});

// Protected (JWT required)
api.use("/inventory/*", authMiddleware);
api.route("/inventory", inventoryRoutes);

api.use("/cash-sim/*", authMiddleware);
api.route("/cash-sim", cashSimRoutes);

api.use("/approvals/*", authMiddleware);
api.route("/approvals", approvalsRoutes);

api.use("/activity/*", authMiddleware);
api.route("/activity", activityRoutes);

api.use("/reports/*", authMiddleware);
api.route("/reports", reportsRoutes);

api.use("/integrations/*", authMiddleware);
api.route("/integrations", integrationsRoutes);

api.use("/reconciliations", authMiddleware);
api.use("/reconciliations/*", authMiddleware);
api.route("/reconciliations", reconciliationsRoutes);

// Mount tại /api/v1 (mirror FastAPI)
app.route("/api/v1", api);

// ─── Cron Handler (keep-warm ping) ──────────────────────────────────────────
// Workers đã zero cold start, nhưng cron này log metrics
app.get("/_cron/health", (c) =>
  c.json({ status: "ok", ts: new Date().toISOString() })
);

// ─── 404 fallback ────────────────────────────────────────────────────────────
app.notFound((c) => {
  const rid =
    c.req.header("X-Request-ID") ||
    c.req.header("CF-Ray") ||
    crypto.randomUUID();
  return c.json(
    {
      error: {
        code: "NOT_FOUND",
        message: "Route không tồn tại",
        request_id: rid,
      },
    },
    404,
    { "X-Request-ID": rid }
  );
});

// ─── Error handler ───────────────────────────────────────────────────────────
app.onError((err, c) => {
  console.error(`[ERROR] ${c.req.method} ${c.req.path}:`, err);
  let rid = "";
  try {
    rid = c.get("rid") || c.req.header("X-Request-ID") || crypto.randomUUID();
  } catch {
    rid = c.req.header("X-Request-ID") || crypto.randomUUID();
  }
  const message =
    c.env.APP_ENV === "production" ? "Internal server error" : err.message;
  return c.json(
    {
      error: { code: "INTERNAL_ERROR", message, request_id: rid },
    },
    500,
    { "X-Request-ID": rid }
  );
});

export default {
  fetch: app.fetch,

  // Cron scheduled handler
  async scheduled(_event: ScheduledEvent, env: Env, _ctx: ExecutionContext) {
    console.log(
      `[CRON] Health ping – ${new Date().toISOString()} env=${env.APP_ENV}`
    );
  },
};
