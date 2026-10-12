/**
 * Activity routes – mirror FastAPI /activity/*
 *
 * Workers free: 30s timeout. Paid: unlimited.
 * Cho demo nội bộ: polling mỗi 10-30s là đủ.
 * SSE reconnect tự động được client xử lý.
 */

import { Hono } from "hono";
import { Env } from "../index";
import { db } from "../db/client";
import { JwtPayload } from "../middleware/auth";

export const activityRoutes = new Hono<{ Bindings: Env }>();

function getUser(c: { get: (key: string) => unknown }): JwtPayload {
  return c.get("user") as JwtPayload;
}

interface ActivityEvent {
  id: string;
  type: string;      // column 'type' in DB (not 'event_type')
  payload: unknown;
  occurred_at: string;  // column 'occurred_at' in DB (not 'created_at')
  owner_id: string | null;
}

// GET /activity/ – lịch sử hoạt động (polling-friendly)
activityRoutes.get("/", async (c) => {
  const since = c.req.query("since");
  const events = since
    ? await db<ActivityEvent>(c.env.DATABASE_URL)`
        SELECT id, type, payload, occurred_at, owner_id
        FROM activity_events
        WHERE occurred_at > ${since}::timestamptz
        ORDER BY occurred_at DESC LIMIT 50
      `
    : await db<ActivityEvent>(c.env.DATABASE_URL)`
        SELECT id, type, payload, occurred_at, owner_id
        FROM activity_events
        ORDER BY occurred_at DESC LIMIT 50
      `;

  return c.json({
    events,
    total: events.length,
    next_poll_after: new Date().toISOString(),
  });
});

// GET /activity/stream – SSE heartbeat
// Workers streaming: client tự reconnect sau khi connection đóng
activityRoutes.get("/stream", async (c) => {
  // Lấy user từ context (đã auth middleware)
  getUser(c); // ensure authenticated

  const { readable, writable } = new TransformStream();
  const writer = writable.getWriter();
  const encoder = new TextEncoder();

  const writeEvent = async (data: unknown) => {
    await writer.write(encoder.encode(`data: ${JSON.stringify(data)}\n\n`));
  };

  // eslint-disable-next-line @typescript-eslint/no-explicit-any
  (c as any).executionCtx?.waitUntil(
    (async () => {
      try {
        await writeEvent({ event: "connected", ts: new Date().toISOString() });
        await new Promise<void>((r) => setTimeout(r, 25_000));
        await writeEvent({ event: "heartbeat", ts: new Date().toISOString() });
      } finally {
        await writer.close().catch(() => void 0);
      }
    })()
  );

  return new Response(readable, {
    headers: {
      "Content-Type": "text/event-stream",
      "Cache-Control": "no-cache",
      Connection: "keep-alive",
      "X-Accel-Buffering": "no",
    },
  });
});
