// Cloudflare Pages BFF for OpsBridge. Business data always comes from the owner API.
interface Env {
  API_BACKEND_URL?: string;
}

const noStore = "private, no-store";

function unavailable(): Response {
  return new Response(JSON.stringify({ error: "UpstreamBackendUnavailable" }), {
    status: 503,
    headers: { "Content-Type": "application/json", "Cache-Control": noStore },
  });
}

export const onRequest: PagesFunction<Env> = async (context) => {
  const requestUrl = new URL(context.request.url);
  if (context.request.method === "OPTIONS") {
    return new Response(null, {
      status: 204,
      headers: {
        "Access-Control-Allow-Origin": requestUrl.origin,
        "Access-Control-Allow-Methods": "GET, POST, PUT, DELETE, PATCH, OPTIONS",
        "Access-Control-Allow-Headers": "Content-Type, Authorization, Idempotency-Key",
        "Access-Control-Allow-Credentials": "true",
        "Cache-Control": noStore,
      },
    });
  }

  let origin: URL;
  try {
    origin = new URL(context.env.API_BACKEND_URL ?? "");
    if (origin.protocol !== "https:" || origin.username || origin.password) return unavailable();
  } catch {
    return unavailable();
  }

  const target = new URL(origin);
  target.pathname = `${origin.pathname.replace(/\/$/, "")}${requestUrl.pathname}`;
  target.search = requestUrl.search;
  try {
    const upstream = await fetch(target.toString(), {
      method: context.request.method,
      headers: context.request.headers,
      body: ["GET", "HEAD"].includes(context.request.method) ? undefined : context.request.body,
      redirect: "manual",
    });
    // Do not send a browser with an Authorization header to a redirect target.
    if (upstream.status >= 300 && upstream.status < 400) return unavailable();
    const headers = new Headers(upstream.headers);
    headers.set("Cache-Control", noStore);
    headers.set("Access-Control-Allow-Origin", requestUrl.origin);
    headers.set("Access-Control-Allow-Credentials", "true");
    return new Response(upstream.body, { status: upstream.status, headers });
  } catch {
    // Never synthesize orders, invoices or replay results during an outage.
    return unavailable();
  }
};
