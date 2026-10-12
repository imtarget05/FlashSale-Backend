import { Hono } from "hono";
import { createRemoteJWKSet, jwtVerify } from "jose";
import { Env } from "../index";
import { db } from "../db/client";
import bcrypt from "bcryptjs";
import {
  createAccessToken,
  createRefreshToken,
  verifyToken,
} from "../middleware/auth";

export const authRoutes = new Hono<{ Bindings: Env }>();

const GOOGLE_JWKS = createRemoteJWKSet(
  new URL("https://www.googleapis.com/oauth2/v3/certs")
);
const GOOGLE_ISSUERS = ["accounts.google.com", "https://accounts.google.com"];

interface DbUser {
  id: string;
  username: string;
  password_hash: string;
  status: string;
  display_name_demo: string;
}

interface DbRole { role_code: string }
interface DbCap  { capability_code: string }

// ─── POST /auth/login ────────────────────────────────────────────────────────

authRoutes.post("/login", async (c) => {
  let body: { username?: string; password?: string };

  const contentType = c.req.header("content-type") ?? "";
  if (contentType.includes("application/x-www-form-urlencoded")) {
    const form = await c.req.formData();
    body = {
      username: form.get("username")?.toString(),
      password: form.get("password")?.toString(),
    };
  } else {
    body = await c.req.json().catch(() => ({}));
  }

  const { username, password } = body;
  if (!username || !password) {
    return c.json({ detail: "username and password are required" }, 422);
  }

  const users = await db<DbUser>(c.env.DATABASE_URL)`
    SELECT id, username, password_hash, status, display_name_demo
    FROM iam_users WHERE username = ${username} LIMIT 1
  `;

  if (!users.length) {
    return c.json({ detail: "Incorrect username or password" }, 401);
  }

  const user = users[0];
  if (user.status !== "active") {
    return c.json({ detail: "Account is not active" }, 401);
  }

  const passwordOk = await bcrypt.compare(password, user.password_hash);
  if (!passwordOk) {
    return c.json({ detail: "Incorrect username or password" }, 401);
  }

  const [rolesRows, capsRows] = await Promise.all([
    db<DbRole>(c.env.DATABASE_URL)`
      SELECT role_code FROM iam_user_roles WHERE user_id = ${user.id}::uuid
    `,
    db<DbCap>(c.env.DATABASE_URL)`
      SELECT DISTINCT rc.capability_code
      FROM iam_role_capabilities rc
      JOIN iam_user_roles ur ON ur.role_code = rc.role_code
      WHERE ur.user_id = ${user.id}::uuid
    `,
  ]);

  const roleList = rolesRows.map((r) => r.role_code);
  const capList  = capsRows.map((r) => r.capability_code);
  const expireMinutes = parseInt(c.env.ACCESS_TOKEN_EXPIRE_MINUTES ?? "30", 10);
  const expireDays    = parseInt(c.env.REFRESH_TOKEN_EXPIRE_DAYS ?? "7", 10);

  const [accessToken, refreshToken] = await Promise.all([
    createAccessToken(
      { sub: user.id, username: user.username, roles: roleList, caps: capList },
      c.env.JWT_SECRET, expireMinutes
    ),
    createRefreshToken(
      { sub: user.id, username: user.username },
      c.env.JWT_SECRET, expireDays
    ),
  ]);

  return c.json({
    access_token: accessToken,
    refresh_token: refreshToken,
    token_type: "bearer",
    expires_in: expireMinutes * 60,
    user: {
      id: user.id,
      username: user.username,
      display_name: user.display_name_demo,
      roles: roleList,
      capabilities: capList,
    },
  });
});

// ─── POST /auth/logout ───────────────────────────────────────────────────────

authRoutes.post("/logout", (_c) =>
  Response.json({ message: "Logged out successfully" })
);

// ─── GET /auth/me ─────────────────────────────────────────────────────────

authRoutes.get("/me", async (c) => {
  const authHeader = c.req.header("Authorization");
  if (!authHeader?.startsWith("Bearer ")) {
    return c.json({ detail: "Not authenticated" }, 401);
  }
  let payload;
  try {
    payload = await verifyToken(authHeader.slice(7), c.env.JWT_SECRET);
  } catch {
    return c.json({ detail: "Could not validate credentials" }, 401);
  }

  const users = await db<DbUser>(c.env.DATABASE_URL)`
    SELECT id, username, display_name_demo FROM iam_users
    WHERE id = ${payload.sub}::uuid LIMIT 1
  `;
  if (!users.length) return c.json({ detail: "User not found" }, 404);

  return c.json({
    id: payload.sub,
    username: payload.username,
    display_name: users[0].display_name_demo,
    roles: payload.roles,
    capabilities: payload.caps,
  });
});

// ─── POST /auth/refresh ──────────────────────────────────────────────────────

authRoutes.post("/refresh", async (c) => {
  const { refresh_token = "" } = await c.req.json<{ refresh_token: string }>().catch(() => ({ refresh_token: "" }));
  if (!refresh_token) return c.json({ detail: "refresh_token is required" }, 422);

  let payload;
  try {
    payload = await verifyToken(refresh_token, c.env.JWT_SECRET);
  } catch {
    return c.json({ detail: "Invalid or expired refresh token" }, 401);
  }
  if (payload.type !== "refresh") return c.json({ detail: "Invalid token type" }, 401);

  const [rolesRows, capsRows] = await Promise.all([
    db<DbRole>(c.env.DATABASE_URL)`
      SELECT role_code FROM iam_user_roles WHERE user_id = ${payload.sub}::uuid
    `,
    db<DbCap>(c.env.DATABASE_URL)`
      SELECT DISTINCT rc.capability_code
      FROM iam_role_capabilities rc
      JOIN iam_user_roles ur ON ur.role_code = rc.role_code
      WHERE ur.user_id = ${payload.sub}::uuid
    `,
  ]);

  const expireMinutes = parseInt(c.env.ACCESS_TOKEN_EXPIRE_MINUTES ?? "30", 10);
  const newToken = await createAccessToken(
    {
      sub: payload.sub,
      username: payload.username,
      roles: rolesRows.map((r) => r.role_code),
      caps: capsRows.map((r) => r.capability_code),
    },
    c.env.JWT_SECRET, expireMinutes
  );

  return c.json({ access_token: newToken, token_type: "bearer", expires_in: expireMinutes * 60 });
});

// ─── POST /auth/google ───────────────────────────────────────────────────────

function googleErrorEnvelope(
  code: string,
  message: string,
  status: 400 | 401 | 422 | 503,
  details: Record<string, unknown> = {}
) {
  return Response.json(
    {
      error: {
        code,
        message,
        request_id: crypto.randomUUID(),
        details,
      },
    },
    { status }
  );
}

authRoutes.post("/google", async (c) => {
  const body = await c.req
    .json<{ id_token?: string }>()
    .catch(() => ({ id_token: undefined }));

  const idToken = (body.id_token ?? "").trim();
  if (idToken.length < 10) {
    return googleErrorEnvelope(
      "VALIDATION_ERROR",
      "Validation failed",
      422,
      { field: "id_token" }
    );
  }

  let claims: Record<string, unknown>;
  try {
    const allowedAudiences = (c.env.GOOGLE_CLIENT_ID ?? "")
      .split(",")
      .map((s) => s.trim())
      .filter(Boolean);

    const verified = await jwtVerify(idToken, GOOGLE_JWKS, {
      issuer: GOOGLE_ISSUERS,
      ...(allowedAudiences.length > 0 ? { audience: allowedAudiences } : {}),
    });
    claims = verified.payload as Record<string, unknown>;
  } catch {
    return googleErrorEnvelope(
      "GOOGLE_TOKEN_INVALID",
      "Google ID token không hợp lệ hoặc hết hạn.",
      401
    );
  }

  const email = typeof claims.email === "string" ? claims.email.trim().toLowerCase() : "";
  if (!email) {
    return googleErrorEnvelope("GOOGLE_EMAIL_MISSING", "ID token thiếu email.", 401);
  }
  if (!claims.email_verified) {
    return googleErrorEnvelope(
      "GOOGLE_EMAIL_UNVERIFIED",
      "Email Google chưa được xác minh.",
      401,
      { email }
    );
  }

  const existing = await db<DbUser>(c.env.DATABASE_URL)`
    SELECT id, username, password_hash, status, display_name_demo
    FROM iam_users WHERE username = ${email} LIMIT 1
  `;

  let user: DbUser;
  const isNewUser = existing.length === 0;

  if (isNewUser) {
    const newId = crypto.randomUUID();
    const displayName =
      typeof claims.name === "string" && claims.name.trim()
        ? claims.name.trim()
        : email;
    const placeholderHash = `GOOGLE_OAUTH_NO_PASSWORD:${crypto.randomUUID()}`;

    const inserted = await db<DbUser>(c.env.DATABASE_URL)`
      INSERT INTO iam_users (id, username, password_hash, status, display_name_demo)
      VALUES (${newId}::uuid, ${email}, ${placeholderHash}, 'active', ${displayName})
      RETURNING id, username, password_hash, status, display_name_demo
    `;
    user = inserted[0];

    await db(c.env.DATABASE_URL)`
      INSERT INTO iam_user_roles (user_id, role_code)
      SELECT ${user.id}::uuid, 'staff'
      WHERE EXISTS (SELECT 1 FROM iam_roles WHERE code = 'staff')
      ON CONFLICT DO NOTHING
    `;
  } else {
    user = existing[0];
    if (user.status !== "active") {
      return googleErrorEnvelope("FORBIDDEN", "Account is not active", 401);
    }
  }

  const [rolesRows, capsRows] = await Promise.all([
    db<DbRole>(c.env.DATABASE_URL)`
      SELECT role_code FROM iam_user_roles WHERE user_id = ${user.id}::uuid
    `,
    db<DbCap>(c.env.DATABASE_URL)`
      SELECT DISTINCT rc.capability_code
      FROM iam_role_capabilities rc
      JOIN iam_user_roles ur ON ur.role_code = rc.role_code
      WHERE ur.user_id = ${user.id}::uuid
    `,
  ]);

  const roleList = rolesRows.map((r) => r.role_code);
  const capList = capsRows.map((r) => r.capability_code);
  const expireMinutes = parseInt(c.env.ACCESS_TOKEN_EXPIRE_MINUTES ?? "30", 10);
  const expireDays = parseInt(c.env.REFRESH_TOKEN_EXPIRE_DAYS ?? "7", 10);

  const [accessToken, refreshToken] = await Promise.all([
    createAccessToken(
      { sub: user.id, username: user.username, roles: roleList, caps: capList },
      c.env.JWT_SECRET,
      expireMinutes
    ),
    createRefreshToken(
      { sub: user.id, username: user.username },
      c.env.JWT_SECRET,
      expireDays
    ),
  ]);

  return c.json({
    access_token: accessToken,
    refresh_token: refreshToken,
    token_type: "bearer",
    expires_in: expireMinutes * 60,
    is_new_user: isNewUser,
    user: {
      id: user.id,
      username: user.username,
      display_name: user.display_name_demo,
      roles: roleList,
      capabilities: capList,
    },
  });
});

