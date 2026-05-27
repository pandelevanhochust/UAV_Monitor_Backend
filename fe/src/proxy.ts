import { NextRequest, NextResponse } from 'next/server';
import { jwtVerify, type JWTPayload } from 'jose';

// ─── Constants ────────────────────────────────────────────────────────────────

const COOKIE_NAME = 'uav_token';
const LOGIN_PATH = '/login';

/**
 * JWT configuration — must EXACTLY match the backend JwtOptions.
 * Source: claude.md Appendix A
 *   JWT_ISSUER=uav-detection-system
 *   JWT_AUDIENCE=uav-supervisors
 *   JWT_EXPIRES_SECONDS=86400
 */
const JWT_ISSUER = process.env.JWT_ISSUER ?? 'uav-detection-system';
const JWT_AUDIENCE = process.env.JWT_AUDIENCE ?? 'uav-supervisors';

// ─── Role type ────────────────────────────────────────────────────────────────

/**
 * C# enum UserRole { Admin, Monitor } — serialized as PascalCase strings.
 * Source: claude.md §4 — "public enum UserRole { Admin, Monitor }"
 */
type UserRole = 'Admin' | 'Monitor';

interface UavJwtPayload extends JWTPayload {
  sub: string;      // User UUID
  role: UserRole;   // "Admin" | "Monitor"
  email?: string;
}

// ─── Path rules ───────────────────────────────────────────────────────────────

/**
 * Role-based path enforcement rules.
 * Source: claude.md §2 — Kong routing matrix:
 *   /api/v1/admin/users/**  → requires Admin
 *   /api/v1/admin/devices/** → requires Admin
 *   /api/v1/devices/**      → requires Admin or Monitor
 *   /api/v1/logs/**         → requires Admin or Monitor
 */
const ADMIN_ONLY_PATHS = ['/admin'];
const AUTHENTICATED_PATHS = ['/dashboard', '/devices', '/logs', '/alerts'];

function requiresAdminRole(pathname: string): boolean {
  return ADMIN_ONLY_PATHS.some((p) => pathname.startsWith(p));
}

function requiresAuthentication(pathname: string): boolean {
  return AUTHENTICATED_PATHS.some((p) => pathname.startsWith(p));
}

// ─── Helper: get signing key ──────────────────────────────────────────────────

function getSigningKey(): Uint8Array {
  const secret = process.env.JWT_SECRET;
  if (!secret) {
    throw new Error('JWT_SECRET environment variable is not set.');
  }
  return new TextEncoder().encode(secret);
}

// ─── Helper: redirect with cleared cookie ─────────────────────────────────────

function redirectToLogin(request: NextRequest): NextResponse {
  const loginUrl = new URL(LOGIN_PATH, request.url);
  const response = NextResponse.redirect(loginUrl);
  // Clear the stale / invalid token cookie
  response.cookies.set(COOKIE_NAME, '', {
    httpOnly: true,
    secure: process.env.NODE_ENV === 'production',
    sameSite: 'strict',
    path: '/',
    maxAge: 0,
  });
  return response;
}

// ─── Middleware ────────────────────────────────────────────────────────────────

export async function proxy(request: NextRequest): Promise<NextResponse> {
  const { pathname } = request.nextUrl;

  // ── Fast path: skip routes that don't need auth ──
  const isPublicPath =
    pathname === LOGIN_PATH ||
    pathname === '/' ||
    pathname.startsWith('/_next') ||
    pathname.startsWith('/favicon');

  if (isPublicPath) {
    return NextResponse.next();
  }

  // ── 1. Read the JWT from HttpOnly cookie ──
  const token = request.cookies.get(COOKIE_NAME)?.value;

  if (!token) {
    return redirectToLogin(request);
  }

  // ── 2. Verify the JWT signature, expiry, issuer, and audience ──
  let payload: UavJwtPayload;
  try {
    const { payload: verified } = await jwtVerify<UavJwtPayload>(
      token,
      getSigningKey(),
      {
        issuer: JWT_ISSUER,
        audience: JWT_AUDIENCE,
        algorithms: ['HS256'],
      }
    );
    payload = verified;
  } catch {
    // Expired, tampered, or wrong secret → force re-login
    return redirectToLogin(request);
  }

  const userId = payload.sub;
  const userRole = payload.role;

  // ── 3. Role-based path enforcement ──
  if (requiresAdminRole(pathname) && userRole !== 'Admin') {
    // Monitor trying to access admin routes → redirect to dashboard
    return NextResponse.redirect(new URL('/dashboard', request.url));
  }

  if (requiresAuthentication(pathname) && !userId) {
    return redirectToLogin(request);
  }

  // ── 4. Pass through — inject verified claims as headers for Server Components ──
  const requestHeaders = new Headers(request.headers);
  requestHeaders.set('x-user-id', userId ?? '');
  requestHeaders.set('x-user-role', userRole ?? '');

  return NextResponse.next({
    request: { headers: requestHeaders },
  });
}

// ─── Matcher ──────────────────────────────────────────────────────────────────

/**
 * Run middleware on all routes EXCEPT:
 *  - /api/*          (Next.js API routes handle their own auth)
 *  - /_next/static   (static assets)
 *  - /_next/image    (image optimization)
 *  - /favicon.ico
 */
export const config = {
  matcher: ['/((?!api|_next/static|_next/image|favicon\\.ico).*)'],
};
