/**
 * Neon Serverless PostgreSQL client cho Cloudflare Workers
 *
 * Dùng HTTP transport thay vì TCP (TCP không hỗ trợ trong Workers).
 * Neon pooled endpoint giữ connection sẵn sàng → không waiting DB spin-up.
 */

import { neon } from "@neondatabase/serverless";

// Cache SQL function per isolate lifetime
let _sql: ReturnType<typeof neon> | null = null;

function getSql(databaseUrl: string): ReturnType<typeof neon> {
  if (!_sql) {
    _sql = neon(databaseUrl);
  }
  return _sql;
}

/**
 * Type-safe query helper.
 * Neon tagged template returns a union type; this helper casts it cleanly.
 *
 * Usage:
 *   const users = await db<DbUser>(env.DATABASE_URL)`SELECT * FROM iam_users`;
 */
export function db<T = Record<string, unknown>>(databaseUrl: string) {
  const sql = getSql(databaseUrl);
  return async (
    strings: TemplateStringsArray,
    ...values: unknown[]
  ): Promise<T[]> => {
    // eslint-disable-next-line @typescript-eslint/no-explicit-any
    const result = await (sql as any)(strings, ...values);
    return result as T[];
  };
}

// Convenience re-export for raw sql (when you don't need types)
export const getDb = getSql;
