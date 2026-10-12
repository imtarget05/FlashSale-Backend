/**
 * JWT Authentication Middleware cho Cloudflare Workers
 *
 * Dùng Web Crypto API (native trong Workers, không cần thư viện bcrypt/passlib).
 * Verify HS256 JWT với thư viện `jose` (thuần Web Crypto, không dùng Node.js).
 */

import { MiddlewareHandler } from "hono";
import { jwtVerify, SignJWT } from "jose";
import { Env } from "../index";

export interface JwtPayload {
  sub: string;          // user UUID
  username: string;
  roles: string[];
  caps: string[];
  type: "access" | "refresh";
  exp: number;
  iat: number;
}

function getSecretKey(secret: string): Uint8Array {
  return new TextEncoder().encode(secret);
}

/**
 * Tạo access token (HS256)
 */
export async function createAccessToken(
  payload: Omit<JwtPayload, "exp" | "iat" | "type">,
  secret: string,
  expireMinutes = 30
): Promise<string> {
  const key = getSecretKey(secret);
  return new SignJWT({ ...payload, type: "access" })
    .setProtectedHeader({ alg: "HS256" })
    .setIssuedAt()
    .setExpirationTime(`${expireMinutes}m`)
    .sign(key);
}

/**
 * Tạo refresh token (HS256)
 */
export async function createRefreshToken(
  payload: Pick<JwtPayload, "sub" | "username">,
  secret: string,
  expireDays = 7
): Promise<string> {
  const key = getSecretKey(secret);
  return new SignJWT({ ...payload, type: "refresh" })
    .setProtectedHeader({ alg: "HS256" })
    .setIssuedAt()
    .setExpirationTime(`${expireDays}d`)
    .sign(key);
}

/**
 * Verify token và trả về payload
 */
export async function verifyToken(
  token: string,
  secret: string
): Promise<JwtPayload> {
  const key = getSecretKey(secret);
  const { payload } = await jwtVerify(token, key);
  return payload as unknown as JwtPayload;
}

/**
 * Middleware: bắt buộc Authorization: Bearer <token>
 * Gắn payload vào c.set("user", payload)
 */
export const authMiddleware: MiddlewareHandler<{ Bindings: Env }> = async (
  c,
  next
) => {
  const authHeader = c.req.header("Authorization");
  if (!authHeader?.startsWith("Bearer ")) {
    return c.json({ detail: "Missing or invalid Authorization header" }, 401);
  }

  const token = authHeader.slice(7);

  try {
    const payload = await verifyToken(token, c.env.JWT_SECRET);
    if (payload.type !== "access") {
      return c.json({ detail: "Invalid token type" }, 401);
    }
    c.set("user" as never, payload);
  } catch {
    return c.json({ detail: "Could not validate credentials" }, 401);
  }

  await next();
};
