import { NextRequest, NextResponse } from 'next/server';

const COOKIE_NAME = 'uav_token';

/**
 * Generic JWT-injecting catch-all proxy.
 *
 * Maps: /api/proxy/<path...>?<query>
 *    → {BACKEND_URL}/api/v1/<path...>?<query>
 *
 * Kong routes handled here (all require forward-auth — claude.md §2 kong.yml):
 *   GET  /api/proxy/devices           → /api/v1/devices
 *   GET  /api/proxy/admin/devices     → /api/v1/admin/devices
 *   POST /api/proxy/admin/devices     → /api/v1/admin/devices  (Admin only)
 *   GET  /api/proxy/logs              → /api/v1/logs
 *   GET  /api/proxy/admin/users       → /api/v1/admin/users    (Admin only)
 *   POST /api/proxy/admin/users       → /api/v1/admin/users    (Admin only)
 *
 * The proxy reads the uav_token from the HttpOnly cookie and injects it
 * as an Authorization: Bearer header — browser JS never touches the token.
 *
 * Kong's forward-auth plugin then performs gRPC validation against UserService
 * and injects X-User-ID / X-User-Role into the upstream request.
 *
 * Note: /ws/alerts (SignalR WebSocket) is NOT proxied here.
 * The @microsoft/signalr client connects directly to Kong with:
 *   new HubConnectionBuilder()
 *     .withUrl(`${NEXT_PUBLIC_WS_URL}/ws/alerts?access_token=<token>`)
 */

type RouteContext = { params: Promise<{ path: string[] }> };

const ALLOWED_METHODS = new Set(['GET', 'POST', 'PUT', 'DELETE', 'PATCH']);

// ─── Shared handler ────────────────────────────────────────────────────────────

async function handler(
  request: NextRequest,
  context: RouteContext
): Promise<NextResponse> {
  const method = request.method.toUpperCase();

  if (!ALLOWED_METHODS.has(method)) {
    return NextResponse.json({ message: 'Method not allowed.' }, { status: 405 });
  }

  // ── 1. Read token from HttpOnly cookie ──
  const token = request.cookies.get(COOKIE_NAME)?.value;
  if (!token) {
    return NextResponse.json(
      { message: 'Unauthorized. No session token found.' },
      { status: 401 }
    );
  }

  // ── 2. Build upstream URL ──
  const { path } = await context.params;
  const backendUrl = process.env.BACKEND_URL ?? 'http://localhost:8000';
  const upstreamPath = `/api/v1/${path.join('/')}`;
  const upstreamUrl = new URL(upstreamPath, backendUrl);

  // Forward all query parameters as-is
  request.nextUrl.searchParams.forEach((value, key) => {
    upstreamUrl.searchParams.set(key, value);
  });

  // ── 3. Build forwarded headers ──
  const forwardedHeaders = new Headers();
  forwardedHeaders.set('Authorization', `Bearer ${token}`);

  const contentType = request.headers.get('Content-Type');
  if (contentType) {
    forwardedHeaders.set('Content-Type', contentType);
  }

  // Propagate correlation ID if present (set by Kong or client)
  const correlationId = request.headers.get('X-Correlation-Id');
  if (correlationId) {
    forwardedHeaders.set('X-Correlation-Id', correlationId);
  }

  // ── 4. Forward the request body (for mutation methods) ──
  let requestBody: BodyInit | null = null;
  if (method !== 'GET' && method !== 'DELETE') {
    requestBody = await request.arrayBuffer();
  }

  // ── 5. Call the upstream Kong gateway ──
  let upstreamResponse: Response;
  try {
    upstreamResponse = await fetch(upstreamUrl.toString(), {
      method,
      headers: forwardedHeaders,
      body: requestBody,
      cache: 'no-store',
      // @ts-expect-error — Node.js fetch supports duplex
      duplex: requestBody ? 'half' : undefined,
    });
  } catch (networkError) {
    console.error(`[proxy] Network error → ${upstreamUrl}:`, networkError);
    return NextResponse.json(
      { message: 'Could not reach the backend service. Please try again.' },
      { status: 503 }
    );
  }

  // ── 6. Stream response back to the browser ──
  // Preserve the upstream status code, body, and Content-Type.
  const responseBody = await upstreamResponse.arrayBuffer();
  const responseHeaders = new Headers();

  const upstreamContentType = upstreamResponse.headers.get('Content-Type');
  if (upstreamContentType) {
    responseHeaders.set('Content-Type', upstreamContentType);
  }

  // Propagate pagination metadata headers if present
  const totalCount = upstreamResponse.headers.get('X-Total-Count');
  if (totalCount) {
    responseHeaders.set('X-Total-Count', totalCount);
  }

  return new NextResponse(responseBody, {
    status: upstreamResponse.status,
    headers: responseHeaders,
  });
}

// ─── HTTP method exports ───────────────────────────────────────────────────────

export const GET = handler;
export const POST = handler;
export const PUT = handler;
export const DELETE = handler;
export const PATCH = handler;
