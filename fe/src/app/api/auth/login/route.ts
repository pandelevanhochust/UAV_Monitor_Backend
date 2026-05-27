import { NextRequest, NextResponse } from 'next/server';

const COOKIE_NAME = 'uav_token';
const COOKIE_MAX_AGE = 86400; // 24h — matches JWT_EXPIRES_SECONDS in claude.md Appendix A

/**
 * POST /api/auth/login
 *
 * Server-side login handler. Forwards credentials to the Kong gateway
 * (→ UserService /api/v1/auth/login public route) and stores the returned
 * JWT as an HttpOnly cookie — never exposed to browser JavaScript.
 *
 * Kong route: public-auth-login (no forward-auth plugin — claude.md §2 kong.yml)
 * UserService endpoint: POST /api/v1/auth/login
 */
export async function POST(request: NextRequest): Promise<NextResponse> {
  // ── 1. Parse and validate request body ──
  let body: { email: string; password: string };
  try {
    body = await request.json();
  } catch {
    return NextResponse.json(
      { message: 'Invalid request body.' },
      { status: 400 }
    );
  }

  if (!body.email || !body.password) {
    return NextResponse.json(
      { message: 'Email and password are required.' },
      { status: 400 }
    );
  }

  // ── 2. Forward to Kong → UserService ──
  const backendUrl = process.env.BACKEND_URL ?? 'http://localhost:8000';
  const loginUrl = `${backendUrl}/api/v1/auth/login`;

  let upstreamResponse: Response;
  try {
    upstreamResponse = await fetch(loginUrl, {
      method: 'POST',
      headers: { 'Content-Type': 'application/json' },
      body: JSON.stringify({ email: body.email, password: body.password }),
      // Disable Next.js fetch cache for auth endpoints
      cache: 'no-store',
    });
  } catch (networkError) {
    console.error('[auth/login] Network error reaching backend:', networkError);
    return NextResponse.json(
      { message: 'Could not reach the authentication service. Please try again.' },
      { status: 503 }
    );
  }

  // ── 3. Handle upstream error responses ──
  if (!upstreamResponse.ok) {
    let errorMessage = 'Authentication failed.';
    try {
      const errorBody = await upstreamResponse.json();
      errorMessage = errorBody.message ?? errorBody.title ?? errorMessage;
    } catch {
      // upstream returned non-JSON error body; use default message
    }
    return NextResponse.json(
      { message: errorMessage },
      { status: upstreamResponse.status }
    );
  }

  // ── 4. Extract token and user from successful response ──
  let data: { token: string; user: Record<string, unknown> };
  try {
    data = await upstreamResponse.json();
  } catch {
    return NextResponse.json(
      { message: 'Unexpected response format from authentication service.' },
      { status: 502 }
    );
  }

  if (!data.token) {
    return NextResponse.json(
      { message: 'Authentication service did not return a token.' },
      { status: 502 }
    );
  }

  // ── 5. Set HttpOnly cookie — token never touches browser JavaScript ──
  const isProduction = process.env.NODE_ENV === 'production';

  const response = NextResponse.json(
    { user: data.user },
    { status: 200 }
  );

  response.cookies.set(COOKIE_NAME, data.token, {
    httpOnly: true,
    secure: isProduction,
    sameSite: 'strict',
    path: '/',
    maxAge: COOKIE_MAX_AGE,
  });

  return response;
}
